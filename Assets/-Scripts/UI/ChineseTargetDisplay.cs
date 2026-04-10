using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Displays the target Chinese phrase, one TargetCell per character.
/// Each cell shows the character below and optionally the pinyin above.
/// </summary>
public class ChineseTargetDisplay : MonoBehaviour
{
    [SerializeField] private GameObject targetCellPrefab;
    [SerializeField] private Transform cellContainer;
    [SerializeField] private bool showPinyin = true;

    private readonly List<TargetCell> cells = new List<TargetCell>();

    public void BuildCells(ChinesePhaseData data)
    {
        Clear();
        for (int i = 0; i < data.characters.Length; i++)
        {
            GameObject go = Instantiate(targetCellPrefab, cellContainer);
            var cell = go.GetComponent<TargetCell>();
            if (cell != null)
            {
                cell.Init(data.characters[i], data.entries[i].pinyin, showPinyin);
                cells.Add(cell);
            }
        }
    }

    /// <summary>
    /// Builds the target display for a mixed phase.
    /// Chinese segments get TargetCell prefabs; English segments get plain TMP labels.
    /// </summary>
    public void BuildMixedCells(MixedPhaseParser.MixedPhaseResult parsed)
    {
        Clear();
        foreach (var seg in parsed.segments)
        {
            if (seg.type == MixedPhaseParser.SegmentType.Chinese)
            {
                for (int i = 0; i < seg.characters.Length; i++)
                {
                    GameObject go = Instantiate(targetCellPrefab, cellContainer);
                    var cell = go.GetComponent<TargetCell>();
                    if (cell != null)
                    {
                        cell.Init(seg.characters[i], seg.entries[i].pinyin, showPinyin);
                        cells.Add(cell);
                    }
                }
            }
            else // English
            {
                GameObject labelGO = new GameObject("EnglishTargetSeg");
                labelGO.transform.SetParent(cellContainer, false);
                var rt = labelGO.AddComponent<RectTransform>();
                rt.sizeDelta = new Vector2(seg.text.Length * 22f, 70f);
                var tmp = labelGO.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 32f;
                tmp.color = Color.white;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.text = seg.text;
                var le = labelGO.AddComponent<LayoutElement>();
                le.minWidth = seg.text.Length * 22f;
                le.preferredWidth = seg.text.Length * 22f;
                le.minHeight = 70f;
                le.preferredHeight = 70f;
            }
        }
    }

    public void SetPinyinVisible(bool visible)
    {
        showPinyin = visible;
        foreach (var cell in cells)
            cell.SetPinyinVisible(visible);
    }

    public void Clear()
    {
        foreach (Transform child in cellContainer)
            Destroy(child.gameObject);
        cells.Clear();
    }
}
