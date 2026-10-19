using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

namespace ReflectiveAudio2D
{
    /// <summary>
    /// CONDITION B - listener-centred raycast reverb.
    ///
    /// Fires a ring of 2D rays from the listener and derives two numbers:
    ///   RoomSize  - average distance to a surface, normalised. Big average, big room.
    ///   Enclosure - fraction of rays that hit anything. High means boxed in.
    /// Optionally also derives Brightness from the Reflectivity of what it hits.
    ///
    /// These are written to FMOD as GLOBAL parameters, so a single reverb return bus
    /// can be automated against them without touching any individual event.
    ///
    /// Put this on the same GameObject as StudioListener / Listener2D.
    /// </summary>
    [AddComponentMenu("Reflective Audio 2D/Acoustic Probe 2D")]
    public class AcousticProbe2D : MonoBehaviour
    {
        [Header("Probing")]
        [Tooltip("16 is plenty. More rays costs more and changes nothing audible.")]
        [SerializeField] private int rayCount = 16;
        [Tooltip("What counts as 'outdoors'. Roughly two screen widths.")]
        [SerializeField] private float maxRayDistance = 25f;
        [SerializeField] private LayerMask surfaceLayers;
        [SerializeField] private float updateInterval = 0.1f;

        [Header("Response")]
        [Tooltip("THE setting that makes or breaks this. Lower = slower response. " +
                 "Too high and reverb flickers as you walk past a doorway.")]
        [SerializeField] private float smoothing = 2f;

        [Header("Optional material sampling")]
        [Tooltip("Reads SurfaceType.Reflectivity from what the rays hit and writes " +
                 "a Brightness global parameter. Requires a matching FMOD parameter.")]
        [SerializeField] private bool sampleMaterials = false;

        [Header("FMOD global parameter names")]
        [SerializeField] private string roomSizeParameter = "RoomSize";
        [SerializeField] private string enclosureParameter = "Enclosure";
        [SerializeField] private string brightnessParameter = "Brightness";

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private bool logValues = false;

        public float RoomSize { get; private set; }
        public float Enclosure { get; private set; }
        public float Brightness { get; private set; } = 0.5f;

        private float timer;
        private ContactFilter2D filter;
        private readonly List<RaycastHit2D> hitBuffer = new List<RaycastHit2D>(8);
        private Vector2[] directions;
        private float[] lastHitDistance;

        private void Awake()
        {
            BuildDirections();
            filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = surfaceLayers,
                useTriggers = false
            };
        }

        private void OnValidate()
        {
            rayCount = Mathf.Max(4, rayCount);
            if (Application.isPlaying) BuildDirections();
        }

        private void BuildDirections()
        {
            directions = new Vector2[rayCount];
            lastHitDistance = new float[rayCount];
            for (int i = 0; i < rayCount; i++)
            {
                float a = (Mathf.PI * 2f / rayCount) * i;
                directions[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                lastHitDistance[i] = maxRayDistance;
            }
        }

        private void OnDisable()
        {
            // CHANGED: during application quit the RuntimeManager may already be
            // torn down; touching StudioSystem then is pointless and can log errors.
            if (!RuntimeManager.IsInitialized) return;

            // Leave no residual reverb behind when this condition is switched off.
            SetGlobal(roomSizeParameter, 0f);
            SetGlobal(enclosureParameter, 0f);

            // CHANGED: Brightness was left at its last value. Back to neutral.
            if (sampleMaterials) SetGlobal(brightnessParameter, 0.5f);
        }

        private void Update()
        {
            timer += Time.deltaTime;
            if (timer >= updateInterval)
            {
                timer = 0f;
                Probe(out float targetSize, out float targetEnclosure, out float targetBrightness);

                float step = smoothing * updateInterval;
                RoomSize = Mathf.MoveTowards(RoomSize, targetSize, step);
                Enclosure = Mathf.MoveTowards(Enclosure, targetEnclosure, step);
                if (sampleMaterials)
                    Brightness = Mathf.MoveTowards(Brightness, targetBrightness, step);

                if (logValues)
                    Debug.Log($"[AcousticProbe2D] size {RoomSize:F2}  enclosure {Enclosure:F2}");
            }

            SetGlobal(roomSizeParameter, RoomSize);
            SetGlobal(enclosureParameter, Enclosure);
            if (sampleMaterials) SetGlobal(brightnessParameter, Brightness);
        }

        private void Probe(out float size, out float enclosureRatio, out float brightness)
        {
            Vector2 origin = transform.position;
            float totalDistance = 0f;
            float totalReflectivity = 0f;
            int hits = 0;

            for (int i = 0; i < directions.Length; i++)
            {
                hitBuffer.Clear();
                int count = Physics2D.Raycast(origin, directions[i], filter, hitBuffer, maxRayDistance);

                if (count > 0)
                {
                    RaycastHit2D hit = hitBuffer[0];
                    totalDistance += hit.distance;
                    lastHitDistance[i] = hit.distance;
                    hits++;

                    if (sampleMaterials)
                    {
                        // CHANGED: explicit global:: - the namespaced
                        // ReflectiveAudio2D.SurfaceType is retired, and the kept class
                        // lives in the global namespace. InParent, matching PlayerAudio,
                        // so a SurfaceType on the tilemap parent of the collider counts.
                        var surface = hit.collider.GetComponentInParent<global::SurfaceType>();
                        totalReflectivity += surface != null ? surface.Reflectivity : 0.5f;
                    }
                }
                else
                {
                    totalDistance += maxRayDistance;   // open sky counts as far away
                    lastHitDistance[i] = maxRayDistance;
                }
            }

            size = Mathf.Clamp01((totalDistance / directions.Length) / maxRayDistance);
            enclosureRatio = (float)hits / directions.Length;
            brightness = hits > 0 ? totalReflectivity / hits : 0.5f;
        }

        private static void SetGlobal(string name, float value)
        {
            if (string.IsNullOrEmpty(name)) return;
            RuntimeManager.StudioSystem.setParameterByName(name, value);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            if (directions == null || directions.Length != rayCount) BuildDirections();

            Vector2 o = transform.position;
            for (int i = 0; i < directions.Length; i++)
            {
                float d = Application.isPlaying ? lastHitDistance[i] : maxRayDistance;
                Gizmos.color = d < maxRayDistance ? Color.cyan : new Color(0.2f, 0.4f, 0.5f, 0.4f);
                Gizmos.DrawLine(o, o + directions[i] * d);
            }
        }
    }
}
