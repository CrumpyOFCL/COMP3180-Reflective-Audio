using FMODUnity;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class KeyPickupAudio : MonoBehaviour
{
    [Header("Pickup")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private bool destroyOnPickup = true;

    [Header("Audio")]
    [Tooltip("FMOD event. Volume lives on the event and its VCA now, so the old " +
             "per-object volume slider is gone.")]
    [SerializeField] private EventReference pickupEvent;

    private bool isPickedUp;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isPickedUp || !other.CompareTag(playerTag))
            return;

        isPickedUp = true;

        // Detached, so the object can be destroyed immediately instead of being
        // kept alive for the length of the clip.
        if (!pickupEvent.IsNull)
            RuntimeManager.PlayOneShot(pickupEvent, transform.position);

        if (destroyOnPickup)
            Destroy(gameObject);
    }
}