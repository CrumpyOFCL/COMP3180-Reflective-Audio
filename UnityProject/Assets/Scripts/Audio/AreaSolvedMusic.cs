using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// One per mansion area, usually on the same object as AreaAudio.
//
// The solved layer is a one-time moment, not a bed that stays up. When this
// area's requirement goes from not-met to met DURING PLAY (an objective fires
// OnObjectiveCompleted while this area is the active track), Solved fades in,
// holds, then fades out. EraMusic moves the parameter only; the music event
// is never restarted.
//
// Already complete when the area is entered or a save is loaded: the Inspector
// shows the requirement as met, and the moment does NOT play. RestoreState
// does not fire OnObjectiveCompleted (it fires OnProgressReset after the ids
// are restored), so a load cannot look like a live solve. OnProgressReset
// clears "played this session", which lets a New Game in the same sitting
// hear the moment again; a reload of an already-solved area still does not
// replay, because the requirement is already met.
//
// No ticked objectives: the moment never plays (Force Solved is the exception).
// Only the active area drives EraMusic. Leaving mid-moment fades Solved to 0
// so a solved foyer does not carry into the next room.
//
// After the fade-in, Solved stays at 1 for at least minHoldSeconds, then the
// fade-out waits for the next 2-bar phrase on the music timeline. Phrase
// Seconds 0 uses the length for this track (see EffectivePhraseSeconds).
// Align off, an unknown track, or a timeline read that fails, uses the flat
// holdSeconds (18). The whole moment is capped at maxMomentSeconds (30).
//
// While this component is the active area it tells EraMusic to ignore the old
// per-objective LiftSolved, so only the area completion plays the layer.
[AddComponentMenu("Audio/Area Solved Music")]
public class AreaSolvedMusic : MonoBehaviour
{
    public enum Requirement
    {
        AllTicked,
        AtLeastN
    }

    [Serializable]
    public class ObjectiveEntry
    {
        [Tooltip("Exact string the puzzle passes to ObjectiveManager.CompleteObjective. Same text as the puzzle's Objective On Solved / objectiveOnSolved field.")]
        public string objectiveId;

        [Tooltip("Counts toward Solved. Leave ticked. Untick to keep the id in the list without it affecting the requirement.")]
        public bool countsTowardSolved = true;
    }

    [Tooltip("Track label, e.g. Library. Leave empty to use the Area Audio track on this same object.")]
    [SerializeField] private string areaTrack;

    [Tooltip("Objectives that can complete this area. Tick Counts toward Solved for each one that should count. None ticked = the layer never plays.")]
    [SerializeField] private List<ObjectiveEntry> objectives = new List<ObjectiveEntry>();

    [Tooltip("All Ticked: every ticked objective must be complete. At Least N: that many ticked objectives must be complete.")]
    [SerializeField] private Requirement requirement = Requirement.AllTicked;

    [Tooltip("Used by At Least N. Clamped to 1 .. number of ticked objectives.")]
    [Min(1)] [SerializeField] private int requiredCount = 1;

    [Tooltip("Seconds for Solved to rise from 0 to 1. About 3 seconds sounds musical; under 1 second sounds like a switch.")]
    [Min(0f)] [SerializeField] private float fadeInSeconds = 3f;

    [Tooltip("Flat seconds at 1 before the fade-out. Used when Align Fade Out To Phrase is off, this track has no phrase length, or the music timeline cannot be read.")]
    [Min(0f)] [SerializeField] private float holdSeconds = 18f;

    [Tooltip("Minimum seconds at 1 before phrase lock is allowed to start the fade-out.")]
    [Min(0f)] [SerializeField] private float minHoldSeconds = 14f;

    [Tooltip("Length of 2 bars, in seconds. Leave at 0 to use this track's length: Foyer 6, Library 6.667, Greenhouse 6.4, BoilerRoom 5.333, AlchemyRoom 6, FinalRoom 6.667. Type a number to override. Set back to 0 to follow the track again.")]
    [Min(0f)] [SerializeField] private float phraseSeconds = 0f;

    [Tooltip("Subtracted from the music timeline before the phrase check. Set this when the area's section does not start at 0 seconds on the event timeline.")]
    [SerializeField] private float timelineOffsetSeconds = 0f;

    [Tooltip("Fade out on the next 2-bar phrase after the minimum hold. Off uses the flat Hold Seconds.")]
    [SerializeField] private bool alignFadeOutToPhrase = true;

    [Tooltip("Fade-in, hold and fade-out together stop by this many seconds, so a long wait for the next phrase cannot run on.")]
    [Min(1f)] [SerializeField] private float maxMomentSeconds = 30f;

    [Tooltip("Seconds for Solved to fall from 1 back to 0. Also used when the player leaves this area mid-moment.")]
    [Min(0f)] [SerializeField] private float fadeOutSeconds = 4f;

    [Tooltip("Play-mode test. Tick while this area is active to play the full fade-in / hold / fade-out once. The box clears itself. Does not complete any objective, and does not use up the real one-time moment.")]
    [SerializeField] private bool forceSolved;

    private static readonly List<AreaSolvedMusic> instances = new List<AreaSolvedMusic>();
    private static AreaSolvedMusic activeArea;
    private static bool warnedDuplicateTrack;

    private ObjectiveManager subscribed;
    private bool isActiveArea;
    private bool requirementMet;
    private bool playedThisSession;
    private bool drivingMoment;
    private bool forceSolvedWasSet;

    public bool IsActiveArea => isActiveArea;
    public bool PlayedThisSession => playedThisSession;

    private void Awake()
    {
        // A tick saved on the prefab must not fire the moment the scene starts.
        // Only a rising edge during play counts.
        forceSolvedWasSet = forceSolved;
    }

    private void OnEnable()
    {
        Prune();
        if (!instances.Contains(this)) instances.Add(this);
        TrySubscribe();

        EraMusic music = EraMusic.Instance;
        if (music != null && MatchesTrack(music.CurrentTrack))
            NotifyAreaEntered(music.CurrentTrack);
    }

    private void OnDisable()
    {
        Unsubscribe();
        instances.Remove(this);
        if (!isActiveArea) return;

        string track = EffectiveTrack();
        bool wasDriving = drivingMoment;
        isActiveArea = false;
        drivingMoment = false;
        if (activeArea == this) activeArea = null;

        AreaSolvedMusic handoff = FindEnabledMatch(track);
        if (handoff != null)
        {
            // Same track is still loaded (another zone of this area). Keep the
            // moment going and remember that it already played.
            handoff.playedThisSession = handoff.playedThisSession || playedThisSession;
            handoff.drivingMoment = handoff.drivingMoment || wasDriving;
            handoff.BecomeActive();
            return;
        }

        ReleaseMusic(wasDriving);
    }

    private void Update()
    {
        TrySubscribe();

        EraMusic musicNow = EraMusic.Instance;
        if (!isActiveArea)
        {
            // AreaAudio notifies on Apply. This also catches a track that was
            // selected before this component enabled, or before EraMusic existed.
            if (musicNow != null && MatchesTrack(musicNow.CurrentTrack))
                NotifyAreaEntered(musicNow.CurrentTrack);
        }
        else if (musicNow == null || !MatchesTrack(musicNow.CurrentTrack))
        {
            // Track moved on without a matching Notify (or EraMusic went away).
            // Drop active so the layer fades and the new area can take over.
            LeaveArea();
        }
        else
        {
            // Re-assert in case EraMusic was created after we became active.
            musicNow.SetAreaSolvedControllerActive(true);
        }

        // Rising edge only. A box left ticked on the prefab is armed in Awake
        // and cleared here without playing. Auto-untick so the next tick plays
        // again; wasSet stays true until we actually observe it unticked, so a
        // late Inspector write of the same tick cannot double-fire.
        if (forceSolved && !forceSolvedWasSet)
        {
            forceSolvedWasSet = true;
            TriggerForceSolved();
        }

        if (forceSolved)
            forceSolved = false;
        else
            forceSolvedWasSet = false;

        if (drivingMoment && isActiveArea)
        {
            EraMusic music = EraMusic.Instance;
            if (music == null || (music.CurrentSolvedValue <= 0.001f && music.CurrentSolvedPhase == EraMusic.SolvedPhase.Idle))
                drivingMoment = false;
        }
    }

    private void OnValidate()
    {
        if (fadeInSeconds < 0f) fadeInSeconds = 0f;
        if (holdSeconds < 0f) holdSeconds = 0f;
        if (minHoldSeconds < 0f) minHoldSeconds = 0f;
        if (phraseSeconds < 0f) phraseSeconds = 0f;
        if (fadeOutSeconds < 0f) fadeOutSeconds = 0f;
        if (maxMomentSeconds < 1f) maxMomentSeconds = 1f;
        if (requiredCount < 1) requiredCount = 1;

        int ticked = CountTicked();
        if (ticked > 0 && requiredCount > ticked)
            requiredCount = ticked;
    }

    // AreaAudio.Apply calls this with the track it just selected. Safe when
    // nothing in the loaded scenes has this component.
    public static void NotifyAreaEntered(string track)
    {
        if (string.IsNullOrWhiteSpace(track)) return;

        Prune();

        AreaSolvedMusic match = null;
        int matches = 0;
        for (int i = 0; i < instances.Count; i++)
        {
            AreaSolvedMusic candidate = instances[i];
            if (candidate == null || !candidate.isActiveAndEnabled) continue;
            if (!candidate.MatchesTrack(track)) continue;

            matches++;
            if (match == null) match = candidate;
        }

        if (matches > 1 && !warnedDuplicateTrack)
        {
            warnedDuplicateTrack = true;
            Debug.LogWarning("[AreaSolvedMusic] More than one component uses track '" + track.Trim() +
                             "'. Only the first one drives Solved.");
        }

        if (match != null && activeArea == match && match.isActiveArea)
        {
            // Same area again (another zone, or Apply ran twice). Do not replay.
            match.requirementMet = match.EvaluateMet();
            return;
        }

        if (activeArea != null && activeArea != match)
            activeArea.LeaveArea();

        if (match != null)
            match.BecomeActive();
    }

    // Same as AreaAudio calling NotifyAreaEntered for this component's track.
    public void Activate()
    {
        string track = EffectiveTrack();
        if (string.IsNullOrEmpty(track))
        {
            Debug.LogWarning($"{name}: Area Solved Music has no track. Set Area Track, or add Area Audio on this object.", this);
            return;
        }

        NotifyAreaEntered(track);
    }

    private void BecomeActive()
    {
        isActiveArea = true;
        activeArea = this;

        if (EraMusic.Instance != null)
            EraMusic.Instance.SetAreaSolvedControllerActive(true);

        // Snapshot saved progress. If the room is already solved, status shows
        // met and we do not start the moment. Only a later not-met -> met
        // transition, while we are still active, plays it.
        requirementMet = EvaluateMet();
    }

    private void LeaveArea()
    {
        if (!isActiveArea) return;

        bool wasDriving = drivingMoment;
        isActiveArea = false;
        drivingMoment = false;
        if (activeArea == this) activeArea = null;
        ReleaseMusic(wasDriving);
    }

    private void ReleaseMusic(bool fadeOutMoment)
    {
        EraMusic music = EraMusic.Instance;
        if (music == null) return;

        music.SetAreaSolvedControllerActive(false);
        if (!fadeOutMoment) return;
        if (music.CurrentSolvedValue <= 0.001f && music.CurrentSolvedPhase == EraMusic.SolvedPhase.Idle)
            return;

        music.FadeOutSolved(Mathf.Max(0f, fadeOutSeconds));
    }

    private void TrySubscribe()
    {
        ObjectiveManager current = ObjectiveManager.Instance;
        if (current == subscribed) return;

        Unsubscribe();
        if (current == null) return;

        current.OnObjectiveCompleted += HandleObjectiveCompleted;
        current.OnProgressReset += HandleProgressReset;
        subscribed = current;

        // The manager may already hold saved completions. That is not a live
        // solve, so refresh the baseline and do not play.
        requirementMet = EvaluateMet();
    }

    private void Unsubscribe()
    {
        if (subscribed != null)
        {
            subscribed.OnObjectiveCompleted -= HandleObjectiveCompleted;
            subscribed.OnProgressReset -= HandleProgressReset;
        }

        subscribed = null;
    }

    private void HandleObjectiveCompleted(string objectiveId)
    {
        bool wasMet = requirementMet;
        requirementMet = EvaluateMet();

        if (!isActiveArea) return;
        if (playedThisSession) return;
        if (CountTicked() == 0) return;
        if (wasMet || !requirementMet) return;

        BeginMoment(objectiveId);
    }

    private void HandleProgressReset()
    {
        // ResetProgress clears the ids first. RestoreState writes the saved
        // ids first, then fires this same event, and never fires
        // OnObjectiveCompleted. Re-baseline only: a restored solved room
        // stays quiet. A wipe (requirement no longer met) may play again
        // the next time it is completed for real.
        bool met = EvaluateMet();
        if (!met) playedThisSession = false;
        drivingMoment = false;
        requirementMet = met;

        if (isActiveArea && EraMusic.Instance != null)
            EraMusic.Instance.ClearSolved();
    }

    private void TriggerForceSolved()
    {
        if (!isActiveArea)
        {
            Debug.LogWarning($"[AreaSolvedMusic] {TrackLabel()} Force Solved ignored: this area is not the active track. Enter the area, then tick it.", this);
            return;
        }

        BeginMoment(null);
    }

    // completingObjectiveId == null is the Force Solved test. It does not mark
    // the real moment as played, so a later live solve still gets its one play.
    private void BeginMoment(string completingObjectiveId)
    {
        EraMusic music = EraMusic.Instance;
        if (music == null)
        {
            Debug.LogWarning($"[AreaSolvedMusic] {TrackLabel()} has no EraMusic in the loaded scenes.", this);
            return;
        }

        if (completingObjectiveId != null)
        {
            playedThisSession = true;
            int met = CountComplete();
            int required = RequiredThreshold();
            Debug.Log($"[AreaSolvedMusic] {TrackLabel()} solved by objective '{completingObjectiveId}' ({met}/{required})", this);
        }
        else
        {
            Debug.Log($"[AreaSolvedMusic] {TrackLabel()} Force Solved moment", this);
        }

        drivingMoment = true;

        float phrase = EffectivePhraseSeconds();
        bool align = alignFadeOutToPhrase && phrase > 0.05f;
        music.PlaySolvedMoment(
            fadeInSeconds,
            holdSeconds,
            fadeOutSeconds,
            align,
            phrase,
            timelineOffsetSeconds,
            minHoldSeconds,
            maxMomentSeconds);
    }

    private string EffectiveTrack()
    {
        if (!string.IsNullOrWhiteSpace(areaTrack)) return areaTrack.Trim();

        AreaAudio audio = GetComponent<AreaAudio>();
        if (audio != null && !string.IsNullOrWhiteSpace(audio.MusicTrack))
            return audio.MusicTrack.Trim();

        return "";
    }

    private string TrackLabel()
    {
        string track = EffectiveTrack();
        return string.IsNullOrEmpty(track) ? name : track;
    }

    // 2 bars at each area's tempo. 0 in phraseSeconds means "use this".
    private float EffectivePhraseSeconds()
    {
        if (phraseSeconds > 0.05f) return phraseSeconds;

        switch (EffectiveTrack())
        {
            case "Foyer": return 6f;
            case "Library": return 6.667f;
            case "Greenhouse": return 6.4f;
            case "BoilerRoom": return 5.333f;
            case "AlchemyRoom": return 6f;
            case "FinalRoom": return 6.667f;
            default: return 0f;
        }
    }

    private bool MatchesTrack(string track)
    {
        if (string.IsNullOrWhiteSpace(track)) return false;
        string mine = EffectiveTrack();
        return mine.Length > 0 && string.Equals(mine, track.Trim(), StringComparison.Ordinal);
    }

    private static AreaSolvedMusic FindEnabledMatch(string track)
    {
        if (string.IsNullOrEmpty(track)) return null;

        for (int i = 0; i < instances.Count; i++)
        {
            AreaSolvedMusic candidate = instances[i];
            if (candidate == null || !candidate.isActiveAndEnabled) continue;
            if (candidate.MatchesTrack(track)) return candidate;
        }

        return null;
    }

    private static void Prune()
    {
        for (int i = instances.Count - 1; i >= 0; i--)
        {
            if (instances[i] == null) instances.RemoveAt(i);
        }
    }

    private int CountTicked()
    {
        if (objectives == null) return 0;

        int count = 0;
        for (int i = 0; i < objectives.Count; i++)
        {
            ObjectiveEntry entry = objectives[i];
            if (entry != null && entry.countsTowardSolved && !string.IsNullOrWhiteSpace(entry.objectiveId))
                count++;
        }

        return count;
    }

    private int CountComplete()
    {
        if (objectives == null) return 0;

        ObjectiveManager manager = ObjectiveManager.Instance;
        int count = 0;
        for (int i = 0; i < objectives.Count; i++)
        {
            ObjectiveEntry entry = objectives[i];
            if (entry == null || !entry.countsTowardSolved || string.IsNullOrWhiteSpace(entry.objectiveId))
                continue;
            if (manager != null && manager.IsObjectiveComplete(entry.objectiveId.Trim()))
                count++;
        }

        return count;
    }

    private int RequiredThreshold()
    {
        int ticked = CountTicked();
        if (ticked <= 0) return 0;
        if (requirement == Requirement.AllTicked) return ticked;
        return Mathf.Clamp(requiredCount, 1, ticked);
    }

    private bool EvaluateMet()
    {
        int ticked = CountTicked();
        if (ticked <= 0) return false;
        return CountComplete() >= RequiredThreshold();
    }

    public string StatusTrack => string.IsNullOrEmpty(EffectiveTrack()) ? "(none)" : EffectiveTrack();

    public float StatusSolvedValue
    {
        get
        {
            EraMusic music = EraMusic.Instance;
            return music != null ? music.CurrentSolvedValue : 0f;
        }
    }

    public string StatusPhrase
    {
        get
        {
            if (!alignFadeOutToPhrase)
                return "off, flat hold " + holdSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";

            float phrase = EffectivePhraseSeconds();
            if (phrase <= 0.05f)
                return "no phrase for this track, flat hold";

            string source = phraseSeconds > 0.05f ? "typed" : "from track";
            return phrase.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " s (" + source + ")";
        }
    }

    public string StatusPhase
    {
        get
        {
            if (!isActiveArea) return "Inactive";
            EraMusic music = EraMusic.Instance;
            return music != null ? music.CurrentSolvedPhase.ToString() : "Idle";
        }
    }

    public string StatusMet
    {
        get
        {
            int ticked = CountTicked();
            if (ticked == 0) return "0/0 (none ticked, will not play)";

            int required = RequiredThreshold();
            int met = CountComplete();
            string state = met >= required ? "met" : "not met";
            return met + "/" + required + " " + state;
        }
    }

    public string StatusOpenObjectives
    {
        get
        {
            if (objectives == null || objectives.Count == 0) return "none";

            ObjectiveManager manager = ObjectiveManager.Instance;
            StringBuilder open = new StringBuilder();
            for (int i = 0; i < objectives.Count; i++)
            {
                ObjectiveEntry entry = objectives[i];
                if (entry == null || !entry.countsTowardSolved || string.IsNullOrWhiteSpace(entry.objectiveId))
                    continue;

                string id = entry.objectiveId.Trim();
                if (manager != null && manager.IsObjectiveComplete(id)) continue;

                if (open.Length > 0) open.Append(", ");
                open.Append(id);
            }

            return open.Length == 0 ? "none" : open.ToString();
        }
    }
}
