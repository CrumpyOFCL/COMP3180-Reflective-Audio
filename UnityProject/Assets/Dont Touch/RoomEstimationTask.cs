using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ReflectiveAudio2D
{
    /// <summary>
    /// The OBJECTIVE evaluation measure. Derived from Jorgensen's (2008) finding that
    /// removing game audio costs players functional spatial information, not just
    /// atmosphere. Reverberation carries room size, enclosure and distance cues, so
    /// "can the player read the space by ear" has a correct answer, unlike preference.
    ///
    /// Sequence: teleport the listener into an unseen test space, play a probe sound,
    /// let the participant estimate width and enclosure, record error against ground
    /// truth, move on. Repeats for every space, in every condition.
    ///
    /// Output: CSV in Application.persistentDataPath.
    /// </summary>
    [AddComponentMenu("Reflective Audio 2D/Room Estimation Task")]
    public class RoomEstimationTask : MonoBehaviour
    {
        [Serializable]
        public class TestSpace
        {
            public string spaceId = "Space1";
            public Transform listenerPosition;

            [Header("Ground truth")]
            [Tooltip("Actual width of the space in world units.")]
            public float trueWidth = 10f;
            [Tooltip("0 = fully open, 1 = fully enclosed.")]
            [Range(0f, 1f)] public float trueEnclosure = 1f;
        }

        [Header("Spaces to test")]
        [SerializeField] private TestSpace[] spaces;
        [SerializeField] private bool randomiseOrder = true;

        [Header("Objects to move")]
        [SerializeField] private Transform listenerRoot;
        [SerializeField] private Transform playerRoot;

        [Header("Probe sound")]
        [Tooltip("An emitter the participant can trigger to excite the space. " +
                 "Without something sounding, there is nothing to judge.")]
        [SerializeField] private GameObject probeEmitter;
        [SerializeField] private KeyCode probeKey = KeyCode.Space;

        [Header("Vision blocking")]
        [Tooltip("A full-screen black overlay. The participant must judge by ear only.")]
        [SerializeField] private GameObject blindfoldOverlay;

        [Header("Answer ranges")]
        [SerializeField] private float minWidthGuess = 2f;
        [SerializeField] private float maxWidthGuess = 40f;

        [Header("Session")]
        [SerializeField] private string participantId = "P00";
        [SerializeField] private KeyCode startKey = KeyCode.F12;

        private int index = -1;
        private int[] order;
        private bool running;
        private float widthGuess;
        private int enclosureGuess = 1;   // 0 open, 1 partly, 2 enclosed
        private string filePath;
        private readonly StringBuilder buffer = new StringBuilder();
        private readonly string[] enclosureLabels = { "Open", "Partly enclosed", "Fully enclosed" };

        private void Start()
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            filePath = Path.Combine(Application.persistentDataPath,
                                    $"room-estimation_{participantId}_{stamp}.csv");
            buffer.AppendLine("participant,condition,space,true_width,guess_width,width_error," +
                              "true_enclosure,guess_enclosure,enclosure_error,seconds_taken");
            Debug.Log($"[RoomEstimationTask] Writing to: {filePath}");

            widthGuess = (minWidthGuess + maxWidthGuess) * 0.5f;
            SetBlindfold(false);
        }

        private float questionStartTime;

        private void Update()
        {
            if (!running && Input.GetKeyDown(startKey)) BeginRun();
            if (running && probeEmitter != null && Input.GetKeyDown(probeKey))
                StartCoroutine(PulseProbe());
        }

        public void BeginRun()
        {
            if (spaces == null || spaces.Length == 0)
            {
                Debug.LogError("[RoomEstimationTask] No test spaces configured.");
                return;
            }

            order = new int[spaces.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            if (randomiseOrder) Shuffle(order);

            index = -1;
            running = true;
            SetBlindfold(true);
            NextSpace();
        }

        private void NextSpace()
        {
            index++;
            if (index >= order.Length) { EndRun(); return; }

            TestSpace space = spaces[order[index]];
            if (space.listenerPosition != null)
            {
                Vector3 pos = space.listenerPosition.position;
                pos.z = 0f;
                if (listenerRoot != null) listenerRoot.position = pos;
                if (playerRoot != null) playerRoot.position = pos;
            }

            widthGuess = (minWidthGuess + maxWidthGuess) * 0.5f;
            enclosureGuess = 1;
            questionStartTime = Time.unscaledTime;
        }

        private void Submit()
        {
            TestSpace space = spaces[order[index]];
            float guessedEnclosure = enclosureGuess * 0.5f;      // 0, 0.5, 1
            float widthError = Mathf.Abs(widthGuess - space.trueWidth);
            float enclosureError = Mathf.Abs(guessedEnclosure - space.trueEnclosure);
            float seconds = Time.unscaledTime - questionStartTime;

            var c = CultureInfo.InvariantCulture;
            buffer.AppendLine(string.Join(",",
                participantId,
                ConditionManager.Current.ToString(),
                space.spaceId,
                space.trueWidth.ToString("F2", c),
                widthGuess.ToString("F2", c),
                widthError.ToString("F2", c),
                space.trueEnclosure.ToString("F2", c),
                guessedEnclosure.ToString("F2", c),
                enclosureError.ToString("F2", c),
                seconds.ToString("F1", c)));

            NextSpace();
        }

        private void EndRun()
        {
            running = false;
            SetBlindfold(false);
            Flush();
            Debug.Log("[RoomEstimationTask] Run complete.");
        }

        private IEnumerator PulseProbe()
        {
            probeEmitter.SetActive(false);
            yield return null;
            probeEmitter.SetActive(true);
        }

        private void SetBlindfold(bool on)
        {
            if (blindfoldOverlay != null) blindfoldOverlay.SetActive(on);
        }

        private static void Shuffle(int[] a)
        {
            for (int i = a.Length - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }
        }

        private void Flush()
        {
            if (buffer.Length == 0) return;
            try { File.AppendAllText(filePath, buffer.ToString()); buffer.Clear(); }
            catch (Exception e) { Debug.LogError($"[RoomEstimationTask] Write failed: {e.Message}"); }
        }

        private void OnApplicationQuit() => Flush();

        private void OnGUI()
        {
            if (!running || index < 0 || index >= order.Length) return;

            const int w = 460, h = 260;
            var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(r, "");
            GUILayout.BeginArea(new Rect(r.x + 16, r.y + 16, w - 32, h - 32));

            GUILayout.Label($"Space {index + 1} of {order.Length}");
            GUILayout.Space(6);
            GUILayout.Label($"Press {probeKey} to make a sound. Judge by ear only.");
            GUILayout.Space(12);

            GUILayout.Label($"How wide is this space?   {widthGuess:F1} units");
            widthGuess = GUILayout.HorizontalSlider(widthGuess, minWidthGuess, maxWidthGuess);
            GUILayout.Space(12);

            GUILayout.Label("How enclosed is it?");
            enclosureGuess = GUILayout.Toolbar(enclosureGuess, enclosureLabels);
            GUILayout.Space(16);

            if (GUILayout.Button("Submit", GUILayout.Height(30))) Submit();

            GUILayout.EndArea();
        }

        private void OnDrawGizmosSelected()
        {
            if (spaces == null) return;
            Gizmos.color = Color.green;
            foreach (var s in spaces)
            {
                if (s.listenerPosition == null) continue;
                Gizmos.DrawWireCube(s.listenerPosition.position,
                                    new Vector3(s.trueWidth, s.trueWidth * 0.6f, 0.1f));
            }
        }
    }
}
