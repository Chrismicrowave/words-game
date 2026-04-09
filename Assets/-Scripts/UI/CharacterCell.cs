using UnityEngine;
using TMPro;

/// <summary>
/// A single cell in the matched display for a Chinese phase.
/// Shows typed pinyin letters while the syllable is incomplete, then snaps to the Chinese character.
/// </summary>
public class CharacterCell : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI letterLabel;   // shows e.g. "ni" while typing
    [SerializeField] private TextMeshProUGUI charLabel;     // shows e.g. "你" when complete

    private string character;
    private string fullTypeTarget;  // the full pinyin typeTarget for the whole word
    private int prevBoundary;       // index of first letter belonging to this syllable
    private int boundary;           // index (exclusive) where this character completes

    public void Init(string chineseChar, string typeTarget, int prevBoundaryIdx, int boundaryIdx)
    {
        character = chineseChar;
        fullTypeTarget = typeTarget;
        prevBoundary = prevBoundaryIdx;
        boundary = boundaryIdx;

        if (charLabel != null) charLabel.text = chineseChar;
        UpdateState(0);
    }

    /// <summary>
    /// Update visual state based on how many pinyin letters have been typed correctly.
    /// </summary>
    public void UpdateState(int typedCount)
    {
        bool complete = typedCount >= boundary;

        if (charLabel != null) charLabel.gameObject.SetActive(complete);

        if (letterLabel != null)
        {
            letterLabel.gameObject.SetActive(!complete);
            if (!complete)
            {
                // Show letters typed so far within this syllable
                int start = prevBoundary;
                int end = Mathf.Min(typedCount, boundary);
                letterLabel.text = end > start
                    ? fullTypeTarget.Substring(start, end - start)
                    : "";
            }
        }
    }
}
