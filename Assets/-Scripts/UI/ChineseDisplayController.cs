using System.Collections;
using UnityEngine;

/// <summary>
/// Sub-manager for Chinese and Mixed phase display.
/// Owns ChineseMatchedDisplay, ChineseTargetDisplay, and ChinesePinyinPopup.
/// UIController holds a reference and delegates all Chinese display work here.
/// </summary>
public class ChineseDisplayController : MonoBehaviour
{
    [SerializeField] private ChineseMatchedDisplay matchedDisplay;
    [SerializeField] private ChineseTargetDisplay targetDisplay;
    [SerializeField] private ChinesePinyinPopup pinyinPopup;

    /// <summary>True when there is Chinese cell content to show (set by Rebuild calls).</summary>
    public bool IsShowingCells { get; private set; }

    public void RebuildForChinese(ChinesePhaseData data)
    {
        matchedDisplay?.BuildCells(data);
        targetDisplay?.BuildCells(data);
        if (SettingsManager.Instance != null)
            targetDisplay?.SetPinyinVisible(SettingsManager.Instance.ShowPinyin);
        IsShowingCells = true;
    }

    public void RebuildForMixed(MixedPhaseParser.MixedPhaseResult parsed)
    {
        if (!MixedPhaseParser.IsPurelyEnglish(parsed))
        {
            matchedDisplay?.BuildMixedCells(parsed);
            if (targetDisplay != null)
            {
                targetDisplay.BuildMixedCells(parsed);
                if (SettingsManager.Instance != null)
                    targetDisplay.SetPinyinVisible(SettingsManager.Instance.ShowPinyin);
                targetDisplay.gameObject.SetActive(true);
                targetDisplay.SyncFontSizesNextFrame();
            }
            IsShowingCells = true;
        }
        else
        {
            matchedDisplay?.Clear();
            if (targetDisplay != null)
            {
                targetDisplay.Clear();
                targetDisplay.gameObject.SetActive(false);
            }
            IsShowingCells = false;
        }
    }

    public void UpdateProgress(int matchedLength)
    {
        matchedDisplay?.UpdateProgress(matchedLength);
    }

    public void SetVisible(bool visible)
    {
        if (matchedDisplay != null) matchedDisplay.gameObject.SetActive(visible);
        if (targetDisplay != null && !visible) targetDisplay.gameObject.SetActive(false);
    }

    public void SetPinyinVisible(bool visible)
    {
        targetDisplay?.SetPinyinVisible(visible);
    }

    public void ShowPinyinPopup(string text, System.Action<MixedWordEntry> onConfirm, System.Action onCancel)
    {
        pinyinPopup?.Show(text, onConfirm, onCancel);
    }
}
