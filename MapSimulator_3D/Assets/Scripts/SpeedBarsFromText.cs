using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text.RegularExpressions;

public class SpeedBarsFromText : MonoBehaviour
{
    public TMP_Text speedText;
    public Image[] bars;

    public int maxSpeed = 120;

    public Color activeColor = new Color(0.2f, 1f, 0.35f, 1f);
    public Color inactiveColor = new Color(0.35f, 0.35f, 0.35f, 0.6f);

    void Update()
    {
        if (speedText == null || bars == null || bars.Length == 0)
            return;

        int speed = ExtractSpeed(speedText.text);

        int activeBars = 0;

        if (speed > 0)
        {
            activeBars = Mathf.CeilToInt((speed / (float)maxSpeed) * bars.Length);
            activeBars = Mathf.Clamp(activeBars, 0, bars.Length);
        }

        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i] != null)
            {
                bars[i].color = (i < activeBars) ? activeColor : inactiveColor;
            }
        }
    }

    int ExtractSpeed(string text)
    {
        string numberText = Regex.Replace(text, @"[^\d]", "");

        if (string.IsNullOrEmpty(numberText))
            return 0;

        int value;
        if (int.TryParse(numberText, out value))
            return value;

        return 0;
    }
}