using UnityEngine;
using TMPro;

/// <summary>
/// A single cell in the target display for a Chinese phase.
/// Shows the Chinese character with optional pinyin above.
/// </summary>
public class TargetCell : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI pinyinLabel;   // small label above, e.g. "ni"
    [SerializeField] private TextMeshProUGUI charLabel;     // large label, e.g. "你"

    public void Init(string chineseChar, string pinyin, bool pinyinVisible)
    {
        if (charLabel != null) charLabel.text = chineseChar;
        if (pinyinLabel != null) pinyinLabel.text = pinyin;
        SetPinyinVisible(pinyinVisible);
    }

    public void SetPinyinVisible(bool visible)
    {
        if (pinyinLabel != null) pinyinLabel.gameObject.SetActive(visible);
    }
}
