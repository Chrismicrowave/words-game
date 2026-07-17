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
    [SerializeField] private float spacing = 20f;

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

        // Prefab natural size — never overridden by grid
        float prefabW = cells[0].GetComponent<RectTransform>().rect.width;
        float prefabH = cells[0].GetComponent<RectTransform>().rect.height;
        if (prefabW < 60f) prefabW = 100f;
        if (prefabH < 60f) prefabH = 200f;

        // Find the size scale that fits all cells within maxRows
        // Start at 100%, shrink until rows <= maxRows
        float scale = 1f;
        int maxCols, rows;
        do
        {
            float scaledW = prefabW * scale;
            maxCols = Mathf.FloorToInt((containerWidth + spacing) / (scaledW + spacing));
            if (maxCols < 1) maxCols = 1;
            rows = Mathf.CeilToInt((float)cells.Count / maxCols);
            if (maxRows > 0 && rows > maxRows)
                scale *= 0.95f; // shrink slightly and retry
            else
                break;
        } while (scale > 0.3f);

        // Width/height scale per row: 100% → 75% → 50%
        float t = Mathf.Clamp01((float)(rows - 1) / Mathf.Max(1, maxRows - 1));
        scale = Mathf.Lerp(1f, 0.5f, t);

        // Grid cellSize = actual display size
        float cellW = prefabW * scale;
        float cellH = prefabH * scale;
        var grid = cellContainer.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        if (grid != null)
        {
            grid.cellSize = new Vector2(cellW, cellH);
            grid.spacing = new Vector2(spacing, spacing);
            grid.constraintCount = maxCols;
        }

        // Uniform sizing — disable VLG, fixed layout, pinyin overflows if too long
        float pinH = cellH * 0.4f;
        float chrH = cellH * 0.6f;
        float pinMaxW = cellW * 1.2f;

        foreach (var cell in cells)
        {
            var vlg = cell.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (vlg != null)
                vlg.enabled = false;

            if (cell.PinyinLabel != null)
            {
                var rt = cell.PinyinLabel.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(pinMaxW, pinH);
                rt.anchoredPosition = new Vector2(0, cellH * 0.3f);
                cell.PinyinLabel.enableAutoSizing = false;
                cell.PinyinLabel.fontSize = pinH * 0.8f;
                cell.PinyinLabel.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                cell.PinyinLabel.overflowMode = TMPro.TextOverflowModes.Overflow;
            }
            if (cell.CharLabel != null)
            {
                var rt = cell.CharLabel.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(cellW, chrH);
                rt.anchoredPosition = new Vector2(0, cellH * -0.25f);
                cell.CharLabel.enableAutoSizing = false;
                cell.CharLabel.fontSize = chrH * 0.8f;
            }
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

                // EaseOutBack: overshoot then settle
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
