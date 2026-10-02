using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SliderValueUpdater : MonoBehaviour
{
    public Slider InnocentSlider;
    public TMP_Text valueText;

    void Start()
    {
        InnocentSlider.value = GameSettings.InnocentCount;
        UpdateValue(InnocentSlider.value);
    }

    public void UpdateValue(float value)
    {
        GameSettings.InnocentCount = (int)value;
        valueText.text = GameSettings.InnocentCount.ToString();
    }
}