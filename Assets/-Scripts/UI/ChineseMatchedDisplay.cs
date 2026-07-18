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
    [SerializeField] private GameObject englishCellPrefab;
    [SerializeField] private Transform cellContainer;
    [SerializeField] private TMPro.TMP_FontAsset chineseFontAsset; // NotoSansSC — for non-ASCII English segments

    private readonly List<CharacterCell> cells = new List<CharacterCell>();
    // Ordered children for LayoutCells (avoids iterating container with stale destroyed children)
    private readonly List<RectTransform> currentChildren = new List<RectTransform>();

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
                currentChildren.Add(go.transform as RectTransform);
            }
        }
        LayoutCells();
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
                        currentChildren.Add(go.transform as RectTransform);
                    }
                }
            }
            else // English
            {
                if (englishCellPrefab == null) continue;
                GameObject go = Instantiate(englishCellPrefab, cellContainer);
                var cell = go.GetComponent<EnglishCell>();
                if (cell?.Label != null)
                {
                    cell.SetText("");
                    // Apply Chinese font when segment contains non-ASCII characters (e.g. 。，、)
                    if (chineseFontAsset != null && PinyinLookup.HasNonAscii(seg.text))
                        cell.Label.font = chineseFontAsset;
                    englishLabels.Add(new EnglishSegmentLabel
                    {
                        label    = cell.Label,
                        typeStart = seg.typeStart,
                        typeEnd   = seg.typeEnd,
                        fullText  = seg.text
                    });
                    currentChildren.Add(go.transform as RectTransform);
                }
            }
        }
        LayoutCells();
    }

    // ── Progress update (works for both Chinese and Mixed) ────────────────────

    public void UpdateProgress(int typedLetterCount)
    {
        foreach (var cell in cells)
            cell.UpdateState(typedLetterCount);

        foreach (var el in englishLabels)
        {
            // typeStart/typeEnd are step-based (letter counts, spaces excluded).
            // Reveal letters one-by-one while preserving spaces in the display string.
            int lettersTyped = Mathf.Clamp(typedLetterCount - el.typeStart, 0, el.typeEnd - el.typeStart);
            char[] chars = el.fullText.ToCharArray();
            int seen = 0;
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsLetterOrDigit(chars[i]))
                {
                    if (seen >= lettersTyped) chars[i] = '_';
                    seen++;
                }
                // spaces / punctuation are left as-is
            }
            el.label.text = new string(chars);
        }
    }

    /// <summary>
    /// Arranges all cells in rows matching the target display's row logic:
    /// max 16 per row, uniform cell sizing, horizontal + vertical centering.
    /// </summary>
    public void LayoutCells()
    {
        UnityEngine.Canvas.ForceUpdateCanvases();

        // Disable HorizontalLayoutGroup — manual row layout
        var hlg = cellContainer.GetComponent<HorizontalLayoutGroup>();
        if (hlg != null) hlg.enabled = false;

        // Zero out container rect
        var containerRt = cellContainer as RectTransform;
        if (containerRt != null)
        {
            containerRt.anchorMin = Vector2.zero;
            containerRt.anchorMax = Vector2.one;
            containerRt.sizeDelta = Vector2.zero;
            containerRt.anchoredPosition = Vector2.zero;
        }

        int total = currentChildren.Count;
        if (total == 0) return;

        // Viewport
        var viewportRt = cellContainer.parent as RectTransform;
        float viewportWidth = viewportRt != null ? viewportRt.rect.width : 1920f;
        float viewportHeight = viewportRt != null ? viewportRt.rect.height : 1080f;

        // Determine row count using same band logic as target display
        int rowCount;
        float cellSize;
        float hSpacing = 2f;
        float vSpacing = 5f;
        const int maxCols = 16;
        float prefabSize = currentChildren.Count > 0 && currentChildren[0] != null
            ? Mathf.Max(currentChildren[0].sizeDelta.x, currentChildren[0].sizeDelta.y) : 60f;

        // Same DetermineOptimalLayout logic as target display
        {
            int totalItems = total;
            int minRows = Mathf.CeilToInt((float)totalItems / maxCols);
            if (minRows >= 3) { rowCount = 3; cellSize = prefabSize * 0.5f; }
            else if (minRows == 2)
            {
                float bandSize = prefabSize * 0.75f;
                int firstRowCols = Mathf.Min(totalItems, maxCols);
                float firstRowWidth = firstRowCols * bandSize + (firstRowCols - 1) * hSpacing;
                float rowScale = firstRowWidth <= viewportWidth ? 1f : viewportWidth / Mathf.Max(firstRowWidth, 1f);
                if (bandSize * rowScale >= prefabSize * 0.5f || totalItems <= 2)
                { rowCount = 2; cellSize = bandSize * rowScale; }
                else
                { rowCount = 3; cellSize = prefabSize * 0.5f; }
            }
            else
            {
                float firstRowWidth = totalItems * prefabSize + (totalItems - 1) * hSpacing;
                float s1 = firstRowWidth <= viewportWidth ? 1f : viewportWidth / Mathf.Max(firstRowWidth, 1f);
                if (prefabSize * s1 >= prefabSize * 0.75f || totalItems <= 1)
                { rowCount = 1; cellSize = prefabSize * s1; }
                else
                {
                    float bandSize = prefabSize * 0.75f;
                    int half2 = Mathf.CeilToInt(totalItems / 2f);
                    float halfWidth2 = Mathf.Min(half2, maxCols) * bandSize + (Mathf.Min(half2, maxCols) - 1) * hSpacing;
                    float s2 = halfWidth2 <= viewportWidth ? 1f : viewportWidth / Mathf.Max(halfWidth2, 1f);
                    if (bandSize * s2 >= prefabSize * 0.5f || totalItems <= 2)
                    { rowCount = 2; cellSize = bandSize * s2; }
                    else
                    { rowCount = 3; cellSize = prefabSize * 0.5f; }
                }
            }
        }
        // Apply uniform cell size to all children (square cells like target display)
        foreach (var rt in currentChildren)
        {
            if (rt == null) continue;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(cellSize, cellSize);
        }

        // Distribute into rows (first rows fill to 16, last row holds remainder)
        var rows = new List<List<RectTransform>>();
        int idx = 0;
        for (int r = 0; r < rowCount && idx < total; r++)
        {
            int count = Mathf.Min(total - idx, maxCols);
            var row = new List<RectTransform>();
            for (int i = 0; i < count; i++)
                row.Add(currentChildren[idx++]);
            rows.Add(row);
        }

        // Vertical centering (same formula as target display)
        float totalHeight = rowCount * cellSize + (rowCount - 1) * vSpacing;
        float yOffset = totalHeight < viewportHeight ? (viewportHeight - totalHeight) / 2f : 0f;

        // Position each row
        float y = yOffset;
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            int cols = row.Count;
            float rowNaturalWidth = cols * cellSize + (cols - 1) * hSpacing;
            float rowScale = rowNaturalWidth <= viewportWidth ? 1f : viewportWidth / Mathf.Max(rowNaturalWidth, 1f);
            float scaledRowWidth = rowNaturalWidth * rowScale;
            float x = (viewportWidth - scaledRowWidth) / 2f;
            float scaledCell = cellSize * rowScale;

            for (int i = 0; i < cols; i++)
            {
                var rt = row[i];
                if (rt == null) continue;
                float w = scaledCell;
                rt.sizeDelta = new Vector2(w, scaledCell);
                rt.anchoredPosition = new Vector2(x + w / 2f, -(y + scaledCell / 2f));
                x += w + hSpacing;
            }
            y += scaledCell + vSpacing;
        }
    }

    public void Clear()
    {
        foreach (Transform child in cellContainer)
        {
            if (child == null) continue;
            child.localScale = Vector3.zero;
            Destroy(child.gameObject);
        }
        cells.Clear();
        englishLabels.Clear();
        currentChildren.Clear();
    }

}
