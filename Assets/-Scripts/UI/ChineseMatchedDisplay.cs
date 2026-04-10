using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Displays the matched progress of a Chinese or Mixed phase.
/// Chinese segments: each character gets a CharacterCell (snap-in on syllable complete).
/// English segments: plain TMP label, letters revealed one-by-one.
/// </summary>
public class ChineseMatchedDisplay : MonoBehaviour
{
    [SerializeField] private GameObject characterCellPrefab;
    [SerializeField] private Transform cellContainer;

    private readonly List<CharacterCell> cells = new List<CharacterCell>();

    // English segment tracking: label + the range of typeTarget indices it covers
    private struct EnglishSegmentLabel
    {
        public TextMeshProUGUI label;
        public int typeStart;
        public int typeEnd;
        public string fullText;
    }
    private readonly List<EnglishSegmentLabel> englishLabels = new List<EnglishSegmentLabel>();

    // ── Chinese-only phase ─────────────────────────────────────────────────────

    public void BuildCells(ChinesePhaseData data)
    {
        Clear();
        for (int i = 0; i < data.characters.Length; i++)
        {
            GameObject go = Instantiate(characterCellPrefab, cellContainer);
            var cell = go.GetComponent<CharacterCell>();
            if (cell != null)
            {
                int prevBoundary = i == 0 ? 0 : data.boundaries[i - 1];
                cell.Init(data.characters[i], data.typeTarget, prevBoundary, data.boundaries[i]);
                cells.Add(cell);
            }
        }
    }

    // ── Mixed phase ────────────────────────────────────────────────────────────

    public void BuildMixedCells(MixedPhaseParser.MixedPhaseResult parsed)
    {
        Clear();
        foreach (var seg in parsed.segments)
        {
            if (seg.type == MixedPhaseParser.SegmentType.Chinese)
            {
                for (int i = 0; i < seg.characters.Length; i++)
                {
                    GameObject go = Instantiate(characterCellPrefab, cellContainer);
                    var cell = go.GetComponent<CharacterCell>();
                    if (cell != null)
                    {
                        int prevBoundary = i == 0 ? seg.typeStart : seg.boundaries[i - 1];
                        cell.Init(seg.characters[i], parsed.typeTarget, prevBoundary, seg.boundaries[i]);
                        cells.Add(cell);
                    }
                }
            }
            else // English
            {
                // Create a plain TMP label for the English segment
                GameObject labelGO = new GameObject("EnglishSeg");
                labelGO.transform.SetParent(cellContainer, false);
                var rt = labelGO.AddComponent<RectTransform>();
                rt.sizeDelta = new Vector2(seg.text.Length * 22f, 70f);
                var tmp = labelGO.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 32f;
                tmp.color = Color.white;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.text = "";
                var le = labelGO.AddComponent<LayoutElement>();
                le.minWidth = seg.text.Length * 22f;
                le.preferredWidth = seg.text.Length * 22f;
                le.minHeight = 70f;
                le.preferredHeight = 70f;
                englishLabels.Add(new EnglishSegmentLabel
                {
                    label    = tmp,
                    typeStart = seg.typeStart,
                    typeEnd   = seg.typeEnd,
                    fullText  = seg.text
                });
            }
        }
    }

    // ── Progress update (works for both Chinese and Mixed) ────────────────────

    public void UpdateProgress(int typedLetterCount)
    {
        foreach (var cell in cells)
            cell.UpdateState(typedLetterCount);

        foreach (var el in englishLabels)
        {
            int revealed = Mathf.Clamp(typedLetterCount - el.typeStart, 0, el.fullText.Length);
            el.label.text = el.fullText.Substring(0, revealed)
                          + new string('_', el.fullText.Length - revealed);
        }
    }

    public void Clear()
    {
        foreach (Transform child in cellContainer)
            Destroy(child.gameObject);
        cells.Clear();
        englishLabels.Clear();
    }
}
