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
    [SerializeField] private Vector3 offsetStartPosition = new Vector3(50f, 100f, 0f);
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
    /// Disables layout and sets cells to their start offset — call after building.
    /// Then PlayEntryAnimation() starts the fly-in coroutine once the GO is active.
    /// </summary>
    private void PrepareEntryAnimation()
    {
        if (cells.Count == 0) return;

        // Disable layout so we can move cells freely
        var layout = cellContainer.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        if (layout != null) layout.enabled = false;
    }

    /// <summary>
    /// Call after the GameObject is active. Animates each TargetCell flying in
    /// from offset position with fade-in and landing sound, matching CurText.
    /// </summary>
    public void PlayEntryAnimation()
    {
        if (cells.Count == 0 || !gameObject.activeInHierarchy) return;
        StartCoroutine(AnimateCellsIn());
    }

    /// <summary>
    /// Animates each Chinese TargetCell flying in from an offset position,
    /// with fade-in and landing sound — matching CurText's letter animation.
    /// </summary>
    private IEnumerator AnimateCellsIn()
    {
        yield return null; // wait one frame so layout positions are final

        // Store final positions; set start positions to offset
        var finalPositions = new Vector3[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            var t = cells[i].transform;
            finalPositions[i] = t.localPosition;
            t.localPosition = finalPositions[i] + offsetStartPosition;
        }

        // Ensure we have an audio source
        var audioSrc = GetComponent<AudioSource>();
        if (audioSrc == null && landingSound != null)
        {
            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false;
        }

        // Animate one by one (same as CurText: move + fade in + landing sound)
        for (int i = 0; i < cells.Count; i++)
        {
            var t = cells[i].transform;
            float elapsed = 0f;
            while (elapsed < 1f)
            {
                elapsed += Time.deltaTime * transitionSpeed;
                float progress = Mathf.Clamp01(elapsed);

                t.localPosition = Vector3.Lerp(finalPositions[i] + offsetStartPosition, finalPositions[i], progress);

                // Fade in the cell contents (pinyin + char label alpha)
                var canvasGroup = t.GetComponent<UnityEngine.CanvasGroup>();
                if (canvasGroup == null)
                {
                    // Add one dynamically for fade control
                    canvasGroup = t.gameObject.AddComponent<UnityEngine.CanvasGroup>();
                }
                canvasGroup.alpha = progress;

                yield return null;
            }
            t.localPosition = finalPositions[i];

            // Ensure fully opaque
            var cg = t.GetComponent<UnityEngine.CanvasGroup>();
            if (cg != null) cg.alpha = 1f;

            // Landing sound (same pitch randomization as CurText)
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
        if (cells.Count == 0) return;
        float min = float.MaxValue;
        foreach (var c in cells)
            min = Mathf.Min(min, c.PinyinFontSize);
        foreach (var c in cells)
            c.SetPinyinFontSize(min);
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
