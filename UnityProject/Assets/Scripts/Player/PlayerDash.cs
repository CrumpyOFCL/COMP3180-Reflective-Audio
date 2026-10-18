using System.Collections;
using UnityEngine;

public class PlayerDash : MonoBehaviour
{
    [HideInInspector] public Vector2 inputDirection;

    [Header("References")]
    [HideInInspector] public Rigidbody2D rb;

    [Header("Dash Values")]
    [SerializeField] private float dashSpeed = 28f;
    [SerializeField] private float dashDuration = 0.18f;
    [SerializeField] private float dashCooldown = 0.35f;
    [SerializeField] private bool removeGravityDuringDash = true;

    [Header("Upgrade Values")]
    public int totalDashes;
    [SerializeField] public float dashDistance;
    [HideInInspector] public bool isDashing;

    private bool canDash = true;

    public bool CanDash => canDash;

    private float defaultGravity;

    [Header("Upgrade Values")]
    public int dashesRemaining;
    private int maxDashes;

    private void Start()
    {
        defaultGravity = rb.gravityScale;
    }

    public bool Dash()
    {
        if (!canDash || isDashing || dashesRemaining <= 0)
            return false;

        dashesRemaining--;

        StartCoroutine(DashCoroutine());
        return true;
    }

    // try/finally, because Unity disposes a coroutine when the component is
    // disabled or the object destroyed - which runs the finally block. Without
    // it, being interrupted mid-dash left isDashing latched on, canDash off and
    // gravityScale at zero: the player floats and can never dash again.
    private IEnumerator DashCoroutine()
    {
        canDash = false;
        isDashing = true;

        try
        {
            if (removeGravityDuringDash)
                rb.gravityScale = 0f;

            rb.linearVelocity = inputDirection.normalized * (dashSpeed + dashDistance);

            yield return new WaitForSeconds(dashDuration);
        }
        finally
        {
            if (removeGravityDuringDash && rb != null)
                rb.gravityScale = defaultGravity;

            isDashing = false;
        }

        try
        {
            yield return new WaitForSeconds(dashCooldown);
        }
        finally
        {
            canDash = true;
        }
    }

    public void RefreshDash(int totalDashes)
    {
        if (!isDashing)
        {
            dashesRemaining = totalDashes;
        }
    }

}