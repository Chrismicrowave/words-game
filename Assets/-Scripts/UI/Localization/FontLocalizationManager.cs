using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

/// <summary>
/// Swaps TMP font assets when the UI locale changes.
/// Assign chineseFont (NotoSansSC) and populate targets in the Inspector.
/// On zh-Hans: applies chineseFont. On any other locale: restores original fonts.
/// </summary>
public class FontLocalizationManager : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset chineseFont;
    [SerializeField] private List<TextMeshProUGUI> targets = new List<TextMeshProUGUI>();

    private TMP_FontAsset[] _originalFonts;

    void Awake()
    {
        _originalFonts = new TMP_FontAsset[targets.Count];
        for (int i = 0; i < targets.Count; i++)
            _originalFonts[i] = targets[i] != null ? targets[i].font : null;
    }

    void OnEnable() => LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    void OnDisable() => LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;

    void Start()
    {
        if (LocalizationSettings.SelectedLocale != null)
            OnLocaleChanged(LocalizationSettings.SelectedLocale);
    }

    void OnLocaleChanged(Locale locale)
    {
        bool isChinese = locale?.Identifier.Code == "zh-Hans";
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null) continue;
            targets[i].font = isChinese && chineseFont != null ? chineseFont : _originalFonts[i];
        }
    }
}
