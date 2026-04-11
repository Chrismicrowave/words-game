using System;
using System.Collections.Generic;
using UnityEngine;

public class PhaseManager : SingletonBehaviour<PhaseManager>
{
    private IWordListProvider activeProvider;
    private List<string> words = new List<string>();
    private List<ChineseWordEntry> chineseWords = new List<ChineseWordEntry>();
    private List<MixedWordEntry> mixedWords = new List<MixedWordEntry>();

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
        mixedWords = provider.GetMixedWords() ?? new List<MixedWordEntry>();

        // For Chinese/Mixed lists, populate words from display strings so TotalPhases is correct
        if (provider.LanguageMode == LanguageMode.Chinese && chineseWords.Count > 0)
        {
            words = new List<string>();
            foreach (var cw in chineseWords)
                words.Add(cw.display);
        }
        else if (provider.LanguageMode == LanguageMode.Mixed && mixedWords.Count > 0)
        {
            words = new List<string>();
            foreach (var mw in mixedWords)
            {
                // Build a display string from segments for the phase list
                var sb = new System.Text.StringBuilder();
                foreach (var seg in mw.segments)
                {
                    if (seg.type == "english") sb.Append(seg.text);
                    else if (seg.entries != null)
                        foreach (var e in seg.entries) sb.Append(e.character);
                }
                words.Add(sb.ToString());
            }
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

    // Returns the MixedWordEntry for the given index (null if not a Mixed list or out of range)
    public MixedWordEntry GetMixedWord(int index)
    {
        if (CurrentLanguageMode != LanguageMode.Mixed) return null;
        if (index < 0 || index >= mixedWords.Count) return null;
        return mixedWords[index];
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

    /// <summary>
    /// Adds a mixed/Chinese phase at the front of the list.
    /// If the current list is English-only, converts it to Mixed mode by wrapping
    /// existing entries as single-segment English MixedWordEntry objects.
    /// </summary>
    public void AddMixedPhase(MixedWordEntry entry)
    {
        // Build display string for the phase list
        var sb = new System.Text.StringBuilder();
        foreach (var seg in entry.segments)
        {
            if (seg.type == "english") sb.Append(seg.text);
            else if (seg.entries != null)
                foreach (var e in seg.entries) sb.Append(e.character);
        }
        string display = sb.ToString();

        // If not already Mixed, convert existing words to Mixed entries
        if (CurrentLanguageMode != LanguageMode.Mixed)
        {
            mixedWords = new List<MixedWordEntry>();
            foreach (var w in words)
            {
                mixedWords.Add(new MixedWordEntry
                {
                    segments = new List<MixedSegmentData>
                    {
                        new MixedSegmentData { type = "english", text = w }
                    }
                });
            }
            if (activeProvider is FileWordListProvider fp)
                fp.SetLanguageMode(LanguageMode.Mixed);
        }

        mixedWords.Insert(0, entry);
        words.Insert(0, display);
        OnWordListChanged?.Invoke();
    }

    public void RemovePhase(int index)
    {
        if (index < 0 || index >= words.Count) return;

        words.RemoveAt(index);
        // Keep mixedWords/chineseWords in sync so SaveCurrentList doesn't restore the deleted entry
        if (index < mixedWords.Count)  mixedWords.RemoveAt(index);
        if (index < chineseWords.Count) chineseWords.RemoveAt(index);

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

        // Keep mixedWords/chineseWords in sync so the move persists after save/reload
        if (fromIndex < mixedWords.Count && toIndex <= mixedWords.Count)
        {
            var mw = mixedWords[fromIndex];
            mixedWords.RemoveAt(fromIndex);
            mixedWords.Insert(toIndex, mw);
        }
        if (fromIndex < chineseWords.Count && toIndex <= chineseWords.Count)
        {
            var cw = chineseWords[fromIndex];
            chineseWords.RemoveAt(fromIndex);
            chineseWords.Insert(toIndex, cw);
        }

        OnWordListChanged?.Invoke();
    }

    public void SaveCurrentList()
    {
        if (activeProvider is FileWordListProvider fileProvider)
        {
            fileProvider.SetWords(words);
            if (CurrentLanguageMode == LanguageMode.Mixed)
                fileProvider.SetMixedWords(mixedWords);
            fileProvider.Save();
        }
    }
}
