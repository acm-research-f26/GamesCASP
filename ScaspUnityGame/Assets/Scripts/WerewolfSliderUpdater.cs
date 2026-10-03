using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class WerewolfSliderUpdater : MonoBehaviour
{
    public Slider WerewolfCount;
    public TMP_Text valueText;

    void Start()
    {
        WerewolfCount.value = GameSettings.WerewolfCount;
        UpdateValue(WerewolfCount.value);
    }

    public void UpdateValue(float value)
    {
        GameSettings.WerewolfCount = (int)value;
        valueText.text = GameSettings.WerewolfCount.ToString();
    }
}