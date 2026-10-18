using UnityEngine;
using UnityEngine.InputSystem;

// SHOWCASE VERSION of the game's aim-and-shoot, trimmed for the reflective-audio
// test scene. Aiming (mouse and controller), the aim ring, the cursor and the
// shot-clearance checks are unchanged.
//
// Removed because those systems are not in this repo: the beam trajectory
// preview, BeamManager (fire rate and beam limits, replaced by fireCooldown),
// mirrors, the staff charge display, GameSettings (deadzone and sensitivity
// come from the fields below), the skill unlock, pause/game-over checks and
// the platform knockback.
//
// Field names are unchanged so the Player_New prefab keeps its Inspector values.
public class PlayerAimAndShoot : MonoBehaviour
{
    [HideInInspector] public PlayerInputManager.playerInputType inputType;
    [HideInInspector] public Gamepad currentController;

    private Vector2 lastStickDirection = Vector2.right;
    private Vector2 cursorPosition;
    private Vector2 aimInput;

    [Header("Components")]
    [SerializeField] private PlayerAnimator animationComponent;

    [Header("References")]
    [SerializeField] private GameObject beamPrefab;

    [Tooltip("Visible crosshair placed at the exact point where the beam will spawn.")]
    [SerializeField] private GameObject aimCursor;

    [Tooltip("Orb displayed around the aiming radius.")]
    [SerializeField] private Transform shootingOrb;

    [SerializeField] private Transform aimCenter;

    [Tooltip("LineRenderer used to show the aiming radius.")]
    [SerializeField] private LineRenderer aimRing;

    [Header("Movement Shooting")]
    [Tooltip("When enabled, aiming remains visible and the player may shoot while moving.")]
    [SerializeField] private bool allowShootingWhileMoving = false;

    [Header("Showcase Firing")]
    [Tooltip("Seconds between shots. Replaces BeamManager's fire rate.")]
    [Min(0f)]
    [SerializeField] private float fireCooldown = 0.35f;

    [Header("Controller Aim")]
    [Tooltip("Distance from Aim Center to the shot crosshair.")]
    [Min(0.1f)]
    [SerializeField] private float aimRadius = 3f;

    [Range(0f, 0.95f)]
    [SerializeField] private float controllerDeadzone = 0.2f;

    [Tooltip("Multiplier on controller aim speed. Replaces GameSettings.ControllerSensitivity.")]
    [Min(0f)]
    [SerializeField] private float controllerSensitivity = 1f;

    [Tooltip("Maximum angular movement of controller aiming in degrees per second.")]
    [Min(1f)]
    [SerializeField] private float controllerRotationSpeed = 180f;

    [Tooltip("When aiming almost straight down outside the allowed semicircle, keep the previous side.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float controllerSideHoldThreshold = 0.08f;

    [Header("Aim Visuals")]
    [Range(8, 128)]
    [SerializeField] private int aimRingSegments = 48;

    [Min(0.001f)]
    [SerializeField] private float aimRingWidth = 0.025f;

    [SerializeField] private Color aimRingColor = new Color(1f, 1f, 1f, 0.35f);

    [Tooltip("Colour of the aiming radius when the current shot position is invalid.")]
    [SerializeField] private Color blockedAimRingColor = new Color(1f, 0.2f, 0.2f, 0.6f);

    [Tooltip("Colour of the aiming cursor when the current shot position is invalid.")]
    [SerializeField] private Color blockedCursorColor = new Color(1f, 0.2f, 0.2f, 1f);

    [Tooltip("Layers the aim ring can visually pass through without being shortened.")]
    [SerializeField] private LayerMask aimRingIgnoredLayers;

    [Min(0f)]
    [SerializeField] private float aimRingCollisionPadding = 0.03f;

    [Tooltip("Restricts aiming to the upper half of the player.")]
    [SerializeField] private bool upperSemicircleOnly = true;

    [Header("Shot Clearance")]
    [Tooltip("Radius around the cursor that must be clear before a projectile can spawn.")]
    [Min(0.01f)]
    [SerializeField] private float shotClearanceRadius = 0.11f;

    [Tooltip("Layers that are not valid locations for the projectile to spawn.")]
    [SerializeField] private LayerMask shotBlockingLayers;

    [Tooltip("Checks a short distance in front of the spawn point, for walls right against the aim radius.")]
    [Min(0f)]
    [SerializeField] private float immediateBlockCheckDistance = 0.25f;

    private float currentControllerAngle;
    private float nextFireTime;
    private Camera cachedCamera;
    private Player player;

    private SpriteRenderer aimCursorRenderer;
    private SpriteRenderer shootingOrbRenderer;
    private Color normalCursorColor = Color.white;
    private Color normalOrbColor = Color.white;

    public Vector2 CursorWorldPosition => cursorPosition;

    public Vector2 AimOrigin => aimCenter != null ? (Vector2)aimCenter.position : (Vector2)transform.position;

    public bool AllowShootingWhileMoving => allowShootingWhileMoving;

    public bool IsPlayerMoving => player != null && player.IsActivelyMoving;

    public Vector2 BeamOrigin => cursorPosition;

    public Vector2 BeamDirection
    {
        get
        {
            Vector2 direction = cursorPosition - AimOrigin;

            if (direction.sqrMagnitude <= 0.0001f)
                return lastStickDirection.sqrMagnitude > 0.0001f ? lastStickDirection.normalized : Vector2.right;

            return direction.normalized;
        }
    }

    private void Awake()
    {
        player = GetComponentInParent<Player>();

        if (aimCursor != null)
        {
            aimCursorRenderer = aimCursor.GetComponent<SpriteRenderer>();
            if (aimCursorRenderer != null) normalCursorColor = aimCursorRenderer.color;
        }

        if (shootingOrb != null)
        {
            shootingOrbRenderer = shootingOrb.GetComponent<SpriteRenderer>();
            if (shootingOrbRenderer != null) normalOrbColor = shootingOrbRenderer.color;
        }

        currentControllerAngle = 0f;
        cursorPosition = AimOrigin + Vector2.right * aimRadius;

        ConfigureAimRing();
    }

    private void Update()
    {
        UpdateCursorVisibility();
        MoveCursor();

        bool showAimVisuals = allowShootingWhileMoving || !IsPlayerMoving;
        SetAimVisualsVisible(showAimVisuals);
        if (!showAimVisuals) return;

        UpdateShotMarker();
        UpdateAimVisuals();
        UpdateBlockedAimVisual(CanShootFromCurrentAim());
    }

    private void ConfigureAimRing()
    {
        if (aimRing == null) return;

        aimRing.useWorldSpace = true;
        aimRing.loop = !upperSemicircleOnly;
        aimRing.startWidth = aimRingWidth;
        aimRing.endWidth = aimRingWidth;
        aimRing.startColor = aimRingColor;
        aimRing.endColor = aimRingColor;
        aimRing.positionCount = Mathf.Max(2, aimRingSegments);
    }

    private void UpdateAimVisuals()
    {
        if (aimRing == null) return;

        Vector2 center = AimOrigin;
        aimRing.enabled = true;

        int segmentCount = Mathf.Max(2, aimRingSegments);
        aimRing.positionCount = segmentCount;
        aimRing.loop = !upperSemicircleOnly;

        for (int i = 0; i < segmentCount; i++)
        {
            float angle = upperSemicircleOnly
                ? Mathf.PI * i / (segmentCount - 1)
                : Mathf.PI * 2f * i / segmentCount;

            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float pointRadius = GetAimRingRadius(center, direction);

            aimRing.SetPosition(i, new Vector3(
                center.x + direction.x * pointRadius,
                center.y + direction.y * pointRadius,
                transform.position.z + 0.1f));
        }
    }

    private float GetAimRingRadius(Vector2 center, Vector2 direction)
    {
        int blockers = Physics2D.DefaultRaycastLayers & ~aimRingIgnoredLayers.value;

        int mirrorLayer = LayerMask.NameToLayer("Mirror");
        if (mirrorLayer >= 0) blockers &= ~(1 << mirrorLayer);

        RaycastHit2D[] hits = Physics2D.RaycastAll(center, direction, aimRadius, blockers);
        float nearestDistance = aimRadius;

        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger) continue;
            if (hit.collider.transform.root == transform.root) continue;

            nearestDistance = Mathf.Min(nearestDistance, Mathf.Max(0f, hit.distance - aimRingCollisionPadding));
        }

        return nearestDistance;
    }

    private void UpdateCursorVisibility()
    {
        Cursor.visible = false;
        Cursor.lockState = inputType == PlayerInputManager.playerInputType.KeyboardMouse
            ? CursorLockMode.Confined
            : CursorLockMode.Locked;
    }

    private void SetAimVisualsVisible(bool visible)
    {
        if (aimCursor != null) aimCursor.SetActive(visible);
        if (shootingOrb != null) shootingOrb.gameObject.SetActive(visible);
        if (aimRing != null) aimRing.enabled = visible;
    }

    private void MoveCursor()
    {
        if (inputType == PlayerInputManager.playerInputType.KeyboardMouse)
        {
            MoveMouseCursor();
            return;
        }

        if (inputType == PlayerInputManager.playerInputType.Controller)
            MoveControllerCursor();
    }

    private void MoveMouseCursor()
    {
        if (cachedCamera == null) cachedCamera = Camera.main;
        if (cachedCamera == null || Mouse.current == null) return;

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Vector3 screenPosition = new Vector3(mousePosition.x, mousePosition.y, Mathf.Abs(cachedCamera.transform.position.z));
        Vector2 mouseWorldPosition = cachedCamera.ScreenToWorldPoint(screenPosition);

        Vector2 direction = mouseWorldPosition - AimOrigin;
        if (direction.sqrMagnitude > 0.0001f)
            lastStickDirection = ConstrainAimDirection(direction.normalized);

        cursorPosition = AimOrigin + lastStickDirection * aimRadius;
    }

    private void MoveControllerCursor()
    {
        if (currentController == null)
        {
            cursorPosition = AimOrigin + lastStickDirection * aimRadius;
            return;
        }

        aimInput = currentController.rightStick.ReadValue();

        if (aimInput.magnitude > controllerDeadzone)
        {
            Vector2 desiredDirection = aimInput.normalized;

            // The valid aiming area is 0..180 degrees, with no wrap-around.
            if (upperSemicircleOnly && desiredDirection.y < 0f)
            {
                bool chooseLeft = Mathf.Abs(desiredDirection.x) <= controllerSideHoldThreshold
                    ? lastStickDirection.x < 0f
                    : desiredDirection.x < 0f;

                desiredDirection = chooseLeft ? Vector2.left : Vector2.right;
            }

            float targetAngle = Mathf.Atan2(desiredDirection.y, desiredDirection.x) * Mathf.Rad2Deg;
            float visibleAngle = Mathf.Atan2(lastStickDirection.y, lastStickDirection.x) * Mathf.Rad2Deg;
            float rotationStep = controllerRotationSpeed * Mathf.Max(0f, controllerSensitivity) * Time.deltaTime;

            if (upperSemicircleOnly)
            {
                targetAngle = NormaliseUpperAngle(targetAngle);
                visibleAngle = NormaliseUpperAngle(visibleAngle);
                currentControllerAngle = Mathf.Clamp(Mathf.MoveTowards(visibleAngle, targetAngle, rotationStep), 0f, 180f);
            }
            else
            {
                currentControllerAngle = Mathf.MoveTowardsAngle(visibleAngle, targetAngle, rotationStep);
            }

            float radians = currentControllerAngle * Mathf.Deg2Rad;
            lastStickDirection = ConstrainAimDirection(new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)));
        }

        cursorPosition = AimOrigin + lastStickDirection * aimRadius;
    }

    private float NormaliseUpperAngle(float angle)
    {
        angle = Mathf.Repeat(angle, 360f);
        if (angle > 180f) angle = angle >= 270f ? 0f : 180f;
        return Mathf.Clamp(angle, 0f, 180f);
    }

    private Vector2 ConstrainAimDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return lastStickDirection.sqrMagnitude > 0.0001f ? lastStickDirection.normalized : Vector2.right;

        direction.Normalize();

        if (!upperSemicircleOnly || direction.y >= 0f) return direction;

        if (Mathf.Abs(direction.x) <= controllerSideHoldThreshold)
            return lastStickDirection.x < 0f ? Vector2.left : Vector2.right;

        return direction.x < 0f ? Vector2.left : Vector2.right;
    }

    private void UpdateShotMarker()
    {
        Vector3 radiusPosition = new Vector3(cursorPosition.x, cursorPosition.y, transform.position.z - 1f);

        if (shootingOrb != null) shootingOrb.position = radiusPosition;
        if (aimCursor != null) aimCursor.transform.position = radiusPosition;
        if (animationComponent != null) animationComponent.SetShootParticlePosition(radiusPosition);
    }

    private void UpdateBlockedAimVisual(bool canShootHere)
    {
        if (aimCursorRenderer != null)
            aimCursorRenderer.color = canShootHere ? normalCursorColor : blockedCursorColor;

        if (shootingOrbRenderer != null)
            shootingOrbRenderer.color = canShootHere ? normalOrbColor : blockedCursorColor;

        if (aimRing != null)
        {
            Color ringColor = canShootHere ? aimRingColor : blockedAimRingColor;
            aimRing.startColor = ringColor;
            aimRing.endColor = ringColor;
        }
    }

    public bool CanShootFromCurrentAim()
    {
        foreach (Collider2D hit in Physics2D.OverlapCircleAll(cursorPosition, shotClearanceRadius))
        {
            if (hit == null || hit.transform.root == transform.root) continue;
            if (IsShotBlockingLayer(hit.gameObject.layer)) return false;
        }

        if (immediateBlockCheckDistance > 0f)
        {
            RaycastHit2D[] hits = Physics2D.CircleCastAll(cursorPosition, shotClearanceRadius, BeamDirection, immediateBlockCheckDistance);

            foreach (RaycastHit2D hit in hits)
            {
                if (hit.collider == null || hit.collider.transform.root == transform.root) continue;
                if (IsShotBlockingLayer(hit.collider.gameObject.layer)) return false;
            }
        }

        return true;
    }

    private bool IsShotBlockingLayer(int layer) => (shotBlockingLayers.value & (1 << layer)) != 0;

    public bool Shoot()
    {
        if (player == null || beamPrefab == null) return false;
        if (Time.time < nextFireTime) return false;
        if (!allowShootingWhileMoving && player.IsActivelyMoving) return false;
        if (!CanShootFromCurrentAim()) return false;

        Vector2 beamDirection = BeamDirection;
        if (beamDirection.sqrMagnitude <= 0.0001f) return false;

        float beamAngle = Mathf.Atan2(beamDirection.y, beamDirection.x) * Mathf.Rad2Deg;
        Vector3 spawnPoint = new Vector3(cursorPosition.x, cursorPosition.y, transform.position.z);

        Instantiate(beamPrefab, spawnPoint, Quaternion.Euler(0f, 0f, beamAngle));
        nextFireTime = Time.time + fireCooldown;

        if (animationComponent != null)
            animationComponent.PlayShootParticleAnimation(spawnPoint, fireCooldown);

        return true;
    }

    private void OnDisable()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (aimCursorRenderer != null) aimCursorRenderer.color = normalCursorColor;
        if (shootingOrbRenderer != null) shootingOrbRenderer.color = normalOrbColor;

        if (aimRing != null)
        {
            aimRing.startColor = aimRingColor;
            aimRing.endColor = aimRingColor;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(cursorPosition, shotClearanceRadius);
        Gizmos.DrawWireSphere(cursorPosition + BeamDirection * immediateBlockCheckDistance, shotClearanceRadius);
    }
#endif
}
