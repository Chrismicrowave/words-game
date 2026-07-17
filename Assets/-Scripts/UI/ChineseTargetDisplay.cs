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

    [Header("Grid")]
    [SerializeField] private int maxRows = 3;
    [SerializeField] private float containerWidth = 1200f;
    [SerializeField] private float spacingX = 20f;
    [SerializeField] private float spacingY = 0f;

    [Header("Entry Animation")]
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
        int count = Mathf.Min(data.characters.Length, 48);
        for (int i = 0; i < count; i++)
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
        int total = 0;
        foreach (var seg in parsed.segments)
        {
            if (seg.type == MixedPhaseParser.SegmentType.Chinese)
            {
                for (int i = 0; i < seg.characters.Length && total < 48; i++)
                {
                    GameObject go = Instantiate(targetCellPrefab, cellContainer);
                    var cell = go.GetComponent<TargetCell>();
                    if (cell != null)
                    {
                        cell.Init(seg.characters[i], seg.entries[i].pinyin, showPinyin);
                        cells.Add(cell);
                        total++;
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
        int totalCells = cells.Count + englishCells.Count;
        if (totalCells == 0 || !gameObject.activeInHierarchy) return;

        // Prefab natural size
        float prefabW = cells.Count > 0 ? cells[0].GetComponent<RectTransform>().rect.width : 120f;
        float prefabH = cells.Count > 0 ? cells[0].GetComponent<RectTransform>().rect.height : 50f;
        if (prefabW < 30f) prefabW = 120f;
        if (prefabH < 30f) prefabH = 50f;

        // Find maxCols/rows that fit ALL cells in container within maxRows
        float cellScale = 1f;
        int maxCols, rows;
        do
        {
            float scaledW = prefabW * cellScale;
            maxCols = Mathf.FloorToInt((containerWidth + spacingX) / (scaledW + spacingX));
            if (maxCols < 1) maxCols = 1;
            if (maxCols > Mathf.CeilToInt(48f / maxRows)) maxCols = Mathf.CeilToInt(48f / maxRows);
            rows = Mathf.CeilToInt((float)totalCells / maxCols);
            if (maxRows > 0 && rows > maxRows)
                cellScale *= 0.95f;
            else
                break;
        } while (cellScale > 0.3f);

        // Keep prefab ratio, scale proportionally
        float t = Mathf.Clamp01((float)(rows - 1) / Mathf.Max(1, maxRows - 1));
        float scale = Mathf.Lerp(1f, 0.5f, t);

        // Grid uses prefab dimensions — cells scaled via localScale only
        var grid = cellContainer.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        if (grid != null)
        {
            grid.cellSize = new Vector2(prefabW, prefabH);
            grid.spacing = new Vector2(spacingX / scale, spacingY / scale);
            grid.constraintCount = maxCols;
        }

        // Font sizes scale with row count only, no auto-sizing
        float pinBase = cells[0].PinyinLabel != null ? cells[0].PinyinLabel.fontSize : 34f;
        float chrBase = cells[0].CharLabel   != null ? cells[0].CharLabel.fontSize   : 56f;

        foreach (var cell in cells)
        {
            cell.transform.localScale = new Vector3(scale, scale, 1f);

            if (cell.PinyinLabel != null)
            {
                cell.PinyinLabel.enableAutoSizing = true;
                cell.PinyinLabel.fontSizeMax = pinBase * scale;
                cell.PinyinLabel.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            }
            if (cell.CharLabel != null)
            {
                cell.CharLabel.enableAutoSizing = false;
                cell.CharLabel.fontSize = chrBase * scale;
            }
        }

        // English cells: 80% font of Chinese, scaled like Chinese cells
        float engFont = (cells.Count > 0 ? chrBase * scale : 10f) * 0.8f;
        foreach (var ec in englishCells)
        {
            ec.transform.localScale = new Vector3(scale, scale, 1f);
            if (ec.Label != null)
            {
                ec.Label.enableAutoSizing = false;
                ec.Label.fontSize = engFont;
            }
        }

        StartCoroutine(AnimateCellsIn());
    }

    /// <summary>
    /// Pops ALL cells (Chinese + English) in from scale 0 to 1.
    /// </summary>
    private IEnumerator AnimateCellsIn()
    {
        var allTransforms = new List<Transform>();
        foreach (Transform child in cellContainer) allTransforms.Add(child);
        if (allTransforms.Count == 0) yield break;

        // Store target scale, start at 0
        Vector3 targetScale = allTransforms[0] != null ? allTransforms[0].localScale : Vector3.one;
        foreach (var t in allTransforms)
            t.localScale = Vector3.zero;

        var audioSrc = GetComponent<AudioSource>();
        if (audioSrc == null && landingSound != null)
        {
            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false;
        }

        for (int i = 0; i < allTransforms.Count; i++)
        {
            var t = allTransforms[i];
            float elapsed = 0f;
            while (elapsed < 1f)
            {
                if (t == null) break;
                elapsed += Time.deltaTime * transitionSpeed;
                float p = Mathf.Clamp01(elapsed);
                float c1 = 1.70158f;
                float c3 = c1 + 1f;
                float eased = 1f + c3 * Mathf.Pow(p - 1f, 3f) + c1 * Mathf.Pow(p - 1f, 2f);
                t.localScale = targetScale * Mathf.Max(0f, eased);
                yield return null;
            }
            if (t != null) t.localScale = targetScale;

            if (t != null && landingSound != null && audioSrc != null)
            {
                audioSrc.pitch = Random.Range(1f / landingSoundPitchRandomization, landingSoundPitchRandomization);
                audioSrc.PlayOneShot(landingSound, landingSoundVolume);
            }

            if (i < allTransforms.Count - 1)
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
