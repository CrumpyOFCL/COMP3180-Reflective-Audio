using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.EventSystems;

public enum UISound
{
    Hover,
    Click,
    Back,
    SliderTick,
    PanelOpen,
    PanelClose,
    Confirm,
    Error,
    TabSwitch,
    Popup,
    GameOver,
    PauseOpen,
    PauseClose,
    JournalOpen,     // also logbook / map / inventory / quest panels
    JournalClose,
    JournalPage,
    ToggleOn,
    ToggleOff,
    RebindListen,
    RebindSet
}

// ADDED (round 2). One place for every UI sound, so menu scripts only need
// UIAudio.Play(UISound.X). Put ONE on the Core object (or any object that
// survives loads) and one in the MainMenu scene; a second copy destroys itself.
//
//  * Hover is automatic for keyboard/gamepad: whenever the EventSystem's
//    selected object changes, Hover plays (mouse hover: add UISelectableSounds
//    to the button, or tick Auto Hook below).
//  * Auto Hook Buttons adds Click to every Button, SliderTick to every Slider and
//    Toggle_On/Off to every Toggle in each loaded scene, so nothing has to be wired by hand. Buttons that carry
//    UISelectableSounds are skipped (that component plays their sounds).
//  * SetPaused(true) starts snapshot:/Pause (duck/low-pass gameplay while the
//    pause menu is up) and SetPaused(false) stops it.
//
// FMOD runs on its own clock, so all of this works while Time.timeScale is 0.
[DefaultExecutionOrder(-50)]
public class UIAudio : MonoBehaviour
{
    [Header("Core UI events (2D, bus:/SFX/UI) - event:/UI/<Name>")]
    [SerializeField] private EventReference hover;        // event:/UI/Hover
    [SerializeField] private EventReference click;        // event:/UI/Click
    [SerializeField] private EventReference back;         // event:/UI/Back
    [SerializeField] private EventReference sliderTick;   // event:/UI/SliderTick
    [SerializeField] private EventReference panelOpen;    // event:/UI/Panel_Open
    [SerializeField] private EventReference panelClose;   // event:/UI/Panel_Close
    [SerializeField] private EventReference confirm;      // event:/UI/Confirm
    [SerializeField] private EventReference error;        // event:/UI/Error

    [Header("Menus (U02)")]
    [SerializeField] private EventReference tabSwitch;    // event:/UI/TabSwitch
    [SerializeField] private EventReference popup;        // event:/UI/Popup
    [Tooltip("Empty = falls back to Panel Open (the game over panel reuses ui_panel_open_01).")]
    [SerializeField] private EventReference gameOver;     // event:/UI/GameOver (optional)
    [SerializeField] private EventReference pauseOpen;    // event:/UI/Pause_Open
    [SerializeField] private EventReference pauseClose;   // event:/UI/Pause_Close
    [SerializeField] private EventReference journalOpen;  // event:/UI/Journal_Open
    [SerializeField] private EventReference journalClose; // event:/UI/Journal_Close
    [SerializeField] private EventReference journalPage;  // event:/UI/Journal_Page
    [SerializeField] private EventReference toggleOn;     // event:/UI/Toggle_On
    [SerializeField] private EventReference toggleOff;    // event:/UI/Toggle_Off
    [SerializeField] private EventReference rebindListen; // event:/UI/Rebind_Listen
    [SerializeField] private EventReference rebindSet;    // event:/UI/Rebind_Set

    [Header("Pause")]
    [Tooltip("Snapshot held while the game is paused, e.g. snapshot:/Pause. Optional.")]
    [SerializeField] private EventReference pauseSnapshot;

    [Header("Behaviour")]
    [Tooltip("Play Hover when the EventSystem selection changes (keyboard / gamepad navigation).")]
    [SerializeField] private bool hoverOnSelectionChange = true;

    [Tooltip("Add Click to every Button, SliderTick to every Slider and Toggle_On/Off to every " +
             "Toggle in each loaded scene.")]
    [SerializeField] private bool autoHookButtons = true;

    [Tooltip("Minimum seconds between two slider ticks (unscaled).")]
    [SerializeField] private float sliderTickInterval = 0.05f;

    [SerializeField] private bool dontDestroyOnLoad = true;

    private static UIAudio instance;
    public static UIAudio Instance => instance != null ? instance : null;

    private GameObject lastSelected;
    private float lastTickTime = -1f;
    private float suppressHoverUntil;
    private EventInstance pauseInstance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;

        if (dontDestroyOnLoad)
        {
            if (transform.parent != null) transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
        }

        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        if (instance == this && autoHookButtons) HookAll();
    }

    private void OnDestroy()
    {
        if (instance != this) return;

        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        StopPauseSnapshot();
        instance = null;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (autoHookButtons) HookAll();
        SuppressHover(0.2f);
    }

    private void Update()
    {
        if (!hoverOnSelectionChange || EventSystem.current == null) return;

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected == lastSelected) return;

        GameObject previous = lastSelected;
        lastSelected = selected;

        // Panels select their first control when they open; that isn't a hover.
        if (selected == null || previous == null || Time.unscaledTime < suppressHoverUntil) return;
        if (selected.GetComponent<UISelectableSounds>() != null) return;   // it plays its own

        PlayInternal(UISound.Hover);
    }

    // ---- public API ---------------------------------------------------------

    /// <summary>Plays a UI sound. Silent if there is no UIAudio or the slot is empty.</summary>
    public static void Play(UISound sound)
    {
        if (Instance != null) Instance.PlayInternal(sound);
    }

    /// <summary>Slider ticks are rate limited so dragging doesn't machine-gun.</summary>
    public static void PlaySliderTick()
    {
        UIAudio ui = Instance;
        if (ui == null) return;

        if (Time.unscaledTime - ui.lastTickTime < ui.sliderTickInterval) return;
        ui.lastTickTime = Time.unscaledTime;
        ui.PlayInternal(UISound.SliderTick);
    }

    /// <summary>Holds snapshot:/Pause while paused.</summary>
    public static void SetPaused(bool paused)
    {
        UIAudio ui = Instance;
        if (ui == null) return;

        if (paused) ui.StartPauseSnapshot();
        else ui.StopPauseSnapshot();
    }

    /// <summary>Stops automatic Hover for a moment (call when a panel opens and auto-selects).</summary>
    public static void SuppressHover(float seconds)
    {
        UIAudio ui = Instance;
        if (ui != null) ui.suppressHoverUntil = Time.unscaledTime + seconds;
    }

    // ---- internals ----------------------------------------------------------

    private void PlayInternal(UISound sound)
    {
        GameAudio.PlayOneShot2D(Get(sound));
    }

    private EventReference Get(UISound sound)
    {
        switch (sound)
        {
            case UISound.Hover:      return hover;
            case UISound.Click:      return click;
            case UISound.Back:       return back;
            case UISound.SliderTick: return sliderTick;
            case UISound.PanelOpen:  return panelOpen;
            case UISound.PanelClose: return panelClose;
            case UISound.Confirm:    return confirm;
            case UISound.Error:      return error;
            case UISound.TabSwitch:    return tabSwitch.IsNull ? click : tabSwitch;
            case UISound.Popup:        return popup;
            case UISound.GameOver:     return gameOver.IsNull ? panelOpen : gameOver;
            case UISound.PauseOpen:    return pauseOpen.IsNull ? panelOpen : pauseOpen;
            case UISound.PauseClose:   return pauseClose.IsNull ? panelClose : pauseClose;
            case UISound.JournalOpen:  return journalOpen.IsNull ? panelOpen : journalOpen;
            case UISound.JournalClose: return journalClose.IsNull ? panelClose : journalClose;
            case UISound.JournalPage:  return journalPage.IsNull ? tabSwitch : journalPage;
            case UISound.ToggleOn:     return toggleOn.IsNull ? click : toggleOn;
            case UISound.ToggleOff:    return toggleOff.IsNull ? click : toggleOff;
            case UISound.RebindListen: return rebindListen;
            case UISound.RebindSet:    return rebindSet.IsNull ? confirm : rebindSet;
            default:                   return default;
        }
    }

    private void StartPauseSnapshot()
    {
        if (pauseSnapshot.IsNull || pauseInstance.isValid()) return;
        pauseInstance = RuntimeManager.CreateInstance(pauseSnapshot);
        pauseInstance.start();
    }

    private void StopPauseSnapshot()
    {
        if (!pauseInstance.isValid()) return;

        if (RuntimeManager.IsInitialized)
        {
            pauseInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            pauseInstance.release();
        }

        pauseInstance.clearHandle();
    }

    private static void HookAll()
    {
        foreach (UnityEngine.UI.Button button in
                 FindObjectsByType<UnityEngine.UI.Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (button.GetComponent<UISelectableSounds>() != null) continue;
            button.onClick.RemoveListener(PlayClickStatic);
            button.onClick.AddListener(PlayClickStatic);
        }

        foreach (UnityEngine.UI.Slider slider in
                 FindObjectsByType<UnityEngine.UI.Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (slider.GetComponent<UISelectableSounds>() != null) continue;
            slider.onValueChanged.RemoveListener(PlayTickStatic);
            slider.onValueChanged.AddListener(PlayTickStatic);
        }

        foreach (UnityEngine.UI.Toggle toggle in
                 FindObjectsByType<UnityEngine.UI.Toggle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (toggle.GetComponent<UISelectableSounds>() != null) continue;
            toggle.onValueChanged.RemoveListener(PlayToggleStatic);
            toggle.onValueChanged.AddListener(PlayToggleStatic);
        }
    }

    // Static method groups so RemoveListener matches and re-hooking never doubles up.
    private static void PlayClickStatic() => Play(UISound.Click);
    private static void PlayTickStatic(float _) => PlaySliderTick();
    private static void PlayToggleStatic(bool on) => Play(on ? UISound.ToggleOn : UISound.ToggleOff);
}
