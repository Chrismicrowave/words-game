using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bridges UnityEngine.UI.Dropdown to a TextMeshProUGUI caption label.
/// Required because the legacy Dropdown.captionText field only accepts
/// UnityEngine.UI.Text — it cannot reference a TMP component directly.
/// Assign the TMP Label child in the Inspector.
/// </summary>
[RequireComponent(typeof(Dropdown))]
public class DropdownTMPBridge : MonoBehaviour
{
    [SerializeField] public TextMeshProUGUI captionTMP;

    private Dropdown _dropdown;

    void Awake() => _dropdown = GetComponent<Dropdown>();

    void OnEnable()
    {
        _dropdown.onValueChanged.AddListener(UpdateCaption);
        UpdateCaption(_dropdown.value);
    }

    void OnDisable() => _dropdown.onValueChanged.RemoveListener(UpdateCaption);

    void UpdateCaption(int index)
    {
        if (captionTMP == null) return;
        if (_dropdown.options == null || index < 0 || index >= _dropdown.options.Count) return;
        captionTMP.text = _dropdown.options[index].text;
    }
}
