using System;
using System.Globalization;
using System.IO;
using System.Text;
using FMODUnity;
using UnityEngine;

namespace ReflectiveAudio2D
{
    /// <summary>
    /// Samples FMOD and Unity performance on a timer and writes a CSV tagged with the
    /// active acoustic condition. Feeds the ANALYSE learning goal: without this you
    /// have no cost figures to weigh against the perceptual results.
    ///
    /// Walk the SAME route in every condition or the comparison means nothing.
    /// Output: Application.persistentDataPath (printed to the console on start).
    /// </summary>
    [AddComponentMenu("Reflective Audio 2D/Audio Benchmark Logger")]
    public class AudioBenchmarkLogger : MonoBehaviour
    {
        [Header("Sampling")]
        [SerializeField] private float sampleInterval = 0.5f;
        [SerializeField] private bool logOnStart = true;

        [Header("Session")]
        [SerializeField] private string participantId = "P00";
        [SerializeField] private string routeName = "RouteA";

        [Header("Controls")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F10;
        [SerializeField] private KeyCode markerKey = KeyCode.F11;

        private readonly StringBuilder buffer = new StringBuilder();
        private string filePath;
        private float timer;
        private bool logging;
        private string pendingMarker = "";

        private void Start()
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            filePath = Path.Combine(Application.persistentDataPath,
                                    $"audio-benchmark_{participantId}_{stamp}.csv");

            buffer.AppendLine("time,participant,route,condition,fps,studio_cpu,dsp_cpu," +
                              "update_cpu,channels_playing,real_channels,fmod_mem_kb,marker");

            Debug.Log($"[AudioBenchmarkLogger] Writing to: {filePath}");
            if (logOnStart) logging = true;
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                logging = !logging;
                Debug.Log($"[AudioBenchmarkLogger] Logging {(logging ? "ON" : "OFF")}");
            }

            if (Input.GetKeyDown(markerKey))
            {
                pendingMarker = "MARK";
                Debug.Log("[AudioBenchmarkLogger] Marker placed");
            }

            if (!logging) return;

            timer += Time.unscaledDeltaTime;
            if (timer < sampleInterval) return;
            timer = 0f;

            Sample();
        }

        private void Sample()
        {
            float fps = 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime);

            float studioCpu = 0f, dspCpu = 0f, updateCpu = 0f;
            int channels = 0, realChannels = 0, memKb = 0;

            try
            {
                RuntimeManager.StudioSystem.getCPUUsage(
                    out FMOD.Studio.CPU_USAGE studioUsage, out FMOD.CPU_USAGE coreUsage);
                studioCpu = studioUsage.update;
                dspCpu = coreUsage.dsp;
                updateCpu = coreUsage.update;

                RuntimeManager.CoreSystem.getChannelsPlaying(out channels, out realChannels);

                FMOD.Memory.GetStats(out int currentAlloc, out int _);
                memKb = currentAlloc / 1024;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AudioBenchmarkLogger] Stat read failed: {e.Message}");
            }

            var c = CultureInfo.InvariantCulture;
            buffer.AppendLine(string.Join(",",
                Time.timeSinceLevelLoad.ToString("F2", c),
                participantId,
                routeName,
                ConditionManager.Current.ToString(),
                fps.ToString("F1", c),
                studioCpu.ToString("F3", c),
                dspCpu.ToString("F3", c),
                updateCpu.ToString("F3", c),
                channels.ToString(c),
                realChannels.ToString(c),
                memKb.ToString(c),
                pendingMarker));

            pendingMarker = "";
        }

        public void SetSession(string participant, string route)
        {
            participantId = participant;
            routeName = route;
        }

        private void Flush()
        {
            if (buffer.Length == 0 || string.IsNullOrEmpty(filePath)) return;
            try
            {
                File.AppendAllText(filePath, buffer.ToString());
                buffer.Clear();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioBenchmarkLogger] Write failed: {e.Message}");
            }
        }

        private void OnApplicationPause(bool paused) { if (paused) Flush(); }
        private void OnApplicationQuit() => Flush();
        private void OnDisable() => Flush();
    }
}
