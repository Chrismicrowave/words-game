using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Displays the matched progress of a Chinese or Mixed phase.
/// Chinese segments: each character gets a CharacterCell in a dynamic GridLayoutGroup,
/// matching the multi-row grid layout of the target display.
/// English segments: plain TMP label, letters revealed one-by-one.
/// </summary>
public class ChineseMatchedDisplay : MonoBehaviour
{
    [SerializeField] private GameObject characterCellPrefab;
    [SerializeField] private GameObject englishCellPrefab;
    [SerializeField] private Transform cellContainer;
    [SerializeField] private TMPro.TMP_FontAsset chineseFontAsset; // NotoSansSC — for non-ASCII English segments

    [Header("Grid")]
    [SerializeField] private int maxRows = 3;
    [SerializeField] private float containerWidth = 1200f;
    [SerializeField] private float spacingX = 20f;
    [SerializeField] private float spacingY = 0f;
    [Header("Entry Animation")]
    [SerializeField] private float transitionSpeed = 20f;
    [SerializeField] private float delayBetweenCells = 0.03f;
    [SerializeField] private AudioClip landingSound;
    [Range(0, 1)] [SerializeField] private float landingSoundVolume = 0.4f;
    [Range(0.5f, 2f)] [SerializeField] private float landingSoundPitchRandomization = 1.1f;

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
        int count = Mathf.Min(data.characters.Length, 48);
        for (int i = 0; i < count; i++)
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
        int total = 0;
        foreach (var seg in parsed.segments)
        {
            if (seg.type == MixedPhaseParser.SegmentType.Chinese)
            {
                for (int i = 0; i < seg.characters.Length && total < 48; i++)
                {
                    GameObject go = Instantiate(characterCellPrefab, cellContainer);
                    var cell = go.GetComponent<CharacterCell>();
                    if (cell != null)
                    {
                        int prevBoundary = i == 0 ? seg.typeStart : seg.boundaries[i - 1];
                        cell.Init(seg.characters[i], parsed.typeTarget, prevBoundary, seg.boundaries[i]);
                        cells.Add(cell);
                        total++;
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
                }
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
            }
            el.label.text = new string(chars);
        }
    }

    // ── Grid sizing & entry animation (matches ChineseTargetDisplay) ──────────

    /// <summary>
    /// Call after the GameObject is active. Configures GridLayoutGroup dynamically
    /// to match the target display's multi-row grid, then animates cells popping in.
    /// </summary>
    public void PlayEntryAnimation()
    {
        // Count all children (cells + English labels) in container
        int totalCells = cellContainer.childCount;
        if (totalCells == 0 || !gameObject.activeInHierarchy) return;

        // Prefab natural size — never overridden by grid
        float prefabW = cells[0].GetComponent<RectTransform>().rect.width;
        float prefabH = cells[0].GetComponent<RectTransform>().rect.height;
        if (prefabW < 30f) prefabW = 45f;
        if (prefabH < 30f) prefabH = 60f;

        // Find maxCols/rows that fit cells in container within maxRows
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

        // Cell dimensions that fit container exactly
        float cellW = maxCols > 0 ? (containerWidth - (maxCols - 1) * spacingX) / maxCols : 1f;
        float cellH = cellW;
        float t = Mathf.Clamp01((float)(rows - 1) / Mathf.Max(1, maxRows - 1));
        float scale = Mathf.Lerp(1f, 0.5f, t);

        // Grid cellSize = actual display size
        var grid = cellContainer.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        if (grid != null)
        {
            grid.cellSize = new Vector2(cellW, cellH);
            grid.spacing = new Vector2(spacingX, spacingY);
            grid.constraintCount = maxCols;
        }

        // Font sizes scale with row count, no auto-sizing
        float pinBase = cells[0].LetterLabel != null ? cells[0].LetterLabel.fontSize : 18f;
        float chrBase = cells[0].CharLabel   != null ? cells[0].CharLabel.fontSize   : 32f;

        foreach (var cell in cells)
        {
            if (cell.LetterLabel != null)
            {
                cell.LetterLabel.enableAutoSizing = false;
                cell.LetterLabel.fontSize = pinBase * scale;
                cell.LetterLabel.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            }
            if (cell.CharLabel != null)
            {
                cell.CharLabel.enableAutoSizing = false;
                cell.CharLabel.fontSize = chrBase * scale;
            }
        }

        // English cells: 80% font of Chinese
        float engFont = (cells.Count > 0 ? chrBase * scale : 10f) * 0.8f;
        foreach (Transform child in cellContainer)
        {
            var eng = child.GetComponent<EnglishCell>();
            if (eng == null || eng.Label == null) continue;
            eng.Label.enableAutoSizing = false;
            eng.Label.fontSize = engFont;
        }

        StartCoroutine(AnimateCellsIn());
    }

    private IEnumerator AnimateCellsIn()
    {
        var allTransforms = new List<Transform>();
        foreach (Transform child in cellContainer) allTransforms.Add(child);
        if (allTransforms.Count == 0) yield break;

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

    public void Clear()
    {
        StopAllCoroutines();
        foreach (Transform child in cellContainer)
            Destroy(child.gameObject);
        cells.Clear();
        englishLabels.Clear();
    }

}
