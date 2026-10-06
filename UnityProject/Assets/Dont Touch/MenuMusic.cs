using System.Collections;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

// ADDED (FMOD pass). Main menu music through FMOD.
//
// Put this on its OWN GameObject in the main menu scene (it may be moved to
// DontDestroyOnLoad while fading out - see survivesSceneLoad).
//
//   * Plays event:/Music/MainMenu (2D, looping, routed to bus:/Music so the
//     Music volume slider / vca:/Music covers it).
//   * Script-driven fade in / fade out using EventInstance.setVolume on
//     unscaled time, so it still works with Time.timeScale = 0.
//   * Optionally pauses EraMusic and EraAmbience while the menu music plays
//     (useful if the persistent Core scene is already loaded under the menu) and
//     un-pauses them when the menu music stops.
//   * Always stopped and released: Stop() fades then releases; OnDestroy stops
//     with ALLOWFADEOUT and releases, so no instance can leak.
//
// Typical "Play" button: MenuMusic.Instance.Stop(); then load the game scene.
// With survivesSceneLoad on, the fade finishes even though the menu scene is
// being unloaded. If EraMusic.playOnStart is off, call EraMusic.Instance.Play()
// once gameplay starts.
public class MenuMusic : MonoBehaviour
{
    [Header("Event")]
    [Tooltip("e.g. event:/Music/MainMenu")]
    [SerializeField] private EventReference menuMusicEvent;

    [SerializeField] private bool playOnStart = true;

    [Header("Fades (unscaled seconds)")]
    [SerializeField, Min(0f)] private float fadeInSeconds = 1f;
    [SerializeField, Min(0f)] private float fadeOutSeconds = 1.5f;

    [Header("Behaviour")]
    [Tooltip("Pause EraMusic and EraAmbience while the menu music plays.")]
    [SerializeField] private bool pauseGameAudioWhilePlaying = true;

    [Tooltip("When Stop() fades out, detach this GameObject into DontDestroyOnLoad " +
             "so the fade completes across a scene load, then destroy it.")]
    [SerializeField] private bool survivesSceneLoad = true;

    private static MenuMusic instance;

    public static MenuMusic Instance
    {
        get => instance != null ? instance : null;
        private set => instance = value;
    }

    private EventInstance music;
    private bool isPlaying;
    private bool pausedGameAudio;
    private Coroutine fadeRoutine;
    private float volume;

    public bool IsPlaying => isPlaying;


    private void Awake()
    {
        // A second menu (e.g. returning to the menu while a previous MenuMusic
        // is still fading out) takes over; the old one finishes its fade.
        Instance = this;
    }


    private void Start()
    {
        if (playOnStart) Play();
    }


    private void OnDestroy()
    {
        StopInternal(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        if (Instance == this) Instance = null;
    }


    public void Play()
    {
        if (menuMusicEvent.IsNull)
        {
            Debug.LogWarning($"{name}: no menu music event assigned.", this);
            return;
        }

        if (fadeRoutine != null) StopCoroutine(fadeRoutine);

        if (!isPlaying)
        {
            music = RuntimeManager.CreateInstance(menuMusicEvent);
            volume = fadeInSeconds > 0f ? 0f : 1f;
            music.setVolume(volume);
            music.start();
            isPlaying = true;
        }

        PauseGameAudio(true);
        fadeRoutine = fadeInSeconds > 0f ? StartCoroutine(FadeTo(1f, fadeInSeconds, false)) : null;
    }


    // Fade out, then stop and release.
    public void Stop()
    {
        if (!isPlaying) return;

        if (fadeOutSeconds <= 0f || !isActiveAndEnabled)
        {
            StopInternal(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            return;
        }

        if (survivesSceneLoad)
        {
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeTo(0f, fadeOutSeconds, true));
    }


    public void StopImmediate() => StopInternal(FMOD.Studio.STOP_MODE.IMMEDIATE);


    // Convenience for menu buttons / loaders that don't hold a reference.
    public static void StopIfPlaying()
    {
        if (Instance != null) Instance.Stop();
    }


    private IEnumerator FadeTo(float target, float seconds, bool stopWhenDone)
    {
        float start = volume;
        float t = 0f;

        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            volume = Mathf.Lerp(start, target, Mathf.Clamp01(t / seconds));
            if (isPlaying) music.setVolume(volume);
            yield return null;
        }

        volume = target;
        if (isPlaying) music.setVolume(volume);
        fadeRoutine = null;

        if (!stopWhenDone) yield break;

        StopInternal(FMOD.Studio.STOP_MODE.IMMEDIATE);

        // We were only kept alive to finish the fade.
        if (survivesSceneLoad && gameObject.scene.name == "DontDestroyOnLoad")
            Destroy(gameObject);
    }


    private void StopInternal(FMOD.Studio.STOP_MODE mode)
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        if (isPlaying)
        {
            music.stop(mode);
            music.release();
            music.clearHandle();
            isPlaying = false;
        }

        PauseGameAudio(false);
    }


    private void PauseGameAudio(bool pause)
    {
        if (!pauseGameAudioWhilePlaying) return;
        if (pause == pausedGameAudio) return;

        pausedGameAudio = pause;

        if (EraMusic.Instance != null) EraMusic.Instance.SetMuted(pause);
        if (EraAmbience.Instance != null) EraAmbience.Instance.SetMuted(pause);
    }
}
