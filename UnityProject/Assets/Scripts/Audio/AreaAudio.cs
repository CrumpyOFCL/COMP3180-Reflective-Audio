using FMODUnity;
using UnityEngine;

// ADDED (round 2; replaces the round-1 AmbienceBedSetter, which never shipped).
// Tells EraMusic and EraAmbience which mansion area the player is in:
//   * Track label on the music event (Foyer, Library, Greenhouse, BoilerRoom,
//     AlchemyRoom, FinalRoom)
//   * the matching ambience bed (event:/Ambience/Beds/<Area>)
//
// Both calls are idempotent: re-entering the same area, or loading another
// additive zone of the same area, changes nothing, so music and ambience never
// restart on a zone load.
//
// Three ways to fire it:
//   On Start        - put one in the first zone scene of each area.
//   On Player Enter - put it on a trigger Collider2D at an area boundary.
//   By Call         - it is a CallScript, so AreaTrigger, doors, SceneFlow
//                     hooks, etc. can fire it from their callScript lists.
//
// CHANGED (area solved), additive: Apply() also tells AreaSolvedMusic that this
// track is now the active area. That call is safe when no AreaSolvedMusic is
// loaded (it does nothing). MusicTrack is exposed so AreaSolvedMusic can copy
// the label when its own area track field is left empty.
public class AreaAudio : CallScript
{
    private enum When { OnStart, OnPlayerEnter, ByCallOnly }

    [Tooltip("Track label on event:/Music/EraMusic. Leave empty to leave the music alone.")]
    [SerializeField] private string musicTrack = "Foyer";

    [Tooltip("2D looping bed, e.g. event:/Ambience/Beds/Foyer. Leave empty to leave the ambience alone.")]
    [SerializeField] private EventReference ambienceBed;

    [Tooltip("Tick to fade ambience to silence when this fires (ignores Ambience Bed).")]
    [SerializeField] private bool silenceAmbience = false;

    [SerializeField] private When when = When.OnStart;

    [Tooltip("Only used with On Player Enter.")]
    [SerializeField] private string playerTag = "Player";

    // AreaSolvedMusic reads this when it sits on the same object and its
    // areaTrack field is blank.
    public string MusicTrack => musicTrack;

    private void Start()
    {
        if (when == When.OnStart) Apply();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (when != When.OnPlayerEnter) return;
        if (!other.CompareTag(playerTag)) return;
        Apply();
    }

    public override void CallBehaviour()
    {
        Apply();
    }

    public void Apply()
    {
        if (!string.IsNullOrWhiteSpace(musicTrack))
        {
            if (EraMusic.Instance != null) EraMusic.Instance.SetTrack(musicTrack);
            else Debug.LogWarning($"{name}: no EraMusic in the loaded scenes.", this);

            AreaSolvedMusic.NotifyAreaEntered(musicTrack);
        }

        if (silenceAmbience || !ambienceBed.IsNull)
        {
            if (EraAmbience.Instance != null) EraAmbience.Instance.SetBed(silenceAmbience ? default : ambienceBed);
            else Debug.LogWarning($"{name}: no EraAmbience in the loaded scenes.", this);
        }
    }
}
