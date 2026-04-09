using System;
using System.Collections.Generic;
using UnityEngine;

public class PhaseManager : SingletonBehaviour<PhaseManager>
{
    private IWordListProvider activeProvider;
    private List<string> words = new List<string>();
    private List<ChineseWordEntry> chineseWords = new List<ChineseWordEntry>();

    public int CurrentPhaseIndex { get; private set; }
    public string CurrentWord => (CurrentPhaseIndex < words.Count) ? words[CurrentPhaseIndex] : "";
    public int TotalPhases => words.Count;
    public bool HasMorePhases => CurrentPhaseIndex < words.Count - 1;
    public IWordListProvider ActiveProvider => activeProvider;
    public IReadOnlyList<string> Words => words;
    public LanguageMode CurrentLanguageMode => activeProvider?.LanguageMode ?? LanguageMode.English;

    public event Action<string> OnPhaseWordChanged;
    public event Action OnWordListChanged;

    public void LoadWordList(IWordListProvider provider)
    {
        activeProvider = provider;
        words = provider.GetWords();
        chineseWords = provider.GetChineseWords() ?? new List<ChineseWordEntry>();

        // For Chinese lists, populate words from display strings so TotalPhases is correct
        if (provider.LanguageMode == LanguageMode.Chinese && chineseWords.Count > 0)
        {
            words = new List<string>();
            foreach (var cw in chineseWords)
                words.Add(cw.display);
        }

        CurrentPhaseIndex = 0;
        OnWordListChanged?.Invoke();
        OnPhaseWordChanged?.Invoke(CurrentWord);
    }

    // Returns the ChineseWordEntry for the given index (null if not a Chinese list or out of range)
    public ChineseWordEntry GetChineseWord(int index)
    {
        if (CurrentLanguageMode != LanguageMode.Chinese) return null;
        if (index < 0 || index >= chineseWords.Count) return null;
        return chineseWords[index];
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
