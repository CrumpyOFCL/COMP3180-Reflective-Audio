using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace ReflectiveAudio2D
{
    /// <summary>
    /// OPTIONAL. Per-emitter occlusion using 2D physics. A sound behind a wall should
    /// be DULLER as well as quieter; dullness is the cue that actually reads as "wall".
    ///
    /// WARNING for tilemaps: a CompositeCollider2D merges many tiles into one collider,
    /// so a ray through six tiles of solid rock reports ONE hit, not six. If occlusion
    /// feels binary, enable measureThickness below, which measures how far the ray
    /// travels inside solid geometry instead of counting hits.
    ///
    /// Requires an FMOD parameter named "Occlusion" (0-1) on the event, automating
    /// low-pass cutoff and volume. Give it a seek speed of about 3.
    /// </summary>
    [AddComponentMenu("Reflective Audio 2D/Occluded Emitter 2D")]
    public class OccludedEmitter2D : MonoBehaviour
    {
        [SerializeField] private EventReference eventRef;
        [SerializeField] private LayerMask occluderLayers;
        [SerializeField] private string occlusionParameter = "Occlusion";

        [Header("Method")]
        [SerializeField] private bool measureThickness = true;
        [Tooltip("Units of solid material for full occlusion, when measuring thickness.")]
        [SerializeField] private float unitsForFullOcclusion = 4f;
        [Tooltip("Occlusion added per collider crossed, when counting hits.")]
        [SerializeField] private float occlusionPerHit = 0.4f;

        [Header("Performance")]
        [Tooltip("Seven checks per second is imperceptible and much cheaper than " +
                 "every frame.")]
        [SerializeField] private float checkInterval = 0.15f;

        private EventInstance instance;
        private Transform listener;
        private float timer;
        private ContactFilter2D filter;
        private readonly List<RaycastHit2D> hits = new List<RaycastHit2D>(16);

        private void Start()
        {
            var studioListener = FindFirstObjectByType<StudioListener>();
            if (studioListener != null) listener = studioListener.transform;

            filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = occluderLayers,
                useTriggers = false
            };

            if (eventRef.IsNull) return;
            instance = RuntimeManager.CreateInstance(eventRef);
            RuntimeManager.AttachInstanceToGameObject(instance, transform);
            instance.start();
        }

        private void Update()
        {
            if (listener == null || !instance.isValid()) return;

            timer += Time.deltaTime;
            if (timer < checkInterval) return;
            timer = 0f;

            instance.setParameterByName(occlusionParameter, CalculateOcclusion());
        }

        private float CalculateOcclusion()
        {
            Vector2 origin = transform.position;
            Vector2 target = listener.position;
            Vector2 delta = target - origin;
            float distance = delta.magnitude;
            if (distance < 0.01f) return 0f;

            Vector2 dir = delta / distance;

            hits.Clear();
            int count = Physics2D.Raycast(origin, dir, filter, hits, distance);
            if (count == 0) return 0f;

            if (!measureThickness)
                return Mathf.Clamp01(count * occlusionPerHit);

            // Cast back from the listener to find where the solid geometry ends,
            // then use the overlap as a thickness estimate.
            var backHits = new List<RaycastHit2D>(16);
            Physics2D.Raycast(target, -dir, filter, backHits, distance);
            if (backHits.Count == 0) return Mathf.Clamp01(count * occlusionPerHit);

            float entry = hits[0].distance;
            float exit = distance - backHits[0].distance;
            float thickness = Mathf.Max(0f, exit - entry);

            return Mathf.Clamp01(thickness / Mathf.Max(0.01f, unitsForFullOcclusion));
        }

        private void OnDestroy()
        {
            if (!instance.isValid()) return;
            instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            instance.release();
        }

        private void OnDrawGizmosSelected()
        {
            if (listener == null) return;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, listener.position);
        }
    }
}
