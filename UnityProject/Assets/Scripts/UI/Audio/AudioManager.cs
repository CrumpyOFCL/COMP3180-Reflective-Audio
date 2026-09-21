using System.Collections;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    private static AudioManager instance;

    // Backed by a field with an explicit null test, so the getter hands back a
    // real null once the object is destroyed. An auto-property returns a
    // reference that is only Unity fake-null, which x?.Method() calls straight
    // through - and that throws MissingReferenceException. It also clears the
    // stale reference left behind by a previous play session when Reload Domain
    // is switched off.
    public static AudioManager Instance
    {
        get => instance != null ? instance : null;
        private set => instance = value;
    }

    public enum AudioChannel
    {
        Master,
        Music,
        SFX,
        Environment,
        Player
    }

    [Header("FMOD VCAs")]
    [Tooltip("VCA paths as they appear in FMOD Studio. Each slider drives one VCA. " +
             "Leave a path blank to skip that channel.")]
    [SerializeField] private string masterVcaPath = "vca:/Master";
    [SerializeField] private string musicVcaPath = "vca:/Music";
    [SerializeField] private string sfxVcaPath = "vca:/SFX";
    [SerializeField] private string environmentVcaPath = "vca:/Environment";
    [SerializeField] private string playerVcaPath = "vca:/Player";

    [Header("Default Volumes")]
    [SerializeField, Range(0f, 100f)]
    private float defaultMasterVolume = 100f;

    [SerializeField, Range(0f, 100f)]
    private float defaultMusicVolume = 100f;

    [SerializeField, Range(0f, 100f)]
    private float defaultSFXVolume = 100f;

    [SerializeField, Range(0f, 100f)]
    private float defaultEnvironmentVolume = 100f;

    [SerializeField, Range(0f, 100f)]
    private float defaultPlayerVolume = 100f;

    // Looked up once each. GetVCA throws when the path is missing, so a bad path
    // would otherwise throw on every slider drag.
    private readonly Dictionary<string, VCA> vcaCache = new Dictionary<string, VCA>();
    private readonly HashSet<string> missingVcas = new HashSet<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        StartCoroutine(ApplyVolumesWhenBanksLoad());
    }

    // VCAs don't exist until the banks are loaded, so applying saved volumes on
    // the first frame silently did nothing.
    private IEnumerator ApplyVolumesWhenBanksLoad()
    {
        while (!RuntimeManager.HaveAllBanksLoaded) yield return null;

        ApplyAllVolumes();
    }

    public void SetVolume(AudioChannel channel, float value)
    {
        value = Mathf.Clamp(value, 0f, 100f);

        ApplyVcaVolume(GetVcaPath(channel), value);

        PlayerPrefs.SetFloat(GetPreferenceKey(channel), value);
        PlayerPrefs.Save();
    }

    public float GetVolume(AudioChannel channel)
    {
        return PlayerPrefs.GetFloat(
            GetPreferenceKey(channel),
            GetDefaultVolume(channel)
        );
    }

    public void ApplyAllVolumes()
    {
        ApplyChannelVolume(AudioChannel.Master);
        ApplyChannelVolume(AudioChannel.Music);
        ApplyChannelVolume(AudioChannel.SFX);
        ApplyChannelVolume(AudioChannel.Environment);
        ApplyChannelVolume(AudioChannel.Player);
    }

    public void ResetAudioSettings()
    {
        SetVolume(AudioChannel.Master, defaultMasterVolume);
        SetVolume(AudioChannel.Music, defaultMusicVolume);
        SetVolume(AudioChannel.SFX, defaultSFXVolume);
        SetVolume(AudioChannel.Environment, defaultEnvironmentVolume);
        SetVolume(AudioChannel.Player, defaultPlayerVolume);
    }

    private void ApplyChannelVolume(AudioChannel channel)
    {
        ApplyVcaVolume(GetVcaPath(channel), GetVolume(channel));
    }


    // FMOD VCA volume is a linear scalar where 1 is unity gain, so the old
    // decibel conversion the AudioMixer needed is gone - 0-100 maps straight to
    // 0-1 and produces the same result.
    private void ApplyVcaVolume(string path, float value)
    {
        if (string.IsNullOrEmpty(path) || !TryGetVca(path, out VCA vca)) return;

        vca.setVolume(Mathf.Clamp01(value / 100f));
    }


    private bool TryGetVca(string path, out VCA vca)
    {
        if (vcaCache.TryGetValue(path, out vca)) return vca.isValid();

        // Already reported as missing - don't throw again on every slider drag.
        if (missingVcas.Contains(path))
        {
            vca = default;
            return false;
        }

        try
        {
            vca = RuntimeManager.GetVCA(path);
        }
        catch (VCANotFoundException)
        {
            missingVcas.Add(path);

            Debug.LogWarning($"AudioManager: no VCA at '{path}'. Check the path in " +
                             "FMOD Studio, and that its bank is in the build.", this);

            vca = default;
            return false;
        }

        if (!vca.isValid())
        {
            missingVcas.Add(path);
            return false;
        }

        vcaCache[path] = vca;
        return true;
    }


    private string GetVcaPath(AudioChannel channel)
    {
        switch (channel)
        {
            case AudioChannel.Master:
                return masterVcaPath;

            case AudioChannel.Music:
                return musicVcaPath;

            case AudioChannel.SFX:
                return sfxVcaPath;

            case AudioChannel.Environment:
                return environmentVcaPath;

            case AudioChannel.Player:
                return playerVcaPath;

            default:
                return masterVcaPath;
        }
    }

    private string GetPreferenceKey(AudioChannel channel)
    {
        return $"Audio_{channel}Volume";
    }

    private float GetDefaultVolume(AudioChannel channel)
    {
        switch (channel)
        {
            case AudioChannel.Master:
                return defaultMasterVolume;

            case AudioChannel.Music:
                return defaultMusicVolume;

            case AudioChannel.SFX:
                return defaultSFXVolume;

            case AudioChannel.Environment:
                return defaultEnvironmentVolume;

            case AudioChannel.Player:
                return defaultPlayerVolume;

            default:
                return 100f;
        }
    }
}