using System.Collections;
using UnityEngine;

public class MusicManager : MonoBehaviour, ITimeSwitchObserver
{
    [Header("Music Sources")]
    [SerializeField] private AudioSource presentMusic;
    [SerializeField] private AudioSource pastMusic;

    [Header("Crossfade")]
    [SerializeField, Min(0f)] private float fadeDuration = 1f;

    private Coroutine timeSystemCoroutine;
    private Coroutine crossfadeCoroutine;
    private TimeSingleton observedTimeSystem;
    private bool musicStarted;

    private void Awake()
    {
        ConfigureSource(presentMusic);
        ConfigureSource(pastMusic);
    }

    private void OnEnable()
    {
        timeSystemCoroutine = StartCoroutine(WaitForTimeSystem());
    }

    private void Start()
    {
        StartMusic();
    }

    private void OnDisable()
    {
        if (timeSystemCoroutine != null)
        {
            StopCoroutine(timeSystemCoroutine);
            timeSystemCoroutine = null;
        }

        if (crossfadeCoroutine != null)
        {
            StopCoroutine(crossfadeCoroutine);
            crossfadeCoroutine = null;
        }

        DetachFromTimeSystem();
    }

    private void ConfigureSource(AudioSource source)
    {
        if (source == null)
            return;

        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
    }

    private IEnumerator WaitForTimeSystem()
    {
        while (TimeSingleton.Instance == null)
            yield return null;

        AttachToTimeSystem();

        if (musicStarted)
            OnNotify(TimeSingleton.Instance.currentGameState);

        timeSystemCoroutine = null;
    }

    private void AttachToTimeSystem()
    {
        if (TimeSingleton.Instance == null)
            return;

        if (observedTimeSystem == TimeSingleton.Instance)
            return;

        DetachFromTimeSystem();

        observedTimeSystem = TimeSingleton.Instance;
        observedTimeSystem.AddObserver(this);
    }

    private void DetachFromTimeSystem()
    {
        if (observedTimeSystem == null)
            return;

        observedTimeSystem.RemoveObserver(this);
        observedTimeSystem = null;
    }

    public void StartMusic()
    {
        if (musicStarted)
            return;

        if (presentMusic == null || pastMusic == null)
        {
            Debug.LogWarning(
                "MusicManager requires both music AudioSources.",
                this
            );

            return;
        }

        if (presentMusic.clip == null || pastMusic.clip == null)
        {
            Debug.LogWarning(
                "Both music AudioSources require an AudioClip.",
                this
            );

            return;
        }

        musicStarted = true;
        presentMusic.Play();
        pastMusic.Play();

        if (TimeSingleton.Instance != null)
        {
            AttachToTimeSystem();
            OnNotify(TimeSingleton.Instance.currentGameState);
        }
        else
        {
            presentMusic.volume = 1f;
            pastMusic.volume = 0f;
        }
    }

    public void OnNotify(TimeSingleton.GameState currentTime)
    {
        if (!musicStarted)
            return;

        bool isPresent =
            currentTime == TimeSingleton.GameState.Present;

        AudioSource targetSource =
            isPresent ? presentMusic : pastMusic;

        AudioSource otherSource =
            isPresent ? pastMusic : presentMusic;

        if (crossfadeCoroutine != null)
            StopCoroutine(crossfadeCoroutine);

        crossfadeCoroutine = StartCoroutine(
            Crossfade(targetSource, otherSource)
        );
    }

    private IEnumerator Crossfade(
        AudioSource targetSource,
        AudioSource otherSource
    )
    {
        float targetStartVolume = targetSource.volume;
        float otherStartVolume = otherSource.volume;

        if (fadeDuration <= 0f)
        {
            targetSource.volume = 1f;
            otherSource.volume = 0f;
            crossfadeCoroutine = null;
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(
                elapsed / fadeDuration
            );

            targetSource.volume = Mathf.Lerp(
                targetStartVolume,
                1f,
                progress
            );

            otherSource.volume = Mathf.Lerp(
                otherStartVolume,
                0f,
                progress
            );

            yield return null;
        }

        targetSource.volume = 1f;
        otherSource.volume = 0f;
        crossfadeCoroutine = null;
    }
}