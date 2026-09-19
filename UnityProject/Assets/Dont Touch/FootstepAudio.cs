using FMODUnity;
using UnityEngine;

namespace ReflectiveAudio2D
{
    /// <summary>
    /// OPTIONAL but recommended for the study build. A dry room with nothing sounding
    /// in it demos badly and gives participants nothing to judge the reverb with.
    /// Footsteps are the cheapest way to excite the space continuously.
    ///
    /// Call PlayFootstep() from an ANIMATION EVENT on the frames where the foot lands,
    /// not from a timer. Timers desynchronise from the animation and read as wrong
    /// immediately.
    ///
    /// Requires an FMOD event with labeled parameters "Surface" and "Gait".
    /// </summary>
    [AddComponentMenu("Reflective Audio 2D/Footstep Audio")]
    public class FootstepAudio : MonoBehaviour
    {
        [SerializeField] private EventReference footstepEvent;
        [SerializeField] private LayerMask groundLayers;
        [SerializeField] private float raycastDistance = 1.5f;
        [SerializeField] private float raycastUpOffset = 0.2f;
        [SerializeField] private string defaultSurface = "Stone";

        private string currentSurface;
        private string currentGait = "Walk";

        private void Awake() => currentSurface = defaultSurface;

        /// <summary>Call from an Animation Event on foot-plant frames.</summary>
        public void PlayFootstep()
        {
            if (footstepEvent.IsNull) return;

            UpdateSurface();

            var instance = RuntimeManager.CreateInstance(footstepEvent);
            instance.set3DAttributes(RuntimeUtils.To3DAttributes(transform.position));
            instance.setParameterByNameWithLabel("Surface", currentSurface);
            instance.setParameterByNameWithLabel("Gait", currentGait);
            instance.start();
            instance.release();   // flags for cleanup once it finishes; not a stop
        }

        public void SetGait(string gait) => currentGait = gait;

        private void UpdateSurface()
        {
            Vector2 origin = (Vector2)transform.position + Vector2.up * raycastUpOffset;
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, raycastDistance, groundLayers);

            if (hit.collider == null) return;

            var surface = hit.collider.GetComponent<SurfaceType>();
            currentSurface = surface != null ? surface.SurfaceName : defaultSurface;
        }
    }
}
