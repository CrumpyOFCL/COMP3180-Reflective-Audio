using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// UI only - talks to AudioManager, which drives the FMOD VCAs. No FMOD calls
// are needed here; AudioManager is the FMOD layer.
//
// CHANGED (FMOD pass):
//   * sliderSetting?.UpdateText(...) replaced by an explicit null test. GetComponent
//     returns a Unity "fake null" in the Editor when the component is missing,
//     and ?. calls straight through it.
//   * If AudioManager is not awake yet when the slider is enabled (script order /
//     scene order) the value is loaded as soon as it appears, instead of the
//     slider silently showing its default.
//   * Works with a 0-1 slider as well as 0-100: AudioManager stores 0-100 and
//     clamps, so a 0-1 slider used to set every VCA to at most 1% (near silent).
[RequireComponent(typeof(Slider))]
public class AudioSettingSlider : MonoBehaviour
{
    [Header("Audio Setting")]
    [SerializeField] private AudioManager.AudioChannel channel;

    private Slider slider;
    private SliderSetting sliderSetting;
    private Coroutine waitRoutine;

    // A slider whose max is 1 or less is treated as normalised (0-1).
    private bool IsNormalised => slider.maxValue <= 1f;

    private void Awake()
    {
        slider = GetComponent<Slider>();
        sliderSetting = GetComponent<SliderSetting>();
    }

    private void OnEnable()
    {
        slider.onValueChanged.RemoveListener(OnSliderChanged);
        slider.onValueChanged.AddListener(OnSliderChanged);

        if (AudioManager.Instance != null) LoadCurrentValue();
        else waitRoutine = StartCoroutine(LoadWhenAudioManagerExists());
    }

    private void OnDisable()
    {
        slider.onValueChanged.RemoveListener(OnSliderChanged);

        if (waitRoutine != null)
        {
            StopCoroutine(waitRoutine);
            waitRoutine = null;
        }
    }

    public void RefreshSlider()
    {
        LoadCurrentValue();
    }

    private IEnumerator LoadWhenAudioManagerExists()
    {
        while (AudioManager.Instance == null) yield return null;
        waitRoutine = null;
        LoadCurrentValue();
    }

    private void LoadCurrentValue()
    {
        if (AudioManager.Instance == null)
            return;

        float volume = AudioManager.Instance.GetVolume(channel);          // 0-100
        float value = IsNormalised ? volume / 100f : volume;

        slider.SetValueWithoutNotify(value);
        if (sliderSetting != null) sliderSetting.UpdateText(value);
    }

    private void OnSliderChanged(float value)
    {
        if (AudioManager.Instance == null)
            return;

        AudioManager.Instance.SetVolume(channel, IsNormalised ? value * 100f : value);
        if (sliderSetting != null) sliderSetting.UpdateText(value);
    }
}
