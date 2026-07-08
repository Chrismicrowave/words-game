using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Drives a TextMeshProUGUI from a LocalizedString.
/// Fires immediately on enable (sets text to current locale) and
/// re-fires whenever the locale changes.
///
/// NOTE: StringChanged only fires when the value *changes* (first load or
/// locale switch), NOT when re-subscribing after OnDisable.  That means a
/// panel that is shown/hidden would only get its text set the first time.
/// We work around this by force-resolving via GetLocalizedString() on every
/// OnEnable — if the value is already cached the handle completes synchronously.
/// </summary>
[RequireComponent(typeof(TextMeshProUGUI))]
public class LocalizeText : MonoBehaviour
{
    [SerializeField] public LocalizedString localizedString;
    TextMeshProUGUI m_Tmp;

    void Awake()
    {
        m_Tmp = GetComponent<TextMeshProUGUI>();
    }

    void OnEnable()
    {
        localizedString.StringChanged += Apply;

        // Force-resolve — StringChanged only fires on change, so re-subscribing
        // after OnDisable would miss the current value.
        var handle = localizedString.GetLocalizedString();
        if (handle.IsDone && handle.Status == AsyncOperationStatus.Succeeded)
            Apply(handle.Result);
    }

    void OnDisable()
    {
        localizedString.StringChanged -= Apply;
    }

    void Apply(string value)
    {
        if (m_Tmp != null) m_Tmp.text = value;
    }
}
