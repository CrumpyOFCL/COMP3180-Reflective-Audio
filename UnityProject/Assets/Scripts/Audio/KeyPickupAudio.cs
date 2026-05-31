using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class KeyPickupAudio : MonoBehaviour
{
    [Header("Pickup")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private bool destroyOnPickup = true;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip pickupClip;
    [Range(0f, 1f)][SerializeField] private float audioVolume = 0.7f;

    private bool isPickedUp;

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isPickedUp || !other.CompareTag(playerTag))
            return;

        isPickedUp = true;

        if (audioSource != null && pickupClip != null)
            audioSource.PlayOneShot(pickupClip, audioVolume);

        if (destroyOnPickup)
            StartCoroutine(DestroyAfterSound());
    }

    private IEnumerator DestroyAfterSound()
    {
        float waitTime = pickupClip != null ? pickupClip.length : 0f;
        yield return new WaitForSeconds(waitTime);
        Destroy(gameObject);
    }
}