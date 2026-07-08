using TMPro;
using UnityEngine;
using UnityEngine.Localization;

/// <summary>
/// Drives a TextMeshProUGUI from a LocalizedString.
///
/// Subscribes once in Awake (never unsubscribes on disable), so the handler
/// stays alive across panel show/hide cycles and locale switches alike.
/// StringChanged fires on first load and on every locale change — no need
/// for GetLocalizedString() or manual refresh.
/// </summary>
[RequireComponent(typeof(TextMeshProUGUI))]
public class LocalizeText : MonoBehaviour
{
    [SerializeField] public LocalizedString localizedString;
    TextMeshProUGUI m_Tmp;

    void Awake()
    {
        m_Tmp = GetComponent<TextMeshProUGUI>();
        localizedString.StringChanged += Apply;
    }

    void OnDestroy()
    {
        localizedString.StringChanged -= Apply;
    }

    void Apply(string value)
    {
        if (m_Tmp != null) m_Tmp.text = value;
    }
}
