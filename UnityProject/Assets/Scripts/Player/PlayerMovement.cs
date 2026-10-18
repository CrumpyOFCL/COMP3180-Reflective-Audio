using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    #region - Variable Init -
    [HideInInspector] public int extraJumpsRemaining;
    [HideInInspector] public float jumpInput;
    [HideInInspector] public bool isOnGround;

    [HideInInspector] public Rigidbody2D rb;
    [HideInInspector] public bool isDashing;
    [HideInInspector] public PlayerDash dashComponent;

    [Header("Movement Values")]
    [SerializeField] private float movementSpeed;
    [HideInInspector] public float speedMultiplier = 1f;
    
    [Header("Platform Momentum")]
    [SerializeField] private float platformMomentumDamping = 12f;
    private Vector2 platformMomentum;       // Decaying momentum, used AFTER leaving a platform
    private Vector2 currentPlatformVelocity; // Live velocity of the platform you're STANDING ON

    [Header("Staff Knockback")]
    [SerializeField] private float staffKnockbackDamping = 18f;
    private Vector2 staffKnockbackVelocity;


    [Header("Jump Values")]
    [SerializeField] private float jumpForce;
    [SerializeField] private float heldJumpGravityScale = 1.2f;
    [SerializeField] private float releasedJumpGravityScale = 3.5f;
    [SerializeField] private float maxJumpHoldTime = 0.15f;
    private float jumpHoldTimer;

    [Space]

    [SerializeField] private float jumpBufferSeconds = 0.15f;
    [SerializeField] private float coyoteTimeSeconds = 0.15f;
    private float jumpBufferTimer;
    private float coyoteTimeTimer;

    [Tooltip("Ground contact is ignored for this long after a jump. On the frame " +
             "after take-off the player has barely left the ground check, so it " +
             "still reports grounded and hands the extra jump straight back. The " +
             "higher the framerate the less you move per frame, so this got worse " +
             "the better the machine.")]
    [SerializeField] private float jumpGroundLockout = 0.1f;
    private float jumpLockoutTimer;
    #endregion

    public int ExtraJumpsRemaining => extraJumpsRemaining;
    public float JumpBufferTimer => jumpBufferTimer;
    public float CoyoteTimeTimer => coyoteTimeTimer;
    public bool IsStandingOnPlatform { get; private set; }
    public Rigidbody2D SupportingPlatform { get; private set; }

    // Called by PlatformPassenger on collision exit — seeds decaying momentum
    public void AddPlatformMomentum(Vector2 momentum)
    {
        platformMomentum += momentum;
    }

    // Called by PlatformPassenger EVERY FRAME while standing on a moving platform.
    // Passing Vector2.zero clears it (e.g. on dismount).
    public void SetCurrentPlatformVelocity(Vector2 velocity)
    {
        currentPlatformVelocity = velocity;
    }

    public void SetPlatformSupport(Rigidbody2D platformBody, bool supported)
    {
        IsStandingOnPlatform = supported;
        SupportingPlatform = supported ? platformBody : null;
    }

    // Keeps staff recoil alive through Move(), which rebuilds horizontal velocity.
    public void AddStaffKnockback(Vector2 impulse)
    {
        staffKnockbackVelocity += impulse;
    }

    private void Start()
    {
    }

    private void Update()
    {
        if (jumpLockoutTimer > 0) { jumpLockoutTimer -= Time.deltaTime; }
        if (jumpBufferTimer >= 0) { jumpBufferTimer -= Time.deltaTime; }
        if (!isOnGround && coyoteTimeTimer >= 0) { coyoteTimeTimer -= Time.deltaTime; }

        if (jumpBufferTimer > 0)
        {
            if (coyoteTimeTimer > 0 || extraJumpsRemaining > 0)
            {
                ApplyJump();
            }
        }

        ShortJumpPhysics();
    }


    #region Move
    public void Move(Vector2 inputDirection)
    {
        // Decay the "leftover" momentum that exists after leaving a platform.
        // currentPlatformVelocity (active carry) is NOT decayed — it's set externally.
        if (isOnGround)
        {
            platformMomentum = Vector2.zero;
        }
        else
        {
            platformMomentum = Vector2.MoveTowards(
                platformMomentum,
                Vector2.zero,
                platformMomentumDamping * Time.fixedDeltaTime
            );
        }

        staffKnockbackVelocity = Vector2.MoveTowards(
            staffKnockbackVelocity,
            Vector2.zero,
            staffKnockbackDamping * Time.fixedDeltaTime
        );

        // Sum: own input + live platform carry + decaying leftover momentum.
        // Because currentPlatformVelocity is baked into linearVelocity each frame
        // while on the platform, ApplyJump (which preserves linearVelocity.x)
        // automatically launches the player with the platform's momentum.
        float horizontal =
            inputDirection.x * movementSpeed * speedMultiplier
            + currentPlatformVelocity.x
            + (isOnGround ? 0f : platformMomentum.x)
            + staffKnockbackVelocity.x;

        float vertical = rb.linearVelocity.y;

        // A descending platform drops away faster than gravity pulls the player
        // down, so contact broke and re-made every physics step - the flicker
        // groundedVisualGrace was added to hide. Matching the descent keeps them
        // touching. Only downward, and only while grounded, so it can't cancel a
        // jump or hold the player up on a rising platform.
        if (isOnGround && jumpLockoutTimer <= 0 && currentPlatformVelocity.y < 0f)
        {
            vertical = Mathf.Min(vertical, currentPlatformVelocity.y);
        }

        rb.linearVelocity = new Vector2(horizontal, vertical);
    }
    #endregion

    #region Jump
    public void Jump()
    {
        jumpBufferTimer = jumpBufferSeconds;
    }


    private void ApplyJump()
    {
        // Was comparing a cached flag built with >= against a branch using >, so
        // a coyote timer sitting at exactly zero read as "grounded jump" and the
        // air jump was never charged for.
        bool groundedJump = coyoteTimeTimer > 0f;

        if (!groundedJump && extraJumpsRemaining > 0)
        {
            extraJumpsRemaining--;
        }

        // linearVelocity.x is preserved here — and because Move() above injects
        // currentPlatformVelocity.x into linearVelocity each frame while on a
        // platform, jumping off retains that velocity automatically.
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
        rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);

        jumpHoldTimer = maxJumpHoldTime;

        jumpBufferTimer = 0;
        coyoteTimeTimer = 0;
        jumpLockoutTimer = jumpGroundLockout;
    }
    #endregion

    public void RefreshJumps(int totalJumps)
    {
        // Ignored while the lockout runs. The ground check is still overlapping
        // for a frame or two after take-off, and topping up here is what used to
        // refund the extra jump.
        if (jumpLockoutTimer > 0) return;

        extraJumpsRemaining = totalJumps;
        coyoteTimeTimer = coyoteTimeSeconds;
    }


    void ShortJumpPhysics()
    {
        if (isDashing || (dashComponent != null && dashComponent.isDashing))
            return;

        // A ladder holds the player up by zeroing gravity. This runs every
        // frame, so without this it would put gravity straight back.

        if (rb.linearVelocity.y > 0)
        {
            jumpHoldTimer -= Time.deltaTime;

            bool holdingJump = jumpInput > 0 && jumpHoldTimer > 0;

            rb.gravityScale = holdingJump
                ? heldJumpGravityScale
                : releasedJumpGravityScale;
        }
        else
        {
            rb.gravityScale = releasedJumpGravityScale;
        }
    }
}