using UnityEngine;
using UnityEngine.Audio;

public class PlayerAudio : MonoBehaviour
{
    #region - Audio Sources -

    private AudioSource walkSource;
    private AudioSource oneShotSource;

    #endregion

    #region - Mixer -

    [Header("Mixer")]
    [SerializeField] private AudioMixerGroup playerSFXGroup;

    #endregion

    #region - Walking -

    [Header("Walking")]
    [SerializeField] private AudioClip walkClip;
    [Range(0f, 1f)][SerializeField] private float walkVolume = 0.5f;
    [Range(0.1f, 3f)][SerializeField] private float walkPitch = 1f;

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


    private void Awake()
    {
        walkSource = gameObject.AddComponent<AudioSource>();
        walkSource.loop = true;
        walkSource.playOnAwake = false;
        walkSource.spatialBlend = 0f;

        oneShotSource = gameObject.AddComponent<AudioSource>();
        oneShotSource.loop = false;
        oneShotSource.playOnAwake = false;
        oneShotSource.spatialBlend = 0f;

        if (playerSFXGroup != null)
        {
            walkSource.outputAudioMixerGroup = playerSFXGroup;
            oneShotSource.outputAudioMixerGroup = playerSFXGroup;
        }
    }

    public void UpdateWalking(bool grounded, bool moving)
    {
        bool shouldWalk = grounded && moving;

        if (shouldWalk && !isWalking)
            StartWalking();
        else if (!shouldWalk && isWalking)
            StopWalking();
    }

    private void StartWalking()
    {
        if (walkClip == null) return;

        isWalking = true;
        walkSource.clip = walkClip;
        walkSource.volume = walkVolume;
        walkSource.pitch = walkPitch;
        walkSource.Play();
    }

    public void StopWalking()
    {
        isWalking = false;
        walkSource.Stop();
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
        if (dashClip != null)
        {
            oneShotSource.pitch = dashPitch;
            oneShotSource.PlayOneShot(dashClip, dashVolume);
        }
    }


    public void PlayShoot()
    {
        if (shootClip != null)
        {
            oneShotSource.pitch = shootPitch;
            oneShotSource.PlayOneShot(shootClip, shootVolume);
        }
    }


    public void PlayTimeSwitch(bool switchingToPast)
    {
        if (switchingToPast && switchToPastClip != null)
        {
            oneShotSource.pitch = switchToPastPitch;
            oneShotSource.PlayOneShot(switchToPastClip, switchToPastVolume);
        }
        else if (!switchingToPast && switchToPresentClip != null)
        {
            oneShotSource.pitch = switchToPresentPitch;
            oneShotSource.PlayOneShot(switchToPresentClip, switchToPresentVolume);
        }
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