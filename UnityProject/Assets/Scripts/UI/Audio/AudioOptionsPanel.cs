using UnityEngine;

// UI only - AudioManager is the FMOD layer (VCAs). Nothing to convert.
//
// CHANGED (FMOD pass): x?.RefreshSlider() replaced by explicit null tests.
// Unassigned serialized UnityEngine.Object fields are Unity "fake null" objects
// in the Editor, which ?. calls straight through, so leaving any slider slot
// empty threw inside RefreshSlider.
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

        AudioManager.Instance.ResetAudioSettings();
        RefreshSliders();
    }

    public void RefreshSliders()
    {
        Refresh(masterVolumeSlider);
        Refresh(musicVolumeSlider);
        Refresh(sfxVolumeSlider);
        Refresh(environmentVolumeSlider);
        Refresh(playerVolumeSlider);
    }

    private static void Refresh(AudioSettingSlider slider)
    {
        if (slider != null) slider.RefreshSlider();
    }
}
