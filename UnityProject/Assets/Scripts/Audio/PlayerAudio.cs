using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.Audio;

public class PlayerAudio : MonoBehaviour
{
    #region - Audio Sources -

    [Header("Audio Sources")]
    [Tooltip("One-shot source used for jump/dash/shoot/time-switch. Assign in Inspector and set its Output to your mixer group.")]
    [SerializeField] private AudioSource oneShotSource;

    #endregion

    #region - Mixer (optional) -

    [Header("Mixer (optional fallback)")]
    [Tooltip("Only used if the sources above don't already have an Output assigned.")]
    [SerializeField] private AudioMixerGroup playerSFXGroup;

    #endregion

    #region - Footsteps (FMOD) -

    [Header("Footsteps (FMOD)")]
    [SerializeField] private EventReference footstepEvent;

    [Tooltip("Horizontal distance travelled between steps. Tune against the walk animation's foot plants.")]
    [SerializeField] private float stepDistance = 0.9f;

    [Tooltip("Layers to probe for a SurfaceType. Include all three era layers plus Solid - inactive eras are filtered out automatically.")]
    [SerializeField] private LayerMask groundLayers;

    [Tooltip("How far below the player to look for the surface.")]
    [SerializeField] private float surfaceProbeDistance = 1.5f;

    [Tooltip("Used when the ground has no SurfaceType. Must match a label on the FMOD Surface parameter.")]
    [SerializeField] private string defaultSurface = "Stone";

    #endregion

    #region - Jumping -

    [Header("Jumping")]
    [SerializeField] private AudioClip jumpClip;
    [Range(0f, 1f)][SerializeField] private float jumpVolume = 0.7f;
    [Range(0.1f, 3f)][SerializeField] private float jumpPitch = 1f;
    [SerializeField] private AudioClip doubleJumpClip;
    [Range(0f, 1f)][SerializeField] private float doubleJumpVolume = 0.7f;
    [Range(0.1f, 3f)][SerializeField] private float doubleJumpPitch = 1f;

    #endregion

    #region - Dashing -

    [Header("Dashing")]
    [SerializeField] private AudioClip dashClip;
    [Range(0f, 1f)][SerializeField] private float dashVolume = 0.7f;
    [Range(0.1f, 3f)][SerializeField] private float dashPitch = 1f;

    #endregion

    #region - Shooting -

    [Header("Shooting")]
    [SerializeField] private AudioClip shootClip;
    [Range(0f, 1f)][SerializeField] private float shootVolume = 0.7f;
    [Range(0.1f, 3f)][SerializeField] private float shootPitch = 1f;

    #endregion

    #region - Time Switch -

    [Header("Time Switch")]
    [SerializeField] private AudioClip switchToPastClip;
    [Range(0f, 1f)][SerializeField] private float switchToPastVolume = 0.7f;
    [Range(0.1f, 3f)][SerializeField] private float switchToPastPitch = 1f;
    [SerializeField] private AudioClip switchToPresentClip;
    [Range(0f, 1f)][SerializeField] private float switchToPresentVolume = 0.7f;
    [Range(0.1f, 3f)][SerializeField] private float switchToPresentPitch = 1f;
    [Header("Time Switch Fail")]
    [SerializeField] private AudioClip switchFailClip;
    [Range(0f, 1f)][SerializeField] private float switchFailVolume = 0.5f;
    [Range(0.1f, 3f)][SerializeField] private float switchFailPitch = 1f;

    #endregion


    private bool isWalking;
    private float distanceSinceStep;
    private float lastStepX;


    private void Awake()
    {
        // Fallback: create the source only if it wasn't assigned in the Inspector.
        if (oneShotSource == null)
            oneShotSource = gameObject.AddComponent<AudioSource>();

        ConfigureSource(oneShotSource, loop: false);
    }

    private void ConfigureSource(AudioSource source, bool loop)
    {
        source.loop = loop;
        source.playOnAwake = false;
        source.spatialBlend = 0f;

        // Only override the output if the source doesn't already have one set in the Inspector.
        if (source.outputAudioMixerGroup == null && playerSFXGroup != null)
            source.outputAudioMixerGroup = playerSFXGroup;
    }

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
        PlayFootstep();
    }

    public void StopWalking()
    {
        isWalking = false;
        distanceSinceStep = 0f;
    }

    private void PlayFootstep()
    {
        if (footstepEvent.IsNull) return;

        EventInstance step = RuntimeManager.CreateInstance(footstepEvent);
        step.set3DAttributes(RuntimeUtils.To3DAttributes(gameObject));
        step.setParameterByNameWithLabel("Surface", DetectSurface());
        step.start();

        // Released instances are cleaned up by FMOD once the one-shot finishes.
        step.release();
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

    public void PlayJump(int jumpCount)
    {
        if (jumpCount <= 0 && jumpClip != null)
        {
            oneShotSource.pitch = jumpPitch;
            oneShotSource.PlayOneShot(jumpClip, jumpVolume);
        }
        else if (jumpCount > 0 && doubleJumpClip != null)
        {
            oneShotSource.pitch = doubleJumpPitch;
            oneShotSource.PlayOneShot(doubleJumpClip, doubleJumpVolume);
        }
    }

    public void PlayDash()
    {
        PlayClip(dashClip, dashVolume, dashPitch);
    }

    public void PlayShoot()
    {
        PlayClip(shootClip, shootVolume, shootPitch);
    }

    public void PlayTimeSwitch(bool switchingToPast)
    {
        if (switchingToPast)
            PlayClip(switchToPastClip, switchToPastVolume, switchToPastPitch);
        else
            PlayClip(switchToPresentClip, switchToPresentVolume, switchToPresentPitch);
    }

    public void PlaySwitchFail()
    {
        PlayClip(switchFailClip, switchFailVolume, switchFailPitch);
    }

    private void PlayClip(AudioClip clip, float volume, float pitch)
    {
        if (clip == null) return;

        oneShotSource.pitch = pitch;
        oneShotSource.PlayOneShot(clip, volume);
    }
}