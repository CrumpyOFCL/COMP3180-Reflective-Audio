using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAimAndShoot : MonoBehaviour
{
    [HideInInspector]
    public PlayerInputManager.playerInputType inputType;

    [HideInInspector]
    public Gamepad currentController;

    private Vector2 lastStickDirection =
        Vector2.right;

    private Vector2 cursorPosition;
    private Vector2 aimInput;

    [Header("Components")]
    [SerializeField] private BeamTrajectory beamTrajectory;
    [SerializeField] private BeamManager beamManager;
    [SerializeField] private PlayerAnimator animationComponent;

    [Header("References")]
    [SerializeField] private GameObject beamPrefab;

    [Tooltip("Visible crosshair placed at the exact point where the beam will spawn.")]
    [SerializeField] private GameObject aimCursor;

    [Tooltip("Equipped element orb displayed around the aiming radius.")]
    [SerializeField] private Transform shootingOrb;

    [SerializeField] private Transform aimCenter;

    [Tooltip("LineRenderer used to show the aiming radius.")]
    [SerializeField] private LineRenderer aimRing;

    [Header("Movement Shooting")]
    [Tooltip(
      "When enabled, aiming remains visible and the player may shoot while moving."
  )]
    [SerializeField] private bool allowShootingWhileMoving = false;

    [Tooltip(
        "When enabled, the predicted beam trajectory remains visible while moving."
    )]
    [SerializeField] private bool showTrajectoryWhileMoving = true;

    [Header("Controller Aim")]
    [Tooltip("Distance from Aim Center to the shot crosshair.")]
    [Min(0.1f)]
    [SerializeField] private float aimRadius = 3f;

    [Range(0f, 0.95f)]
    [SerializeField] private float controllerDeadzone = 0.2f;

    [Tooltip("Maximum angular movement of controller aiming in degrees per second.")]
    [Min(1f)]
    [SerializeField] private float controllerRotationSpeed = 180f;

    [Tooltip(
        "When aiming almost directly downward outside the allowed semicircle, " +
        "keep the previous left/right side instead of flickering between edges."
    )]
    [Range(0f, 0.5f)]
    [SerializeField] private float controllerSideHoldThreshold = 0.08f;

    [Header("Aim Visuals")]
    [Range(8, 128)]
    [SerializeField] private int aimRingSegments = 48;

    [Min(0.001f)]
    [SerializeField] private float aimRingWidth = 0.025f;

    [SerializeField]
    private Color aimRingColor =
        new Color(
            1f,
            1f,
            1f,
            0.35f
        );

    [Tooltip("Colour of the entire aiming radius when the current shot position is invalid.")]
    [SerializeField]
    private Color blockedAimRingColor =
        new Color(
            1f,
            0.2f,
            0.2f,
            0.6f
        );

    [Tooltip("Colour of the aiming cursor when the current shot position is invalid.")]
    [SerializeField]
    private Color blockedCursorColor =
        new Color(
            1f,
            0.2f,
            0.2f,
            1f
        );

    [Tooltip(
        "Layers the aim ring itself can visually pass through without being shortened."
    )]
    [SerializeField] private LayerMask aimRingIgnoredLayers;

    [Min(0f)]
    [SerializeField] private float aimRingCollisionPadding = 0.03f;

    [Tooltip("Restricts aiming to the upper half of the player.")]
    [SerializeField] private bool upperSemicircleOnly = true;

    [Header("Shot Clearance")]
    [Tooltip(
        "Radius around the cursor that must be clear before a projectile can spawn."
    )]
    [Min(0.01f)]
    [SerializeField] private float shotClearanceRadius = 0.11f;

    [Tooltip(
        "Layers that are not valid locations for the projectile to spawn."
    )]
    [SerializeField] private LayerMask shotBlockingLayers;

    [Tooltip(
        "Checks a short distance in front of the projectile spawn point. " +
        "Useful for mirrors or walls sitting directly against the aiming radius."
    )]
    [Min(0f)]
    [SerializeField] private float immediateBlockCheckDistance = 0.25f;

    [Header("Staff Knockback")]
    [SerializeField] private float staffKnockback = 4f;

    private float currentControllerAngle;

    private Camera cachedCamera;

    private PlayerMovement playerMovement;
    private Player player;

    private SpriteRenderer aimCursorRenderer;
    private SpriteRenderer shootingOrbRenderer;

    // When the orb is a StaffChargeDisplay, that owns its colour.
    private StaffChargeDisplay shootingOrbDisplay;

    private Color normalCursorColor =
        Color.white;

    private Color normalOrbColor =
        Color.white;

    public Vector2 CursorWorldPosition =>
        cursorPosition;

    public Vector2 AimOrigin =>
        aimCenter != null
            ? (Vector2)aimCenter.position
            : (Vector2)transform.position;

    public bool AllowShootingWhileMoving =>
        allowShootingWhileMoving;

    public bool IsPlayerMoving =>
        player != null &&
        player.IsActivelyMoving;

    public Vector2 BeamOrigin =>
        cursorPosition;

    public Vector2 BeamDirection
    {
        get
        {
            Vector2 direction =
                cursorPosition -
                AimOrigin;

            if (direction.sqrMagnitude <=
                0.0001f)
            {
                return lastStickDirection.sqrMagnitude >
                       0.0001f
                    ? lastStickDirection.normalized
                    : Vector2.right;
            }

            return direction.normalized;
        }
    }

    private void Awake()
    {
        player =
            GetComponentInParent<Player>();

        playerMovement =
            GetComponentInParent<PlayerMovement>();

        if (shootingOrb == null)
        {
            StaffChargeDisplay display =
                GetComponentInChildren<StaffChargeDisplay>();

            if (display != null)
            {
                shootingOrb =
                    display.transform;
            }
        }

        if (beamManager == null)
        {
            beamManager =
                GetComponentInParent<BeamManager>();
        }

        if (beamManager == null)
        {
            beamManager =
                FindAnyObjectByType<BeamManager>();
        }

        if (aimCursor != null)
        {
            aimCursorRenderer =
                aimCursor.GetComponent<SpriteRenderer>();

            if (aimCursorRenderer != null)
            {
                normalCursorColor =
                    aimCursorRenderer.color;
            }
        }

        if (shootingOrb != null)
        {
            shootingOrbDisplay =
                shootingOrb.GetComponent<StaffChargeDisplay>();

            shootingOrbRenderer =
                shootingOrb.GetComponent<SpriteRenderer>();

            if (shootingOrbRenderer != null)
            {
                normalOrbColor =
                    shootingOrbRenderer.color;
            }
        }

        currentControllerAngle =
            0f;

        cursorPosition =
            AimOrigin +
            Vector2.right *
            aimRadius;

        ConfigureAimRing();
    }

    private void Update()
    {
        if (PauseMenu.IsPaused ||
            GameOverMenu.IsGameOver)
        {
            SetAimVisualsVisible(false);

            if (beamTrajectory != null)
            {
                beamTrajectory.ClearTrajectory();
            }

            return;
        }

        UpdateCursorVisibility();
        MoveCursor();

        bool showAimVisuals =
            allowShootingWhileMoving ||
            !IsPlayerMoving;

        SetAimVisualsVisible(
            showAimVisuals
        );

        if (!showAimVisuals)
        {
            if (beamTrajectory != null)
            {
                beamTrajectory.ClearTrajectory();
            }

            return;
        }

        UpdateShotMarker();
        UpdateAimVisuals();

        bool canShootHere =
            CanShootFromCurrentAim();

        UpdateBlockedAimVisual(
            canShootHere
        );

        if (!canShootHere)
        {
            if (beamTrajectory != null)
            {
                beamTrajectory.ClearTrajectory();
            }

            return;
        }

        bool shouldShowTrajectory =
            !IsPlayerMoving ||
            (allowShootingWhileMoving &&
             showTrajectoryWhileMoving);

        if (beamTrajectory != null)
        {
            if (shouldShowTrajectory)
            {
                beamTrajectory.DrawTrajectory();
            }
            else
            {
                beamTrajectory.ClearTrajectory();
            }
        }
    }

    private void ConfigureAimRing()
    {
        if (aimRing == null)
            return;

        aimRing.useWorldSpace =
            true;

        aimRing.loop =
            !upperSemicircleOnly;

        aimRing.startWidth =
            aimRingWidth;

        aimRing.endWidth =
            aimRingWidth;

        aimRing.startColor =
            aimRingColor;

        aimRing.endColor =
            aimRingColor;

        aimRing.positionCount =
            Mathf.Max(
                2,
                aimRingSegments
            );
    }

    private void UpdateAimVisuals()
    {
        if (aimRing == null)
            return;

        Vector2 center =
            AimOrigin;

        aimRing.enabled =
            true;

        int segmentCount =
            Mathf.Max(
                2,
                aimRingSegments
            );

        aimRing.positionCount =
            segmentCount;

        aimRing.loop =
            !upperSemicircleOnly;

        for (int i = 0;
             i < segmentCount;
             i++)
        {
            float angle;

            if (upperSemicircleOnly)
            {
                angle =
                    Mathf.PI *
                    i /
                    (segmentCount - 1);
            }
            else
            {
                angle =
                    Mathf.PI *
                    2f *
                    i /
                    segmentCount;
            }

            Vector2 direction =
                new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)
                );

            float pointRadius =
                GetAimRingRadius(
                    center,
                    direction
                );

            aimRing.SetPosition(
                i,
                new Vector3(
                    center.x +
                    direction.x *
                    pointRadius,

                    center.y +
                    direction.y *
                    pointRadius,

                    transform.position.z +
                    0.1f
                )
            );
        }
    }

    private float GetAimRingRadius(
        Vector2 center,
        Vector2 direction)
    {
        int blockers =
            Physics2D.DefaultRaycastLayers &
            ~aimRingIgnoredLayers.value;

        /*
         * Mirrors should affect the beam, not deform the aiming UI.
         *
         * If your project has a layer literally called "Mirror", exclude it
         * automatically as an additional safety measure.
         */
        int mirrorLayer =
            LayerMask.NameToLayer(
                "Mirror"
            );

        if (mirrorLayer >= 0)
        {
            blockers &=
                ~(1 << mirrorLayer);
        }

        RaycastHit2D[] hits =
            Physics2D.RaycastAll(
                center,
                direction,
                aimRadius,
                blockers
            );

        float nearestDistance =
            aimRadius;

        foreach (RaycastHit2D hit
                 in hits)
        {
            if (hit.collider == null)
                continue;

            if (hit.collider.isTrigger)
                continue;

            if (hit.collider.transform.root ==
                transform.root)
            {
                continue;
            }

            BeamMirror mirror =
                GetMirror(
                    hit.collider
                );

            if (mirror != null)
                continue;

            nearestDistance =
                Mathf.Min(
                    nearestDistance,
                    Mathf.Max(
                        0f,
                        hit.distance -
                        aimRingCollisionPadding
                    )
                );
        }

        return nearestDistance;
    }

    private void UpdateCursorVisibility()
    {
        if (inputType ==
            PlayerInputManager.playerInputType.KeyboardMouse)
        {
            Cursor.visible =
                false;

            Cursor.lockState =
                CursorLockMode.Confined;
        }
        else
        {
            Cursor.visible =
                false;

            Cursor.lockState =
                CursorLockMode.Locked;
        }
    }

    private void SetAimVisualsVisible(
        bool visible)
    {
        if (aimCursor != null)
        {
            aimCursor.SetActive(
                visible
            );
        }

        if (shootingOrb != null)
        {
            shootingOrb.gameObject.SetActive(
                visible
            );
        }

        if (aimRing != null)
        {
            aimRing.enabled =
                visible;
        }
    }

    private void MoveCursor()
    {
        GameSettings settings =
            GameSettings.Instance;

        if (inputType ==
            PlayerInputManager.playerInputType.KeyboardMouse)
        {
            MoveMouseCursor();

            return;
        }

        if (inputType ==
            PlayerInputManager.playerInputType.Controller)
        {
            MoveControllerCursor(
                settings
            );
        }
    }

    private void MoveMouseCursor()
    {
        if (cachedCamera == null)
        {
            cachedCamera =
                Camera.main;
        }

        if (cachedCamera == null ||
            Mouse.current == null)
        {
            return;
        }

        Vector2 mousePosition =
            Mouse.current.position
                .ReadValue();

        Vector3 screenPosition =
            new Vector3(
                mousePosition.x,
                mousePosition.y,
                Mathf.Abs(
                    cachedCamera.transform
                        .position.z
                )
            );

        Vector2 mouseWorldPosition =
            cachedCamera
                .ScreenToWorldPoint(
                    screenPosition
                );

        Vector2 direction =
            mouseWorldPosition -
            AimOrigin;

        if (direction.sqrMagnitude >
            0.0001f)
        {
            lastStickDirection =
                ConstrainAimDirection(
                    direction.normalized
                );
        }

        cursorPosition =
            AimOrigin +
            lastStickDirection *
            aimRadius;
    }

    private void MoveControllerCursor(
        GameSettings settings)
    {
        float deadzone =
            settings != null
                ? settings.RightStickDeadzone
                : controllerDeadzone;

        float sensitivity =
            settings != null
                ? settings.ControllerSensitivity
                : 1f;

        if (currentController == null)
        {
            cursorPosition =
                AimOrigin +
                lastStickDirection *
                aimRadius;

            return;
        }

        aimInput =
            currentController
                .rightStick
                .ReadValue();

        if (aimInput.magnitude >
            deadzone)
        {
            Vector2 desiredDirection =
                aimInput.normalized;

            /*
             * The valid aiming area is 0..180 degrees.
             *
             * Do not feed a -180..180 angle directly into
             * MoveTowardsAngle because it treats the values as a full circle
             * and can wrap from the left edge back toward the right.
             */
            if (upperSemicircleOnly &&
                desiredDirection.y < 0f)
            {
                bool chooseLeft;

                if (Mathf.Abs(
                        desiredDirection.x
                    ) <=
                    controllerSideHoldThreshold)
                {
                    /*
                     * When the stick is almost straight down, retain whichever
                     * side the cursor is already on instead of allowing tiny
                     * stick noise to swap left/right.
                     */
                    chooseLeft =
                        lastStickDirection.x <
                        0f;
                }
                else
                {
                    chooseLeft =
                        desiredDirection.x <
                        0f;
                }

                desiredDirection =
                    chooseLeft
                        ? Vector2.left
                        : Vector2.right;
            }

            float targetAngle =
                Mathf.Atan2(
                    desiredDirection.y,
                    desiredDirection.x
                ) *
                Mathf.Rad2Deg;

            /*
             * Always derive our current angle from the actual visible cursor.
             *
             * This also prevents stale controller angle state if the user
             * previously aimed with the mouse.
             */
            float visibleAngle =
                Mathf.Atan2(
                    lastStickDirection.y,
                    lastStickDirection.x
                ) *
                Mathf.Rad2Deg;

            float rotationStep =
                controllerRotationSpeed *
                Mathf.Max(
                    0f,
                    sensitivity
                ) *
                Time.deltaTime;

            if (upperSemicircleOnly)
            {
                targetAngle =
                    NormaliseUpperAngle(
                        targetAngle
                    );

                visibleAngle =
                    NormaliseUpperAngle(
                        visibleAngle
                    );

                /*
                 * IMPORTANT:
                 * MoveTowards rather than MoveTowardsAngle.
                 *
                 * There is no wrap-around in a semicircle.
                 */
                currentControllerAngle =
                    Mathf.MoveTowards(
                        visibleAngle,
                        targetAngle,
                        rotationStep
                    );

                currentControllerAngle =
                    Mathf.Clamp(
                        currentControllerAngle,
                        0f,
                        180f
                    );
            }
            else
            {
                currentControllerAngle =
                    Mathf.MoveTowardsAngle(
                        visibleAngle,
                        targetAngle,
                        rotationStep
                    );
            }

            float radians =
                currentControllerAngle *
                Mathf.Deg2Rad;

            Vector2 newDirection =
                new Vector2(
                    Mathf.Cos(radians),
                    Mathf.Sin(radians)
                );

            lastStickDirection =
                ConstrainAimDirection(
                    newDirection
                );
        }

        cursorPosition =
            AimOrigin +
            lastStickDirection *
            aimRadius;
    }

    private float NormaliseUpperAngle(
        float angle)
    {
        angle =
            Mathf.Repeat(
                angle,
                360f
            );

        /*
         * Lower half is not valid for the upper semicircle.
         * Select whichever horizontal edge it belongs to.
         */
        if (angle > 180f)
        {
            angle =
                angle >= 270f
                    ? 0f
                    : 180f;
        }

        return Mathf.Clamp(
            angle,
            0f,
            180f
        );
    }

    private Vector2 ConstrainAimDirection(
        Vector2 direction)
    {
        if (direction.sqrMagnitude <=
            0.0001f)
        {
            if (lastStickDirection.sqrMagnitude >
                0.0001f)
            {
                return lastStickDirection.normalized;
            }

            return Vector2.right;
        }

        direction.Normalize();

        if (!upperSemicircleOnly)
        {
            return direction;
        }

        if (direction.y >= 0f)
        {
            return direction;
        }

        /*
         * Never default an invalid lower-left direction to Vector2.right.
         */
        if (Mathf.Abs(direction.x) <=
            controllerSideHoldThreshold)
        {
            return lastStickDirection.x <
                   0f
                ? Vector2.left
                : Vector2.right;
        }

        return direction.x < 0f
            ? Vector2.left
            : Vector2.right;
    }

    private void UpdateShotMarker()
    {
        /*
         * The cursor/orb always represents the actual beam spawn point and
         * therefore always remains on the aiming radius.
         */
        Vector3 radiusPosition =
            new Vector3(
                cursorPosition.x,
                cursorPosition.y,
                transform.position.z -
                1f
            );

        if (shootingOrb != null)
        {
            shootingOrb.position =
                radiusPosition;
        }

        if (aimCursor != null)
        {
            aimCursor.transform.position =
                radiusPosition;
        }

        if (animationComponent != null)
        {
            animationComponent
                .SetShootParticlePosition(
                    radiusPosition
                );
        }
    }

    private void UpdateBlockedAimVisual(
        bool canShootHere)
    {
        if (aimCursorRenderer != null)
        {
            aimCursorRenderer.color =
                canShootHere
                    ? normalCursorColor
                    : blockedCursorColor;
        }

        /*
         * The display tints the orb with the held element. Writing a colour
         * here every frame wiped that, so it is asked for the blocked tint
         * instead.
         */
        if (shootingOrbDisplay != null)
        {
            shootingOrbDisplay.SetBlocked(
                !canShootHere,
                blockedCursorColor
            );
        }
        else if (shootingOrbRenderer != null)
        {
            shootingOrbRenderer.color =
                canShootHere
                    ? normalOrbColor
                    : blockedCursorColor;
        }

        if (aimRing != null)
        {
            Color ringColor =
                canShootHere
                    ? aimRingColor
                    : blockedAimRingColor;

            aimRing.startColor =
                ringColor;

            aimRing.endColor =
                ringColor;
        }
    }

    public bool CanShootFromCurrentAim()
    {
        /*
         * Check the actual projectile spawn area.
         */
        Collider2D[] overlaps =
            Physics2D.OverlapCircleAll(
                cursorPosition,
                shotClearanceRadius
            );

        foreach (Collider2D hit
                 in overlaps)
        {
            if (hit == null)
                continue;

            if (hit.transform.root ==
                transform.root)
            {
                continue;
            }

            /*
             * Mirrors are valid further along a trajectory, but the projectile
             * must not spawn inside one.
             */
            if (GetMirror(hit) != null)
            {
                return false;
            }

            if (IsShotBlockingLayer(
                    hit.gameObject.layer
                ))
            {
                return false;
            }
        }

        /*
         * Also check immediately in front of the spawn point. This catches the
         * case where a mirror/wall sits directly against the aiming circle.
         */
        if (immediateBlockCheckDistance >
            0f)
        {
            Vector2 direction =
                BeamDirection;

            RaycastHit2D[] hits =
                Physics2D.CircleCastAll(
                    cursorPosition,
                    shotClearanceRadius,
                    direction,
                    immediateBlockCheckDistance
                );

            foreach (RaycastHit2D hit
                     in hits)
            {
                if (hit.collider == null)
                    continue;

                if (hit.collider.transform.root ==
                    transform.root)
                {
                    continue;
                }

                if (GetMirror(
                        hit.collider
                    ) != null)
                {
                    return false;
                }

                if (IsShotBlockingLayer(
                        hit.collider
                            .gameObject
                            .layer
                    ))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool IsShotBlockingLayer(
        int layer)
    {
        return
            (shotBlockingLayers.value &
             (1 << layer)) != 0;
    }

    private BeamMirror GetMirror(
        Collider2D collider)
    {
        if (collider == null)
            return null;

        BeamMirror mirror =
            collider.GetComponent<BeamMirror>();

        if (mirror == null)
        {
            mirror =
                collider
                    .GetComponentInParent<BeamMirror>();
        }

        return mirror;
    }

    public bool Shoot()
    {
        if (player == null ||
            !player.PlayerSkills.CheckSkill(
                SkillTree.Skill.TimeBeam
            ))
        {
            return false;
        }

        /*
         * Normal mode:
         * movement blocks shooting.
         *
         * Movement-shooting mode:
         * this restriction is completely bypassed.
         */
        if (!allowShootingWhileMoving &&
            player.IsActivelyMoving)
        {
            return false;
        }

        if (!CanShootFromCurrentAim())
        {
            return false;
        }

        if (beamManager == null ||
            !beamManager.CanFire())
        {
            return false;
        }

        if (beamTrajectory == null ||
            beamPrefab == null)
        {
            return false;
        }

        beamTrajectory.DrawTrajectory();

        if (beamTrajectory.IsPathBlocked)
        {
            return false;
        }

        Vector2 spawnPoint2D =
            cursorPosition;

        Vector2 beamDirection =
            BeamDirection;

        if (beamDirection.sqrMagnitude <=
            0.0001f)
        {
            return false;
        }

        float beamAngle =
            Mathf.Atan2(
                beamDirection.y,
                beamDirection.x
            ) *
            Mathf.Rad2Deg;

        Quaternion beamRotation =
            Quaternion.Euler(
                0f,
                0f,
                beamAngle
            );

        Vector3 spawnPoint =
            new Vector3(
                spawnPoint2D.x,
                spawnPoint2D.y,
                transform.position.z
            );

        Instantiate(
            beamPrefab,
            spawnPoint,
            beamRotation
        );

        if (playerMovement != null &&
            playerMovement.IsStandingOnPlatform &&
            staffKnockback > 0f)
        {
            Rigidbody2D platformBody =
                playerMovement.SupportingPlatform;

            if (platformBody != null &&
                platformBody.bodyType ==
                RigidbodyType2D.Dynamic)
            {
                RigidBox platformBox =
                    platformBody
                        .GetComponent<RigidBox>();

                if (platformBox != null)
                {
                    platformBox.ApplyBeamKnockback(
                        -beamDirection *
                        staffKnockback
                    );
                }
                else
                {
                    platformBody.AddForce(
                        -beamDirection *
                        staffKnockback,
                        ForceMode2D.Impulse
                    );
                }
            }
        }

        if (animationComponent != null)
        {
            animationComponent
                .PlayShootParticleAnimation(
                    spawnPoint
                , beamManager.FireRate);
        }

        beamTrajectory.ClearTrajectory();

        beamManager.RegisterShot();

        return true;
    }

    private void OnDisable()
    {
        Cursor.visible =
            true;

        Cursor.lockState =
            CursorLockMode.None;

        if (aimCursorRenderer != null)
        {
            aimCursorRenderer.color =
                normalCursorColor;
        }

        if (shootingOrbDisplay != null)
        {
            shootingOrbDisplay.SetBlocked(
                false,
                blockedCursorColor
            );
        }
        else if (shootingOrbRenderer != null)
        {
            shootingOrbRenderer.color =
                normalOrbColor;
        }

        if (aimRing != null)
        {
            aimRing.startColor =
                aimRingColor;

            aimRing.endColor =
                aimRingColor;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(
            cursorPosition,
            shotClearanceRadius
        );

        Vector2 direction =
            BeamDirection;

        Gizmos.DrawWireSphere(
            cursorPosition +
            direction *
            immediateBlockCheckDistance,
            shotClearanceRadius
        );
    }
#endif
}