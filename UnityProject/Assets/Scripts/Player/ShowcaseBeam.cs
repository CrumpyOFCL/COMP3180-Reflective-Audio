using UnityEngine;

// SHOWCASE VERSION of the time beam, used on the TimeBeam prefab in place of
// BeamBehaviour (which is tied into the puzzle systems: elements, freezables,
// receptors, crystals, mirrors, crates and skills, none of which are in this repo).
//
// It flies straight, stops on the first thing it hits, and plays the same
// FMOD events as the game: Impact where it stops, Fizzle if it runs out of
// lifetime. The shot sound itself is PlayerAudio's Shoot event, as in the game.
[RequireComponent(typeof(Rigidbody2D))]
public class ShowcaseBeam : MonoBehaviour
{
    [Header("Flight")]
    [SerializeField] private float speed = 50f;
    [SerializeField] private float lifetime = 5f;

    [Header("Audio (FMOD)")]
    [Tooltip("One-shot where the beam stops on something (event:/SFX/Beam/Impact).")]
    [SerializeField] private FMODUnity.EventReference impactEvent;

    [Tooltip("One-shot where the beam dies at the end of its lifetime (event:/SFX/Beam/Fizzle).")]
    [SerializeField] private FMODUnity.EventReference fizzleEvent;

    private Rigidbody2D rigidBody;
    private bool hitSomething;
    private bool finished;

    private void Awake()
    {
        rigidBody = GetComponent<Rigidbody2D>();
        rigidBody.gravityScale = 0f;
        rigidBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    private void Start()
    {
        // The prefab is spawned rotated to face the aim direction.
        rigidBody.linearVelocity = (Vector2)transform.right * speed;
        Invoke(nameof(Expire), lifetime);
    }

    private void OnCollisionEnter2D(Collision2D collision) => Hit();

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.isTrigger) Hit();
    }

    private void Hit()
    {
        if (finished) return;
        hitSomething = true;
        Finish();
    }

    private void Expire()
    {
        if (finished) return;
        Finish();
    }

    private void Finish()
    {
        finished = true;
        rigidBody.linearVelocity = Vector2.zero;

        if (GameAudio.IsAlive)
            GameAudio.PlayOneShot(hitSomething ? impactEvent : fizzleEvent, transform.position);

        Destroy(gameObject);
    }
}
