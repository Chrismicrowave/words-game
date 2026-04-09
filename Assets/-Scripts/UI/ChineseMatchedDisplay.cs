using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Displays the matched progress of a Chinese phase.
/// Each character gets a CharacterCell: letters shown while typing, Chinese char snaps in on syllable completion.
/// </summary>
public class ChineseMatchedDisplay : MonoBehaviour
{
    [SerializeField] private GameObject characterCellPrefab;
    [SerializeField] private Transform cellContainer;

    private readonly List<CharacterCell> cells = new List<CharacterCell>();
    private ChinesePhaseData currentData;

    public void BuildCells(ChinesePhaseData data)
    {
        Clear();
        currentData = data;

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

    /// <summary>
    /// Call each time the typed letter count changes.
    /// </summary>
    public void UpdateProgress(int typedLetterCount)
    {
        for (int i = 0; i < cells.Count; i++)
            cells[i].UpdateState(typedLetterCount);
    }

    public void Clear()
    {
        foreach (Transform child in cellContainer)
            Destroy(child.gameObject);
        cells.Clear();
    }
}
