using FMOD.Studio;
using FMODUnity;
using UnityEngine;

// All player sound now runs through FMOD. Every method here is a facade the rest
// of the game calls without knowing what the backend is.
public class PlayerAudio : MonoBehaviour
{
    [Header("Footsteps")]
    [SerializeField] private EventReference footstepEvent;

    [Tooltip("Horizontal distance travelled between steps. Tune against the walk animation's foot plants.")]
    [SerializeField] private float stepDistance = 0.9f;

    [Tooltip("Layers to probe for a SurfaceType. Include all three era layers plus Solid - inactive eras are filtered out automatically.")]
    [SerializeField] private LayerMask groundLayers;

    [Tooltip("How far below the player to look for the surface.")]
    [SerializeField] private float surfaceProbeDistance = 1.5f;

    [Tooltip("Used when the ground has no SurfaceType. Must match a label on the FMOD Surface parameter.")]
    [SerializeField] private string defaultSurface = "Stone";

    [Header("Movement")]
    [Tooltip("Labelled parameter 'JumpType' with labels First and Double.")]
    [SerializeField] private EventReference jumpEvent;
    [SerializeField] private EventReference dashEvent;

    [Tooltip("Takes the same 'Surface' parameter as footsteps, plus a continuous " +
             "'FallSpeed' so a long drop lands harder than a hop.")]
    [SerializeField] private EventReference landEvent;

    [Tooltip("Falls slower than this make no sound at all - stepping off a kerb shouldn't thud.")]
    [SerializeField] private float minLandingSpeed = 2f;

    [Tooltip("Fall speed that maps to FallSpeed = 1. Anything faster is clamped.")]
    [SerializeField] private float maxLandingSpeed = 20f;

    [Header("Ladder")]
    [Tooltip("One-shot when the player gets onto a ladder.")]
    [SerializeField] private EventReference climbGrabEvent;

    [Tooltip("One-shot per rung while climbing, up or down.")]
    [SerializeField] private EventReference climbStepEvent;

    [Tooltip("Vertical distance climbed between step sounds. Tune against the climb animation's hand grabs.")]
    [SerializeField] private float climbStepDistance = 0.5f;

    [Header("Combat")]
    [SerializeField] private EventReference shootEvent;

    [Header("Time")]
    [Tooltip("Labelled parameter 'Era' with labels Past, Present and Future, and " +
             "'Direction' with labels Forward and Backward.")]
    [SerializeField] private EventReference timeSwitchEvent;
    [SerializeField] private EventReference switchFailEvent;
    [SerializeField] private EventReference crushEvent;

    private bool isWalking;
    private float distanceSinceStep;
    private float lastStepX;

    private bool isClimbing;
    private float distanceSinceClimbStep;
    private float lastClimbY;

    private Rigidbody2D body;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
    }


    #region - Footsteps -

    // Steps are driven by distance travelled rather than animation events, so they
    // stay in sync at any movement speed and simply stop when there is no ground.
    public void UpdateWalking(bool grounded, bool moving)
    {
        if (!grounded || !moving)
        {
            StopWalking();
            return;
        }

        if (!isWalking)
        {
            isWalking = true;
            lastStepX = transform.position.x;

            // Land the first step immediately rather than half a stride late.
            distanceSinceStep = stepDistance;
        }

        distanceSinceStep += Mathf.Abs(transform.position.x - lastStepX);
        lastStepX = transform.position.x;

        if (distanceSinceStep < stepDistance) return;

        distanceSinceStep = 0f;
        PlayWithLabel(footstepEvent, "Surface", DetectSurface());
    }


    public void StopWalking()
    {
        isWalking = false;
        distanceSinceStep = 0f;
    }


    // Getting onto a ladder. The first rung step then waits a full rung, so
    // grabbing with Up held doesn't play a step on top of the grab.
    public void PlayClimbGrab()
    {
        Play(climbGrabEvent);

        isClimbing = true;
        lastClimbY = transform.position.y;
        distanceSinceClimbStep = 0f;
    }


    // Ladder equivalent of UpdateWalking: distance climbed rather than walked,
    // so it keeps pace with the climb and goes quiet while hanging still.
    public void UpdateClimbing(bool climbing, bool moving)
    {
        if (!climbing || !moving)
        {
            isClimbing = false;
            distanceSinceClimbStep = 0f;
            return;
        }

        if (!isClimbing)
        {
            isClimbing = true;
            lastClimbY = transform.position.y;

            // Setting off again after hanging still steps straight away, like
            // the first footstep.
            distanceSinceClimbStep = climbStepDistance;
        }

        distanceSinceClimbStep += Mathf.Abs(transform.position.y - lastClimbY);
        lastClimbY = transform.position.y;

        if (distanceSinceClimbStep < climbStepDistance) return;

        distanceSinceClimbStep = 0f;
        Play(climbStepEvent);
    }


    private string DetectSurface()
    {
        int mask = groundLayers.value;

        // Never sample a floor belonging to an era we are not standing in.
        if (TimeSingleton.Instance != null)
            mask &= ~TimeSingleton.Instance.InactiveEraMask.value;

        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, surfaceProbeDistance, mask);

        if (hit.collider == null) return defaultSurface;

        SurfaceType surface = hit.collider.GetComponentInParent<SurfaceType>();
        return surface != null ? surface.SurfaceLabel : defaultSurface;
    }

    #endregion


    #region - One Shots -

    public void PlayJump(int jumpCount)
    {
        PlayWithLabel(jumpEvent, "JumpType", jumpCount <= 0 ? "First" : "Double");
    }


    // fallSpeed is how fast the player was descending on the frame before contact,
    // as a positive number. The landing zeroes velocity, so it has to be sampled
    // before the ground check rather than read here.
    public void PlayLanding(float fallSpeed)
    {
        if (landEvent.IsNull || fallSpeed < minLandingSpeed) return;

        float weight = Mathf.InverseLerp(minLandingSpeed, maxLandingSpeed, fallSpeed);

        EventInstance instance = RuntimeManager.CreateInstance(landEvent);
        instance.set3DAttributes(RuntimeUtils.To3DAttributes(gameObject));
        instance.setParameterByNameWithLabel("Surface", DetectSurface());
        instance.setParameterByName("FallSpeed", weight);
        RuntimeManager.AttachInstanceToGameObject(instance, gameObject, body);
        instance.start();
        instance.release();
    }


    public void PlayDash()
    {
        Play(dashEvent);
    }


    public void PlayShoot()
    {
        Play(shootEvent);
    }


    // Takes the era rather than a bool, so the Future gets its own sound instead
    // of borrowing the Present's. Direction is +1 for a step forward through the
    // eras and -1 for a step back, including the wrap at either end.
    public void PlayTimeSwitch(TimeSingleton.GameState era, int direction)
    {
        if (timeSwitchEvent.IsNull) return;

        EventInstance instance = RuntimeManager.CreateInstance(timeSwitchEvent);
        instance.set3DAttributes(RuntimeUtils.To3DAttributes(gameObject));
        instance.setParameterByNameWithLabel("Era", era.ToString());
        instance.setParameterByNameWithLabel("Direction", direction >= 0 ? "Forward" : "Backward");
        RuntimeManager.AttachInstanceToGameObject(instance, gameObject, body);
        instance.start();
        instance.release();
    }


    public void PlaySwitchFail()
    {
        Play(switchFailEvent);
    }


    // Detached: it must keep playing through the respawn that follows.
    public void PlayCrush()
    {
        if (crushEvent.IsNull) return;

        RuntimeManager.PlayOneShot(crushEvent, transform.position);
    }

    #endregion


    private void Play(EventReference reference)
    {
        if (reference.IsNull) return;

        RuntimeManager.PlayOneShotAttached(reference, gameObject);
    }


    private void PlayWithLabel(EventReference reference, string parameter, string label)
    {
        if (reference.IsNull) return;

        EventInstance instance = RuntimeManager.CreateInstance(reference);
        instance.set3DAttributes(RuntimeUtils.To3DAttributes(gameObject));
        instance.setParameterByNameWithLabel(parameter, label);
        RuntimeManager.AttachInstanceToGameObject(instance, gameObject, body);
        instance.start();

        // Released instances are cleaned up by FMOD once the one-shot finishes.
        instance.release();
    }
}