using TMPro;
using UnityEngine;
using UnityEngine.Localization;

/// <summary>
/// Localizes the placeholder text of a TMP_InputField.
/// LocalizeStringEvent cannot target InputField placeholders directly.
/// </summary>
[RequireComponent(typeof(TMP_InputField))]
public class LocalizePlaceholder : MonoBehaviour
{
    [SerializeField] private LocalizedString localizedString;

    void OnEnable() => localizedString.StringChanged += OnStringChanged;
    void OnDisable() => localizedString.StringChanged -= OnStringChanged;

    void OnStringChanged(string value)
    {
        var field = GetComponent<TMP_InputField>();
        if (field?.placeholder is TextMeshProUGUI tmp)
            tmp.text = value;
    }
}
