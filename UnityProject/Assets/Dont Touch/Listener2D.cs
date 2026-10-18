using UnityEngine;

namespace ReflectiveAudio2D
{
    /// <summary>
    /// Positions the FMOD listener for a 2D game.
    ///
    /// Put the FMODUnity.StudioListener component on THIS GameObject, never on the
    /// camera. A camera in a 2D scene sits at z = -10, which would place the listener
    /// 10 units from every sound in the game, permanently. That breaks minimum
    /// distance, flattens panning and makes every raycast measurement wrong.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after camera movement, so the listener never lags a frame
    [AddComponentMenu("Reflective Audio 2D/Listener 2D")]
    public class Listener2D : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform player;
        [SerializeField] private Transform cameraTransform;

        [Header("Placement")]
        [Tooltip("0 = listen from the player, 1 = listen from the camera centre. " +
                 "0.2-0.4 suits a platformer with a look-ahead camera.")]
        [Range(0f, 1f)]
        [SerializeField] private float cameraBias = 0.3f;

        [Tooltip("Smooths listener movement. 0 disables smoothing.")]
        [SerializeField] private float followSpeed = 0f;

        [Tooltip("ADDED (test scene): where the ears are relative to the player's pivot. " +
                 "The pivot is at the feet, so without this the listener sits on the floor " +
                 "and the probe, reflection and echo rays graze along it.")]
        [SerializeField] private Vector2 earOffset = new Vector2(0f, 1.5f);

        public Transform Player => player;

        private void Reset()
        {
            if (Camera.main != null) cameraTransform = Camera.main.transform;
        }

        private void Start()
        {
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;

            if (player == null)
                Debug.LogWarning("[Listener2D] No player assigned. Listener will follow the camera only.");
        }

        private void LateUpdate()
        {
            Vector3 target = ResolveTarget();

            transform.position = followSpeed > 0f
                ? Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime))
                : target;

            // A rotating listener in a 2D game swaps left and right unexpectedly.
            transform.rotation = Quaternion.identity;
        }

        private Vector3 ResolveTarget()
        {
            if (player == null && cameraTransform == null) return transform.position;
            if (player == null) return Flatten(cameraTransform.position);
            if (cameraTransform == null) return Flatten(player.position + (Vector3)earOffset);

            Vector3 p = player.position + (Vector3)earOffset;
            Vector3 c = cameraTransform.position;
            return new Vector3(Mathf.Lerp(p.x, c.x, cameraBias),
                               Mathf.Lerp(p.y, c.y, cameraBias),
                               0f);
        }

        private static Vector3 Flatten(Vector3 v) => new Vector3(v.x, v.y, 0f);

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.4f);
            if (player != null) Gizmos.DrawLine(transform.position, player.position);
        }
    }
}