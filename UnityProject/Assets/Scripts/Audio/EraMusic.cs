using FMOD.Studio;
using FMODUnity;
using UnityEngine;

// One looping music event for the whole game, with an Era parameter that FMOD
// uses to cross-fade between the Past, Present and Future arrangements.
//
// Lives on Core so it survives section loads. Sections change the track by
// calling SetTrack, which sets a second labelled parameter rather than starting
// a new event - so a swap between eras never restarts the music.
public class EraMusic : MonoBehaviour, ITimeSwitchObserver
{
    [Header("Event")]
    [Tooltip("Looping event with a labelled 'Era' parameter (Past, Present, Future) " +
             "and a labelled 'Track' parameter naming each section's music.")]
    [SerializeField] private EventReference musicEvent;

    [Tooltip("Track label started with. Must match a label on the Track parameter.")]
    [SerializeField] private string startingTrack = "Mansion";

    [Tooltip("Seconds the Era parameter takes to reach its new value. 0 snaps.")]
    [SerializeField] private float eraBlendSeconds = 1.5f;

    public static EraMusic Instance { get; private set; }

    private EventInstance music;
    private bool isPlaying;

    private string currentTrack;
    private float eraValue;      // 0 Past, 1 Present, 2 Future - moved gradually
    private float targetEraValue;


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
        if (musicEvent.IsNull)
        {
            Debug.LogWarning($"{name}: no music event assigned.", this);
            enabled = false;
            return;
        }

        music = RuntimeManager.CreateInstance(musicEvent);
        isPlaying = true;

        currentTrack = startingTrack;
        music.setParameterByNameWithLabel("Track", currentTrack);

        if (TimeSingleton.Instance != null)
        {
            eraValue = (int)TimeSingleton.Instance.currentGameState;
            TimeSingleton.Instance.AddObserver(this);
        }

        targetEraValue = eraValue;
        music.setParameterByName("Era", eraValue);

        music.start();
    }


    private void OnDestroy()
    {
        if (TimeSingleton.Instance != null)
            TimeSingleton.Instance.RemoveObserver(this);

        if (isPlaying)
        {
            music.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            music.release();
        }

        if (Instance == this) Instance = null;
    }


    // Era changes glide rather than snap, so a swap mid-phrase doesn't jump.
    private void Update()
    {
        if (!isPlaying || Mathf.Approximately(eraValue, targetEraValue)) return;

        eraValue = eraBlendSeconds > 0f
            ? Mathf.MoveTowards(eraValue, targetEraValue, Time.unscaledDeltaTime / eraBlendSeconds)
            : targetEraValue;

        music.setParameterByName("Era", eraValue);
    }


    public void OnNotify(TimeSingleton.GameState currentTime)
    {
        targetEraValue = (int)currentTime;
    }


    // Called when a section loads. Does nothing if that track is already playing,
    // so re-entering a room doesn't restart it.
    public void SetTrack(string track)
    {
        if (!isPlaying || string.IsNullOrWhiteSpace(track) || track == currentTrack) return;

        currentTrack = track;
        music.setParameterByNameWithLabel("Track", track);
    }


    // Silence without stopping, for cutscenes or menus. FMOD keeps the playhead,
    // so unmuting picks up where the music would have been.
    public void SetMuted(bool muted)
    {
        if (isPlaying) music.setPaused(muted);
    }
}