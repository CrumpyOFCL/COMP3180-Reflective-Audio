using System;
using System.Collections;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

// Main menu music. Paste over the game project's MenuMusic.cs.
//
// One FMOD event per track (lead-in, then the track, Handoff marker at the
// lead-in length). This script chains them:
//   * The first track starts at the Handoff marker (lead-in skipped).
//   * Every later track starts at timeline 0 when the current event reaches
//     its length minus 1000 ms. Overlap is 48000 samples / 1000 ms.
//   * Six normals play shuffled, with no repeat inside a cycle and no repeat
//     across the cycle boundary.
//   * After each normal reaches that window, one secret roll. The chance
//     starts at secretChanceStart (0.01 = 1%), rises by secretChanceStep after
//     a normal that did not trigger a secret, and resets when a secret plays.
//     secretChanceCap defaults to 0.10.
//   * Secrets come from a shuffled bag of all six.
//
// Empty EventReference slots fall back to EventReference.Find(path).Guid
// (FMOD for Unity 2.03). Handoff sample counts are from Docs/menu/TRANSITIONS.md.
//
// Fade in/out uses unscaled time. EraMusic and EraAmbience stay paused while
// this plays. survivesSceneLoad keeps the fade alive across menu to game.
public class MenuMusic : MonoBehaviour
{
    const int SampleRate = 48000;
    const int OverlapMs = 1000;
    const int TrackCount = 6;

    [Header("Normal events")]
    [SerializeField] EventReference menu01Strings;
    [SerializeField] EventReference menu02;
    [SerializeField] EventReference menu03;
    [SerializeField] EventReference menu01StringsV2;
    [SerializeField] EventReference menu03V2;
    [SerializeField] EventReference menu04;

    [Header("Secret events")]
    [SerializeField] EventReference menuSecret;
    [SerializeField] EventReference menuSecret02;
    [SerializeField] EventReference menuSecret03;
    [SerializeField] EventReference menuSecret04;
    [SerializeField] EventReference menuSecret05;
    [SerializeField] EventReference menuSecret06;

    [Header("Secret chance")]
    [Tooltip("Probability after a normal track. 0.01 = 1%.")]
    [SerializeField] float secretChanceStart = 0.01f;
    [Tooltip("Added after a normal that did not trigger a secret. 0.01 = 1%.")]
    [SerializeField] float secretChanceStep = 0.01f;
    [Tooltip("Highest probability. 0.10 = 10%.")]
    [SerializeField] float secretChanceCap = 0.10f;

    [SerializeField] bool playOnStart = true;

    [Header("Fades (unscaled seconds)")]
    [SerializeField, Min(0f)] float fadeInSeconds = 1f;
    [SerializeField, Min(0f)] float fadeOutSeconds = 1.5f;

    [Header("Behaviour")]
    [Tooltip("Pause EraMusic and EraAmbience while the menu music plays.")]
    [SerializeField] bool pauseGameAudioWhilePlaying = true;

    [Tooltip("When Stop() fades out, detach this GameObject into DontDestroyOnLoad " +
             "so the fade completes across a scene load, then destroy it.")]
    [SerializeField] bool survivesSceneLoad = true;

    struct TrackDef
    {
        public EventReference Reference;
        public string Path;
        public int HandoffSamples;
        public int TrackSamples;

        public int HandoffMs => RoundMs(HandoffSamples);
        public int FallbackLengthMs => RoundMs(HandoffSamples + TrackSamples);
    }

    class Voice
    {
        public EventInstance Instance;
        public int LengthMs;
        public bool SpawnedNext;
        public bool RollSecret;
    }

    static MenuMusic instance;

    public static MenuMusic Instance
    {
        get => instance != null ? instance : null;
        private set => instance = value;
    }

    TrackDef[] normals;
    TrackDef[] secrets;
    readonly List<Voice> voices = new List<Voice>();
    readonly int[] normalBag = new int[TrackCount];
    readonly int[] secretBag = new int[TrackCount];

    int normalCursor = TrackCount;
    int secretCursor = TrackCount;
    int lastNormal = -1;
    int lastSecret = -1;
    float secretChance;
    float volume;
    bool isPlaying;
    bool stopping;
    bool pausedGameAudio;
    bool warnedShort;
    Coroutine fadeRoutine;

    public bool IsPlaying => isPlaying;

    static int RoundMs(int samples)
    {
        return (int)Math.Round(samples * 1000.0 / SampleRate);
    }

    void Awake()
    {
        // A second menu (e.g. returning to the menu while a previous MenuMusic
        // is still fading out) takes over; the old one finishes its fade.
        Instance = this;
    }

    void Start()
    {
        if (playOnStart) Play();
    }

    void Update()
    {
        if (!isPlaying) return;
        if (!stopping) Advance();
        ReleaseFinished();
    }

    void OnDestroy()
    {
        StopInternal(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        if (Instance == this) Instance = null;
    }

    public void Play()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        stopping = false;

        if (!isPlaying)
        {
            BuildCatalog();
            ResetBags();
            volume = fadeInSeconds > 0f ? 0f : 1f;
            if (!BeginNormal(true))
            {
                Debug.LogWarning($"{name}: no menu track could be started.", this);
                return;
            }
            isPlaying = true;
        }

        PauseGameAudio(true);
        fadeRoutine = fadeInSeconds > 0f ? StartCoroutine(FadeTo(1f, fadeInSeconds, false)) : null;
    }

    // Fade out, then stop and release.
    public void Stop()
    {
        if (!isPlaying) return;
        stopping = true;

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

    void BuildCatalog()
    {
        normals = new[]
        {
            Def(menu01Strings, "event:/Music/MainMenu/01_strings", 376648, 4999360),
            Def(menu02, "event:/Music/MainMenu/02", 376648, 4999360),
            Def(menu03, "event:/Music/MainMenu/03", 376659, 4747200),
            Def(menu01StringsV2, "event:/Music/MainMenu/01_strings_v2", 375945, 4999360),
            Def(menu03V2, "event:/Music/MainMenu/03_v2", 375945, 4747200),
            Def(menu04, "event:/Music/MainMenu/04", 364890, 4800000)
        };
        secrets = new[]
        {
            Def(menuSecret, "event:/Music/MainMenu/Secret", 332885, 5155200),
            Def(menuSecret02, "event:/Music/MainMenu/Secret_02", 328558, 5299200),
            Def(menuSecret03, "event:/Music/MainMenu/Secret_03", 301167, 5270400),
            Def(menuSecret04, "event:/Music/MainMenu/Secret_04", 375945, 5136000),
            Def(menuSecret05, "event:/Music/MainMenu/Secret_05", 354240, 5064000),
            Def(menuSecret06, "event:/Music/MainMenu/Secret_06", 284601, 9571200)
        };
    }

    static TrackDef Def(EventReference reference, string path, int handoffSamples, int trackSamples)
    {
        return new TrackDef
        {
            Reference = reference,
            Path = path,
            HandoffSamples = handoffSamples,
            TrackSamples = trackSamples
        };
    }

    void ResetBags()
    {
        lastNormal = -1;
        lastSecret = -1;
        normalCursor = TrackCount;
        secretCursor = TrackCount;
        secretChance = Mathf.Max(0f, secretChanceStart);
    }

    void Advance()
    {
        for (int i = 0; i < voices.Count; i++)
        {
            Voice voice = voices[i];
            if (voice.SpawnedNext || !voice.Instance.isValid()) continue;

            int pos;
            if (voice.Instance.getTimelinePosition(out pos) != FMOD.RESULT.OK) continue;
            if (voice.LengthMs <= OverlapMs)
            {
                voice.SpawnedNext = true;
                if (!warnedShort)
                {
                    warnedShort = true;
                    Debug.LogWarning($"{name}: menu event length is under {OverlapMs} ms, so the chain stopped.", this);
                }
                continue;
            }
            if (pos < voice.LengthMs - OverlapMs) continue;

            voice.SpawnedNext = true;
            if (voice.RollSecret) BeginAfterNormal();
            else BeginNormal(false);
        }
    }

    void BeginAfterNormal()
    {
        bool hit = UnityEngine.Random.value < secretChance;
        if (hit)
        {
            secretChance = Mathf.Max(0f, secretChanceStart);
            if (!BeginSecret()) BeginNormal(false);
            return;
        }

        secretChance += secretChanceStep;
        if (secretChance > secretChanceCap) secretChance = secretChanceCap;
        if (secretChance < 0f) secretChance = 0f;
        BeginNormal(false);
    }

    bool BeginNormal(bool atHandoff)
    {
        for (int n = 0; n < TrackCount; n++)
        {
            if (StartTrack(normals[NextIndex(normalBag, ref normalCursor, ref lastNormal)], atHandoff, true))
                return true;
        }
        Debug.LogWarning($"{name}: normal menu tracks could not start.", this);
        return false;
    }

    bool BeginSecret()
    {
        for (int n = 0; n < TrackCount; n++)
        {
            if (StartTrack(secrets[NextIndex(secretBag, ref secretCursor, ref lastSecret)], false, false))
                return true;
        }
        Debug.LogWarning($"{name}: secret menu tracks could not start.", this);
        return false;
    }

    static int NextIndex(int[] bag, ref int cursor, ref int last)
    {
        int n = bag.Length;
        if (cursor >= n)
        {
            for (int i = 0; i < n; i++) bag[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                int tmp = bag[i];
                bag[i] = bag[j];
                bag[j] = tmp;
            }
            if (last >= 0 && n > 1 && bag[0] == last)
            {
                int swap = UnityEngine.Random.Range(1, n);
                int tmp = bag[0];
                bag[0] = bag[swap];
                bag[swap] = tmp;
            }
            cursor = 0;
        }

        int picked = bag[cursor];
        cursor++;
        last = picked;
        return picked;
    }

    bool StartTrack(TrackDef def, bool atHandoff, bool rollOnHandoff)
    {
        EventInstance inst = CreateInstance(def);
        if (!inst.isValid()) return false;

        int length = QueryLength(inst, def.FallbackLengthMs);
        inst.setVolume(volume);
        if (atHandoff && def.HandoffMs > 0) inst.setTimelinePosition(def.HandoffMs);
        inst.start();
        if (atHandoff && def.HandoffMs > 0) inst.setTimelinePosition(def.HandoffMs);

        voices.Add(new Voice
        {
            Instance = inst,
            LengthMs = length,
            SpawnedNext = false,
            RollSecret = rollOnHandoff
        });
        return true;
    }

    EventInstance CreateInstance(TrackDef def)
    {
        EventReference reference = def.Reference;
        if (reference.IsNull)
        {
            try
            {
                reference = RuntimeManager.PathToEventReference(def.Path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{name}: EventReference.Find('{def.Path}') failed ({ex.Message}).", this);
                return default(EventInstance);
            }
        }

        FMOD.GUID guid = reference.Guid;
        if (guid.IsNull)
        {
            Debug.LogWarning($"{name}: no guid for {def.Path}. Assign the slot or build banks.", this);
            return default(EventInstance);
        }

        return RuntimeManager.CreateInstance(guid);
    }

    static int QueryLength(EventInstance inst, int fallbackMs)
    {
        EventDescription desc;
        if (inst.getDescription(out desc) != FMOD.RESULT.OK || !desc.isValid()) return fallbackMs;
        int length;
        if (desc.getLength(out length) != FMOD.RESULT.OK || length <= 0) return fallbackMs;
        return length;
    }

    void ReleaseFinished()
    {
        for (int i = voices.Count - 1; i >= 0; i--)
        {
            Voice voice = voices[i];
            if (!voice.Instance.isValid())
            {
                voices.RemoveAt(i);
                continue;
            }

            PLAYBACK_STATE state;
            if (voice.Instance.getPlaybackState(out state) != FMOD.RESULT.OK) continue;
            if (state != PLAYBACK_STATE.STOPPED) continue;

            voice.Instance.release();
            voice.Instance.clearHandle();
            voices.RemoveAt(i);
        }
    }

    void ApplyVolume()
    {
        for (int i = 0; i < voices.Count; i++)
        {
            if (voices[i].Instance.isValid()) voices[i].Instance.setVolume(volume);
        }
    }

    IEnumerator FadeTo(float target, float seconds, bool stopWhenDone)
    {
        float start = volume;
        float t = 0f;

        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            volume = Mathf.Lerp(start, target, Mathf.Clamp01(t / seconds));
            if (isPlaying) ApplyVolume();
            yield return null;
        }

        volume = target;
        if (isPlaying) ApplyVolume();
        fadeRoutine = null;

        if (!stopWhenDone) yield break;

        StopInternal(FMOD.Studio.STOP_MODE.IMMEDIATE);

        if (survivesSceneLoad && gameObject.scene.name == "DontDestroyOnLoad")
            Destroy(gameObject);
    }

    void StopInternal(FMOD.Studio.STOP_MODE mode)
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        for (int i = voices.Count - 1; i >= 0; i--)
        {
            Voice voice = voices[i];
            if (voice.Instance.isValid())
            {
                voice.Instance.stop(mode);
                voice.Instance.release();
                voice.Instance.clearHandle();
            }
            voices.RemoveAt(i);
        }

        isPlaying = false;
        stopping = false;
        PauseGameAudio(false);
    }

    void PauseGameAudio(bool pause)
    {
        if (!pauseGameAudioWhilePlaying) return;
        if (pause == pausedGameAudio) return;

        // The menu that took over owns the pause. A fading predecessor must not
        // un-pause EraMusic while the new instance is still playing.
        if (!pause && Instance != null && Instance != this)
        {
            pausedGameAudio = false;
            return;
        }

        pausedGameAudio = pause;

        if (EraMusic.Instance != null) EraMusic.Instance.SetMuted(pause);
        if (EraAmbience.Instance != null) EraAmbience.Instance.SetMuted(pause);
    }
}
