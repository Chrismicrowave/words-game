using System;
using System.Collections.Generic;
using UnityEngine;

public class PhaseManager : MonoBehaviour
{
    public static PhaseManager Instance { get; private set; }

    private IWordListProvider activeProvider;
    private List<string> words = new List<string>();

    public int CurrentPhaseIndex { get; private set; }
    public string CurrentWord => (CurrentPhaseIndex < words.Count) ? words[CurrentPhaseIndex] : "";
    public int TotalPhases => words.Count;
    public bool HasMorePhases => CurrentPhaseIndex < words.Count - 1;
    public IWordListProvider ActiveProvider => activeProvider;
    public List<string> Words => words;

    public event Action<string> OnPhaseWordChanged;
    public event Action OnWordListChanged;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    public void LoadWordList(IWordListProvider provider)
    {
        activeProvider = provider;
        words = provider.GetWords();
        CurrentPhaseIndex = 0;
        OnWordListChanged?.Invoke();
        OnPhaseWordChanged?.Invoke(CurrentWord);
    }

    public bool AdvancePhase()
    {
        if (CurrentPhaseIndex >= words.Count - 1)
            return false;

        CurrentPhaseIndex++;
        OnPhaseWordChanged?.Invoke(CurrentWord);
        return true;
    }

    public void RestartPhase()
    {
        OnPhaseWordChanged?.Invoke(CurrentWord);
    }

    public void ResetToBeginning()
    {
        CurrentPhaseIndex = 0;
        OnPhaseWordChanged?.Invoke(CurrentWord);
    }

    public void JumpToPhase(int index)
    {
        if (index >= 0 && index < words.Count)
        {
            CurrentPhaseIndex = index;
            OnPhaseWordChanged?.Invoke(CurrentWord);
        }
    }

    public void AddPhase(string word, int index = 0)
    {
        words.Insert(index, word);
        OnWordListChanged?.Invoke();
    }

    public void RemovePhase(int index)
    {
        if (index < 0 || index >= words.Count) return;

        words.RemoveAt(index);
        if (CurrentPhaseIndex >= words.Count)
            CurrentPhaseIndex = Mathf.Max(0, words.Count - 1);
        OnWordListChanged?.Invoke();
    }

    public void MovePhase(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= words.Count) return;
        if (toIndex < 0 || toIndex >= words.Count) return;

        string word = words[fromIndex];
        words.RemoveAt(fromIndex);
        words.Insert(toIndex, word);
        OnWordListChanged?.Invoke();
    }

    public void SaveCurrentList()
    {
        if (activeProvider is FileWordListProvider fileProvider)
        {
            fileProvider.SetWords(words);
            fileProvider.Save();
        }
    }
}
