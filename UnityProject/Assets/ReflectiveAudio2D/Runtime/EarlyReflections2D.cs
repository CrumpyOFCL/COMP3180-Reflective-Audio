using FMODUnity;
using UnityEngine;

namespace ReflectiveAudio2D
{
    /// <summary>
    /// CONDITION B+ - discrete audible bounces.
    ///
    /// AcousticProbe2D gives a sense of space. This gives an actual echo.
    /// Casts rays in a few directions, converts wall distance into delay time:
    ///
    ///     delay (ms) = (2 * distance in metres / 343) * 1000
    ///
    /// A wall 10 m away gives about 58 ms. Below roughly 30 ms the repeat fuses with
    /// the direct sound and reads as colouration rather than echo.
    ///
    /// CRITICAL: when a ray misses, fade the GAIN to zero and leave the delay time
    /// where it is. Sweeping a delay time while it is audible pitch-shifts the signal
    /// and sounds like a tape machine.
    ///
    /// Put this on the same GameObject as StudioListener / Listener2D.
    /// </summary>
    [AddComponentMenu("Reflective Audio 2D/Early Reflections 2D")]
    public class EarlyReflections2D : MonoBehaviour
    {
        [System.Serializable]
        public class Tap
        {
            public string label = "L";
            public Vector2 direction = Vector2.left;
            public string delayParameter = "ReflectDelayL";
            public string gainParameter = "ReflectGainL";

            [HideInInspector] public float delayMs;
            [HideInInspector] public float gain;
            [HideInInspector] public float lastDistance;
            [HideInInspector] public bool lastHit;
        }

        [Header("Taps")]
        [SerializeField]
        private Tap[] taps =
        {
            new Tap { label = "L",  direction = Vector2.left,  delayParameter = "ReflectDelayL",  gainParameter = "ReflectGainL"  },
            new Tap { label = "R",  direction = Vector2.right, delayParameter = "ReflectDelayR",  gainParameter = "ReflectGainR"  },
            new Tap { label = "Up", direction = Vector2.up,    delayParameter = "ReflectDelayUp", gainParameter = "ReflectGainUp" }
        };

        [Header("Measurement")]
        [SerializeField] private LayerMask surfaceLayers;
        [SerializeField] private float maxDistance = 30f;
        [Tooltip("World units per metre. Leave at 1 if 1 Unity unit = 1 metre.")]
        [SerializeField] private float unitsPerMetre = 1f;
        [SerializeField] private float speedOfSound = 343f;
        [SerializeField] private float updateInterval = 0.2f;

        [Header("Response")]
        [Tooltip("Milliseconds per second the delay time is allowed to travel. " +
                 "Keep low; fast delay-time changes are audible as pitch shift.")]
        [SerializeField] private float delaySlewMsPerSecond = 80f;
        [SerializeField] private float gainSlewPerSecond = 2f;

        [Tooltip("Reflections shorter than this fuse with the direct sound. " +
                 "Below this threshold the tap is silenced.")]
        [SerializeField] private float minUsefulDelayMs = 25f;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        private float timer;

        private void OnDisable()
        {
            // CHANGED: skip during quit (RuntimeManager may be gone) and skip taps
            // with no gain parameter, matching the guard Update already uses.
            if (!RuntimeManager.IsInitialized || taps == null) return;

            foreach (var tap in taps)
                if (!string.IsNullOrEmpty(tap.gainParameter))
                    RuntimeManager.StudioSystem.setParameterByName(tap.gainParameter, 0f);
        }

        private void Update()
        {
            timer += Time.deltaTime;
            if (timer >= updateInterval)
            {
                timer = 0f;
                Measure();
            }

            foreach (var tap in taps)
            {
                if (!string.IsNullOrEmpty(tap.delayParameter))
                    RuntimeManager.StudioSystem.setParameterByName(tap.delayParameter, tap.delayMs);
                if (!string.IsNullOrEmpty(tap.gainParameter))
                    RuntimeManager.StudioSystem.setParameterByName(tap.gainParameter, tap.gain);
            }
        }

        private void Measure()
        {
            Vector2 origin = transform.position;
            float delayStep = delaySlewMsPerSecond * updateInterval;
            float gainStep = gainSlewPerSecond * updateInterval;

            foreach (var tap in taps)
            {
                Vector2 dir = tap.direction.normalized;
                RaycastHit2D hit = Physics2D.Raycast(origin, dir, maxDistance, surfaceLayers);

                float targetDelay;
                float targetGain;

                if (hit.collider != null)
                {
                    float metres = hit.distance / Mathf.Max(0.0001f, unitsPerMetre);
                    targetDelay = (2f * metres / speedOfSound) * 1000f;

                    // Distant walls return a weaker reflection. Very near walls
                    // produce a delay too short to read as a bounce, so mute those too.
                    targetGain = Mathf.Clamp01(1f - hit.distance / maxDistance);
                    if (targetDelay < minUsefulDelayMs) targetGain = 0f;

                    tap.lastDistance = hit.distance;
                    tap.lastHit = true;
                }
                else
                {
                    targetDelay = tap.delayMs;   // HOLD. Do not sweep to zero.
                    targetGain = 0f;
                    tap.lastDistance = maxDistance;
                    tap.lastHit = false;
                }

                // Only move the delay time while the tap is effectively silent.
                if (tap.gain < 0.02f)
                    tap.delayMs = Mathf.MoveTowards(tap.delayMs, targetDelay, delayStep * 8f);
                else
                    tap.delayMs = Mathf.MoveTowards(tap.delayMs, targetDelay, delayStep);

                tap.gain = Mathf.MoveTowards(tap.gain, targetGain, gainStep);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || taps == null) return;
            Vector2 o = transform.position;
            foreach (var tap in taps)
            {
                Gizmos.color = tap.lastHit ? Color.magenta : new Color(0.5f, 0.2f, 0.5f, 0.4f);
                float d = Application.isPlaying ? tap.lastDistance : maxDistance;
                Gizmos.DrawLine(o, o + tap.direction.normalized * d);
            }
        }
    }
}
