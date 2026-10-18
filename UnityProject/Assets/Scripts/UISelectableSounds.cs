using UnityEngine;
using UnityEngine.EventSystems;

// ADDED (round 2). Per-control UI sounds, for controls that need something other
// than the defaults UIAudio hooks automatically (e.g. a Back or Confirm button),
// and for mouse hover. Add to a Button / Slider / Toggle prefab.
public class UISelectableSounds : MonoBehaviour, IPointerEnterHandler, ISelectHandler, ISubmitHandler, IPointerClickHandler
{
    [SerializeField] private bool hoverOnPointerEnter = true;
    [SerializeField] private bool hoverOnSelect = true;

    [Tooltip("Sound for click / submit. Back for close buttons, Confirm for apply/save, etc.")]
    [SerializeField] private UISound pressSound = UISound.Click;
    [SerializeField] private bool playPressSound = true;

    [Tooltip("Tick while a Slider on this object changes.")]
    [SerializeField] private bool sliderTicks = true;

    [Tooltip("Toggle_On / Toggle_Off when a Toggle on this object changes (replaces the press sound).")]
    [SerializeField] private bool toggleSounds = true;

    private UnityEngine.UI.Selectable selectable;
    private UnityEngine.UI.Slider slider;
    private UnityEngine.UI.Toggle toggle;

    private void Awake()
    {
        selectable = GetComponent<UnityEngine.UI.Selectable>();
        slider = GetComponent<UnityEngine.UI.Slider>();
        toggle = GetComponent<UnityEngine.UI.Toggle>();
    }

    private void OnEnable()
    {
        if (slider != null && sliderTicks) slider.onValueChanged.AddListener(OnSliderChanged);
        if (toggle != null && toggleSounds) toggle.onValueChanged.AddListener(OnToggleChanged);
    }

    private void OnDisable()
    {
        if (slider != null) slider.onValueChanged.RemoveListener(OnSliderChanged);
        if (toggle != null) toggle.onValueChanged.RemoveListener(OnToggleChanged);
    }

    private bool PressHandledByToggle => toggle != null && toggleSounds;

    private bool Interactable => selectable == null || selectable.IsInteractable();

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (hoverOnPointerEnter && Interactable) UIAudio.Play(UISound.Hover);
    }

    public void OnSelect(BaseEventData eventData)
    {
        // Pointer hover usually selects too; only play for navigation selects.
        if (hoverOnSelect && Interactable && !(eventData is PointerEventData)) UIAudio.Play(UISound.Hover);
    }

    public void OnSubmit(BaseEventData eventData)
    {
        if (PressHandledByToggle && Interactable) return;
        if (playPressSound) UIAudio.Play(Interactable ? pressSound : UISound.Error);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (PressHandledByToggle && Interactable) return;
        if (playPressSound) UIAudio.Play(Interactable ? pressSound : UISound.Error);
    }

    private void OnSliderChanged(float _) => UIAudio.PlaySliderTick();
    private void OnToggleChanged(bool on) => UIAudio.Play(on ? UISound.ToggleOn : UISound.ToggleOff);
}
