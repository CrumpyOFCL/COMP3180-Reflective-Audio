using System;

using System.Collections;

using System.Collections.Generic;

using UnityEngine;

using UnityEngine.Serialization;

[RequireComponent(typeof(SpriteRenderer))]

[RequireComponent(typeof(Rigidbody2D))]

[RequireComponent(typeof(BoxCollider2D))]

[RequireComponent(typeof(Animator))]

public class Player : MonoBehaviour

{

    #region - Variable Initialization -

    private Vector2 moveInput;

    private Vector2 lastNonZeroMoveInput = Vector2.right;

    private bool isOnGround;

    private bool isDashing;

    private bool isDropping;

    /// <summary>True while DropThroughPlatformCoroutine is ignoring platform collision.</summary>
    public bool IsDroppingThroughPlatform => isDropping;


    private SkillTree playerSkills;

    [Header("GameObjects")]

    [SerializeField] private SpriteRenderer sprite;

    [SerializeField] private GameObject staffTipObject;

    [SerializeField] private Transform staffTipTransform;

    [SerializeField] private Rigidbody2D rigidBody;

    [SerializeField] private BoxCollider2D playerCollider;

    private Vector3 originalScale;

    [SerializeField] private Animator animator;

    [Header("Components")]

    [SerializeField] private PlayerInputManager inputManager;

    [SerializeField] private PlayerAnimator animationComponent;

    [SerializeField] private PlayerMovement movementComponent;

    [SerializeField] private PlayerDash dashComponent;

    [SerializeField] private PlayerSwitchTime switchComponent;

    [SerializeField] private PlayerAimAndShoot shootComponent;

    [SerializeField] private PlayerClimb climbComponent;

    [SerializeField] private BeamManager beamManager;

    [SerializeField] private PlayerAudio audioComponent;

    [SerializeField] private BoxPush boxPushComponent;

    [SerializeField] private UIInventoryManager inventoryUI;

    [Header("Player Stat Values")]

    [Range(0, 10)][SerializeField] private int totalDashes;

    [Range(0, 10)][SerializeField] private int extraJumps;

    [Header("Ground Check")]

    [Tooltip("Keeps the grounded look for a moment after the ground check drops out. " +

             "A descending platform steps out from under the check each physics frame, " +

             "which otherwise reads as landing over and over.")]

    [SerializeField] private float groundedVisualGrace = 0.12f;

    [SerializeField] private Transform groundCheck;

    [SerializeField] private float checkRadius;

    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.5f, 0.2f);

    [SerializeField] private LayerMask groundLayers;

    [Header("Grounded Platform Drop-Through")]

    [SerializeField] private LayerMask platformLayerMask;

    [Tooltip("Collision-ignore duration for ordinary grounded platform drops. Ladder drops are controlled by PlayerClimb.")]

    [FormerlySerializedAs("dropThroughDuration")]

    [Range(0.05f, 1f)]

    [SerializeField] private float groundedDropDuration = 0.3f;

    [Header("Movement Input")]

    [Tooltip("Vertical input at or above this value suppresses minor horizontal stick drift.")]

    [Range(0.1f, 1f)]

    [SerializeField] private float verticalMovementPriority = 0.5f;

    [Tooltip("While pushing up or down, horizontal input below this counts as stick drift and is ignored. " +

             "Anything stronger is a real diagonal and still moves the player.")]

    [Range(0f, 1f)]

    [SerializeField] private float horizontalDriftLimit = 0.3f;

    [Header("Debug Upgrades")]

    [SerializeField] private bool hasDashUpgrade;

    [SerializeField] private bool hasSwitchTimeUpgrade;

    [SerializeField] private bool hasBeamUpgrade;

    [SerializeField] private bool hasDoubleJumpUpgrade;

    [Tooltip("Start with the alchemy crystal, so the staff can cycle to it.")]

    [SerializeField] private bool hasAlchemyCrystalUpgrade;

    [Header("Playtest 1 A-B test")]

    [SerializeField] private bool canInteractInAir;

    private float lastGroundedTime = float.NegativeInfinity;

    // Last frame's climbing state, so the ladder grab sound plays once.
    private bool wasClimbing;

    // Cached lookups. All of these were being fetched or recomputed every frame.

    private PlayerRespawn respawnComponent;

    private SpriteRenderer staffTipRenderer;

    private Transform staffTip;

    private bool hasBeamSkill;

    private bool hasDashSkill;

    private bool hasSwitchSkill;

    private TimeSingleton.GameState cachedMaskEra;

    private bool hasCachedMasks;

    private LayerMask cachedGroundLayers;

    private LayerMask cachedPlatformLayers;

    public SkillTree PlayerSkills => playerSkills;

    public PlayerMovement MovementComponent => movementComponent;

    public PlayerDash DashComponent => dashComponent;

    public BeamManager BeamManagerRef => beamManager;

    public Collider2D PlayerCollider => playerCollider;

    public bool IsActivelyMoving

    {

        get

        {

            // Horizontal only. W/S on the ground don't move the player, and
            // climbing is covered below, so they shouldn't lock out shooting.
            bool hasMovementInput =

                Mathf.Abs(moveInput.x) > 0.01f;

            bool climbing =

                climbComponent != null &&

                climbComponent.isClimbing;

            bool airborne = !isOnGround;

            return hasMovementInput ||

                   isDashing ||

                   climbing ||

                   airborne;

        }

    }

    #endregion

    private void Awake()

    {

        if (playerCollider == null)

            playerCollider = GetComponent<BoxCollider2D>();

        // Every other method here guards its references; Awake assumed six of

        // them. One empty inspector slot gave a bare NullReferenceException on

        // an unnamed line and the player was dead with nothing to go on.

        if (!ReferencesAreWired())

        {

            enabled = false;

            return;

        }

        switchComponent.playerCollider = playerCollider;

        playerSkills = new SkillTree();

        movementComponent.rb = rigidBody;

        movementComponent.dashComponent = dashComponent;

        movementComponent.climbComponent = climbComponent;

        switchComponent.rb = rigidBody;

        dashComponent.rb = rigidBody;

        climbComponent.rb = rigidBody;

        boxPushComponent.rb = rigidBody;

        boxPushComponent.playerCollider = playerCollider;

        boxPushComponent.movementComponent = movementComponent;

        if (audioComponent == null)

            audioComponent = GetComponent<PlayerAudio>();

        staffCharge = GetComponentInChildren<StaffCharge>();

        respawnComponent = GetComponent<PlayerRespawn>();

        staffTip = staffTipTransform != null

            ? staffTipTransform

            : (staffTipObject != null ? staffTipObject.transform : null);

        if (staffTip != null) staffTipRenderer = staffTip.GetComponent<SpriteRenderer>();

        originalScale = transform.localScale;

    }

    private bool ReferencesAreWired()

    {

        bool wired = true;

        wired &= Require(rigidBody, nameof(rigidBody));

        wired &= Require(sprite, nameof(sprite));

        wired &= Require(animationComponent, nameof(animationComponent));

        wired &= Require(movementComponent, nameof(movementComponent));

        wired &= Require(dashComponent, nameof(dashComponent));

        wired &= Require(switchComponent, nameof(switchComponent));

        wired &= Require(shootComponent, nameof(shootComponent));

        wired &= Require(climbComponent, nameof(climbComponent));

        wired &= Require(boxPushComponent, nameof(boxPushComponent));

        wired &= Require(inputManager, nameof(inputManager));

        wired &= Require(groundCheck, nameof(groundCheck));

        return wired;

    }

    private bool Require(UnityEngine.Object reference, string fieldName)

    {

        if (reference != null) return true;

        Debug.LogError($"From Player: '{fieldName}' is not assigned on {name}. Player disabled.", this);

        return false;

    }

    public SaveData CaptureSaveData()

    {

        SaveData data = new SaveData();

        data.playerPosition = transform.position;

        data.timeState = TimeSingleton.Instance != null

            ? (int)TimeSingleton.Instance.currentGameState

            : 0;

        data.extraJumpsTotal = extraJumps;

        data.totalDashes = totalDashes;

        data.unlockedSkills = playerSkills.GetUnlockedSkillsForSave();

        playerSkills.GetImprovementsForSave(out data.improvementKeys, out data.improvementCounts);

        if (beamManager != null)

            beamManager.CaptureStats(data);

        if (respawnComponent != null)

            respawnComponent.GetCheckpointForSave(

                out data.respawnScene, out data.respawnPosition,

                out data.respawnCheckpointId, out data.respawnTimeState);

        return data;

    }

    public void ApplySaveData(SaveData data)

    {

        if (data == null) return;

        transform.position = data.playerPosition;

        if (rigidBody != null) rigidBody.linearVelocity = Vector2.zero;

        if (TimeSingleton.Instance != null)

            TimeSingleton.Instance.SetTime((TimeSingleton.GameState)data.timeState);

        extraJumps = data.extraJumpsTotal;

        totalDashes = data.totalDashes;

        playerSkills.LoadFromSave(data.unlockedSkills, data.improvementKeys, data.improvementCounts);

        RefreshSkillFlags();

        if (beamManager != null)

            beamManager.ApplyStats(data);

        // Reapply visual side-effects

        if (playerSkills.CheckSkill(SkillTree.Skill.TimeBeam) && animationComponent != null)

            animationComponent.SwitchToWandAnim();

        if (respawnComponent != null)

            respawnComponent.RestoreCheckpointFromSave(

                data.respawnScene, data.respawnPosition, data.respawnCheckpointId,

                (TimeSingleton.GameState)data.respawnTimeState);

        if (inventoryUI != null)

        {

            inventoryUI.RefreshUI(playerSkills);

        }

    }

    private void Start()

    {

        DebugCode();

        RefreshSkillFlags();

    }

    // CheckSkill walks a list, and Update asked it three or four times a frame.

    // Anything that changes the skill set calls this instead.

    private void RefreshSkillFlags()

    {

        hasBeamSkill = playerSkills.CheckSkill(SkillTree.Skill.TimeBeam);

        hasDashSkill = playerSkills.CheckSkill(SkillTree.Skill.Dash);

        hasSwitchSkill = playerSkills.CheckSkill(SkillTree.Skill.TimeSwitch);

    }

    // Both masks only change when the era does, so they are rebuilt then rather

    // than on every ground check.

    private void RefreshEraMasks()

    {

        TimeSingleton time = TimeSingleton.Instance;

        if (time == null)

        {

            cachedGroundLayers = groundLayers;

            cachedPlatformLayers = platformLayerMask;

            hasCachedMasks = false;

            return;

        }

        if (hasCachedMasks && cachedMaskEra == time.currentGameState) return;

        cachedMaskEra = time.currentGameState;

        hasCachedMasks = true;

        int inactive = time.InactiveEraMask;

        cachedGroundLayers = groundLayers & ~inactive;

        cachedPlatformLayers = platformLayerMask & ~inactive;

    }

    private LayerMask GetActiveGroundLayers()

    {

        RefreshEraMasks();

        return cachedGroundLayers;

    }

    private LayerMask GetActivePlatformLayers()

    {

        RefreshEraMasks();

        return cachedPlatformLayers;

    }

    private void Update()

    {

        if (PauseMenu.IsPaused || GameOverMenu.IsGameOver) return;

        // While holding a box the player can only push and pull it.
        bool grabbing = boxPushComponent != null && boxPushComponent.IsGrabbing;

        bool dropPressed = !grabbing && inputManager.dropAction != null && inputManager.dropAction.WasPressedThisFrame();

        // DROP THROUGH PLATFORM (grounded platforms)

        // Guarded: mashing the key used to start a second coroutine, and the

        // first one to finish handed collision back while the other still

        // expected it off.

        if (dropPressed && RequestDropThroughPlatform() &&

            climbComponent != null && climbComponent.isClimbing)

        {

            climbComponent.StopClimbing();

        }

        isDashing = dashComponent.isDashing;

        movementComponent.isDashing = isDashing;

        animationComponent.PlayDashAnimation(isDashing, !sprite.flipX);

        if (isDashing) return;

        shootComponent.currentController = inputManager.currentController;

        shootComponent.inputType = inputManager.inputType;

        moveInput = ReadMovementInput();

        animationComponent.inputDirection = moveInput;

        if (moveInput != Vector2.zero)

            lastNonZeroMoveInput = moveInput;

        dashComponent.inputDirection = sprite.flipX ? Vector2.left : Vector2.right;

        /*

 * Movement shooting is handled before climbing because the climbing section

 * returns early while the player remains attached to a ladder.

 */

        bool shootHandledThisFrame =

            false;

        if (!grabbing &&

            shootComponent != null &&

            shootComponent.AllowShootingWhileMoving &&

            inputManager.shootAction.triggered &&

            hasBeamSkill)

        {

            shootHandledThisFrame =

                true;

            bool shootSucceeded =

                shootComponent.Shoot();

            if (shootSucceeded &&

                audioComponent != null)

            {

                audioComponent.PlayShoot();

            }

        }

        // Climbing

        if (climbComponent != null)

        {

            if (!grabbing)

                climbComponent.ClimbInput(

                    moveInput

                );

            if (climbComponent.isClimbing &&

                inputManager.jumpAction.triggered)

            {

                climbComponent.JumpOffLadder();

            }

            // Climb presentation: bool + optional vertical float, re-asserted in
            // PlayerAnimator.LateUpdate so moving/ground/fall cannot yank out of climb.
            bool climbing = climbComponent.isClimbing;
            float vertical = climbComponent.VerticalClimbInput;
            animationComponent.SetClimbPresentation(climbing, vertical);
            animationComponent.SetClimbPlaybackSpeed(1f);

            if (audioComponent != null)
            {
                // A grab sound on getting on (including a drop catch), then
                // rung steps only while actually moving up or down.
                if (climbing && !wasClimbing)
                    audioComponent.PlayClimbGrab();

                audioComponent.UpdateClimbing(climbing, climbComponent.IsClimbAnimating);
            }

            wasClimbing = climbing;

        }

        UpdateGroundedPresentation();

        FlipSprite();

        movementComponent.jumpInput = inputManager.jumpAction.ReadValue<float>();

        if (inputManager.jumpAction.triggered && !isDropping && !grabbing)

        {

            bool canJump = movementComponent.CoyoteTimeTimer > 0 || movementComponent.ExtraJumpsRemaining > 0;

            movementComponent.Jump();

            if (audioComponent != null && canJump)

                audioComponent.PlayJump(movementComponent.ExtraJumpsRemaining);

            if (canJump)

            {

                animationComponent.PlayJumpParticleAnimation(groundCheck.position);

            }

        }

        if (inputManager.dashAction.triggered && hasDashSkill && !grabbing)

        {

            bool dashSucceeded = dashComponent.Dash();

            if (dashSucceeded && audioComponent != null)

                audioComponent.PlayDash();

        }

        // Time switch, crystal cycle and the grounded shot all live in here.
        if (!grabbing && (isOnGround || canInteractInAir))

        {

            if (inputManager.switchAction.triggered && hasSwitchSkill)

            {

                TrySwitchTime(1);

            }

            if (inputManager.switchBackAction.triggered && hasSwitchSkill)

            {

                TrySwitchTime(-1);

            }

            CheckCrystalCycle();

            if (!shootHandledThisFrame &&

     inputManager.shootAction.triggered &&

     hasBeamSkill)

            {

                bool shootSucceeded =

                    shootComponent.Shoot();

                if (shootSucceeded &&

                    audioComponent != null)

                {

                    audioComponent.PlayShoot();

                }

            }

        }

        bool falling =
            rigidBody.linearVelocityY < -0.1f &&
            (climbComponent == null || !climbComponent.isClimbing);

        animationComponent.PlayFallingAnimation(falling);

    }

    private Vector2 ReadMovementInput()

    {

        Vector2 input = inputManager.moveAction.ReadValue<Vector2>();

        // Only drift is dropped. This used to zero every horizontal input while
        // up or down was held, so a diagonal stopped the player dead.
        if (Mathf.Abs(input.y) >= verticalMovementPriority &&

            Mathf.Abs(input.x) < horizontalDriftLimit)

            input.x = 0f;

        return input;

    }

    // D-pad left and right swap which crystal is socketed in the staff.

    private void CheckCrystalCycle()

    {

        if (staffCharge == null) return;

        if (inputManager.cycleCrystalNextAction != null

            && inputManager.cycleCrystalNextAction.triggered)

        {

            staffCharge.CycleCrystal(1);

        }

        if (inputManager.cycleCrystalPrevAction != null

            && inputManager.cycleCrystalPrevAction.triggered)

        {

            staffCharge.CycleCrystal(-1);

        }

    }

    private StaffCharge staffCharge;

    private void TrySwitchTime(int direction)

    {

        // Drop any held box before the era changes, otherwise it is left behind

        // on a layer we can no longer touch.

        if (boxPushComponent != null)

            boxPushComponent.ReleaseGrab();

        bool switched = switchComponent.SwitchTime(direction);

        if (audioComponent == null) return;

        if (switched)

        {

            audioComponent.PlayTimeSwitch(TimeSingleton.Instance.currentGameState, direction);

            animationComponent.PlayTimeSwitchAnimation();

        }

        else

        {

            audioComponent.PlaySwitchFail();

        }

    }

    private void FixedUpdate()

    {

        if (PauseMenu.IsPaused || GameOverMenu.IsGameOver) return;

        // Unconditional and on the physics clock. This used to live in Update

        // behind the dash and climb early-outs, so Move() below could be reading

        // a grounded flag that was several frames stale.

        CheckGround();

        if (dashComponent.isDashing == false &&

            (climbComponent == null || !climbComponent.isClimbing))

        {

            moveInput = ReadMovementInput();

            float horizontalInput = Mathf.Abs(moveInput.x) > 0.01f ? Mathf.Sign(moveInput.x) : 0f;

            movementComponent.Move(new Vector2(horizontalInput, 0f));

        }

        if (boxPushComponent != null)

        {

            // A grab can't start mid-dash or on a ladder. Neither can start
            // during a grab, so this only matters for the first step.
            bool canGrab = !dashComponent.isDashing &&

                           (climbComponent == null || !climbComponent.isClimbing);

            if (inputManager.boxPushAction.IsPressed() && canGrab)

                boxPushComponent.HoldGrab();

            else

                boxPushComponent.ReleaseGrab();

            animationComponent.PlayPushingAnimation(boxPushComponent.IsGrabbing, boxPushComponent.PushSpeed);

        }

    }

    private IEnumerator DropThroughPlatformCoroutine()

    {

        LayerMask activePlatformLayers = GetActivePlatformLayers();

        if (activePlatformLayers.value == 0)

        {

            Debug.LogError("Platform LayerMask is not set!");

            yield break;

        }

        if (groundCheck == null || playerCollider == null)

            yield break;

        Collider2D[] hits = Physics2D.OverlapCircleAll(

            groundCheck.position,

            checkRadius,

            activePlatformLayers

        );

        if (hits.Length == 0)

            yield break;

        isDropping = true;

        List<Collider2D> ignoredPlatforms = new List<Collider2D>();

        // Ignore ALL player colliders against ALL hit platforms

        foreach (Collider2D platform in hits)

        {

            if (platform == null)

                continue;

            Physics2D.IgnoreCollision(playerCollider, platform, true);

            ignoredPlatforms.Add(platform);

        }

        try

        {

            float elapsed = 0f;

            bool grabbedLadder = false;

            while (elapsed < groundedDropDuration)

            {

                if (!grabbedLadder &&

                    climbComponent != null &&

                    climbComponent.GrabIfOnLadder())

                {

                    grabbedLadder = true;

                }

                elapsed += Time.deltaTime;

                yield return null;

            }

            if (grabbedLadder && climbComponent != null)

            {

                while (climbComponent.isClimbing &&

                       IsStillBelowIgnoredPlatforms(ignoredPlatforms))

                {

                    yield return null;

                }

            }

        }

        finally

        {

            // Re-enable collisions even if this player object is disabled or

            // the coroutine is interrupted during a scene transition.

            foreach (Collider2D platform in ignoredPlatforms)

            {

                if (platform == null)

                    continue;

                Physics2D.IgnoreCollision(playerCollider, platform, false);

            }

            isDropping = false;

        }

    }

    private bool IsStillBelowIgnoredPlatforms(List<Collider2D> platforms)

    {

        if (playerCollider == null) return false;

        float playerBottom = playerCollider.bounds.min.y;

        foreach (Collider2D platform in platforms)

        {

            if (platform != null && playerBottom <= platform.bounds.max.y + 0.05f)

                return true;

        }

        return false;

    }

    public bool RequestDropThroughPlatform()

    {

        if (isDropping || groundCheck == null || playerCollider == null)

            return false;

        LayerMask activePlatformLayers = GetActivePlatformLayers();

        if (activePlatformLayers.value == 0 ||

            Physics2D.OverlapCircleAll(

                groundCheck.position,

                checkRadius,

                activePlatformLayers).Length == 0)

        {

            return false;

        }

        StartCoroutine(DropThroughPlatformCoroutine());

        return true;

    }

    // After a drop, poll each frame until the player enters a ladder trigger,

    // then auto-grab. Gives up after a short window so normal drops aren't affected.

    private IEnumerator GrabLadderAfterDrop()

    {

        float timeout = 0.5f;

        float elapsed = 0f;

        while (elapsed < timeout)

        {

            if (climbComponent != null && climbComponent.GrabIfOnLadder())

                yield break;

            elapsed += Time.deltaTime;

            yield return null;

        }

    }

    // Landing sound. PlayerAudio.PlayLanding existed but nothing called it.
    // Uses the impact speed from the collision, like RigidBox does for crates,
    // so a respawn or door teleport onto the ground (no impact) stays silent.
    // PlayLanding ignores anything under its Min Landing Speed.
    private void OnCollisionEnter2D(Collision2D collision)
    {

        if (audioComponent == null) return;

        // Climbing down onto the floor isn't a landing.
        if (climbComponent != null && climbComponent.isClimbing) return;

        // Only ground under the feet: the normal points up into the player.
        if (collision.contactCount == 0 || collision.GetContact(0).normal.y < 0.5f) return;

        audioComponent.PlayLanding(Mathf.Abs(collision.relativeVelocity.y));

    }

    private void CheckGround()

    {

        bool groundOverlap = Physics2D.OverlapCapsule(

            groundCheck.position,

            groundCheckSize,

            CapsuleDirection2D.Horizontal,

            0f,

            GetActiveGroundLayers()

        );

        // PlatformPassenger has already verified an upward supporting contact.

        // Use that result too because floating crates may be excluded from the

        // ground query layer mask while still being valid surfaces to stand on.

        isOnGround = groundOverlap || movementComponent.IsStandingOnPlatform;

        movementComponent.isOnGround = isOnGround;

        if (isOnGround) lastGroundedTime = Time.time;

        if (isOnGround)

        {

            // RefreshJumps ignores this while the post-jump lockout runs.

            movementComponent.RefreshJumps(extraJumps);

            dashComponent.RefreshDash(totalDashes);

        }

    }

    // Physics uses the raw check; the animation and the landing particle use a

    // buffered one, so a flickering contact isn't read as landing over and over.

    private void UpdateGroundedPresentation()

    {

        bool looksGrounded = isOnGround || Time.time - lastGroundedTime <= groundedVisualGrace;

        if (climbComponent != null && climbComponent.isClimbing)

            looksGrounded = false;

        if (audioComponent != null)

            // Horizontal only, the same test Move uses. W/S on their own don't
            // move a grounded player, but used to start the footsteps.
            audioComponent.UpdateWalking(looksGrounded, Mathf.Abs(moveInput.x) > 0.01f);

        animationComponent.PlayGroundAnimation(looksGrounded, groundCheck.position);

    }

    private void FlipSprite()

    {

        if (boxPushComponent != null && boxPushComponent.IsGrabbing)

            return;

        bool isMoving = Mathf.Abs(moveInput.x) > 0.01f;

        if (isMoving && (boxPushComponent == null || !boxPushComponent.IsGrabbing))

        {

            ApplyFacingDirection(moveInput.x < -0.01f);

        }

        else if (hasBeamSkill)

        {

            float xDiff = shootComponent.CursorWorldPosition.x - transform.position.x;

            if (Mathf.Abs(xDiff) > 0.01f)

                ApplyFacingDirection(xDiff < 0f);

        }

    }

    public void FaceDirection(float direction)

    {

        if (Mathf.Abs(direction) > 0.01f)

            ApplyFacingDirection(direction < 0f);

    }

    private void ApplyFacingDirection(bool flip)

    {

        sprite.flipX = flip;

        if (staffTip != null)

        {

            Vector3 localPos = staffTip.localPosition;

            localPos.x = sprite.flipX ? -Mathf.Abs(localPos.x) : Mathf.Abs(localPos.x);

            staffTip.localPosition = localPos;

            if (staffTipRenderer != null)

            {

                staffTipRenderer.flipX = sprite.flipX;

            }

            else

            {

                Vector3 tipScale = staffTip.localScale;

                tipScale.x = sprite.flipX ? -Mathf.Abs(tipScale.x) : Mathf.Abs(tipScale.x);

                staffTip.localScale = tipScale;

            }

        }

        animationComponent.SwitchTimeSwitchSprite(!sprite.flipX);

    }

    private void OnDrawGizmosSelected()

    {

        if (groundCheck != null)

        {

            Gizmos.color = Color.yellow;

            Gizmos.DrawWireCube(groundCheck.position, (Vector3)groundCheckSize);

        }

        // Draw each collider's actual bounds so you can see if flipping is working

        if (playerCollider != null)

        {

            Gizmos.color = Color.green;

            Gizmos.DrawWireCube(playerCollider.bounds.center, playerCollider.bounds.size);

        }

    }

    public void GetSkill(SkillTree.Skill skill)

    {

        playerSkills.UnlockSkill(skill);

        RefreshSkillFlags();

        if (inventoryUI != null)

        {

            inventoryUI.UnlockSkill(skill);

        }

        if (skill == SkillTree.Skill.TimeBeam)

        {

            animationComponent.SwitchToWandAnim();

        }

    }

    public void GetImprovement(SkillTree.ImproveSkill improvement)

    {

        switch (improvement)

        {

            case SkillTree.ImproveSkill.DoubleJump:

                extraJumps++;

                break;

            case SkillTree.ImproveSkill.DashDistance:

                dashComponent.dashDistance += 2f;

                break;

            case SkillTree.ImproveSkill.BeamSpeed:

                if (beamManager != null) beamManager.UpgradeBeamSpeed(10f);

                break;

            case SkillTree.ImproveSkill.BeamLifetime:

                if (beamManager != null) beamManager.UpgradeBeamLifetime(1f);

                break;

            case SkillTree.ImproveSkill.FreezeDuration:

                if (beamManager != null) beamManager.UpgradeFreezeDuration(2f);

                break;

            case SkillTree.ImproveSkill.MaxFrozenObjects:

                if (beamManager != null) beamManager.maxFrozenObjects += 1;

                break;

            case SkillTree.ImproveSkill.FireRate:

                if (beamManager != null) beamManager.UpgradeFireRate(0.1f);

                break;

        }

    }

    private void DebugCode()

    {

        if (hasSwitchTimeUpgrade)

        {

            playerSkills.UnlockSkill(SkillTree.Skill.TimeSwitch);

        }

        if (hasBeamUpgrade)

        {

            playerSkills.UnlockSkill(SkillTree.Skill.TimeBeam);

            animationComponent.SwitchToWandAnim();

        }

        if (hasDashUpgrade)

        {

            playerSkills.UnlockSkill(SkillTree.Skill.Dash);

        }

        if (hasDoubleJumpUpgrade)

        {

            playerSkills.UnlockImprovement(SkillTree.ImproveSkill.DoubleJump);

            extraJumps++;

        }

        if (hasAlchemyCrystalUpgrade)

        {

            GrantAlchemyCrystal();

        }

    }

    // StaffCharge counts the alchemy crystal as collected once this objective is
    // complete. Must match Alchemy Crystal Objective Id on StaffCharge.
    public const string AlchemyCrystalObjectiveId = "alchemy.crystal.collected";

    // Used by the Debug Upgrades tickbox and the debug console.
    public void GrantAlchemyCrystal()

    {

        ObjectiveManager.EnsureExists().CompleteObjective(AlchemyCrystalObjectiveId);

    }

}