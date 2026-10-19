using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ReflectiveAudio2D.EditorTools
{
    /// <summary>
    /// Tools > Reflective Audio 2D > Package Exporter. Not part of the package itself.
    ///
    /// 1. Gather: moves the package's scripts from wherever they live in this
    ///    project into the package folder's Runtime folder. Moving through the
    ///    AssetDatabase keeps each script's GUID, so scenes and prefabs that use
    ///    them keep working.
    /// 2. Check: flags anything that would not compile in someone else's project:
    ///    references to this game's own scripts, and the old Input class.
    /// 3. Export: writes ReflectiveAudio2D.unitypackage from the package folder,
    ///    optionally with the built FMOD banks.
    ///
    /// Test the result by importing it into a new, empty project (see README.md).
    /// </summary>
    public class ReflectiveAudio2DPackager : EditorWindow
    {
        private static string Root => DemoSceneCreator.PackageRoot;
        private static string RuntimeFolder => Root + "/Runtime";
        private const string ThisFileName = "ReflectiveAudio2DPackager.cs";

        // File name = class name. These are the scripts the package ships.
        private static readonly string[] RuntimeScripts =
        {
            "Listener2D", "AcousticProbe2D", "EarlyReflections2D", "ImageSourceEchoes2D",
            "AcousticZone2D", "ConditionManager", "OccludedEmitter2D", "SurfaceType",
            "TilemapAcousticProxy", "SteamAudioFmodPluginLoader", "KeyInput",
            "RoomEstimationTask", "AudioBenchmarkLogger", "AcousticsGalleryBuilder",
        };

        // Game or showcase classes the package must not depend on.
        private static readonly string[] GameTypes =
        {
            "TimeSingleton", "PlayerMovement", "PlayerAimAndShoot", "PlayerAudio", "PlayerDash",
            "GameAudio", "AudioManager", "ShowcaseBeam", "ShowcaseCamera", "ShowcaseAimLaser",
            "BeamBehaviour", "BeamManager",
        };

        private bool includeBanks;
        private string bankFolder = "Assets/FMOD/Desktop";
        private readonly List<string> report = new List<string>();
        private Vector2 scroll;

        [MenuItem("Tools/Reflective Audio 2D/Package Exporter")]
        private static void Open() => GetWindow<ReflectiveAudio2DPackager>("RA2D Package");

        private void OnEnable() => RunCheck();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Package scripts", EditorStyles.boldLabel);
            foreach (string name in RuntimeScripts)
            {
                string path = FindScript(name);
                string state = path == null ? "MISSING" : path == RuntimeFolder + "/" + name + ".cs" ? "in package" : path;
                EditorGUILayout.LabelField(name, state);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("1. Gather scripts into " + RuntimeFolder)) { Gather(); RunCheck(); }
            if (GUILayout.Button("2. Check for problems")) RunCheck();

            EditorGUILayout.Space();
            includeBanks = EditorGUILayout.ToggleLeft("Include built FMOD banks", includeBanks);
            using (new EditorGUI.DisabledScope(!includeBanks))
                bankFolder = EditorGUILayout.TextField("Bank folder", bankFolder);
            if (includeBanks)
                EditorGUILayout.HelpBox("Only include banks if you may redistribute every sound in them. " +
                                        "They also load as the importer's Master bank, replacing theirs.",
                                        MessageType.Warning);

            if (GUILayout.Button("3. Export ReflectiveAudio2D.unitypackage")) Export();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(report.Count == 0 ? "No problems found." : "Problems", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (string line in report) EditorGUILayout.HelpBox(line, MessageType.Warning);
            EditorGUILayout.EndScrollView();
        }

        // ---------- 1. gather ----------

        private static void Gather()
        {
            DemoSceneCreator.EnsureFolder(RuntimeFolder);
            int moved = 0;
            foreach (string name in RuntimeScripts)
            {
                string from = FindScript(name);
                string to = RuntimeFolder + "/" + name + ".cs";
                if (from == null || from == to) continue;

                string error = AssetDatabase.MoveAsset(from, to);
                if (string.IsNullOrEmpty(error)) moved++;
                else Debug.LogError($"[RA2D Package] Could not move {from}: {error}");
            }
            AssetDatabase.Refresh();
            Debug.Log($"[RA2D Package] Moved {moved} script(s) into {RuntimeFolder}.");
        }

        // ---------- 2. check ----------

        private void RunCheck()
        {
            report.Clear();
            List<string> scripts = PackageScripts(out List<string> missing);
            foreach (string path in scripts)
            {
                string name = Path.GetFileName(path);
                string code = StripCommentsAndStrings(File.ReadAllText(path));

                foreach (string type in GameTypes)
                    if (Regex.IsMatch(code, $@"\b{type}\b"))
                        report.Add($"{name} uses {type}, which is not in the package.");

                if (Regex.IsMatch(code, @"global::Player\b|<\s*Player\s*>|\bPlayer\s+\w+\s*[=;,)]"))
                    report.Add($"{name} uses the game's Player class, which is not in the package.");

                if (Regex.IsMatch(code, @"\bInput\.(GetKey|GetKeyDown|GetKeyUp|GetMouseButton\w*|GetAxis\w*)\b") &&
                    !code.Contains("ENABLE_INPUT_SYSTEM"))
                    report.Add($"{name} uses the old Input class, which throws when a project uses the " +
                               "Input System. Use KeyInput.Down / KeyInput.Held.");
            }
            foreach (string name in missing)
                report.Add($"{name}.cs was not found in Assets.");
        }

        // Comments and string literals can mention class names harmlessly (tooltips, notes).
        private static string StripCommentsAndStrings(string code)
        {
            code = Regex.Replace(code, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            code = Regex.Replace(code, @"//[^\n]*", " ");
            code = Regex.Replace(code, @"@""(?:""""|[^""])*""", "\"\"");
            code = Regex.Replace(code, @"""(?:\\.|[^""\\\n])*""", "\"\"");
            return code;
        }

        // ---------- 3. export ----------

        private void Export()
        {
            RunCheck();
            if (report.Count > 0 &&
                !EditorUtility.DisplayDialog("RA2D Package",
                    $"{report.Count} problem(s) found. The package may not compile in another project. Export anyway?",
                    "Export anyway", "Cancel"))
                return;

            var paths = new List<string>();
            AddAssets(Root, paths);
            paths.RemoveAll(p => p.EndsWith("/" + ThisFileName));

            // Scripts not gathered yet are still exported, from where they are.
            foreach (string path in PackageScripts(out _))
                if (!paths.Contains(path)) paths.Add(path);

            if (includeBanks)
            {
                if (AssetDatabase.IsValidFolder(bankFolder)) AddAssets(bankFolder, paths);
                else Debug.LogWarning($"[RA2D Package] No folder at {bankFolder}; banks not included.");
            }

            string file = EditorUtility.SaveFilePanel("Export package", "", "ReflectiveAudio2D", "unitypackage");
            if (string.IsNullOrEmpty(file)) return;

            AssetDatabase.ExportPackage(paths.ToArray(), file, ExportPackageOptions.Default);
            Debug.Log($"[RA2D Package] Exported {paths.Count} asset(s) to {file}");
            EditorUtility.RevealInFinder(file);
        }

        // ---------- helpers ----------

        private static void AddAssets(string folder, List<string> paths)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Object", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!AssetDatabase.IsValidFolder(path) && !paths.Contains(path)) paths.Add(path);
            }
        }

        private static List<string> PackageScripts(out List<string> missing)
        {
            var found = new List<string>();
            missing = new List<string>();
            foreach (string name in RuntimeScripts)
            {
                string path = FindScript(name);
                if (path == null) missing.Add(name);
                else found.Add(path);
            }
            foreach (string extra in new[] { "DemoPlayer2D", "DemoSceneCreator" })
            {
                string path = FindScript(extra);
                if (path != null) found.Add(path);
            }
            return found;
        }

        private static string FindScript(string className)
        {
            string match = null;
            foreach (string guid in AssetDatabase.FindAssets(className + " t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/") || Path.GetFileNameWithoutExtension(path) != className) continue;
                if (match != null)
                {
                    Debug.LogWarning($"[RA2D Package] Two scripts called {className}.cs: {match} and {path}. Using the first.");
                    break;
                }
                match = path;
            }
            return match;
        }
    }
}
