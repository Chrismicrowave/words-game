using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "FixedWordList", menuName = "Words/Fixed Word List")]
public class FixedWordListProvider : ScriptableObject, IWordListProvider
{
    [SerializeField] private string listName = "Demo List";
    [SerializeField] private List<string> words = new List<string> { "No Food" };

    public string DisplayName => listName;
    public bool IsEditable => false;

    public List<string> GetWords()
    {
        return new List<string>(words);
    }
}
