using System;
using TMPro;
using UnityEngine;

public class TimerDisplayManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI phaseTimeUI;
    [SerializeField] private TextMeshProUGUI totalTimeUI;

    void OnEnable()
    {
        if (TimerSystem.Instance != null)
            TimerSystem.Instance.OnTimerUpdated += UpdateTimerDisplay;
    }

    void OnDisable()
    {
        if (TimerSystem.Instance != null)
            TimerSystem.Instance.OnTimerUpdated -= UpdateTimerDisplay;
    }

    private void UpdateTimerDisplay(float phaseDuration, float total)
    {
        phaseTimeUI.text = FormatTime(TimeSpan.FromSeconds(phaseDuration));
        totalTimeUI.text = FormatTime(TimeSpan.FromSeconds(total));
    }

    private string FormatTime(TimeSpan t) =>
        $"{t.Hours:D2}\"{t.Minutes:D2}\'{t.Seconds:D2}.{t.Milliseconds / 10:D2}";
}
