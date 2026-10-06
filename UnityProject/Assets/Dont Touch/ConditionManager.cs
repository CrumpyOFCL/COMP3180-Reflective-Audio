using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace ReflectiveAudio2D
{
    public enum AcousticCondition
    {
        ZoneBaseline = 0,   // Condition A - authored snapshots only
        RaycastDynamic = 1, // Condition B - probe + delay taps
        SteamAudio = 2      // Condition C - geometry-based reference
    }

    /// <summary>
    /// Enables exactly ONE acoustic condition and mutes the return buses belonging to
    /// the other two. Without this you cannot counterbalance the study, and any
    /// comparison between conditions is contaminated by leftover reverb.
    ///
    /// Put this on a persistent GameObject in the scene.
    /// </summary>
    [AddComponentMenu("Reflective Audio 2D/Condition Manager")]
    public class ConditionManager : MonoBehaviour
    {
        public static ConditionManager Instance { get; private set; }
        public static event Action<AcousticCondition> ConditionChanged;

        public static AcousticCondition Current =>
            Instance != null ? Instance.current : AcousticCondition.ZoneBaseline;

        [Header("Starting condition")]
        [SerializeField] private AcousticCondition startingCondition = AcousticCondition.ZoneBaseline;

        [Header("FMOD bus paths")]
        [Tooltip("Check these against your FMOD mixer. A return bus created at the top " +
                 "level has the path bus:/Name.")]
        [SerializeField] private string zoneReverbBus = "bus:/Reverb Zone";
        [SerializeField] private string dynamicReverbBus = "bus:/Reverb Dynamic";
        [SerializeField] private string reflectionsBus = "bus:/Reflections";
        [SerializeField] private string steamAudioBus = "bus:/Steam Audio Reverb";

        [Header("Components to enable per condition")]
        [SerializeField] private AcousticProbe2D probe;
        [SerializeField] private EarlyReflections2D reflections;
        [Tooltip("Any Steam Audio components, emitters or GameObjects used only by " +
                 "the geometry-based reference condition.")]
        [SerializeField] private GameObject[] steamAudioObjects;

        [Header("Operator controls")]
        [Tooltip("Press to cycle conditions during a session. Keep hidden from participants.")]
        [SerializeField] private KeyCode cycleKey = KeyCode.F9;
        [SerializeField] private KeyCode conditionAKey = KeyCode.Alpha7;
        [SerializeField] private KeyCode conditionBKey = KeyCode.Alpha8;
        [SerializeField] private KeyCode conditionCKey = KeyCode.Alpha9;
        [SerializeField] private bool showOperatorLabel = true;

        private AcousticCondition current;
        private readonly Dictionary<string, Bus> buses = new Dictionary<string, Bus>();
        private bool busesResolved;

        public string CurrentConditionName => current.ToString();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            ResolveBuses();
            SetCondition(startingCondition);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(cycleKey))
                SetCondition((AcousticCondition)(((int)current + 1) % 3));
            else if (Input.GetKeyDown(conditionAKey)) SetCondition(AcousticCondition.ZoneBaseline);
            else if (Input.GetKeyDown(conditionBKey)) SetCondition(AcousticCondition.RaycastDynamic);
            else if (Input.GetKeyDown(conditionCKey)) SetCondition(AcousticCondition.SteamAudio);
        }

        public void SetCondition(AcousticCondition condition)
        {
            current = condition;
            if (!busesResolved) ResolveBuses();

            bool zone = condition == AcousticCondition.ZoneBaseline;
            bool dynamic = condition == AcousticCondition.RaycastDynamic;
            bool steam = condition == AcousticCondition.SteamAudio;

            Mute(zoneReverbBus, !zone);
            Mute(dynamicReverbBus, !dynamic);
            Mute(reflectionsBus, !dynamic);
            Mute(steamAudioBus, !steam);

            // Disabling the components stops the raycasting cost as well as the sound,
            // which matters because CPU cost is one of the measures being compared.
            if (probe != null) probe.enabled = dynamic;
            if (reflections != null) reflections.enabled = dynamic;

            if (steamAudioObjects != null)
                foreach (var go in steamAudioObjects)
                    if (go != null) go.SetActive(steam);

            ConditionChanged?.Invoke(current);
            Debug.Log($"[ConditionManager] Condition set to {current}");
        }

        /// <summary>Counterbalancing helper. Order string like "BAC" or "0,2,1".</summary>
        public static AcousticCondition[] ParseOrder(string order)
        {
            var result = new List<AcousticCondition>();
            foreach (char c in order.ToUpperInvariant())
            {
                switch (c)
                {
                    case 'A': case '0': result.Add(AcousticCondition.ZoneBaseline); break;
                    case 'B': case '1': result.Add(AcousticCondition.RaycastDynamic); break;
                    case 'C': case '2': result.Add(AcousticCondition.SteamAudio); break;
                }
            }
            return result.ToArray();
        }

        private void ResolveBuses()
        {
            buses.Clear();
            TryAdd(zoneReverbBus);
            TryAdd(dynamicReverbBus);
            TryAdd(reflectionsBus);
            TryAdd(steamAudioBus);

            // CHANGED: was set true unconditionally, so if the banks were not loaded
            // yet when Start ran, the buses were never looked up again and the
            // condition switch silently stopped muting anything. Now retried on the
            // next SetCondition until every configured bus has been found.
            busesResolved = AllConfiguredBusesFound();
        }

        private bool AllConfiguredBusesFound()
        {
            foreach (string path in new[] { zoneReverbBus, dynamicReverbBus, reflectionsBus, steamAudioBus })
                if (!string.IsNullOrEmpty(path) && !buses.ContainsKey(path)) return false;
            return true;
        }

        private void TryAdd(string path)
        {
            if (string.IsNullOrEmpty(path) || buses.ContainsKey(path)) return;
            try
            {
                Bus bus = RuntimeManager.GetBus(path);
                if (bus.isValid()) buses[path] = bus;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConditionManager] Could not find bus '{path}'. " +
                                 $"Check the path in the FMOD mixer. ({e.Message})");
            }
        }

        private void Mute(string path, bool muted)
        {
            if (buses.TryGetValue(path, out Bus bus) && bus.isValid())
                bus.setMute(muted);
        }

        private void OnGUI()
        {
            if (!showOperatorLabel) return;
            GUI.Label(new Rect(8, 8, 400, 22), $"Condition: {current}   [{cycleKey} to cycle]");
        }
    }
}
