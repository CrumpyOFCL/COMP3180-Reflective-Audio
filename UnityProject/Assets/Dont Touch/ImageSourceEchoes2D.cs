using System.Collections;
using FMODUnity;
using UnityEngine;

namespace ReflectiveAudio2D
{
    /// <summary>
    /// CONDITION B: source-aware first-order reflections for one-shot sounds made
    /// away from the player, such as the beam hitting a wall.
    ///
    /// EarlyReflections2D measures from the listener, so it assumes every sound
    /// starts at the player. That is right for footsteps and jumps, but not for a
    /// sound made across the room. This uses the image-source method instead:
    /// mirror the source in each nearby wall, and the mirrored point is where the
    /// reflection appears to come from.
    ///
    ///     extra path = |image - listener| - |source - listener|
    ///     delay (ms) = extra path / speed of sound * 1000
    ///     gain       = reflectivity * |source - listener| / |image - listener|
    ///
    /// So a bigger room means a longer path: more delay and more fall-off.
    ///
    /// Each echo is the same FMOD event, played after the delay. It is placed in
    /// the reflection's direction but at the direct sound's distance, so FMOD pans
    /// it correctly and attenuates it like the direct sound. The extra fall-off
    /// comes from the gain above, so nothing is counted twice.
    ///
    /// Rooms are assumed to have axis-aligned walls (true for tilemaps). A bounce
    /// point that lands in an opening such as a doorway is dropped, because the
    /// sound would leave the room there.
    ///
    /// Put this on the listener object (next to the Studio Listener). Call
    /// ImageSourceEchoes2D.Emit(event, position) after playing a one-shot.
    /// </summary>
    [AddComponentMenu("Reflective Audio 2D/Image Source Echoes 2D")]
    public class ImageSourceEchoes2D : MonoBehaviour
    {
        public static ImageSourceEchoes2D Instance { get; private set; }

        [Header("Geometry")]
        [Tooltip("Left empty, this object is the listener.")]
        [SerializeField] private Transform listener;
        [SerializeField] private LayerMask wallLayers;
        [SerializeField] private float maxRayDistance = 40f;
        [Tooltip("World units per metre. Leave at 1 if 1 Unity unit = 1 metre.")]
        [SerializeField] private float unitsPerMetre = 1f;
        [SerializeField] private float speedOfSound = 343f;

        [Header("Response")]
        [Tooltip("Share of the sound a wall reflects. 1 = perfect mirror, 0 = absorbs everything.")]
        [Range(0f, 1f)][SerializeField] private float reflectivity = 0.7f;
        [Tooltip("Echoes shorter than this fuse with the direct sound and are skipped.")]
        [SerializeField] private float minDelayMs = 10f;
        [SerializeField] private float maxDelayMs = 250f;
        [Tooltip("Echoes quieter than this are skipped.")]
        [Range(0f, 1f)][SerializeField] private float minGain = 0.05f;

        [Header("Openings")]
        [Tooltip("Drop a reflection whose bounce point is in a doorway or other gap. " +
                 "Turn off to treat openings as if the wall continued.")]
        [SerializeField] private bool requireSolidAtBounce = true;
        [SerializeField] private float bounceTolerance = 0.3f;

        [Header("Condition")]
        [SerializeField] private AcousticCondition activeInCondition = AcousticCondition.RaycastDynamic;
        [SerializeField] private bool alwaysActive;

        [Header("Debug")]
        [Tooltip("Draws each reflection path in the Scene view for a second.")]
        [SerializeField] private bool drawPaths = true;
        [SerializeField] private bool logEchoes;

        private static readonly Vector2[] Axes = { Vector2.left, Vector2.right, Vector2.up, Vector2.down };

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[ImageSourceEchoes2D] More than one in the scene; this one is disabled.", this);
                enabled = false;
                return;
            }
            Instance = this;
            if (listener == null) listener = transform;
            if (wallLayers.value == 0) wallLayers = LayerMask.GetMask("Solid");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Plays the reflections of a one-shot made at <paramref name="source"/>.</summary>
        public static void Emit(EventReference sound, Vector2 source)
        {
            if (Instance != null && Instance.isActiveAndEnabled)
                Instance.EmitEchoes(sound, source);
        }

        private bool ConditionAllows =>
            alwaysActive || ConditionManager.Instance == null || ConditionManager.Current == activeInCondition;

        private void EmitEchoes(EventReference sound, Vector2 source)
        {
            if (sound.IsNull || !ConditionAllows || listener == null) return;

            Vector2 ear = listener.position;
            float direct = Vector2.Distance(source, ear);
            if (direct < 0.01f) return;

            // The source usually sits on (or just inside) the wall it hit. Rays are
            // cast from a point nudged towards the listener so they start in the room.
            Vector2 sourceProbe = source + (ear - source).normalized * 0.2f;

            foreach (Vector2 axis in Axes)
            {
                if (!TryFindWall(sourceProbe, ear, axis, out float plane)) continue;

                Vector2 image = Mirror(source, axis, plane);
                float reflected = Vector2.Distance(image, ear);
                float delayMs = (reflected - direct) / unitsPerMetre / speedOfSound * 1000f;
                if (delayMs < minDelayMs || delayMs > maxDelayMs) continue;

                if (!TryBouncePoint(ear, image, axis, plane, out Vector2 bounce)) continue;
                if (requireSolidAtBounce && !(PathReaches(ear, bounce) && PathReaches(sourceProbe, bounce))) continue;

                float gain = reflectivity * direct / reflected;
                if (gain < minGain) continue;

                // Arrives from the bounce direction, at the direct sound's distance.
                Vector2 position = ear + (bounce - ear).normalized * direct;
                StartCoroutine(PlayLater(sound, position, gain, delayMs / 1000f));

                if (drawPaths)
                {
                    Debug.DrawLine(source, bounce, Color.yellow, 1f);
                    Debug.DrawLine(bounce, ear, Color.yellow, 1f);
                }
                if (logEchoes)
                    Debug.Log($"[ImageSourceEchoes2D] {AxisName(axis)} wall: +{delayMs:F0} ms, gain {gain:F2} ({20f * Mathf.Log10(gain):F1} dB)");
            }
        }

        // The wall bounding the room in this direction: the nearer of the walls hit
        // from the source and from the listener. Casting from both copes with a
        // doorway at one of the two heights.
        private bool TryFindWall(Vector2 source, Vector2 ear, Vector2 axis, out float plane)
        {
            plane = 0f;
            bool found = false;
            float best = float.PositiveInfinity;

            foreach (Vector2 origin in new[] { source, ear })
            {
                RaycastHit2D hit = Physics2D.Raycast(origin, axis, maxRayDistance, wallLayers);
                if (hit.collider == null) continue;

                float coordinate = axis.x != 0f ? hit.point.x : hit.point.y;
                float distanceAlongAxis = Vector2.Dot(axis, hit.point);
                if (distanceAlongAxis < best)
                {
                    best = distanceAlongAxis;
                    plane = coordinate;
                    found = true;
                }
            }

            if (!found) return false;

            // The wall must be beyond both points, or there is nothing to bounce off.
            float sourceAlong = Vector2.Dot(axis, source);
            float earAlong = Vector2.Dot(axis, ear);
            return best > Mathf.Max(sourceAlong, earAlong);
        }

        private static Vector2 Mirror(Vector2 point, Vector2 axis, float plane) =>
            axis.x != 0f
                ? new Vector2(2f * plane - point.x, point.y)
                : new Vector2(point.x, 2f * plane - point.y);

        private static bool TryBouncePoint(Vector2 ear, Vector2 image, Vector2 axis, float plane, out Vector2 bounce)
        {
            bounce = Vector2.zero;
            float from = axis.x != 0f ? ear.x : ear.y;
            float to = axis.x != 0f ? image.x : image.y;
            if (Mathf.Abs(to - from) < 0.0001f) return false;

            float t = (plane - from) / (to - from);
            if (t <= 0f || t >= 1f) return false;

            bounce = ear + (image - ear) * t;
            return true;
        }

        // True if a ray from 'from' towards the bounce point hits a wall right at it:
        // not blocked earlier, and not passing straight through an opening.
        private bool PathReaches(Vector2 from, Vector2 bounce)
        {
            Vector2 delta = bounce - from;
            float length = delta.magnitude;
            if (length < 0.0001f) return true;

            RaycastHit2D hit = Physics2D.Raycast(from, delta / length, length + bounceTolerance, wallLayers);
            return hit.collider != null && hit.distance >= length - bounceTolerance;
        }

        private static IEnumerator PlayLater(EventReference sound, Vector3 position, float gain, float delay)
        {
            // Unity resumes coroutines once per frame, so the echo can land up to
            // one frame late (about 8 ms on average at 60 fps).
            yield return new WaitForSecondsRealtime(delay);
            if (!RuntimeManager.IsInitialized) yield break;

            FMOD.Studio.EventInstance echo = RuntimeManager.CreateInstance(sound);
            echo.set3DAttributes(RuntimeUtils.To3DAttributes(position));
            echo.setVolume(gain);
            echo.start();
            echo.release();
        }

        private static string AxisName(Vector2 axis) =>
            axis == Vector2.left ? "Left" : axis == Vector2.right ? "Right" : axis == Vector2.up ? "Ceiling" : "Floor";
    }
}
