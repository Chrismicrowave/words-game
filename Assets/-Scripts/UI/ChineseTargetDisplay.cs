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
    [SerializeField] private int maxRows = 4;
    [SerializeField] private float delayBetweenCells = 0.03f;
    [SerializeField] private float transitionSpeed = 20f;
    [SerializeField] private AudioClip landingSound;
    [Range(0, 1)] [SerializeField] private float landingSoundVolume = 0.4f;
    [Range(0.5f, 2f)] [SerializeField] private float landingSoundPitchRandomization = 1.1f;

    private readonly List<TargetCell> cells = new List<TargetCell>();
    private readonly List<EnglishCell> englishCells = new List<EnglishCell>();

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
        PrepareEntryAnimation();
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
                }
            }
        }
        PrepareEntryAnimation();
    }

    /// <summary>
    /// Stores cell references for entry animation — called after building.
    /// PlayEntryAnimation() positions and animates cells once the GO is active.
    /// </summary>
    private void PrepareEntryAnimation()
    {
    }

    /// <summary>
    /// Call after the GameObject is active. Configures GridLayoutGroup cell size
    /// based on actual content widths, then animates cells popping in.
    /// </summary>
    public void PlayEntryAnimation()
    {
        if (cells.Count == 0 || !gameObject.activeInHierarchy) return;

        // Measure natural cell size from the first cell's prefab
        float maxCellWidth = cells[0].GetComponent<RectTransform>().rect.width;
        float cellHeight = cells[0].GetComponent<RectTransform>().rect.height;
        if (maxCellWidth < 60f) maxCellWidth = 60f;
        if (cellHeight < 60f) cellHeight = 300f;

        // Calculate columns: fit maxCellWidth cells into 1200px container with 20px spacing
        float containerWidth = 1200f;
        float spacing = 20f;
        int maxCols = Mathf.FloorToInt((containerWidth + spacing) / (maxCellWidth + spacing));
        if (maxCols < 1) maxCols = 1;
        if (maxCols > 12) maxCols = 12;

        // If more than 4 rows, shrink cell width to fit
        int rows = Mathf.CeilToInt((float)cells.Count / maxCols);
        float cellWidth = maxCellWidth;
        if (maxRows > 0 && rows > maxRows)
        {
            int neededCols = Mathf.CeilToInt((float)cells.Count / maxRows);
            cellWidth = (containerWidth - (neededCols - 1) * spacing) / neededCols;
            maxCols = neededCols;
            if (maxCols > 12) maxCols = 12;
        }

        // Apply to GridLayoutGroup — scale height proportionally when width shrinks
        var grid = cellContainer.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        if (grid != null)
        {
            float scaleRatio = cellWidth / maxCellWidth;
            float scaledHeight = cellHeight * scaleRatio * 0.5f;
            if (scaledHeight < 60f) scaledHeight = 60f;
            grid.cellSize = new Vector2(cellWidth, scaledHeight);
            grid.constraintCount = maxCols;
        }

        StartCoroutine(AnimateCellsIn());
    }

    /// <summary>
    /// Pops each cell in from scale 0 to 1 with easeOutBack, fade, and landing sound.
    /// Layout stays on — positions are never touched.
    /// </summary>
    private IEnumerator AnimateCellsIn()
    {
        // Start all cells at scale 0
        foreach (var cell in cells)
            cell.transform.localScale = Vector3.zero;

        // Audio source setup
        var audioSrc = GetComponent<AudioSource>();
        if (audioSrc == null && landingSound != null)
        {
            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false;
        }

        // Pop in one by one
        for (int i = 0; i < cells.Count; i++)
        {
            var t = cells[i].transform;
            float elapsed = 0f;
            while (elapsed < 1f)
            {
                elapsed += Time.deltaTime * transitionSpeed;
                float p = Mathf.Clamp01(elapsed);

                // EaseOutBack: overshoot then settle (same feel as CurText's bounce)
                float c1 = 1.70158f;
                float c3 = c1 + 1f;
                float eased = 1f + c3 * Mathf.Pow(p - 1f, 3f) + c1 * Mathf.Pow(p - 1f, 2f);
                t.localScale = Vector3.one * Mathf.Max(0f, eased);

                yield return null;
            }
            t.localScale = Vector3.one;

            // Landing sound
            if (landingSound != null && audioSrc != null)
            {
                audioSrc.pitch = Random.Range(1f / landingSoundPitchRandomization, landingSoundPitchRandomization);
                audioSrc.PlayOneShot(landingSound, landingSoundVolume);
            }

            if (i < cells.Count - 1)
                yield return new WaitForSeconds(delayBetweenCells);
        }
    }

    public void SetPinyinVisible(bool visible)
    {
        showPinyin = visible;
        foreach (var cell in cells)
            cell.SetPinyinVisible(visible);
    }

    /// <summary>
    /// Locks all pinyin labels to the smallest auto-sized pinyin font in this phase,
    /// so they stay visually consistent across cells.
    /// Call after Canvas.ForceUpdateCanvases() so TMP has resolved its font sizes.
    /// </summary>
    public void SyncPinyinFontSize()
    {
        // Prefab auto-sizing handles this — no manual override needed.
    }

    /// <summary>
    /// Syncs English cell font size to the Chinese cells' live auto-sized font,
    /// then resizes each cell's width so the word fits on one line.
    /// Call after Canvas.ForceUpdateCanvases() so TMP has resolved its font size.
    /// </summary>
    public void SyncEnglishFontSize()
    {
        if (cells.Count == 0 || englishCells.Count == 0) return;
        float size = cells[0].CharFontSize;
        foreach (var ec in englishCells)
        {
            if (ec.Label == null) continue;
            ec.Label.fontSize = size;
            ec.Label.ForceMeshUpdate();
            // Measure preferred width for one line at this font size
            float w = ec.Label.GetPreferredValues(ec.Label.text, float.MaxValue, 200f).x;
            var rt = ec.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
        }
    }

    public void Clear()
    {
        StopAllCoroutines();
        foreach (Transform child in cellContainer)
            Destroy(child.gameObject);
        cells.Clear();
        englishCells.Clear();
    }

    public void SyncFontSizesNextFrame()
    {
        StartCoroutine(SyncFontSizesCoroutine());
    }

    private IEnumerator SyncFontSizesCoroutine()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        SyncPinyinFontSize();
        SyncEnglishFontSize();
    }

}
