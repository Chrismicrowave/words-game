using System;
using System.Collections.Generic;
using UnityEngine;

public class PhaseManager : SingletonBehaviour<PhaseManager>
{
    [SerializeField] private GameConfig config;
    [SerializeField, Tooltip("Allow Chinese and mixed Chinese-English gameplay content. This does not affect the UI language.")]
    private bool allowChineseAndMixed = true;

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
    public bool AllowChineseAndMixed => config != null && config.activeProfile != null
        ? config.activeProfile.allowChineseAndMixed
        : allowChineseAndMixed;
    public GameConfigProfile ActiveProfile => config != null ? config.activeProfile : null;
    public bool CanAddWords => config == null || config.activeProfile == null
        || config.activeProfile.IsFeatureAvailable(config.activeProfile.unlockAddWords);
    public bool CanEditLists => config == null || config.activeProfile == null
        || config.activeProfile.IsFeatureAvailable(config.activeProfile.unlockEditLists);
    public bool HasReachedWordLimit => config != null && config.activeProfile != null
        && config.activeProfile.isDemo && !config.activeProfile.unlockAllFeatures
        && config.activeProfile.maxWordsPerList > 0
        && words.Count >= config.activeProfile.maxWordsPerList;
    public LevelMode CurrentLevelMode { get; set; } = LevelMode.Challenge;

    /// <summary>Total failed attempts during the current game session.</summary>
    public int TotalErrors { get; private set; }

    public void RecordFailure() => TotalErrors++;
    public void ResetErrors() => TotalErrors = 0;

    public event Action<string> OnPhaseWordChanged;
    public event Action OnWordListChanged;

    /// <summary>Returns false without changing the active list when Chinese content is disabled.</summary>
    public bool LoadWordList(IWordListProvider provider)
    {
        if (!CanLoadWordList(provider))
        {
            Debug.LogWarning("[PhaseManager] Chinese and mixed word lists are disabled; keeping the current list.");
            return false;
        }

        activeProvider = provider;
        LanguageMode mode = provider.LanguageMode;
        words = provider.GetWords();
        chineseWords = provider.GetChineseWords() ?? new List<ChineseWordEntry>();
        mixedWords = provider.GetMixedWords() ?? new List<MixedWordEntry>();

        ResetErrors(); // fresh session, clear failure count

        // For Chinese/Mixed lists, populate words from display strings so TotalPhases is correct
        if (mode == LanguageMode.Chinese && chineseWords.Count > 0)
        {
            words = new List<string>();
            foreach (var cw in chineseWords)
                words.Add(cw.display);
        }
        else if (mode == LanguageMode.Mixed && mixedWords.Count > 0)
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

        // Filter out over-long words
        if (mode == LanguageMode.English)
        {
            words = DisplayWidthUtil.FilterWords(words, LanguageMode.English);
        }
        else if (mode == LanguageMode.Mixed)
        {
            if (mixedWords.Count > 0)
            {
                var filteredWords = new List<string>();
                var filteredMixed = new List<MixedWordEntry>();
                for (int i = 0; i < words.Count && i < mixedWords.Count; i++)
                {
                    if (!DisplayWidthUtil.IsOverLimit(words[i], LanguageMode.Mixed))
                    {
                        filteredWords.Add(words[i]);
                        filteredMixed.Add(mixedWords[i]);
                    }
                }
                words = filteredWords;
                mixedWords = filteredMixed;
            }
            else
            {
                words = DisplayWidthUtil.FilterWords(words, LanguageMode.Mixed);
            }
        }
        else if (mode == LanguageMode.Chinese)
        {
            if (chineseWords.Count > 0)
            {
                var filteredWords = new List<string>();
                var filteredChinese = new List<ChineseWordEntry>();
                for (int i = 0; i < words.Count && i < chineseWords.Count; i++)
                {
                    if (!DisplayWidthUtil.IsOverLimit(words[i], LanguageMode.Chinese))
                    {
                        filteredWords.Add(words[i]);
                        filteredChinese.Add(chineseWords[i]);
                    }
                }
                words = filteredWords;
                chineseWords = filteredChinese;
            }
            else
            {
                words = DisplayWidthUtil.FilterWords(words, LanguageMode.Chinese);
            }
        }

        CurrentPhaseIndex = 0;
        OnWordListChanged?.Invoke();
        OnPhaseWordChanged?.Invoke(CurrentWord);
        return true;
    }

    /// <summary>Checks provider content rather than its declared mode so English-only mixed data remains usable.</summary>
    public bool CanLoadWordList(IWordListProvider provider)
    {
        if (provider == null) return false;
        return AllowChineseAndMixed || !ProviderContainsHan(provider);
    }

    /// <summary>Detects Han characters including supplementary and compatibility ideographs.</summary>
    public static bool ContainsHanCharacters(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        for (int i = 0; i < text.Length; i++)
        {
            int codePoint = text[i];
            if (char.IsSurrogate(text[i]))
            {
                if (!char.IsHighSurrogate(text[i]) || i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    return true; // Conservatively reject malformed/split supplementary characters.

                codePoint = char.ConvertToUtf32(text, i);
                i++;
            }

            if (IsHanCodePoint(codePoint)) return true;
        }

        return false;
    }

    private static bool IsHanCodePoint(int codePoint)
    {
        return (codePoint >= 0x3400 && codePoint <= 0x4DBF)   // CJK Unified Ideographs Extension A
            || (codePoint >= 0x4E00 && codePoint <= 0x9FFF)   // CJK Unified Ideographs
            || (codePoint >= 0xF900 && codePoint <= 0xFAFF)   // CJK Compatibility Ideographs
            || (codePoint >= 0x20000 && codePoint <= 0x2A6DF) // Extension B
            || (codePoint >= 0x2A700 && codePoint <= 0x2B73F) // Extension C
            || (codePoint >= 0x2B740 && codePoint <= 0x2B81F) // Extension D
            || (codePoint >= 0x2B820 && codePoint <= 0x2CEAF) // Extension E
            || (codePoint >= 0x2CEB0 && codePoint <= 0x2EBEF) // Extension F
            || (codePoint >= 0x2EBF0 && codePoint <= 0x2EE5F) // Extension I
            || (codePoint >= 0x2F800 && codePoint <= 0x2FA1F) // Compatibility Ideographs Supplement
            || (codePoint >= 0x30000 && codePoint <= 0x3134F) // Extension G
            || (codePoint >= 0x31350 && codePoint <= 0x323AF); // Extension H
    }

    private static bool ProviderContainsHan(IWordListProvider provider)
    {
        if (provider == null) return false;

        var providerWords = provider.GetWords();
        if (ContainsHanCharacters(providerWords)) return true;

        var chineseEntries = provider.GetChineseWords();
        if (ChineseWordsContainHan(chineseEntries)) return true;

        var mixedEntries = provider.GetMixedWords();
        if (mixedEntries != null)
        {
            foreach (var word in mixedEntries)
                if (MixedEntryContainsHan(word)) return true;
        }

        return false;
    }

    private static bool ContainsHanCharacters(List<string> values)
    {
        if (values == null) return false;
        foreach (var value in values)
            if (ContainsHanCharacters(value)) return true;
        return false;
    }

    private static bool ChineseEntriesContainHan(List<ChinesePhaseEntry> entries)
    {
        if (entries == null) return false;
        foreach (var entry in entries)
            if (entry != null && ContainsHanCharacters(entry.character)) return true;
        return false;
    }

    private static bool ChineseWordsContainHan(List<ChineseWordEntry> entries)
    {
        if (entries == null) return false;
        foreach (var word in entries)
        {
            if (word != null &&
                (ContainsHanCharacters(word.display) || ChineseEntriesContainHan(word.entries)))
                return true;
        }
        return false;
    }

    private static bool MixedEntryContainsHan(MixedWordEntry word)
    {
        if (word?.segments == null) return false;
        foreach (var segment in word.segments)
        {
            if (segment == null) continue;
            if (ContainsHanCharacters(segment.text) || ChineseEntriesContainHan(segment.entries)) return true;
        }
        return false;
    }

    private bool CurrentListContainsHan()
    {
        return ProviderContainsHan(activeProvider)
            || ContainsHanCharacters(words)
            || ChineseWordsContainHan(chineseWords)
            || (mixedWords != null && mixedWords.Exists(MixedEntryContainsHan));
    }

    private bool CanModifyCurrentList(string addedText = null)
    {
        return AllowChineseAndMixed
            || (!CurrentListContainsHan() && !ContainsHanCharacters(addedText));
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

    public bool AddPhase(string word, int index = 0)
    {
        if (!CanAddWords || HasReachedWordLimit || !CanModifyCurrentList(word)) return false;

        words.Insert(index, word);
        // In Mixed mode, keep mixedWords in sync so the entry persists after save/reload
        if (CurrentLanguageMode == LanguageMode.Mixed)
        {
            mixedWords.Insert(index, new MixedWordEntry
            {
                segments = new List<MixedSegmentData>
                {
                    new MixedSegmentData { type = "english", text = word }
                }
            });
        }
        OnWordListChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Adds a mixed/Chinese phase at the front of the list.
    /// If the current list is English-only, converts it to Mixed mode by wrapping
    /// existing entries as single-segment English MixedWordEntry objects.
    /// </summary>
    public bool AddMixedPhase(MixedWordEntry entry)
    {
        if (entry == null || !CanAddWords || HasReachedWordLimit || !CanModifyCurrentList()
            || (!AllowChineseAndMixed && MixedEntryContainsHan(entry)))
            return false;

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
            else if (activeProvider is LevelWordListProvider lvlp)
                lvlp.SetLanguageMode(LanguageMode.Mixed);
        }

        mixedWords.Insert(0, entry);
        words.Insert(0, display);
        OnWordListChanged?.Invoke();
        return true;
    }

    public void RemovePhase(int index)
    {
        if (!CanEditLists || !CanModifyCurrentList()) return;
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
        if (!CanEditLists || !CanModifyCurrentList()) return;
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
        if (!AllowChineseAndMixed && CurrentListContainsHan())
        {
            Debug.LogWarning("[PhaseManager] Chinese content is disabled; the current Chinese list was not saved.");
            return;
        }

        if (activeProvider is LevelWordListProvider lvlProvider)
        {
            lvlProvider.SetWords(words);
            if (CurrentLanguageMode == LanguageMode.Mixed)
                lvlProvider.SetMixedWords(mixedWords);
            lvlProvider.Save();
        }
        else if (activeProvider is FileWordListProvider fileProvider)
        {
            fileProvider.SetWords(words);
            if (CurrentLanguageMode == LanguageMode.Mixed)
                fileProvider.SetMixedWords(mixedWords);
            fileProvider.Save();
        }
    }
}
