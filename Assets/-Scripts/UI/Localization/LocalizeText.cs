using TMPro;
using UnityEngine;
using UnityEngine.Localization;

/// <summary>
/// Drives a TextMeshProUGUI from a LocalizedString.
/// Fires immediately on enable (sets text to current locale) and
/// re-fires whenever the locale changes.
/// </summary>
[RequireComponent(typeof(TextMeshProUGUI))]
public class LocalizeText : MonoBehaviour
{
    [SerializeField] public LocalizedString localizedString;

    void OnEnable()  => localizedString.StringChanged += Apply;
    void OnDisable() => localizedString.StringChanged -= Apply;

    void Apply(string value)
    {
        var tmp = GetComponent<TextMeshProUGUI>();
        if (tmp != null) tmp.text = value;
    }
}
