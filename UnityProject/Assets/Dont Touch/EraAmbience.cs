using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

// ADDED (FMOD pass). Mirrors EraMusic for ambience beds.
//
// Plays ONE 2D stereo ambience bed per scene/area. Each bed event carries a
// CONTINUOUS local "Era" parameter (0 Past, 1 Present, 2 Future) that this
// component glides on every time switch, so the bed cross-fades between its
// three era layers instead of restarting.
//
// Changing area (SetBed with a different event) cross-fades the old bed out and
// the new one in over bedCrossfadeSeconds. Fades use unscaled time, so they
// still run while the game is paused with Time.timeScale = 0.
//
// FMOD contract (see FMOD_CONTRACT.md):
//   event:/Ambience/Beds/<Area>  - 2D (no spatializer), looping, dry (no reverb
//                                  sends), routed to bus:/Environment/Amb, which
//                                  sits under vca:/Environment.
//   "Era"  - CONTINUOUS local parameter 0..2.
// Point sources (machinery, drips, the radio...) are NOT beds: use mono 3D
// events on OccludedEmitter2D / RadioSource instead.
//
// Put this on the same persistent Core object as EraMusic. Sections pick their
// bed with AmbienceBedSetter, or by calling EraAmbience.Instance.SetBed(...).
public class EraAmbience : MonoBehaviour, ITimeSwitchObserver
{
    [Header("Bed")]
    [Tooltip("Bed started with. Leave empty to start silent until a section calls SetBed.")]
    [SerializeField] private EventReference startingBed;

    [SerializeField] private string eraParameterName = "Era";

    [Tooltip("Seconds the Era parameter takes to reach its new value. 0 snaps.")]
    [SerializeField] private float eraBlendSeconds = 1.5f;

    [Tooltip("Seconds to cross-fade between beds when the area changes.")]
    [SerializeField, Min(0f)] private float bedCrossfadeSeconds = 2f;

    private static EraAmbience instance;

    // Same fake-null-safe pattern as EraMusic / AudioManager.
    public static EraAmbience Instance
    {
        get => instance != null ? instance : null;
        private set => instance = value;
    }

    private class Bed
    {
        public EventInstance Instance;
        public FMOD.GUID Guid;
        public float Volume;
    }

    private Bed current;
    private readonly List<Bed> fadingOut = new List<Bed>();

    private bool observing;
    private bool muted;
    private float eraValue;
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
        if (Instance != this) return;

        TryObserveTime();

        // Only if no section has already picked a bed (AmbienceBedSetter.Start
        // may run before this Start).
        if (current == null && !startingBed.IsNull) SetBed(startingBed, instant: true);
    }


    private void OnDestroy()
    {
        if (observing && TimeSingleton.Instance != null)
            TimeSingleton.Instance.RemoveObserver(this);
        observing = false;

        if (current != null) Kill(current, FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        current = null;

        foreach (var bed in fadingOut) Kill(bed, FMOD.Studio.STOP_MODE.IMMEDIATE);
        fadingOut.Clear();

        if (Instance == this) Instance = null;
    }


    // Switch to a new bed. Does nothing if that bed is already playing, so
    // re-entering an area doesn't restart it. Pass a null EventReference
    // (default) to fade to silence.
    public void SetBed(EventReference bed, bool instant = false)
    {
        if (current != null && !bed.IsNull && current.Guid.Equals(bed.Guid)) return;

        if (current != null)
        {
            if (instant || bedCrossfadeSeconds <= 0f) Kill(current, FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            else fadingOut.Add(current);
            current = null;
        }

        if (bed.IsNull) return;

        var next = new Bed
        {
            Instance = RuntimeManager.CreateInstance(bed),
            Guid = bed.Guid,
            Volume = instant || bedCrossfadeSeconds <= 0f ? 1f : 0f
        };

        next.Instance.setParameterByName(eraParameterName, eraValue);
        next.Instance.setVolume(next.Volume);
        next.Instance.start();
        if (muted) next.Instance.setPaused(true);

        current = next;
    }


    public void ClearBed() => SetBed(default);


    // Pause every bed (menus, cutscenes). Same semantics as EraMusic.SetMuted.
    public void SetMuted(bool value)
    {
        muted = value;
        if (current != null) current.Instance.setPaused(value);
        foreach (var bed in fadingOut) bed.Instance.setPaused(value);
    }


    private void Update()
    {
        if (!observing) TryObserveTime();
        if (muted) return;

        float dt = Time.unscaledDeltaTime;

        // Era glide.
        if (!Mathf.Approximately(eraValue, targetEraValue))
        {
            eraValue = eraBlendSeconds > 0f
                ? Mathf.MoveTowards(eraValue, targetEraValue, dt / eraBlendSeconds)
                : targetEraValue;

            if (current != null) current.Instance.setParameterByName(eraParameterName, eraValue);
            foreach (var bed in fadingOut) bed.Instance.setParameterByName(eraParameterName, eraValue);
        }

        float step = bedCrossfadeSeconds > 0f ? dt / bedCrossfadeSeconds : 1f;

        // Fade the current bed in.
        if (current != null && current.Volume < 1f)
        {
            current.Volume = Mathf.MoveTowards(current.Volume, 1f, step);
            current.Instance.setVolume(current.Volume);
        }

        // Fade old beds out, then stop and release them.
        for (int i = fadingOut.Count - 1; i >= 0; i--)
        {
            Bed bed = fadingOut[i];
            bed.Volume = Mathf.MoveTowards(bed.Volume, 0f, step);
            bed.Instance.setVolume(bed.Volume);

            if (bed.Volume > 0f) continue;

            Kill(bed, FMOD.Studio.STOP_MODE.IMMEDIATE);
            fadingOut.RemoveAt(i);
        }
    }


    private void TryObserveTime()
    {
        if (observing || TimeSingleton.Instance == null) return;

        TimeSingleton.Instance.AddObserver(this);
        observing = true;

        eraValue = targetEraValue = (int)TimeSingleton.Instance.currentGameState;
        if (current != null) current.Instance.setParameterByName(eraParameterName, eraValue);
    }


    public void OnNotify(TimeSingleton.GameState currentTime)
    {
        targetEraValue = (int)currentTime;
    }


    private static void Kill(Bed bed, FMOD.Studio.STOP_MODE mode)
    {
        if (!bed.Instance.isValid()) return;
        bed.Instance.stop(mode);
        bed.Instance.release();
        bed.Instance.clearHandle();
    }
}
