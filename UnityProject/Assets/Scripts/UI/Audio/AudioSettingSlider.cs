using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public class AudioSettingSlider : MonoBehaviour
{
    [Header("Audio Setting")]
    [SerializeField] private AudioManager.AudioChannel channel;

    private Slider slider;
    private SliderSetting sliderSetting;

    private void Awake()
    {
        slider = GetComponent<Slider>();
        sliderSetting = GetComponent<SliderSetting>();
    }

    private void OnEnable()
    {
        slider.onValueChanged.RemoveListener(OnSliderChanged);
        slider.onValueChanged.AddListener(OnSliderChanged);
        LoadCurrentValue();
    }

    private void OnDisable()
    {
        slider.onValueChanged.RemoveListener(OnSliderChanged);
    }

    public void RefreshSlider()
    {
        LoadCurrentValue();
    }

    private void LoadCurrentValue()
    {
        if (AudioManager.Instance == null)
            return;

        float value = AudioManager.Instance.GetVolume(channel);

        slider.SetValueWithoutNotify(value);
        sliderSetting?.UpdateText(value);
    }

    private void OnSliderChanged(float value)
    {
        if (AudioManager.Instance == null)
            return;

        AudioManager.Instance?.SetVolume(channel, value);
        sliderSetting?.UpdateText(value);
    }
}