using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Displays the target Chinese phrase, one TargetCell per character.
/// Each cell shows the character below and optionally the pinyin above.
/// Cells animate in sequentially from scale 0 with a landing sound,
/// matching the CurText fly-in feel for English letters.
/// </summary>
public class ChineseTargetDisplay : MonoBehaviour
{
    [SerializeField] private GameObject targetCellPrefab;
    [SerializeField] private GameObject englishTargetCellPrefab;
    [SerializeField] private Transform cellContainer;
    [SerializeField] private bool showPinyin = true;
    [SerializeField] private TMPro.TMP_FontAsset chineseFontAsset; // NotoSansSC — for non-ASCII English segments

    [Header("Entry Animation")]
    [SerializeField] private float cellSpacing = 20f;
    [SerializeField] private Vector3 offsetStartPosition = new Vector3(50f, 100f, 0f);
    [SerializeField] private float delayBetweenCells = 0.03f;
    [SerializeField] private float transitionSpeed = 20f;
    [SerializeField] private AudioClip landingSound;
    [Range(0, 1)] [SerializeField] private float landingSoundVolume = 0.4f;
    [Range(0.5f, 2f)] [SerializeField] private float landingSoundPitchRandomization = 1.1f;

    [Header("Manual Layout")]
    [SerializeField] private float charRatio = 0.6f;
    [SerializeField] private float pinyinRatio = 0.3f;
    [SerializeField] private float englishFontRatio = 0.8f;
    [SerializeField] private float horizontalSpacing = 10f;
    [SerializeField] private float verticalSpacing = 15f;

    private readonly List<TargetCell> cells = new List<TargetCell>();
    private readonly List<EnglishCell> englishCells = new List<EnglishCell>();
    // Ordered items used by layout — rebuilt during Build*Cells to avoid iterating stale destroyed children
    private readonly List<LayoutItem> currentItems = new List<LayoutItem>();

    private void DisableGrid()
    {
        var g = cellContainer.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        if (g != null) g.enabled = false;
    }

    public void BuildCells(ChinesePhaseData data)
    {
        Clear();
        DisableGrid();
        for (int i = 0; i < data.characters.Length; i++)
        {
            GameObject go = Instantiate(targetCellPrefab, cellContainer);
            var cell = go.GetComponent<TargetCell>();
            if (cell != null)
            {
                cell.Init(data.characters[i], data.entries[i].pinyin, showPinyin);
                cells.Add(cell);
                currentItems.Add(new LayoutItem { rect = go.transform as RectTransform, target = cell, english = null });
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
        DisableGrid();
        foreach (var seg in parsed.segments)
        {
            if (seg.type == MixedPhaseParser.SegmentType.Chinese)
            {
                int n = seg.characters?.Length ?? 0;
                for (int i = 0; i < n; i++)
                {
                    GameObject go = Instantiate(targetCellPrefab, cellContainer);
                    var cell = go.GetComponent<TargetCell>();
                    if (cell != null)
                    {
                        cell.Init(seg.characters[i], seg.entries[i].pinyin, showPinyin);
                        cells.Add(cell);
                        currentItems.Add(new LayoutItem { rect = go.transform as RectTransform, target = cell, english = null });
                    }
                }
            }
            else // English
            {
                if (englishTargetCellPrefab == null) continue;
                GameObject go = Instantiate(englishTargetCellPrefab, cellContainer);
                var cell = go.GetComponent<EnglishCell>();
                if (cell != null)
                {
                    cell.SetText(seg.text);
                    // Apply Chinese font when segment contains non-ASCII characters (e.g. 。，、)
                    if (chineseFontAsset != null && PinyinLookup.HasNonAscii(seg.text) && cell.Label != null)
                        cell.Label.font = chineseFontAsset;
                    englishCells.Add(cell);
                    currentItems.Add(new LayoutItem { rect = go.transform as RectTransform, target = null, english = cell });
                }
            }
        }
    }

    /// <summary>
    /// Call after the GameObject is active. Performs manual row-based layout:
    /// - Collects all Chinese + English items in container order
    /// - Determines optimal row count (1→2→3) based on 75%/50% sizing bands
    /// - Applies uniform font sizes (auto-sizing OFF)
    /// - Measures English text widths at the sized font
    /// - Positions each row manually, independently scaled to fit viewport
    /// </summary>
    public void PlayEntryAnimation()
    {
        if (!gameObject.activeInHierarchy) return;

        // Ensure canvas layout is up to date before measuring viewport
        UnityEngine.Canvas.ForceUpdateCanvases();

        // Use currentItems (built during Build*Cells, not container iteration)
        var items = currentItems;
        if (items.Count == 0) return;

        // Disable GridLayoutGroup — manual layout
        var grid = cellContainer.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        if (grid != null) grid.enabled = false;

        // Zero out container rect so manual positions are relative to viewport top-left
        var containerRt = cellContainer as RectTransform;
        if (containerRt != null)
        {
            containerRt.anchorMin = Vector2.zero;
            containerRt.anchorMax = Vector2.one;
            containerRt.sizeDelta = Vector2.zero;
            containerRt.anchoredPosition = Vector2.zero;
        }

        // Viewport
        var viewportRt = cellContainer.parent as RectTransform;
        float viewportWidth = viewportRt != null ? viewportRt.rect.width : 1920f;

        // Prefab base size from grid inspector
        float prefabSize = grid != null && grid.cellSize.x > 0 ? grid.cellSize.x : 100f;

        // Determine row count and base cell size
        int rowCount;
        float cellSize;
        DetermineOptimalLayout(items, prefabSize, viewportWidth, out rowCount, out cellSize);

        // Apply uniform font sizes to all cells
        ApplyUniformFontSizes(items, cellSize);

        // Measure English widths at the sized font
        foreach (var item in items)
        {
            if (item.english != null && item.english.Label != null)
            {
                item.english.Label.ForceMeshUpdate();
                item.width = item.english.Label.GetPreferredValues(
                    item.english.Label.text, float.MaxValue, float.MaxValue).x + 8f; // padding
                if (item.width < cellSize * 0.5f) item.width = cellSize * 0.5f;
            }
            else
            {
                item.width = cellSize;
            }
        }

        // Distribute items into rows
        var rows = DistributeRows(items, rowCount);

        // Calculate per-row metrics
        float viewportHeight = viewportRt != null ? viewportRt.rect.height : 1080f;
        var rowMetrics = new List<(float scale, float cellS, float charS, float pinyinS, float enS, float rowW)>();
        float totalContentHeight = 0f;
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            float naturalRowWidth = 0f;
            for (int i = 0; i < row.Count; i++)
                naturalRowWidth += row[i].width;
            naturalRowWidth += (row.Count - 1) * horizontalSpacing;

            float rowScale = naturalRowWidth <= viewportWidth
                ? 1f
                : viewportWidth / Mathf.Max(naturalRowWidth, 1f);

            float scs = cellSize * rowScale; // scaled cell size
            float rw = naturalRowWidth * rowScale; // scaled row width
            rowMetrics.Add((rowScale, scs, scs * charRatio, scs * pinyinRatio, scs * charRatio * englishFontRatio, rw));
            totalContentHeight += scs + (r < rows.Count - 1 ? verticalSpacing : 0f);
        }

        // Vertical centering offset
        float yOffset = totalContentHeight < viewportHeight
            ? (viewportHeight - totalContentHeight) / 2f
            : 0f;

        // Position each row, centered horizontally and vertically
        float y = yOffset;
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var m = rowMetrics[r];
            float x = (viewportWidth - m.rowW) / 2f; // horizontal center

            for (int i = 0; i < row.Count; i++)
            {
                var item = row[i];
                float w = item.width * m.scale;

                RectTransform rt = item.rect;
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0.5f, 0.5f);

                if (item.target != null)
                {
                    rt.sizeDelta = new Vector2(w, m.cellS);
                    item.target.SetFixedFontSizes(m.charS, m.pinyinS);
                }
                else if (item.english != null)
                {
                    rt.sizeDelta = new Vector2(w, m.cellS);
                    item.english.SetFixedFontSize(m.enS);
                }

                rt.anchoredPosition = new Vector2(x + w / 2f, -(y + m.cellS / 2f));
                x += w + horizontalSpacing;
            }

            y += m.cellS + verticalSpacing;
        }

        // Container rect stays zero — children are positioned manually

        StartCoroutine(AnimateCellsIn());
    }

    /// <summary>
    /// Pops each cell in from scale 0 to 1 with easeOutBack, fade, and landing sound.
    /// Animates all children (both Chinese and English cells) in container order.
    /// </summary>
    private IEnumerator AnimateCellsIn()
    {
        // Use currentItems (populated during Build*Cells, guaranteed valid)
        var children = new List<Transform>(currentItems.Count);
        for (int i = 0; i < currentItems.Count; i++)
            children.Add(currentItems[i].rect);
        // Items guaranteed valid from currentItems

        // Start all at scale 0
        foreach (var child in children)
        {
            if (child == null) continue;
            child.localScale = Vector3.zero;
        }

        // Audio source setup
        var audioSrc = GetComponent<AudioSource>();
        if (audioSrc == null && landingSound != null)
        {
            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false;
        }

        // Pop in one by one
        for (int i = 0; i < children.Count; i++)
        {
            var t = children[i];
            if (t == null) continue;

            float elapsed = 0f;
            while (elapsed < 1f)
            {
                elapsed += Time.deltaTime * transitionSpeed;
                float p = Mathf.Clamp01(elapsed);

                // EaseOutBack: overshoot then settle
                float c1 = 1.70158f;
                float c3 = c1 + 1f;
                float eased = 1f + c3 * Mathf.Pow(p - 1f, 3f) + c1 * Mathf.Pow(p - 1f, 2f);
                if (t != null)
                    t.localScale = Vector3.one * Mathf.Max(0f, eased);

                yield return null;
                if (t == null) break; // child was destroyed during yield
            }
            if (t == null) continue;
            t.localScale = Vector3.one;

            // Landing sound on every cell
            if (landingSound != null && audioSrc != null)
            {
                audioSrc.pitch = Random.Range(1f / landingSoundPitchRandomization, landingSoundPitchRandomization);
                audioSrc.PlayOneShot(landingSound, landingSoundVolume);
            }

            if (i < children.Count - 1)
                yield return new WaitForSeconds(delayBetweenCells);
        }
    }

    /// <summary>
    /// Determines optimal row count and base cell size.
    /// Respects max 16 cols per row. Applies sizing bands:
    /// 1 row → 100% base → try 2 rows if effective <75%
    /// 2 rows → 75% base → try 3 rows if effective <50%
    /// 3 rows → 50% base
    /// </summary>
    private void DetermineOptimalLayout(List<LayoutItem> items, float prefabSize,
        float viewportWidth, out int rowCount, out float cellSize)
    {
        const int maxCols = 16;
        int total = items.Count;
        int minRows = Mathf.CeilToInt((float)total / maxCols); // minimum rows by column constraint

        // Start from minRows band, check if we need more rows
        float bandSize = prefabSize;
        if (minRows >= 3) { rowCount = Mathf.Min(minRows, 3); cellSize = prefabSize * 0.5f; return; }
        if (minRows == 2) { bandSize = prefabSize * 0.75f; }
        // minRows == 1 → check sizing

        // Items in first row (capped at maxCols)
        int firstRowCols = Mathf.Min(total, maxCols);
        float firstRowWidth = firstRowCols * bandSize + (firstRowCols - 1) * horizontalSpacing;
        float rowScale = firstRowWidth <= viewportWidth ? 1f : viewportWidth / Mathf.Max(firstRowWidth, 1f);
        float effective = bandSize * rowScale;

        if (minRows == 1)
        {
            if (effective >= prefabSize * 0.75f || total <= 1)
            {
                rowCount = 1;
                cellSize = effective;
                return;
            }
            // Try 2 rows at 75%
            float base2 = prefabSize * 0.75f;
            float row2eff = base2;
            if (firstRowCols > 1)
            {
                float row2w = maxCols * base2 + (maxCols - 1) * horizontalSpacing;
                float s2 = row2w <= viewportWidth ? 1f : viewportWidth / Mathf.Max(row2w, 1f);
                row2eff = base2 * s2;
            }
            if (row2eff >= prefabSize * 0.5f || total <= 2)
            {
                rowCount = 2;
                cellSize = row2eff;
                return;
            }
            rowCount = 3;
            cellSize = prefabSize * 0.5f;
            return;
        }
        else // minRows == 2
        {
            if (effective >= prefabSize * 0.5f || total <= 2)
            {
                rowCount = 2;
                cellSize = effective;
                return;
            }
            rowCount = 3;
            cellSize = prefabSize * 0.5f;
            return;
        }
    }

    /// <summary>Applies uniform font sizes to all cells based on the cell size.</summary>
    private void ApplyUniformFontSizes(List<LayoutItem> items, float cellSize)
    {
        float charSize = cellSize * charRatio;
        float pinyinSize = cellSize * pinyinRatio;
        float enSize = charSize * englishFontRatio;

        foreach (var item in items)
        {
            if (item.target != null)
                item.target.SetFixedFontSizes(charSize, pinyinSize);
            else if (item.english != null)
                item.english.SetFixedFontSize(enSize);
        }
    }

    /// <summary>
    /// Distributes items across rows with max 16 columns per row.
    /// First rows hold 16 items, last row holds the remainder (matching old GridLayoutGroup 16-col behavior).
    /// </summary>
    private List<List<LayoutItem>> DistributeRows(List<LayoutItem> items, int rowCount)
    {
        var rows = new List<List<LayoutItem>>();
        if (items.Count == 0) return rows;

        const int maxCols = 16;
        int idx = 0;
        for (int r = 0; r < rowCount && idx < items.Count; r++)
        {
            int count = Mathf.Min(items.Count - idx, maxCols);
            var row = new List<LayoutItem>();
            for (int i = 0; i < count; i++)
                row.Add(items[idx++]);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Layout item — either a Chinese TargetCell or an EnglishCell.</summary>
    private class LayoutItem
    {
        public RectTransform rect;
        public TargetCell target;
        public EnglishCell english;
        public float width; // calculated during layout
    }

    public void SetPinyinVisible(bool visible)
    {
        showPinyin = visible;
        foreach (var cell in cells)
            cell.SetPinyinVisible(visible);
    }

    public void Clear()
    {
        StopAllCoroutines();
        foreach (Transform child in cellContainer)
        {
            if (child == null) continue;
            child.localScale = Vector3.zero;
            Destroy(child.gameObject);
        }
        cells.Clear();
        englishCells.Clear();
        currentItems.Clear();
    }

}
