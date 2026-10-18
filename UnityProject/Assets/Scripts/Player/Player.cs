using UnityEngine;

// SHOWCASE VERSION of the Group Flux player, trimmed for the reflective-audio
// test scene: walk, jump, (optional) dash and shoot the time beam.
//
// Removed from the game version because those systems are not in this repo:
// skills/inventory, saving, respawn, time switching, climbing, box pushing,
// platform drop-through, staff crystals, pause/game-over checks.
//
// Field names are unchanged so the Player_New prefab keeps its Inspector
// values. Audio calls into PlayerAudio are exactly as in the game.
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(Animator))]
public class Player : MonoBehaviour
{
    #region - Variable Initialization -

    private Vector2 moveInput;
    private bool isOnGround;
    private bool isDashing;

    [Header("GameObjects")]
    [SerializeField] private SpriteRenderer sprite;
    [SerializeField] private GameObject staffTipObject;
    [SerializeField] private Transform staffTipTransform;
    [SerializeField] private Rigidbody2D rigidBody;
    [SerializeField] private BoxCollider2D playerCollider;
    [SerializeField] private Animator animator;

    [Header("Components")]
    [SerializeField] private PlayerInputManager inputManager;
    [SerializeField] private PlayerAnimator animationComponent;
    [SerializeField] private PlayerMovement movementComponent;
    [SerializeField] private PlayerDash dashComponent;
    [SerializeField] private PlayerAimAndShoot shootComponent;
    [SerializeField] private PlayerAudio audioComponent;

    [Header("Player Stat Values")]
    [Range(0, 10)][SerializeField] private int totalDashes;
    [Range(0, 10)][SerializeField] private int extraJumps;

    [Header("Ground Check")]
    [Tooltip("Keeps the grounded look for a moment after the ground check drops out.")]
    [SerializeField] private float groundedVisualGrace = 0.12f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.5f, 0.2f);
    [SerializeField] private LayerMask groundLayers;

    [Header("Movement Input")]
    [Range(0.1f, 1f)][SerializeField] private float verticalMovementPriority = 0.5f;
    [Range(0f, 1f)][SerializeField] private float horizontalDriftLimit = 0.3f;

    [Header("Showcase")]
    [Tooltip("Shooting is always available in the showcase (no skill unlock).")]
    [SerializeField] private bool canShoot = true;
    [Tooltip("Dash is off by default: the showcase only needs walking, jumping and shooting.")]
    [SerializeField] private bool hasDashUpgrade;
    [SerializeField] private bool hasDoubleJumpUpgrade;

    private float lastGroundedTime = float.NegativeInfinity;
    private Transform staffTip;
    private SpriteRenderer staffTipRenderer;

    private TimeSingleton.GameState cachedMaskEra;
    private bool hasCachedMasks;
    private LayerMask cachedGroundLayers;

    public PlayerMovement MovementComponent => movementComponent;
    public PlayerDash DashComponent => dashComponent;
    public Collider2D PlayerCollider => playerCollider;

    // Read by PlayerAimAndShoot to decide whether shooting is allowed.
    public bool IsActivelyMoving => Mathf.Abs(moveInput.x) > 0.01f || isDashing || !isOnGround;

    #endregion

    private void Awake()
    {
        if (playerCollider == null) playerCollider = GetComponent<BoxCollider2D>();
        if (audioComponent == null) audioComponent = GetComponent<PlayerAudio>();

        if (!ReferencesAreWired())
        {
            enabled = false;
            return;
        }

        movementComponent.rb = rigidBody;
        movementComponent.dashComponent = dashComponent;
        if (dashComponent != null) dashComponent.rb = rigidBody;

        staffTip = staffTipTransform != null
            ? staffTipTransform
            : (staffTipObject != null ? staffTipObject.transform : null);
        if (staffTip != null) staffTipRenderer = staffTip.GetComponent<SpriteRenderer>();
    }

    private bool ReferencesAreWired()
    {
        bool wired = true;
        wired &= Require(rigidBody, nameof(rigidBody));
        wired &= Require(sprite, nameof(sprite));
        wired &= Require(animationComponent, nameof(animationComponent));
        wired &= Require(movementComponent, nameof(movementComponent));
        wired &= Require(shootComponent, nameof(shootComponent));
        wired &= Require(inputManager, nameof(inputManager));
        wired &= Require(groundCheck, nameof(groundCheck));
        return wired;
    }

    private bool Require(Object reference, string fieldName)
    {
        if (reference != null) return true;
        Debug.LogError($"From Player: '{fieldName}' is not assigned on {name}. Player disabled.", this);
        return false;
    }

    private void Start()
    {
        if (hasDoubleJumpUpgrade) extraJumps++;
        if (canShoot) animationComponent.SwitchToWandAnim();
    }

    // Ground layers exclude the eras the player is not standing in, as in the game.
    private LayerMask GetActiveGroundLayers()
    {
        TimeSingleton time = TimeSingleton.Instance;
        if (time == null) return groundLayers;

        if (hasCachedMasks && cachedMaskEra == time.currentGameState) return cachedGroundLayers;

        cachedMaskEra = time.currentGameState;
        hasCachedMasks = true;
        cachedGroundLayers = groundLayers & ~time.InactiveEraMask;
        return cachedGroundLayers;
    }

    private void Update()
    {
        isDashing = dashComponent != null && dashComponent.isDashing;
        movementComponent.isDashing = isDashing;
        animationComponent.PlayDashAnimation(isDashing, !sprite.flipX);
        if (isDashing) return;

        shootComponent.currentController = inputManager.currentController;
        shootComponent.inputType = inputManager.inputType;

        moveInput = ReadMovementInput();
        animationComponent.inputDirection = moveInput;

        if (dashComponent != null)
            dashComponent.inputDirection = sprite.flipX ? Vector2.left : Vector2.right;

        // Shooting while moving (if the aim component allows it).
        bool shootHandledThisFrame = false;
        if (canShoot && shootComponent.AllowShootingWhileMoving && inputManager.shootAction.triggered)
        {
            shootHandledThisFrame = true;
            TryShoot();
        }

        UpdateGroundedPresentation();
        FlipSprite();

        movementComponent.jumpInput = inputManager.jumpAction.ReadValue<float>();

        if (inputManager.jumpAction.triggered)
        {
            bool canJump = movementComponent.CoyoteTimeTimer > 0 || movementComponent.ExtraJumpsRemaining > 0;
            movementComponent.Jump();

            if (canJump)
            {
                if (audioComponent != null) audioComponent.PlayJump(movementComponent.ExtraJumpsRemaining);
                animationComponent.PlayJumpParticleAnimation(groundCheck.position);
            }
        }

        if (hasDashUpgrade && dashComponent != null && inputManager.dashAction.triggered)
        {
            if (dashComponent.Dash() && audioComponent != null) audioComponent.PlayDash();
        }

        // The grounded shot.
        if (canShoot && !shootHandledThisFrame && isOnGround && inputManager.shootAction.triggered)
            TryShoot();

        bool falling = rigidBody.linearVelocityY < -0.1f;
        animationComponent.PlayFallingAnimation(falling);
    }

    private void TryShoot()
    {
        if (shootComponent.Shoot() && audioComponent != null)
            audioComponent.PlayShoot();
    }

    private Vector2 ReadMovementInput()
    {
        Vector2 input = inputManager.moveAction.ReadValue<Vector2>();

        if (Mathf.Abs(input.y) >= verticalMovementPriority && Mathf.Abs(input.x) < horizontalDriftLimit)
            input.x = 0f;

        return input;
    }

    private void FixedUpdate()
    {
        CheckGround();

        if (dashComponent == null || !dashComponent.isDashing)
        {
            moveInput = ReadMovementInput();
            float horizontalInput = Mathf.Abs(moveInput.x) > 0.01f ? Mathf.Sign(moveInput.x) : 0f;
            movementComponent.Move(new Vector2(horizontalInput, 0f));
        }
    }

    // Landing sound from the impact speed, exactly as in the game.
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (audioComponent == null) return;
        if (collision.contactCount == 0 || collision.GetContact(0).normal.y < 0.5f) return;
        audioComponent.PlayLanding(Mathf.Abs(collision.relativeVelocity.y));
    }

    private void CheckGround()
    {
        bool groundOverlap = Physics2D.OverlapCapsule(
            groundCheck.position, groundCheckSize, CapsuleDirection2D.Horizontal, 0f, GetActiveGroundLayers());

        isOnGround = groundOverlap || movementComponent.IsStandingOnPlatform;
        movementComponent.isOnGround = isOnGround;

        if (isOnGround)
        {
            lastGroundedTime = Time.time;
            movementComponent.RefreshJumps(extraJumps);
            if (dashComponent != null) dashComponent.RefreshDash(totalDashes);
        }
    }

    private void UpdateGroundedPresentation()
    {
        bool looksGrounded = isOnGround || Time.time - lastGroundedTime <= groundedVisualGrace;

        if (audioComponent != null)
            audioComponent.UpdateWalking(looksGrounded, Mathf.Abs(moveInput.x) > 0.01f);

        animationComponent.PlayGroundAnimation(looksGrounded, groundCheck.position);
    }

    private void FlipSprite()
    {
        if (Mathf.Abs(moveInput.x) > 0.01f)
        {
            ApplyFacingDirection(moveInput.x < -0.01f);
        }
        else if (canShoot)
        {
            float xDiff = shootComponent.CursorWorldPosition.x - transform.position.x;
            if (Mathf.Abs(xDiff) > 0.01f) ApplyFacingDirection(xDiff < 0f);
        }
    }

    public void FaceDirection(float direction)
    {
        if (Mathf.Abs(direction) > 0.01f) ApplyFacingDirection(direction < 0f);
    }

    private void ApplyFacingDirection(bool flip)
    {
        sprite.flipX = flip;

        if (staffTip != null)
        {
            Vector3 localPos = staffTip.localPosition;
            localPos.x = flip ? -Mathf.Abs(localPos.x) : Mathf.Abs(localPos.x);
            staffTip.localPosition = localPos;

            if (staffTipRenderer != null)
            {
                staffTipRenderer.flipX = flip;
            }
            else
            {
                Vector3 tipScale = staffTip.localScale;
                tipScale.x = flip ? -Mathf.Abs(tipScale.x) : Mathf.Abs(tipScale.x);
                staffTip.localScale = tipScale;
            }
        }

        animationComponent.SwitchTimeSwitchSprite(!flip);
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(groundCheck.position, (Vector3)groundCheckSize);
        }

        if (playerCollider != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(playerCollider.bounds.center, playerCollider.bounds.size);
        }
    }
}
