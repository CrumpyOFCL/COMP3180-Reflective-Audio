using System.Collections;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace ReflectiveAudio2D
{
    /// <summary>
    /// CONDITION A - authored zone reverb.
    ///
    /// Put this on a trigger Collider2D covering a region. It cross-fades a BLENDING
    /// FMOD snapshot in and out using the snapshot's built-in Intensity macro.
    /// Blending snapshots stack, so overlapping zones blend at a doorway instead of
    /// fighting. Never switch a snapshot instantly; the fade is the whole point.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [AddComponentMenu("Reflective Audio 2D/Acoustic Zone 2D")]
    public class AcousticZone2D : MonoBehaviour
    {
        [SerializeField] private EventReference zoneSnapshot;
        [SerializeField] private string playerTag = "Player";
        [SerializeField] private float fadeSeconds = 0.6f;

        [Tooltip("Zones only apply in this condition. Leave as ZoneBaseline unless " +
                 "you want authored reverb layered under the dynamic system too.")]
        [SerializeField] private AcousticCondition activeInCondition = AcousticCondition.ZoneBaseline;

        [SerializeField] private bool alwaysActive = false;

        private EventInstance snapshot;
        private Coroutine fadeRoutine;
        private bool playerInside;

        private void Reset()
        {
            GetComponent<Collider2D>().isTrigger = true;
        }

        private void OnEnable()
        {
            ConditionManager.ConditionChanged += OnConditionChanged;
        }

        private void OnDisable()
        {
            ConditionManager.ConditionChanged -= OnConditionChanged;
            KillSnapshot();
        }

        private bool ConditionAllows =>
            alwaysActive ||
            ConditionManager.Current == activeInCondition ||
            ConditionManager.Instance == null;

        private void OnConditionChanged(AcousticCondition condition)
        {
            if (!ConditionAllows) FadeOutAndKill();
            else if (playerInside) Begin();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag(playerTag)) return;
            playerInside = true;
            if (ConditionAllows) Begin();
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (!other.CompareTag(playerTag)) return;
            playerInside = false;
            FadeOutAndKill();
        }

        private void Begin()
        {
            if (zoneSnapshot.IsNull) return;

            if (!snapshot.isValid())
            {
                snapshot = RuntimeManager.CreateInstance(zoneSnapshot);
                snapshot.start();
            }
            StartFade(CurrentIntensity(), 100f, false);
        }

        private void FadeOutAndKill()
        {
            if (!snapshot.isValid()) return;
            StartFade(CurrentIntensity(), 0f, true);
        }

        private float CurrentIntensity()
        {
            if (!snapshot.isValid()) return 0f;
            snapshot.getParameterByName("Intensity", out float value);
            return value;
        }

        private void StartFade(float from, float to, bool killWhenDone)
        {
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            fadeRoutine = StartCoroutine(Fade(from, to, killWhenDone));
        }

        private IEnumerator Fade(float from, float to, bool killWhenDone)
        {
            float t = 0f;
            float duration = Mathf.Max(0.01f, fadeSeconds);

            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                if (snapshot.isValid())
                    snapshot.setParameterByName("Intensity", Mathf.Lerp(from, to, t / duration));
                yield return null;
            }

            if (snapshot.isValid()) snapshot.setParameterByName("Intensity", to);
            if (killWhenDone) KillSnapshot();
            fadeRoutine = null;
        }

        private void KillSnapshot()
        {
            if (!snapshot.isValid()) return;
            // CHANGED: fully qualified - bare STOP_MODE is ambiguous between
            // FMOD.Studio and FMODUnity (CS0104) with both usings in scope.
            snapshot.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            snapshot.release();
            snapshot.clearHandle();
        }
    }
}
