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

    [Header("Combat")]
    [SerializeField] private EventReference shootEvent;

    [Header("Time")]
    [Tooltip("Labelled parameter 'Era' with labels Past, Present and Future.")]
    [SerializeField] private EventReference timeSwitchEvent;
    [SerializeField] private EventReference switchFailEvent;
    [SerializeField] private EventReference crushEvent;

    private bool isWalking;
    private float distanceSinceStep;
    private float lastStepX;


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


    public void PlayDash()
    {
        Play(dashEvent);
    }


    public void PlayShoot()
    {
        Play(shootEvent);
    }


    // Takes the era rather than a bool, so the Future gets its own sound instead
    // of borrowing the Present's.
    public void PlayTimeSwitch(TimeSingleton.GameState era)
    {
        PlayWithLabel(timeSwitchEvent, "Era", era.ToString());
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
        instance.start();

        // Released instances are cleaned up by FMOD once the one-shot finishes.
        instance.release();
    }
}