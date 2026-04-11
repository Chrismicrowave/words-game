using System;
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

    void Awake()
    {
        okBtn.onClick.AddListener(OnOK);
        cancelBtn.onClick.AddListener(OnCancel);
        // Bug 5: do NOT call SetActive(false) here — if the GO starts disabled in the Editor,
        // Awake fires on first activation (triggered by Show()), and SetActive(false) would
        // immediately re-hide the popup before it is ever displayed.
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
        _segments  = PinyinLookup.Segment(inputText);
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
                // English segment — show in both char and pinyin fields for reference
                charParts.Add(text);
                pinyinParts.Add(text); // Bug 2: show English words in pinyin field too
            }
        }

        // Populate fields
        charField.text   = string.Join(" ", charParts);
        pinyinField.text = string.Join(" ", pinyinParts).Trim();

        if (errorLabel != null) errorLabel.text = "";

        // Build preview cells (only Chinese characters get TargetCells)
        BuildPreview();

        gameObject.SetActive(true);
        pinyinField.Select();
    }

    private void BuildPreview()
    {
        foreach (Transform child in previewContainer)
            Destroy(child.gameObject);

        // Walk all segments: English segments get an EnglishCell, Chinese chars get a TargetCell.
        // pinyinField tokens are ordered: [english_tokens...] [pinyin_per_char...] interspersed.
        var pinyinTokens = pinyinField.text.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
        int tokenIdx = 0;

        foreach (var (isChinese, text) in _segments)
        {
            if (!isChinese)
            {
                // Bug 1: show English segment as an EnglishCell in the preview row
                if (englishTargetCellPrefab != null)
                {
                    GameObject go = Instantiate(englishTargetCellPrefab, previewContainer);
                    var cell = go.GetComponent<EnglishCell>();
                    cell?.SetText(text.Trim());
                }
                // Skip the corresponding token(s) that represent this English segment
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
                    cell?.Init(c.ToString(), pinyin, true);
                }
            }
        }
    }

    // Called when pinyin field changes — refresh preview
    public void OnPinyinFieldChanged(string _)
    {
        BuildPreview();
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
                // Skip the token(s) representing this English segment
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
