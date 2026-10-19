using FMODUnity;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ReflectiveAudio2D
{
    /// <summary>
    /// A stand-in player for the demo scene, so the package needs no game code or art.
    ///
    ///     A / D or arrows   walk
    ///     Space / W / Up    jump
    ///     Left click        fire at the wall under the cursor
    ///
    /// Firing plays the impact event where the shot hits a wall and asks
    /// ImageSourceEchoes2D for the reflections of that sound, so you can hear the
    /// echo delay and fall-off change from room to room. Tag the object "Player":
    /// AcousticZone2D and the gallery builder look for that tag.
    /// </summary>
    [AddComponentMenu("Reflective Audio 2D/Demo Player 2D")]
    [RequireComponent(typeof(Rigidbody2D))]
    public class DemoPlayer2D : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float jumpSpeed = 11f;
        [Tooltip("Left as Nothing, it uses the Solid layer.")]
        [SerializeField] private LayerMask groundLayers;
        [SerializeField] private float groundCheckDistance = 0.1f;

        [Header("Impact sound")]
        [Tooltip("Any short one-shot from your FMOD project, such as a clap or a hit.")]
        [SerializeField] private EventReference impactEvent;
        [Tooltip("Left as Nothing, it uses the Solid layer.")]
        [SerializeField] private LayerMask wallLayers;
        [SerializeField] private float maxShotDistance = 40f;
        [Tooltip("Off plays only the direct sound, for comparison.")]
        [SerializeField] private bool playEchoes = true;

        [Header("Look")]
        [SerializeField] private Color colour = new Color(0.35f, 0.75f, 1f);

        private Rigidbody2D body;
        private Collider2D bodyCollider;
        private Camera viewCamera;
        private bool warnedNoEvent;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            bodyCollider = GetComponent<Collider2D>();
            body.freezeRotation = true;

            if (groundLayers.value == 0) groundLayers = LayerMask.GetMask("Solid");
            if (wallLayers.value == 0) wallLayers = LayerMask.GetMask("Solid");

            // The package ships no art: draw a plain block if no sprite is set.
            var sprite = GetComponent<SpriteRenderer>();
            if (sprite != null && sprite.sprite == null)
            {
                Texture2D white = Texture2D.whiteTexture;
                sprite.sprite = Sprite.Create(white, new Rect(0f, 0f, white.width, white.height),
                                              new Vector2(0.5f, 0.5f), white.width);
                sprite.color = colour;

                // A box collider added before the sprite existed sized itself to nothing.
                // Match it to the 1 x 1 block (the transform's scale sets the real size).
                if (bodyCollider is BoxCollider2D box)
                {
                    box.size = Vector2.one;
                    box.offset = Vector2.zero;
                }
            }
        }

        private void Update()
        {
            float input = 0f;
            if (KeyInput.Held(KeyCode.D) || KeyInput.Held(KeyCode.RightArrow)) input += 1f;
            if (KeyInput.Held(KeyCode.A) || KeyInput.Held(KeyCode.LeftArrow)) input -= 1f;

            Vector2 velocity = body.linearVelocity;
            velocity.x = input * moveSpeed;

            bool jump = KeyInput.Down(KeyCode.Space) || KeyInput.Down(KeyCode.W) || KeyInput.Down(KeyCode.UpArrow);
            if (jump && IsGrounded()) velocity.y = jumpSpeed;

            body.linearVelocity = velocity;

            if (ClickedThisFrame(out Vector2 screenPoint)) Fire(screenPoint);
        }

        private bool IsGrounded()
        {
            if (bodyCollider == null) return true;
            Bounds bounds = bodyCollider.bounds;
            RaycastHit2D hit = Physics2D.BoxCast(bounds.center, bounds.size * 0.95f, 0f,
                                                 Vector2.down, groundCheckDistance, groundLayers);
            return hit.collider != null;
        }

        private void Fire(Vector2 screenPoint)
        {
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera == null) return;

            Vector3 world = viewCamera.ScreenToWorldPoint(
                new Vector3(screenPoint.x, screenPoint.y, -viewCamera.transform.position.z));

            Vector2 origin = body.position;
            Vector2 toTarget = (Vector2)world - origin;
            if (toTarget.sqrMagnitude < 0.0001f) return;

            RaycastHit2D hit = Physics2D.Raycast(origin, toTarget.normalized, maxShotDistance, wallLayers);
            if (hit.collider == null) return;

            Debug.DrawLine(origin, hit.point, Color.cyan, 0.5f);

            if (impactEvent.IsNull)
            {
                if (!warnedNoEvent)
                    Debug.LogWarning("[DemoPlayer2D] Assign an Impact Event to hear the shot and its echoes.", this);
                warnedNoEvent = true;
                return;
            }

            RuntimeManager.PlayOneShot(impactEvent, hit.point);
            if (playEchoes) ImageSourceEchoes2D.Emit(impactEvent, hit.point);
        }

        private static bool ClickedThisFrame(out Vector2 screenPoint)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screenPoint = mouse.position.ReadValue();
                return true;
            }
#else
            if (Input.GetMouseButtonDown(0))
            {
                screenPoint = Input.mousePosition;
                return true;
            }
#endif
            screenPoint = default;
            return false;
        }
    }
}