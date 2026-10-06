using FMOD.Studio;
using FMODUnity;
using UnityEngine;

// One looping music event for the whole game, with an Era parameter that FMOD
// uses to cross-fade between the Past, Present and Future arrangements.
//
// Lives on Core so it survives section loads. Sections change the track by
// calling SetTrack, which sets a second labelled parameter rather than starting
// a new event - so a swap between eras never restarts the music.
//
// FMOD contract (see FMOD_CONTRACT.md):
//   event:/Music/EraMusic
//   "Era"   - CONTINUOUS local parameter, 0..2 (0 Past, 1 Present, 2 Future).
//             This script glides it as a float, so it must NOT be a labelled
//             parameter (a labelled one would snap at the half-way point).
//             Three stacked era layers on crossfade volume curves.
//   "Track" - LABELLED local parameter, one label per section (only "Mansion"
//             is known so far).
//
// CHANGED (FMOD pass), all additive - existing callers and serialized fields
// are untouched:
//   * A duplicate destroyed in Awake no longer goes on to create its own music
//     instance in Start (Destroy is deferred to end of frame, Start still ran).
//   * If TimeSingleton does not exist yet at Start (e.g. it lives in a scene
//     that loads after Core) the observer is attached as soon as it appears.
//     The retired MusicManager handled this; EraMusic did not.
//   * playOnStart / Play() / Stop(), so a main menu can keep game music silent
//     until the game actually starts. playOnStart defaults to true = old behaviour.
public class EraMusic : MonoBehaviour, ITimeSwitchObserver
{
    [Header("Event")]
    [Tooltip("Looping event with a CONTINUOUS 'Era' parameter (0 Past, 1 Present, 2 Future) " +
             "and a labelled 'Track' parameter naming each section's music.")]
    [SerializeField] private EventReference musicEvent;

    [Tooltip("Track label started with. Must match a label on the Track parameter.")]
    [SerializeField] private string startingTrack = "Mansion";

    [Tooltip("Seconds the Era parameter takes to reach its new value. 0 snaps.")]
    [SerializeField] private float eraBlendSeconds = 1.5f;

    [Tooltip("Start the music in Start(). Turn off if a menu or bootstrap script " +
             "calls Play() when gameplay begins.")]
    [SerializeField] private bool playOnStart = true;

    private static EraMusic instance;

    // Backed by a field with an explicit null test, so the getter hands back a
    // real null once the object is destroyed. An auto-property returns a
    // reference that is only Unity fake-null, which x?.Method() calls straight
    // through - and that throws MissingReferenceException. It also clears the
    // stale reference left behind by a previous play session when Reload Domain
    // is switched off.
    public static EraMusic Instance
    {
        get => instance != null ? instance : null;
        private set => instance = value;
    }

    private EventInstance music;
    private bool isPlaying;
    private bool observing;

    private string currentTrack;
    private float eraValue;      // 0 Past, 1 Present, 2 Future - moved gradually
    private float targetEraValue;

    public bool IsPlaying => isPlaying;


    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }


    private void Start()
    {
        if (Instance != this) return;   // duplicate, already being destroyed

        if (musicEvent.IsNull)
        {
            Debug.LogWarning($"{name}: no music event assigned.", this);
            enabled = false;
            return;
        }

        currentTrack = startingTrack;
        TryObserveTime();

        if (playOnStart) Play();
    }


    private void OnDestroy()
    {
        if (observing && TimeSingleton.Instance != null)
            TimeSingleton.Instance.RemoveObserver(this);
        observing = false;

        Stop(true);

        if (Instance == this) Instance = null;
    }


    // Starts the music if it is not already playing. Safe to call repeatedly.
    public void Play()
    {
        if (isPlaying || musicEvent.IsNull) return;

        if (string.IsNullOrEmpty(currentTrack)) currentTrack = startingTrack;

        music = RuntimeManager.CreateInstance(musicEvent);
        isPlaying = true;

        if (!string.IsNullOrWhiteSpace(currentTrack))
            music.setParameterByNameWithLabel("Track", currentTrack);

        if (TimeSingleton.Instance != null)
            eraValue = (int)TimeSingleton.Instance.currentGameState;

        targetEraValue = eraValue;
        music.setParameterByName("Era", eraValue);

        music.start();
    }


    // Stops and releases the music. allowFadeout lets the event's AHDSR release
    // (authored in FMOD Studio) play out.
    public void Stop(bool allowFadeout = true)
    {
        if (!isPlaying) return;

        music.stop(allowFadeout ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT
                                : FMOD.Studio.STOP_MODE.IMMEDIATE);
        music.release();
        music.clearHandle();
        isPlaying = false;
    }


    // Era changes glide rather than snap, so a swap mid-phrase doesn't jump.
    private void Update()
    {
        if (!observing) TryObserveTime();

        if (!isPlaying || Mathf.Approximately(eraValue, targetEraValue)) return;

        eraValue = eraBlendSeconds > 0f
            ? Mathf.MoveTowards(eraValue, targetEraValue, Time.unscaledDeltaTime / eraBlendSeconds)
            : targetEraValue;

        music.setParameterByName("Era", eraValue);
    }


    private void TryObserveTime()
    {
        if (observing || TimeSingleton.Instance == null) return;

        TimeSingleton.Instance.AddObserver(this);
        observing = true;

        // Late attach: jump straight to the current era rather than gliding
        // from Past.
        eraValue = targetEraValue = (int)TimeSingleton.Instance.currentGameState;
        if (isPlaying) music.setParameterByName("Era", eraValue);
    }


    public void OnNotify(TimeSingleton.GameState currentTime)
    {
        targetEraValue = (int)currentTime;
    }


    // Called when a section loads. Does nothing if that track is already playing,
    // so re-entering a room doesn't restart it. Remembered while stopped, so the
    // next Play() starts on the right track.
    public void SetTrack(string track)
    {
        if (string.IsNullOrWhiteSpace(track) || track == currentTrack) return;

        currentTrack = track;
        if (isPlaying) music.setParameterByNameWithLabel("Track", track);
    }


    // Silence without stopping, for cutscenes or menus. Implemented as a pause,
    // so unmuting resumes from the point where it was paused (comment corrected:
    // it does not jump ahead to where the music "would have been").
    public void SetMuted(bool muted)
    {
        if (isPlaying) music.setPaused(muted);
    }
}
