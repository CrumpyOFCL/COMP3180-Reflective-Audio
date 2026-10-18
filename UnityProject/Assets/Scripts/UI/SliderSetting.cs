using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SliderSetting : MonoBehaviour
{
    public enum SliderDisplayType
    {
        Volume,
        GeneralDecimal
    }

    [Header("References")]
    [SerializeField] private Slider slider;
    [SerializeField] private TextMeshProUGUI valueText;

    [Header("Display Type")]
    [SerializeField]
    private SliderDisplayType displayType = SliderDisplayType.Volume;

    private void Start()
    {
        if (slider == null || valueText == null)
        {
            Debug.LogWarning($"SliderSetting is missing references on {gameObject.name}.", this);
            return;
        }

        UpdateText(slider.value);
    }

    public void UpdateText(float value)
    {
        if (valueText == null)
            return;

        switch (displayType)
        {
            case SliderDisplayType.Volume:
                valueText.text = value.ToString("0");
                break;

            case SliderDisplayType.GeneralDecimal:
                valueText.text = value.ToString("0.0");
                break;
        }
    }
}