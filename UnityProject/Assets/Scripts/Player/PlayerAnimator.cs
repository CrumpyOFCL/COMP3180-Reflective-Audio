using UnityEngine;
using System.Collections.Generic;
using System.Collections;

[RequireComponent(typeof(Animator))]
public class PlayerAnimator : MonoBehaviour
{
    // Gets set by Player.cs.
    [HideInInspector] public Vector2 inputDirection;
    [HideInInspector] public Animator playerAnimator;

    [Header("Animation")]
    [SerializeField] private string movingAnimation;
    [SerializeField] private string onGroundAnimation;
    [SerializeField] private string pushingAnimation;
    [SerializeField] private string fallingAnimation;
    [SerializeField] private string climbingAnimation;
    [SerializeField] private string shootingAnimation;
    [SerializeField] private float shootingAnimationDuration = 0.1f;

    [Tooltip("Dedicated Animator float for Climbing state Speed Multiplier (e.g. climbSpeed). Hang=0, move=1. No bare Speed auto-detect.")]
    [SerializeField] private string climbingSpeedFloat = "climbSpeed";

    [Tooltip("Dedicated Animator float for Pushing state Speed Multiplier (e.g. pushSpeed). Still or blocked=0, pushing or pulling=1.")]
    [SerializeField] private string pushingSpeedFloat = "pushSpeed";

    [SerializeField] private Animator[] jumpingParticle;
    [SerializeField] private Animator[] landingParticle;
    [SerializeField] private Animator dashParticle;
    [SerializeField] private Animator timeBeamParticle;
    [SerializeField] private Animator timeSwitchParticle;

    [SerializeField] private string particleTrigger;

    private int jumpingParticleIndex;
    private int landingParticleIndex;

    private bool justTouchedGround;
    private bool justDashed;

    private Coroutine shootingCoroutine;

    // Name to hash, resolved once. SetBool(string) hashes the name on every
    // call, and this runs every frame.
    private readonly Dictionary<string, int> animHashes = new Dictionary<string, int>();

    private bool climbPresentationActive;
    private float climbPresentationVertical;
    private readonly List<int> climbSpeedFloatHashes = new List<int>();
    private bool climbParamMissingLogged;
    private bool climbSpeedFloatMissingLogged;

    private int pushSpeedFloatHash;
    private bool hasPushSpeedFloat;

    private void Awake()
    {
        playerAnimator =
            GetComponent<Animator>();

        CacheAnimationHashes();

        SetLayerWeightSafe(
            0,
            1f
        );

        SetLayerWeightSafe(
            1,
            0f
        );

        jumpingParticleIndex =
            0;

        landingParticleIndex =
            0;

        justTouchedGround =
            false;

        justDashed =
            false;
    }

    private void Update()
    {
        // Critical: while climbing, never let movingAnimation yank us out of climb.
        if (climbPresentationActive)
        {
            ChangeAnimation(movingAnimation, false);
            return;
        }

        ChangeAnimation(movingAnimation, Mathf.Abs(inputDirection.x) > 0.01f);
    }

    private void LateUpdate()
    {
        // Re-assert every frame so nothing after Player.Update undoes climb.
        if (climbPresentationActive)
            ApplyClimbPresentation();
    }

    public void PlayDashAnimation(
        bool turn,
        bool flip)
    {
        if (!justDashed && turn && dashParticle != null)
        {
            dashParticle.SetTrigger(
                particleTrigger
            );

            dashParticle.transform.position =
                transform.position;

            if (flip)
            {
                dashParticle.transform.rotation =
                    Quaternion.Euler(
                        0f,
                        0f,
                        0f
                    );
            }
            else
            {
                dashParticle.transform.rotation =
                    Quaternion.Euler(
                        0f,
                        180f,
                        0f
                    );
            }
        }

        justDashed =
            turn;
    }

    public void PlayFallingAnimation(
        bool turn)
    {
        ChangeAnimation(fallingAnimation, turn);
    }

    public void PlayClimbingAnimation(bool turn)
    {
        SetClimbPresentation(turn, turn ? climbPresentationVertical : 0f);
    }

    public void SetClimbPresentation(bool climbing, float verticalInput)
    {
        climbPresentationActive = climbing;
        climbPresentationVertical = verticalInput;
        ApplyClimbPresentation();
    }

    private void ApplyClimbPresentation()
    {
        if (playerAnimator == null) return;
        playerAnimator.speed = 1f;

        if (climbPresentationActive)
        {
            // Kill competing locomotion bools so Idle/Run/Fall don't win.
            ChangeAnimation(movingAnimation, false);
            ChangeAnimation(onGroundAnimation, false);
            ChangeAnimation(fallingAnimation, false);

            // Climb bool (Inspector climbingAnimation). Primary path — PE is bool-only.
            if (!string.IsNullOrEmpty(climbingAnimation) &&
                animHashes.TryGetValue(climbingAnimation, out int climbHash))
            {
                playerAnimator.SetBool(climbHash, true);
            }
            else if (!climbParamMissingLogged && !string.IsNullOrEmpty(climbingAnimation))
            {
                climbParamMissingLogged = true;
                Debug.LogError($"{name}: climb bool '{climbingAnimation}' missing on Animator — climb anim cannot play.", this);
            }

            // Bool-only path (Lead/PE): do not Animator.Play every LateUpdate —
            // that resets normalizedTime to 0 and looks like a still frame.

            // Dedicated climbSpeed float → Climbing state Speed Multiplier.
            // Hang still = 0, moving = 1. Never touch global Animator.speed.
            float climbMove = Mathf.Abs(climbPresentationVertical) > 0.01f ? 1f : 0f;
            foreach (int floatHash in climbSpeedFloatHashes)
                playerAnimator.SetFloat(floatHash, climbMove);
        }
        else
        {
            if (!string.IsNullOrEmpty(climbingAnimation) &&
                animHashes.TryGetValue(climbingAnimation, out int climbHash))
            {
                playerAnimator.SetBool(climbHash, false);
            }

            foreach (int floatHash in climbSpeedFloatHashes)
                playerAnimator.SetFloat(floatHash, 0f);
        }
    }

    /// <summary>
    /// Kept for callers; climb no longer uses speed 0 to freeze hang.
    /// Always clamps to a safe positive speed (defaults to 1).
    /// </summary>
    public void SetClimbPlaybackSpeed(float speed)
    {
        if (playerAnimator == null)
            return;

        if (speed < 0.999f)
            speed = 1f;

        playerAnimator.speed = speed;
    }

    public void PlayGroundAnimation(
        bool turn,
        Vector3 spawnPoint)
    {
        ChangeAnimation(
            onGroundAnimation,
            turn
        );

        if (!justTouchedGround &&
            turn &&
            landingParticle != null &&
            landingParticle.Length > 0)
        {
            Animator particle =
                landingParticle[
                    landingParticleIndex
                ];

            if (particle != null)
            {
                particle.SetTrigger(
                    particleTrigger
                );

                particle.transform.position =
                    spawnPoint;
            }

            landingParticleIndex++;

            if (landingParticleIndex >=
                landingParticle.Length)
            {
                landingParticleIndex =
                    0;
            }
        }

        justTouchedGround =
            turn;
    }

    public void PlayPushingAnimation(bool turn, float pushSpeed = 0f)
    {
        ChangeAnimation(pushingAnimation, turn);

        // Same as climbSpeed: the Pushing state's Speed Multiplier reads this,
        // so the clip only plays while the box is actually moving.
        if (playerAnimator != null && hasPushSpeedFloat)
        {
            float pushMove = turn && Mathf.Abs(pushSpeed) > 0.01f ? 1f : 0f;
            playerAnimator.SetFloat(pushSpeedFloatHash, pushMove);
        }
    }

    public void PlayJumpParticleAnimation(
        Vector3 spawnPoint)
    {
        if (jumpingParticle == null ||
            jumpingParticle.Length == 0)
        {
            return;
        }

        Animator particle =
            jumpingParticle[
                jumpingParticleIndex
            ];

        if (particle != null)
        {
            particle.SetTrigger(
                particleTrigger
            );

            particle.transform.position =
                spawnPoint;
        }

        jumpingParticleIndex++;

        if (jumpingParticleIndex >=
            jumpingParticle.Length)
        {
            jumpingParticleIndex =
                0;
        }
    }

    public void PlayShootParticleAnimation(
        Vector3 spawnPoint, float duration)
    {
        ChangeAnimation(shootingAnimation, true);

        if (timeBeamParticle == null)
            return;

        timeBeamParticle.transform.position =
            spawnPoint;

        timeBeamParticle.SetTrigger(
            particleTrigger
        );

        if (shootingCoroutine != null) StopCoroutine(shootingCoroutine);

        shootingCoroutine = StartCoroutine(ResetShootingAnimation(duration));
    }

    public void SetShootParticlePosition(
        Vector3 position)
    {
        if (timeBeamParticle != null)
        {
            timeBeamParticle.transform.position =
                position;
        }
    }

    public void PlayTimeSwitchAnimation()
    {
        if (timeSwitchParticle != null)
        {
            timeSwitchParticle.SetTrigger(
                particleTrigger
            );
        }
    }

    public void SwitchTimeSwitchSprite(
        bool flip)
    {
        if (timeSwitchParticle == null)
            return;

        if (flip)
        {
            timeSwitchParticle.transform.rotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    0f
                );
        }
        else
        {
            timeSwitchParticle.transform.rotation =
                Quaternion.Euler(
                    0f,
                    180f,
                    0f
                );
        }
    }

    private void CacheAnimationHashes()
    {
        if (playerAnimator == null)
            return;

        HashSet<string> available =
            new HashSet<string>();

        climbSpeedFloatHashes.Clear();

        foreach (
            AnimatorControllerParameter parameter
            in playerAnimator.parameters)
        {
            if (parameter.type ==
                AnimatorControllerParameterType.Bool)
            {
                available.Add(
                    parameter.name
                );
            }
            else if (parameter.type ==
                AnimatorControllerParameterType.Float)
            {
                // Dedicated float only (Lead/PE): never auto-detect bare Speed.
                if (!string.IsNullOrEmpty(climbingSpeedFloat) &&
                    parameter.name == climbingSpeedFloat)
                {
                    climbSpeedFloatHashes.Add(parameter.nameHash);
                }

                if (!string.IsNullOrEmpty(pushingSpeedFloat) &&
                    parameter.name == pushingSpeedFloat)
                {
                    pushSpeedFloatHash = parameter.nameHash;
                    hasPushSpeedFloat = true;
                }
            }
        }

        if (!string.IsNullOrEmpty(pushingSpeedFloat) &&
            !hasPushSpeedFloat)
        {
            Debug.LogWarning(
                $"{name}: push speed float '{pushingSpeedFloat}' missing on Animator — " +
                "add it and set Pushing state Speed Multiplier to that float.",
                this);
        }

        if (!string.IsNullOrEmpty(climbingSpeedFloat) &&
            climbSpeedFloatHashes.Count == 0 &&
            !climbSpeedFloatMissingLogged)
        {
            climbSpeedFloatMissingLogged = true;
            Debug.LogWarning(
                $"{name}: climb speed float '{climbingSpeedFloat}' missing on Animator — " +
                "add it and set Climbing state Speed Multiplier to that float for hang-still.",
                this);
        }

        string[] names =
        {
            movingAnimation,
            onGroundAnimation,
            pushingAnimation,
            fallingAnimation,
            climbingAnimation,
            shootingAnimation,
        };

        foreach (string animName in names)
        {
            if (string.IsNullOrEmpty(animName) ||
                animHashes.ContainsKey(animName))
            {
                continue;
            }

            if (!available.Contains(animName))
            {
                Debug.LogError(
                    $"{name}: the Animator has no bool parameter '{animName}'.",
                    this
                );

                continue;
            }

            animHashes[animName] =
                Animator.StringToHash(
                    animName
                );
        }
    }

    private void SetLayerWeightSafe(
        int layer,
        float weight)
    {
        if (playerAnimator == null ||
            layer >= playerAnimator.layerCount)
        {
            return;
        }

        playerAnimator.SetLayerWeight(
            layer,
            weight
        );
    }

    private void ChangeAnimation(
        string animName,
        bool turn)
    {
        if (playerAnimator == null)
            return;

        if (animHashes.TryGetValue(
                animName,
                out int hash))
        {
            playerAnimator.SetBool(
                hash,
                turn
            );
        }
    }

    private IEnumerator ResetShootingAnimation(float duration)
    {
        yield return new WaitForSeconds(duration + shootingAnimationDuration);
        ChangeAnimation(shootingAnimation, false);
    }

    public void SwitchToWandAnim()
    {
        SetLayerWeightSafe(
            0,
            1f
        );

        SetLayerWeightSafe(
            1,
            1f
        );

        if (timeSwitchParticle != null &&
            timeSwitchParticle.layerCount > 1)
        {
            timeSwitchParticle.SetLayerWeight(
                0,
                0f
            );

            timeSwitchParticle.SetLayerWeight(
                1,
                1f
            );
        }
    }
}