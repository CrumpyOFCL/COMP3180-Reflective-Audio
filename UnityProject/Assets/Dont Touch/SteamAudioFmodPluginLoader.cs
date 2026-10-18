using System;
using System.IO;
using UnityEngine;

namespace ReflectiveAudio2D
{
    /// <summary>
    /// Loads Steam Audio's FMOD plugin (phonon_fmod) from the project's Assets folder.
    ///
    /// Why this exists: FMOD for Unity is installed here as a package, and its
    /// Dynamic Plugins list only looks inside the package's own folder
    /// (Library/PackageCache/...), which is read-only and rebuilt by Unity. The
    /// Steam Audio FMOD package puts the plugin under Assets/Plugins/FMOD instead,
    /// so the Dynamic Plugins entry fails with ERR_FILE_NOTFOUND.
    ///
    /// Setup: Assets > Create > Reflective Audio 2D > Steam Audio FMOD Plugin Loader,
    /// then FMOD > Edit Settings > Callback Handler = that asset, and remove
    /// "phonon_fmod" from Dynamic Plugins.
    ///
    /// A failure is logged, never reported as an init error: reporting it would
    /// make FMOD abandon initialisation and the whole game would go silent, when
    /// only Condition C needs this plugin.
    /// </summary>
    // Only one of the two folders is read per build target.
#pragma warning disable 0414
    [CreateAssetMenu(menuName = "Reflective Audio 2D/Steam Audio FMOD Plugin Loader",
                     fileName = "SteamAudioFmodPluginLoader")]
    public class SteamAudioFmodPluginLoader : FMODUnity.PlatformCallbackHandler
    {
        [SerializeField] private string pluginFile = "phonon_fmod.dll";

        [Tooltip("Folder holding the plugin in the editor, relative to the Assets folder.")]
        [SerializeField] private string editorFolder = "Plugins/FMOD/platforms/win/lib/x86_64";

        [Tooltip("Folder holding the plugin in a Windows build, relative to <Game>_Data. " +
                 "Unity copies 64-bit native plugins to Plugins/x86_64.")]
        [SerializeField] private string buildFolder = "Plugins/x86_64";

        public override void PreInitialize(FMOD.Studio.System studioSystem, Action<FMOD.RESULT, string> reportResult)
        {
            FMOD.RESULT result = studioSystem.getCoreSystem(out FMOD.System core);
            if (result != FMOD.RESULT.OK)
            {
                Debug.LogWarning($"[SteamAudioFmodPluginLoader] getCoreSystem failed: {result}");
                return;
            }

#if UNITY_EDITOR
            string folder = Path.Combine(Application.dataPath, editorFolder);
#else
            string folder = Path.Combine(Application.dataPath, buildFolder);
#endif
            string path = Path.Combine(folder, pluginFile);

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[SteamAudioFmodPluginLoader] Not found: {path}. Condition C will be silent.");
                return;
            }

            result = core.loadPlugin(path, out uint handle);
            if (result == FMOD.RESULT.OK)
                Debug.Log($"[SteamAudioFmodPluginLoader] Loaded {pluginFile} (handle {handle}).");
            else
                Debug.LogWarning($"[SteamAudioFmodPluginLoader] Loading {path} failed: {result}. Condition C will be silent.");
        }
    }
#pragma warning restore 0414
}
