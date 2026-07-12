using UnityEngine;
using TMPro;

/// <summary>
/// A single cell in the mixed display for an English segment.
/// Used in both the target display (static text) and matched display (progressive reveal).
/// </summary>
public class EnglishCell : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;

    public TextMeshProUGUI Label => label;

    public void SetText(string text)
    {
        if (label != null) label.text = text;
    }
}
