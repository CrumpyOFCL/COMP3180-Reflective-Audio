using FMODUnity;
using UnityEngine;

// ADDED (FMOD pass). Tells EraAmbience which bed belongs to this scene/area.
//
// Either drop one in each section scene with "On Start" (the section's bed
// starts when the section loads - same idea as sections calling
// EraMusic.SetTrack), or put it on a trigger Collider2D with "On Player Enter"
// to change bed when the player walks into an area.
public class AmbienceBedSetter : MonoBehaviour
{
    private enum When { OnStart, OnPlayerEnter }

    [Tooltip("2D looping bed event, e.g. event:/Ambience/Beds/Mansion. Leave empty to fade ambience to silence.")]
    [SerializeField] private EventReference bed;

    [SerializeField] private When when = When.OnStart;

    [Tooltip("Only used with On Player Enter.")]
    [SerializeField] private string playerTag = "Player";

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

    public void Apply()
    {
        EraAmbience ambience = EraAmbience.Instance;
        if (ambience == null)
        {
            Debug.LogWarning($"{name}: no EraAmbience in the loaded scenes.", this);
            return;
        }

        ambience.SetBed(bed);
    }
}
