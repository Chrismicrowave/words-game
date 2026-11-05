using System;
using System.Collections.Generic;
using UnityEngine;

public class PhaseManager : SingletonBehaviour<PhaseManager>
{
    [SerializeField] private GameConfig config;
    [SerializeField, Tooltip("Allow Chinese and mixed Chinese-English gameplay content. This does not affect the UI language.")]
    private bool allowChineseAndMixed = true;

    private IWordListProvider activeProvider;
    private LanguageMode activeLanguageMode = LanguageMode.English;
    private WordListLibraryService wordListLibrary;
    private bool subscribedToLibrary;
    private bool committingLocalMutation;
    private List<string> words = new List<string>();
    private List<ChineseWordEntry> chineseWords = new List<ChineseWordEntry>();
    private List<MixedWordEntry> mixedWords = new List<MixedWordEntry>();

    public int CurrentPhaseIndex { get; private set; }
    public string CurrentWord => (CurrentPhaseIndex < words.Count) ? words[CurrentPhaseIndex] : "";
    public int TotalPhases => words.Count;
    public bool HasMorePhases => CurrentPhaseIndex < words.Count - 1;
    public IWordListProvider ActiveProvider => activeProvider;
    public IReadOnlyList<string> Words => words;
    public LanguageMode CurrentLanguageMode => activeProvider != null ? activeLanguageMode : LanguageMode.English;
    public bool AllowChineseAndMixed => config != null && config.activeProfile != null
        ? config.activeProfile.allowChineseAndMixed
        : allowChineseAndMixed;
    public GameConfigProfile ActiveProfile => config != null ? config.activeProfile : null;
    public string PreferredProgressTrackPrefix => ChallengeTrackResolver.ResolvePreferredTrackPrefix(
        AllowChineseAndMixed, GetSelectedLocaleCode());
    public int CompletedChallengesForProfile =>
        ChallengeProgression.GetCompletedChallengesForTrack(PreferredProgressTrackPrefix);
    public bool IsCustomUnlockedForProfile => ActiveProfile != null
        && ActiveProfile.CanOpenCustomLists(CompletedChallengesForProfile);
    public bool CanAddWords => IsCustomUnlockedForProfile;
    public bool CanEditLists => IsCustomUnlockedForProfile;
    public bool HasReachedWordLimit => ActiveProfile != null
        && ActiveProfile.maxWordsPerList > 0
        && words.Count >= ActiveProfile.maxWordsPerList;
    public LevelMode CurrentLevelMode { get; set; } = LevelMode.Challenge;

    private static string GetSelectedLocaleCode()
    {
        try
        {
            var locale = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale;
            return locale != null ? locale.Identifier.Code : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Total failed attempts during the current game session.</summary>
    public int TotalErrors { get; private set; }

    public void RecordFailure() => TotalErrors++;
    public void ResetErrors() => TotalErrors = 0;

    public event Action<string> OnPhaseWordChanged;
    public event Action OnWordListChanged;

    protected override void Awake()
    {
        base.Awake();
    }

    internal void SetWordListLibraryForTests(WordListLibraryService service)
    {
        if (subscribedToLibrary && wordListLibrary != null)
            wordListLibrary.ContentChanged -= HandleLibraryContentChanged;
        wordListLibrary = service;
        subscribedToLibrary = false;
    }

    private void EnsureWordListLibrary()
    {
        if (wordListLibrary == null)
            wordListLibrary = WordListLibraryService.Shared;
        if (subscribedToLibrary) return;
        wordListLibrary.ContentChanged += HandleLibraryContentChanged;
        subscribedToLibrary = true;
    }

    private void OnDestroy()
    {
        if (wordListLibrary != null)
        {
            if (subscribedToLibrary)
                wordListLibrary.ContentChanged -= HandleLibraryContentChanged;
            if (activeProvider is LevelWordListProvider levelProvider)
                wordListLibrary.ClearActiveProvider(levelProvider);
        }
    }

    /// <summary>Returns false without changing the active list when Chinese content is disabled.</summary>
    public bool LoadWordList(IWordListProvider provider)
    {
        if (!CanLoadWordList(provider))
        {
            Debug.LogWarning("[PhaseManager] Chinese and mixed word lists are disabled; keeping the current list.");
            return false;
        }

        if (provider is LevelWordListProvider || activeProvider is LevelWordListProvider)
            EnsureWordListLibrary();

        if (wordListLibrary != null && activeProvider is LevelWordListProvider previousProvider
            && !ReferenceEquals(activeProvider, provider))
            wordListLibrary.ClearActiveProvider(previousProvider);

        activeProvider = provider;
        LanguageMode mode = provider.LanguageMode;
        activeLanguageMode = mode;
        words = provider.GetWords();
        chineseWords = provider.GetChineseWords() ?? new List<ChineseWordEntry>();
        mixedWords = provider.GetMixedWords() ?? new List<MixedWordEntry>();

        if (wordListLibrary != null && provider is LevelWordListProvider levelProvider)
            wordListLibrary.SetActiveProvider(levelProvider);

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
        return activeProvider != null && activeProvider.IsEditable
            && (AllowChineseAndMixed
                || (!CurrentListContainsHan() && !ContainsHanCharacters(addedText)));
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

        WordListLibraryContent before = CaptureCurrentContent();
        index = Mathf.Clamp(index, 0, words.Count);
        LanguageMode languageBeforeMutation = CurrentLanguageMode;
        words.Insert(index, word);
        // Keep the current typed source rows intact until auto-normalization has matched
        // their display text and pinyin. The new row is English unless already Mixed.
        if (languageBeforeMutation == LanguageMode.Chinese)
            chineseWords.Insert(index, null);
        else if (languageBeforeMutation == LanguageMode.Mixed)
        {
            mixedWords.Insert(index, new MixedWordEntry
            {
                segments = new List<MixedSegmentData>
                {
                    new MixedSegmentData { type = "english", text = word }
                }
            });
        }
        return CommitCurrentMutation(before);
    }

    /// <summary>
    /// Adds a mixed/Chinese phase at the front of the list.
    /// If the current list is English-only, converts it to Mixed mode by wrapping
    /// existing entries as single-segment English MixedWordEntry objects.
    /// </summary>
    public bool AddMixedPhase(MixedWordEntry entry)
    {
        if (entry == null || entry.segments == null || !CanAddWords || HasReachedWordLimit || !CanModifyCurrentList()
            || (!AllowChineseAndMixed && MixedEntryContainsHan(entry)))
            return false;

        WordListLibraryContent before = CaptureCurrentContent();

        // Build display string for the phase list
        var sb = new System.Text.StringBuilder();
        foreach (var seg in entry.segments)
        {
            if (seg == null) return false;
            if (seg.type == "english") sb.Append(seg.text);
            else if (seg.entries != null)
                foreach (var e in seg.entries) sb.Append(e.character);
        }
        string display = sb.ToString();

        // Keep typed Chinese rows available as a separate exact-display source while
        // normalization combines them with this new typed Mixed row.
        if (CurrentLanguageMode == LanguageMode.Chinese)
        {
            mixedWords = new List<MixedWordEntry>();
            for (int i = 0; i < words.Count; i++) mixedWords.Add(null);
            chineseWords.Insert(0, null);
        }
        else if (CurrentLanguageMode == LanguageMode.English)
            ConvertCurrentWordsToMixed();

        mixedWords.Insert(0, LevelWordListProvider.CloneMixedWords(new List<MixedWordEntry> { entry })[0]);
        words.Insert(0, display);
        return CommitCurrentMutation(before);
    }

    public bool RemovePhase(int index)
    {
        if (!CanEditLists || !CanModifyCurrentList()) return false;
        if (index < 0 || index >= words.Count) return false;

        WordListLibraryContent before = CaptureCurrentContent();

        words.RemoveAt(index);
        // Keep mixedWords/chineseWords in sync so SaveCurrentList doesn't restore the deleted entry
        if (index < mixedWords.Count)  mixedWords.RemoveAt(index);
        if (index < chineseWords.Count) chineseWords.RemoveAt(index);

        if (!CommitCurrentMutation(before))
        {
            Debug.LogWarning("[PhaseManager] The word-list removal could not be saved; the original content was restored.");
            return false;
        }
        return true;
    }

    public bool MovePhase(int fromIndex, int toIndex)
    {
        if (!CanEditLists || !CanModifyCurrentList()) return false;
        if (fromIndex < 0 || fromIndex >= words.Count) return false;
        if (toIndex < 0 || toIndex >= words.Count) return false;

        WordListLibraryContent before = CaptureCurrentContent();

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

        if (!CommitCurrentMutation(before))
        {
            Debug.LogWarning("[PhaseManager] The word-list move could not be saved; the original content was restored.");
            return false;
        }
        return true;
    }

    public void SaveCurrentList()
    {
        if (!TrySaveCurrentList(out string error))
            Debug.LogWarning("[PhaseManager] The current word list could not be saved: " + error);
    }

    public bool TrySaveCurrentList(out string error)
    {
        return TryPersistCurrentContent(true, out error);
    }

    private bool CommitCurrentMutation(WordListLibraryContent previousContent)
    {
        WordListLibraryContent candidate = CaptureCurrentContent();
        LanguageMode fallback = previousContent != null ? previousContent.LanguageMode : candidate.LanguageMode;
        if (!WordListLibraryContentCodec.TryNormalizeAutoContent(candidate, fallback,
            out _, out _, out WordListLibraryContent normalized, out string error))
        {
            RestoreCurrentContent(previousContent);
            Debug.LogWarning("[PhaseManager] The word-list mutation could not be saved: " + error);
            return false;
        }

        if (!TryPersistCurrentContent(false, out error, normalized))
        {
            RestoreCurrentContent(previousContent);
            Debug.LogWarning("[PhaseManager] The current word list could not be saved: " + error);
            return false;
        }

        ApplyCurrentContentSnapshot(normalized);
        ResetAfterContentChange();
        return true;
    }

    private bool TryPersistCurrentContent(bool publishChange, out string error,
        WordListLibraryContent contentOverride = null)
    {
        error = null;
        if (!AllowChineseAndMixed && CurrentListContainsHan())
        {
            Debug.LogWarning("[PhaseManager] Chinese content is disabled; the current Chinese list was not saved.");
            error = "Chinese and mixed content is disabled by the active profile.";
            return false;
        }

        if (activeProvider is LevelWordListProvider lvlProvider)
        {
            EnsureWordListLibrary();
            WordListLibraryItem current = wordListLibrary.GetOrRegister(lvlProvider);
            if (current == null)
            {
                error = wordListLibrary.SidecarLoadError ?? "The Library metadata is unavailable.";
                return false;
            }
            WordListLibraryContent content = contentOverride ?? CaptureCurrentContent();
            bool changed = !string.Equals(current.ContentHash,
                WordListLibraryService.ComputeContentHash(content), StringComparison.Ordinal);
            committingLocalMutation = true;
            bool saved;
            WordListLibraryItem updated;
            try
            {
                saved = wordListLibrary.TrySaveListEdit(current.LocalListId,
                    current.Title, current.Description, current.Tags, current.IsFavorite,
                    content, out updated, out error);
            }
            finally { committingLocalMutation = false; }
            if (!saved) return false;

            activeProvider = updated.Provider;
            activeLanguageMode = updated.LanguageMode;
            wordListLibrary.SetActiveProvider(updated.Provider);
            if (changed && publishChange) ResetAfterContentChange();
            return true;
        }
        else if (activeProvider is FileWordListProvider fileProvider)
        {
            if (!fileProvider.IsEditable || !CanEditLists)
            {
                error = "The current word list is read-only or editing is locked by the active profile.";
                return false;
            }
            WordListLibraryContent before = CaptureCurrentContent();
            byte[] oldFile = null;
            if (!string.IsNullOrEmpty(fileProvider.FilePath) && System.IO.File.Exists(fileProvider.FilePath))
                oldFile = System.IO.File.ReadAllBytes(fileProvider.FilePath);
            try
            {
                WordListLibraryContent content = contentOverride ?? CaptureCurrentContent();
                fileProvider.SetContent(content.LanguageMode, content.Words, content.ChineseWords, content.MixedWords);
                fileProvider.Save();
            }
            catch (Exception ex)
            {
                RestoreCurrentContent(before);
                if (oldFile != null)
                {
                    try { System.IO.File.WriteAllBytes(fileProvider.FilePath, oldFile); }
                    catch (Exception restoreError)
                    {
                        error = ex.Message + " Original legacy file restore failed: " + restoreError.Message;
                        return false;
                    }
                }
                error = ex.Message;
                return false;
            }
            if (publishChange) ResetAfterContentChange();
            return true;
        }

        error = "The current word list does not support saving.";
        return false;
    }

    private WordListLibraryContent CaptureCurrentContent()
    {
        return new WordListLibraryContent
        {
            LanguageMode = CurrentLanguageMode,
            Words = new List<string>(words),
            ChineseWords = LevelWordListProvider.CloneChineseWords(chineseWords),
            MixedWords = LevelWordListProvider.CloneMixedWords(mixedWords)
        };
    }

    private void ApplyCurrentContentSnapshot(WordListLibraryContent content)
    {
        if (content == null) return;
        activeLanguageMode = content.LanguageMode;
        words = new List<string>(content.Words ?? new List<string>());
        chineseWords = LevelWordListProvider.CloneChineseWords(content.ChineseWords);
        mixedWords = LevelWordListProvider.CloneMixedWords(content.MixedWords);
    }

    private void ConvertCurrentWordsToMixed()
    {
        mixedWords = new List<MixedWordEntry>();
        if (activeLanguageMode == LanguageMode.Chinese && chineseWords.Count == words.Count)
        {
            foreach (ChineseWordEntry chinese in chineseWords)
            {
                mixedWords.Add(new MixedWordEntry
                {
                    segments = new List<MixedSegmentData>
                    {
                        new MixedSegmentData
                        {
                            type = "chinese",
                            entries = chinese != null ? LevelWordListProvider.CloneChineseWords(new List<ChineseWordEntry> { chinese })[0].entries : new List<ChinesePhaseEntry>()
                        }
                    }
                });
            }
        }
        else
        {
            foreach (string word in words)
            {
                mixedWords.Add(new MixedWordEntry
                {
                    segments = new List<MixedSegmentData>
                    {
                        new MixedSegmentData { type = "english", text = word }
                    }
                });
            }
        }
        chineseWords = new List<ChineseWordEntry>();
        activeLanguageMode = LanguageMode.Mixed;
    }

    private void RestoreCurrentContent(WordListLibraryContent content)
    {
        if (content == null) return;
        activeLanguageMode = content.LanguageMode;
        words = new List<string>(content.Words ?? new List<string>());
        chineseWords = LevelWordListProvider.CloneChineseWords(content.ChineseWords);
        mixedWords = LevelWordListProvider.CloneMixedWords(content.MixedWords);
        if (activeProvider is LevelWordListProvider levelProvider)
            levelProvider.SetContent(content.LanguageMode, content.Words, content.ChineseWords, content.MixedWords);
        else if (activeProvider is FileWordListProvider fileProvider)
            fileProvider.SetContent(content.LanguageMode, content.Words, content.ChineseWords, content.MixedWords);
    }

    private void ResetAfterContentChange()
    {
        CurrentPhaseIndex = 0;
        ResetErrors();
        OnWordListChanged?.Invoke();
        OnPhaseWordChanged?.Invoke(CurrentWord);
    }

    private void HandleLibraryContentChanged(WordListLibraryItem item)
    {
        if (committingLocalMutation || item == null || !(activeProvider is LevelWordListProvider levelProvider)) return;
        if (!string.Equals(item.LocalListId, levelProvider.GetListKey(), StringComparison.Ordinal)
            || !ReferenceEquals(item.Provider, levelProvider))
            return;

        // The service has already saved canonical content and sidecar before raising this event.
        LoadWordList(item.Provider);
    }
}
