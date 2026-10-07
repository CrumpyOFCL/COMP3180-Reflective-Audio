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
//   "Track" - LABELLED local parameter, one label per area: Foyer, Library,
//             Greenhouse, BoilerRoom, AlchemyRoom, FinalRoom (in that order).
//   "Solved" - CONTINUOUS local parameter, 0..1. Drives the puzzle-solved layer.
//             FMOD seek speed stays 0; this script owns the curve. The layer
//             volume in FMOD is never above 0 dB (not set from here).
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
//
// CHANGED (round 2):
//   * SetSolved(float) sets the "Solved" parameter directly. LiftSolved() runs
//     the automatic rise -> hold -> release envelope (unscaled time, so it keeps
//     moving while the game is paused or slowed).
//   * Every puzzle in the project reports success through
//     ObjectiveManager.CompleteObjective(objectiveOnSolved), which fires
//     OnObjectiveCompleted once per new id. EraMusic listens to that event and
//     calls LiftSolved(). Restoring a save does NOT fire it (RestoreState is
//     silent), so loading never lifts the music. Optional allow-list below.
//   * startingTrack default is now "Foyer". The old placeholder "Mansion" is no
//     longer a Track label, so a scene still serialised with "Mansion" is mapped
//     to "Foyer" with a warning.
//
// CHANGED (area solved), all additive - existing public methods and serialized
// fields are untouched:
//   * PlaySolvedMoment / FadeOutSolved / ClearSolved drive the same envelope
//     with caller-supplied times. The music event is never restarted; only the
//     Solved parameter moves, so the layer stays sample-synced with the stems.
//   * The long PlaySolvedMoment can keep the hold going until the next phrase
//     on the event timeline (TryGetTimelineSeconds). Phrase lock off, or a
//     timeline read that fails, uses the flat hold instead. The whole aligned
//     moment is capped (default 30 s).
//   * SetAreaSolvedControllerActive(true) suppresses HandleObjectiveCompleted's
//     LiftSolved. AreaSolvedMusic sets this while it is the active area, so the
//     old per-objective blip does not fight the area moment. Direct LiftSolved()
//     calls (MusicSolvedCallable) still work.
public class EraMusic : MonoBehaviour, ITimeSwitchObserver
{
    public enum SolvedPhase { Idle, Rise, Hold, Release }

    [Header("Event")]
    [Tooltip("Looping event with a CONTINUOUS 'Era' parameter (0 Past, 1 Present, 2 Future) " +
             "and a labelled 'Track' parameter naming each section's music.")]
    [SerializeField] private EventReference musicEvent;

    [Tooltip("Track label started with. Must match a label on the Track parameter: " +
             "Foyer, Library, Greenhouse, BoilerRoom, AlchemyRoom, FinalRoom.")]
    [SerializeField] private string startingTrack = "Foyer";

    [Tooltip("Seconds the Era parameter takes to reach its new value. 0 snaps.")]
    [SerializeField] private float eraBlendSeconds = 1.5f;

    [Tooltip("Start the music in Start(). Turn off if a menu or bootstrap script " +
             "calls Play() when gameplay begins.")]
    [SerializeField] private bool playOnStart = true;

    [Header("Puzzle Solved")]
    [Tooltip("Continuous 0..1 parameter on the music event that brings in the solved layer.")]
    [SerializeField] private string solvedParameter = "Solved";

    [Tooltip("Lift Solved whenever ObjectiveManager reports a newly completed objective.")]
    [SerializeField] private bool liftSolvedOnObjective = true;

    [Tooltip("Optional allow-list. Empty = every objective lifts the music. Fill it to " +
             "limit the lift to puzzle objectives (e.g. not item pickups).")]
    [SerializeField] private System.Collections.Generic.List<string> solvedObjectiveIds =
        new System.Collections.Generic.List<string>();

    [Tooltip("Seconds to rise from the current value to 1.")]
    [Min(0f)] [SerializeField] private float solvedRiseSeconds = 1f;

    [Tooltip("Seconds Solved stays at 1.")]
    [Min(0f)] [SerializeField] private float solvedHoldSeconds = 8f;

    [Tooltip("Seconds to ease from 1 back to 0.")]
    [Min(0f)] [SerializeField] private float solvedReleaseSeconds = 4f;

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

    private SolvedPhase solvedPhase = SolvedPhase.Idle;
    private float solvedValue;
    private float solvedHoldTimer;
    private ObjectiveManager subscribedObjectives;

    // One-shot moment timings. When false, the envelope uses the serialized
    // rise / hold / release fields (LiftSolved). Never written back to those
    // fields, so the Inspector values stay put.
    private bool useCustomSolvedTiming;
    private float customRiseSeconds;
    private float customHoldSeconds;
    private float customReleaseSeconds;

    // Phrase-locked fade-out. Only used when alignFadeOutToPhrase is set by
    // the long PlaySolvedMoment. LiftSolved leaves this off and keeps the
    // serialized hold timer.
    private bool alignFadeOutToPhrase;
    private float phraseLengthSeconds;
    private float phraseTimelineOffset;
    private float phraseMinHoldSeconds;
    private float momentCapSeconds = 30f;
    private float momentElapsed;
    private float holdElapsed;
    private float previousPhraseMod = -1f;

    // While an AreaSolvedMusic is the active area, a completed objective must
    // not also run LiftSolved. That short blip would fight the area moment.
    private bool areaSolvedControllerActive;

    public bool IsPlaying => isPlaying;
    public string CurrentTrack => currentTrack;
    public float CurrentSolvedValue => solvedValue;
    public SolvedPhase CurrentSolvedPhase => solvedPhase;

    private float ActiveRiseSeconds => useCustomSolvedTiming ? customRiseSeconds : solvedRiseSeconds;
    private float ActiveReleaseSeconds => useCustomSolvedTiming ? customReleaseSeconds : solvedReleaseSeconds;


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

        if (startingTrack == "Mansion")
        {
            // Pre-round-2 placeholder; the Track list is now one label per area.
            Debug.LogWarning($"{name}: startingTrack 'Mansion' is no longer a Track label - using 'Foyer'. " +
                             "Update the Inspector value.", this);
            startingTrack = "Foyer";
        }

        // AreaAudio.Apply (and SetTrack) may already have run this frame if their
        // Start landed before this one. Keep that label. Falling back only when
        // nothing has chosen a track preserves the old default of startingTrack.
        if (string.IsNullOrEmpty(currentTrack))
            currentTrack = startingTrack;
        TryObserveTime();
        TrySubscribeObjectives();

        if (playOnStart) Play();
    }


    private void OnDestroy()
    {
        if (observing && TimeSingleton.Instance != null)
            TimeSingleton.Instance.RemoveObserver(this);
        observing = false;

        UnsubscribeObjectives();
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

        if (!string.IsNullOrEmpty(solvedParameter))
            music.setParameterByName(solvedParameter, solvedValue);

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
        if (liftSolvedOnObjective && subscribedObjectives != ObjectiveManager.Instance) TrySubscribeObjectives();

        UpdateSolvedEnvelope();

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


    // ---- Puzzle solved -------------------------------------------------------

    // Sets the Solved parameter directly (0..1) and cancels any running
    // lift envelope, for scripts that want to drive it themselves.
    public void SetSolved(float value)
    {
        useCustomSolvedTiming = false;
        alignFadeOutToPhrase = false;
        previousPhraseMod = -1f;
        solvedPhase = SolvedPhase.Idle;
        ApplySolved(Mathf.Clamp01(value));
    }


    // Rise to 1, hold, then ease back to 0. Calling it again while it is running
    // restarts the hold, so back-to-back solves keep the layer up.
    // Uses the serialized rise / hold / release times, not a PlaySolvedMoment.
    public void LiftSolved()
    {
        useCustomSolvedTiming = false;
        alignFadeOutToPhrase = false;
        previousPhraseMod = -1f;
        solvedPhase = solvedRiseSeconds > 0f ? SolvedPhase.Rise : SolvedPhase.Hold;
        solvedHoldTimer = solvedHoldSeconds;
        if (solvedPhase == SolvedPhase.Hold) ApplySolved(1f);
    }


    // Fade Solved 0 -> 1, hold, then 1 -> 0, on unscaled time. Flat hold.
    // Does not restart the music event. Reuses the LiftSolved envelope.
    public void PlaySolvedMoment(float fadeInSeconds, float holdSeconds, float fadeOutSeconds)
    {
        PlaySolvedMoment(fadeInSeconds, holdSeconds, fadeOutSeconds, false, 0f, 0f, holdSeconds, 30f);
    }


    // Same envelope. When alignToPhrase is set, the hold lasts at least
    // minHoldSeconds and then continues until the next phrase boundary on the
    // event timeline. If the timeline cannot be read, flatHoldSeconds is used.
    // maxMomentSeconds caps fade-in + hold + fade-out (the fade-out starts
    // early enough to finish inside the cap).
    public void PlaySolvedMoment(
        float fadeInSeconds,
        float flatHoldSeconds,
        float fadeOutSeconds,
        bool alignToPhrase,
        float phraseSeconds,
        float timelineOffsetSeconds,
        float minHoldSeconds,
        float maxMomentSeconds)
    {
        customRiseSeconds = Mathf.Max(0f, fadeInSeconds);
        customHoldSeconds = Mathf.Max(0f, flatHoldSeconds);
        customReleaseSeconds = Mathf.Max(0f, fadeOutSeconds);
        useCustomSolvedTiming = true;

        alignFadeOutToPhrase = alignToPhrase && phraseSeconds > 0.05f;
        phraseLengthSeconds = Mathf.Max(0f, phraseSeconds);
        phraseTimelineOffset = timelineOffsetSeconds;
        phraseMinHoldSeconds = Mathf.Max(0f, minHoldSeconds);
        momentCapSeconds = maxMomentSeconds > 0f ? maxMomentSeconds : 30f;
        momentElapsed = 0f;
        holdElapsed = 0f;
        previousPhraseMod = -1f;

        solvedPhase = customRiseSeconds > 0f ? SolvedPhase.Rise : SolvedPhase.Hold;
        solvedHoldTimer = customHoldSeconds;
        if (solvedPhase == SolvedPhase.Hold) ApplySolved(1f);
    }


    // Ease whatever the current Solved value is back to 0. Used when the player
    // leaves an area mid-moment so the layer does not carry into the next track.
    public void FadeOutSolved(float fadeOutSeconds)
    {
        if (solvedPhase == SolvedPhase.Idle && solvedValue <= 0.0001f) return;

        customReleaseSeconds = Mathf.Max(0f, fadeOutSeconds);
        useCustomSolvedTiming = true;
        alignFadeOutToPhrase = false;

        if (customReleaseSeconds <= 0f || solvedValue <= 0.0001f)
        {
            solvedPhase = SolvedPhase.Idle;
            ApplySolved(0f);
            return;
        }

        solvedPhase = SolvedPhase.Release;
    }


    // Snap Solved to 0 and cancel any rise / hold / release in progress.
    public void ClearSolved()
    {
        SetSolved(0f);
    }


    // AreaSolvedMusic calls this with true while its area is the one EraMusic
    // is playing. The per-objective LiftSolved in HandleObjectiveCompleted is
    // skipped for that whole time. Pass false when the area is left and no
    // other AreaSolvedMusic has taken over.
    public void SetAreaSolvedControllerActive(bool active)
    {
        areaSolvedControllerActive = active;
    }


    // Event timeline cursor, in seconds. FMOD reports milliseconds.
    // False when the event is not playing or the read fails. Does not seek.
    public bool TryGetTimelineSeconds(out float seconds)
    {
        seconds = 0f;
        if (!isPlaying) return false;

        int milliseconds;
        FMOD.RESULT result = music.getTimelinePosition(out milliseconds);
        if (result != FMOD.RESULT.OK || milliseconds < 0) return false;

        seconds = milliseconds / 1000f;
        return true;
    }


    private void UpdateSolvedEnvelope()
    {
        if (solvedPhase == SolvedPhase.Idle) return;

        float dt = Time.unscaledDeltaTime;
        if (useCustomSolvedTiming)
            momentElapsed += dt;

        // A fade-in longer than the cap must start releasing before Hold.
        if (useCustomSolvedTiming && alignFadeOutToPhrase
            && solvedPhase == SolvedPhase.Rise && MomentPastCap())
        {
            BeginSolvedRelease();
            if (solvedPhase == SolvedPhase.Idle) return;
        }

        switch (solvedPhase)
        {
            case SolvedPhase.Rise:
                ApplySolved(Mathf.MoveTowards(solvedValue, 1f, dt / Mathf.Max(0.0001f, ActiveRiseSeconds)));
                if (solvedValue >= 1f) solvedPhase = SolvedPhase.Hold;
                break;

            case SolvedPhase.Hold:
                if (useCustomSolvedTiming && alignFadeOutToPhrase)
                {
                    if (AlignedHoldFinished(dt))
                        BeginSolvedRelease();
                }
                else
                {
                    solvedHoldTimer -= dt;
                    if (solvedHoldTimer <= 0f)
                        BeginSolvedRelease();
                }
                break;

            case SolvedPhase.Release:
                ApplySolved(Mathf.MoveTowards(solvedValue, 0f, dt / Mathf.Max(0.0001f, ActiveReleaseSeconds)));
                if (solvedValue <= 0f) solvedPhase = SolvedPhase.Idle;
                break;
        }
    }


    private void BeginSolvedRelease()
    {
        if (ActiveReleaseSeconds > 0f && solvedValue > 0.0001f)
            solvedPhase = SolvedPhase.Release;
        else
        {
            solvedPhase = SolvedPhase.Idle;
            ApplySolved(0f);
        }
    }


    private bool MomentPastCap()
    {
        float release = Mathf.Max(0f, customReleaseSeconds);
        float cap = momentCapSeconds > 0f ? momentCapSeconds : 30f;
        return momentElapsed >= cap - release;
    }


    // True when the fade-out should start. Min hold is measured from the
    // moment Solved reaches 1, not from the start of the fade-in.
    private bool AlignedHoldFinished(float dt)
    {
        holdElapsed += dt;

        if (MomentPastCap())
            return true;

        // Seed for CS0165: && may skip TryGetTimelineSeconds. That path returns
        // on !timelineOk and never reads this value.
        float timeline = 0f;
        bool timelineOk = phraseLengthSeconds > 0.05f && TryGetTimelineSeconds(out timeline);
        if (!timelineOk)
            return holdElapsed >= customHoldSeconds;

        if (holdElapsed < phraseMinHoldSeconds)
        {
            // Ignore phrase crossings during the minimum hold, and don't let
            // one of them look like a wrap on the first frame we're allowed
            // to leave.
            previousPhraseMod = -1f;
            return false;
        }

        float local = timeline - phraseTimelineOffset;
        float mod = Mathf.Repeat(local, phraseLengthSeconds);
        float until = mod <= 0.0001f ? 0f : phraseLengthSeconds - mod;

        const float window = 0.05f;
        bool nearBoundary = until <= window || mod <= window;
        // A hitch that skips over the downbeat: cursor was near the end of
        // the phrase and is now just after the next one.
        bool crossed = previousPhraseMod > phraseLengthSeconds - 0.5f && mod < 0.5f;
        previousPhraseMod = mod;

        if (nearBoundary || crossed)
            return true;

        float release = Mathf.Max(0f, customReleaseSeconds);
        float cap = momentCapSeconds > 0f ? momentCapSeconds : 30f;
        if (momentElapsed + until >= cap - release)
            return true;

        return false;
    }


    private void ApplySolved(float value)
    {
        solvedValue = value;
        if (isPlaying && !string.IsNullOrEmpty(solvedParameter))
            music.setParameterByName(solvedParameter, solvedValue);
    }


    // ObjectiveManager can be created late (EnsureExists) or replaced by a
    // scene copy, so the subscription follows whichever one is live.
    private void TrySubscribeObjectives()
    {
        if (!liftSolvedOnObjective) return;

        ObjectiveManager current = ObjectiveManager.Instance;
        if (current == subscribedObjectives) return;

        UnsubscribeObjectives();
        if (current == null) return;

        current.OnObjectiveCompleted += HandleObjectiveCompleted;
        subscribedObjectives = current;
    }


    private void UnsubscribeObjectives()
    {
        if (subscribedObjectives != null)
            subscribedObjectives.OnObjectiveCompleted -= HandleObjectiveCompleted;
        subscribedObjectives = null;
    }


    private void HandleObjectiveCompleted(string objectiveId)
    {
        // An active AreaSolvedMusic owns the Solved curve. Skip the old
        // per-objective blip so the two envelopes cannot fight.
        if (areaSolvedControllerActive) return;

        if (solvedObjectiveIds != null && solvedObjectiveIds.Count > 0 && !solvedObjectiveIds.Contains(objectiveId))
            return;

        LiftSolved();
    }


    // Silence without stopping, for cutscenes or menus. Implemented as a pause,
    // so unmuting resumes from the point where it was paused (comment corrected:
    // it does not jump ahead to where the music "would have been").
    public void SetMuted(bool muted)
    {
        if (isPlaying) music.setPaused(muted);
    }
}
