using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Popup shown when the user adds a phase containing Chinese characters.
/// Displays a preview row (TargetCell prefabs), a readonly character field,
/// and an editable pinyin field. OK validates and returns ChineseWordEntry or
/// MixedWordEntry depending on content.
/// </summary>
public class ChinesePinyinPopup : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private GameObject targetCellPrefab;
    [SerializeField] private GameObject englishTargetCellPrefab;  // same prefab as ChineseTargetDisplay uses
    [SerializeField] private TMP_FontAsset chineseFontAsset;      // NotoSansSC — for non-ASCII segments (Chinese punctuation)

    [Header("UI References")]
    [SerializeField] private Transform previewContainer;      // HorizontalLayoutGroup row
    [SerializeField] private TMP_InputField charField;        // readonly, shows "你 好"
    [SerializeField] private TMP_InputField pinyinField;      // editable, shows "ni hao"
    [SerializeField] private TextMeshProUGUI errorLabel;      // validation message
    [SerializeField] private Button okBtn;
    [SerializeField] private Button cancelBtn;

    // Segments from the input text: (isChinese, text)
    private List<(bool isChinese, string text)> _segments;
    // Flat list of only the Chinese characters (for validation)
    private List<char> _chineseChars;

    private Action<MixedWordEntry> _onConfirm;
    private Action _onCancel;

    // Spawned preview cells — refreshed each BuildPreview() call
    private readonly List<TargetCell>  _previewChineseCells  = new List<TargetCell>();
    private readonly List<EnglishCell> _previewEnglishCells  = new List<EnglishCell>();

    void Awake()
    {
        okBtn.onClick.AddListener(OnOK);
        cancelBtn.onClick.AddListener(OnCancel);
        // Bug 5: do NOT call SetActive(false) here — if the GO starts disabled in the Editor,
        // Awake fires on first activation (triggered by Show()), and SetActive(false) would
        // immediately re-hide the popup before it is ever displayed.

        // Set Chinese font on pinyinField so Chinese punctuation (，。、etc.) renders correctly
        if (chineseFontAsset != null && pinyinField != null && pinyinField.textComponent != null)
            pinyinField.textComponent.font = chineseFontAsset;
    }

    /// <summary>
    /// Opens the popup for the given input text.
    /// Auto-detects Chinese/English segments, auto-fills pinyin for known characters.
    /// onConfirm receives a MixedWordEntry ready to save.
    /// </summary>
    public void Show(string inputText, Action<MixedWordEntry> onConfirm, Action onCancel)
    {
        _onConfirm = onConfirm;
        _onCancel  = onCancel;
        // ExpandSegments splits non-Chinese runs at letter/non-letter boundaries,
        // so "，good?" becomes ["，", "good", "?"] — each gets its own preview cell.
        _segments  = ExpandSegments(PinyinLookup.Segment(inputText));
        _chineseChars = new List<char>();

        // Build character display and auto-pinyin
        var charParts   = new List<string>();
        var pinyinParts = new List<string>();

        foreach (var (isChinese, text) in _segments)
        {
            if (isChinese)
            {
                foreach (char c in text)
                {
                    _chineseChars.Add(c);
                    charParts.Add(c.ToString());
                    pinyinParts.Add(PinyinLookup.Get(c)); // "" if unknown
                }
            }
            else
            {
                // All non-Chinese segments go into both char field and pinyin field
                // (punctuation like ， renders via chineseFontAsset set on pinyinField)
                charParts.Add(text);
                pinyinParts.Add(text);
            }
        }

        // Populate fields
        charField.text   = string.Join(" ", charParts);
        pinyinField.text = string.Join(" ", pinyinParts).Trim();

        if (errorLabel != null) errorLabel.text = "";

        // Build preview cells (only Chinese characters get TargetCells)
        BuildPreview();

        gameObject.SetActive(true);
        StartCoroutine(SyncPreviewFontNextFrame());
        pinyinField.Select();
    }

    private void BuildPreview()
    {
        foreach (Transform child in previewContainer)
            Destroy(child.gameObject);

        _previewChineseCells.Clear();
        _previewEnglishCells.Clear();

        // Walk all segments: English segments get an EnglishCell, Chinese chars get a TargetCell.
        // Every non-Chinese segment now has exactly one token in pinyinField (ExpandSegments
        // split them at letter/non-letter boundaries, no spaces within a segment).
        var pinyinTokens = pinyinField.text.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
        int tokenIdx = 0;

        foreach (var (isChinese, text) in _segments)
        {
            if (!isChinese)
            {
                // Show English / punctuation segment as an EnglishCell in the preview row
                if (englishTargetCellPrefab != null)
                {
                    GameObject go = Instantiate(englishTargetCellPrefab, previewContainer);
                    var cell = go.GetComponent<EnglishCell>();
                    if (cell != null)
                    {
                        cell.SetText(text.Trim());
                        // Apply Chinese font for non-ASCII characters (Chinese punctuation, fullwidth)
                        if (chineseFontAsset != null && HasNonAscii(text) && cell.Label != null)
                            cell.Label.font = chineseFontAsset;
                        _previewEnglishCells.Add(cell);
                    }
                }
                // Every non-Chinese segment has exactly one token in pinyinField
                int skipCount = text.Trim().Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries).Length;
                tokenIdx += skipCount;
            }
            else
            {
                // One TargetCell per Chinese character, consuming one pinyin token each
                foreach (char c in text)
                {
                    string pinyin = (tokenIdx < pinyinTokens.Length) ? pinyinTokens[tokenIdx] : "";
                    tokenIdx++;
                    GameObject go = Instantiate(targetCellPrefab, previewContainer);
                    var cell = go.GetComponent<TargetCell>();
                    if (cell != null)
                    {
                        cell.Init(c.ToString(), pinyin, true);
                        _previewChineseCells.Add(cell);
                    }
                }
            }
        }
    }

    // Called when pinyin field changes — refresh preview and re-sync font size
    public void OnPinyinFieldChanged(string _)
    {
        BuildPreview();
        if (gameObject.activeInHierarchy)
            StartCoroutine(SyncPreviewFontNextFrame());
    }

    /// <summary>
    /// After one frame (TMP auto-size resolves), sync English cell font size to the Chinese
    /// TargetCell char size and resize the cell width so text fits on one line.
    /// </summary>
    private IEnumerator SyncPreviewFontNextFrame()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        if (_previewChineseCells.Count == 0 || _previewEnglishCells.Count == 0) yield break;

        float size = _previewChineseCells[0].CharFontSize;
        foreach (var ec in _previewEnglishCells)
        {
            if (ec == null || ec.Label == null) continue;
            ec.Label.enableAutoSizing = false;
            ec.Label.fontSize = size;
            ec.Label.ForceMeshUpdate();
            float w = ec.Label.GetPreferredValues(ec.Label.text, float.MaxValue, 200f).x;
            var rt = ec.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(w + 4f, rt.sizeDelta.y);
        }
    }

    /// <summary>
    /// Splits non-Chinese runs at letter/non-letter boundaries, dropping pure-whitespace chunks.
    /// "，good?" → [(false,"，"), (false,"good"), (false,"?")]
    /// Each expanded segment maps 1:1 to a pinyinField token and a preview EnglishCell.
    /// </summary>
    private static List<(bool isChinese, string text)> ExpandSegments(List<(bool isChinese, string text)> raw)
    {
        var result = new List<(bool, string)>();
        foreach (var (isChinese, text) in raw)
        {
            if (isChinese) { result.Add((true, text)); continue; }
            if (string.IsNullOrEmpty(text)) continue;
            var sb = new System.Text.StringBuilder();
            bool curIsLetter = char.IsLetterOrDigit(text[0]);
            foreach (char c in text)
            {
                bool isLetter = char.IsLetterOrDigit(c);
                if (isLetter != curIsLetter)
                {
                    string chunk = sb.ToString();
                    if (!string.IsNullOrWhiteSpace(chunk)) result.Add((false, chunk));
                    sb.Clear();
                    curIsLetter = isLetter;
                }
                sb.Append(c);
            }
            string last = sb.ToString();
            if (!string.IsNullOrWhiteSpace(last)) result.Add((false, last));
        }
        return result;
    }

    private static bool HasLetterOrDigit(string text)
    {
        foreach (char c in text) if (char.IsLetterOrDigit(c)) return true;
        return false;
    }

    private static bool HasNonAscii(string text)
    {
        foreach (char c in text) if (c > 127) return true;
        return false;
    }

    private void OnOK()
    {
        if (errorLabel != null) errorLabel.text = "";

        // pinyinField now contains both English words and Chinese pinyin (interspersed).
        // Walk _segments to extract only the Chinese pinyin tokens, skipping English ones.
        var allTokens = pinyinField.text.Trim()
            .Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);

        int tokenIdx = 0;
        var chinesePinyinWords = new List<string>();

        foreach (var (isChinese, text) in _segments)
        {
            if (!isChinese)
            {
                // Every non-Chinese segment has exactly one token in pinyinField — always skip it
                int skipCount = text.Trim().Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries).Length;
                tokenIdx += skipCount;
            }
            else
            {
                foreach (char c in text)
                {
                    if (tokenIdx < allTokens.Length)
                        chinesePinyinWords.Add(allTokens[tokenIdx++]);
                }
            }
        }

        if (chinesePinyinWords.Count != _chineseChars.Count)
        {
            if (errorLabel != null)
                errorLabel.text = $"Need {_chineseChars.Count} pinyin word(s), got {chinesePinyinWords.Count}.";
            return;
        }

        // Validate: only a-z allowed in each pinyin syllable
        foreach (var p in chinesePinyinWords)
        {
            foreach (char c in p)
            {
                if (c < 'a' || c > 'z')
                {
                    if (errorLabel != null)
                        errorLabel.text = $"Pinyin must be lowercase a-z only (no tones). Got: {p}";
                    return;
                }
            }
        }

        // Build MixedWordEntry from segments
        var mixedSegments = new List<MixedSegmentData>();
        int pinyinIdx = 0;

        foreach (var (isChinese, text) in _segments)
        {
            if (!isChinese)
            {
                mixedSegments.Add(new MixedSegmentData { type = "english", text = text });
            }
            else
            {
                var entries = new List<ChinesePhaseEntry>();
                foreach (char c in text)
                {
                    entries.Add(new ChinesePhaseEntry
                    {
                        character = c.ToString(),
                        pinyin    = chinesePinyinWords[pinyinIdx++]
                    });
                }
                mixedSegments.Add(new MixedSegmentData { type = "chinese", entries = entries });
            }
        }

        var result = new MixedWordEntry { segments = mixedSegments };
        gameObject.SetActive(false);
        _onConfirm?.Invoke(result);
    }

    private void OnCancel()
    {
        gameObject.SetActive(false);
        _onCancel?.Invoke();
    }
}
