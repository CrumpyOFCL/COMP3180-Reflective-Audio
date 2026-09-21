using UnityEngine;

public class AudioOptionsPanel : MonoBehaviour
{
    [Header("Audio Sliders")]
    [SerializeField] private AudioSettingSlider masterVolumeSlider;
    [SerializeField] private AudioSettingSlider musicVolumeSlider;
    [SerializeField] private AudioSettingSlider sfxVolumeSlider;
    [SerializeField] private AudioSettingSlider environmentVolumeSlider;
    [SerializeField] private AudioSettingSlider playerVolumeSlider;

    public void ResetToDefaults()
    {
        if (AudioManager.Instance == null)
            return;

        AudioManager.Instance?.ResetAudioSettings();
        RefreshSliders();
    }

    public void RefreshSliders()
    {
        masterVolumeSlider?.RefreshSlider();
        musicVolumeSlider?.RefreshSlider();
        sfxVolumeSlider?.RefreshSlider();
        environmentVolumeSlider?.RefreshSlider();
        playerVolumeSlider?.RefreshSlider();
    }
}