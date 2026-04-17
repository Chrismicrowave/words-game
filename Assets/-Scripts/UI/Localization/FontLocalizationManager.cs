using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

/// <summary>
/// Swaps TMP font assets when the UI locale changes.
/// Assign chineseFont (NotoSansSC) and populate targets in the Inspector.
/// On zh-Hans: applies chineseFont. On any other locale: restores original fonts.
/// Font swap is spread one target per frame to avoid triggering 42 simultaneous
/// CJK glyph atlas rebuilds which crashes Unity (even when deferred one frame).
/// </summary>
public class FontLocalizationManager : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset chineseFont;
    [SerializeField] private List<TextMeshProUGUI> targets = new List<TextMeshProUGUI>();

    private TMP_FontAsset[] _originalFonts;
    private Coroutine _pendingSwap;

    void Awake()
    {
        _originalFonts = new TMP_FontAsset[targets.Count];
        for (int i = 0; i < targets.Count; i++)
            _originalFonts[i] = targets[i] != null ? targets[i].font : null;
    }

    void OnEnable() => LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
        if (_pendingSwap != null) StopCoroutine(_pendingSwap);
    }

    void Start()
    {
        if (LocalizationSettings.SelectedLocale != null)
            OnLocaleChanged(LocalizationSettings.SelectedLocale);
    }

    void OnLocaleChanged(Locale locale)
    {
        if (_pendingSwap != null) StopCoroutine(_pendingSwap);
        _pendingSwap = StartCoroutine(ApplyFontsSpread(locale?.Identifier.Code == "zh-Hans"));
    }

    IEnumerator ApplyFontsSpread(bool isChinese)
    {
        yield return null; // wait one frame before starting
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] != null)
                targets[i].font = isChinese && chineseFont != null ? chineseFont : _originalFonts[i];
            yield return null; // one target per frame — avoids mass CJK atlas rebuild crash
        }
        _pendingSwap = null;
    }
}
