using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Converts the editor's one-line display projection back to structured runtime content.
/// Unchanged legacy lines retain their existing Chinese/mixed data, including incomplete
/// old pinyin; annotated lines create or correct pinyin without flattening segment structure.
/// </summary>
public static class WordListLibraryContentCodec
{
    public static bool TryParseAutoEditorLines(IEnumerable<string> lines, LanguageMode fallback,
        WordListLibraryContent original, out LanguageMode detectedMode, out bool usedFallback,
        out WordListLibraryContent content, out string error)
    {
        content = null;
        List<string> stableLines = lines != null ? new List<string>(lines) : null;
        if (!WordListLanguageDetector.TryDetectEditorLines(stableLines, fallback,
            out detectedMode, out usedFallback, out error))
            return false;
        LanguageMode storageMode = SelectAutoStorageMode(
            stableLines, detectedMode, usedFallback, true);
        return TryParseEditorLinesInternal(stableLines, storageMode, original, true, out content, out error);
    }

    public static bool TryParseEditorLines(IEnumerable<string> lines, LanguageMode mode,
        WordListLibraryContent original, out WordListLibraryContent content, out string error)
    {
        return TryParseEditorLinesInternal(lines, mode, original, true, out content, out error);
    }

    public static bool TryNormalizeAutoContent(WordListLibraryContent candidate, LanguageMode fallback,
        out LanguageMode detectedMode, out bool usedFallback, out WordListLibraryContent content, out string error)
    {
        content = null;
        if (candidate == null)
        {
            detectedMode = IsSupportedMode(fallback) ? fallback : LanguageMode.English;
            usedFallback = true;
            error = "The word-list content is unavailable.";
            return false;
        }

        List<string> displays = candidate.Words != null ? new List<string>(candidate.Words) : new List<string>();
        if (!WordListLanguageDetector.TryDetectDisplayLines(displays, fallback,
            out detectedMode, out usedFallback, out error))
            return false;
        LanguageMode storageMode = SelectAutoStorageMode(
            displays, detectedMode, usedFallback, false);
        return TryParseEditorLinesInternal(displays, storageMode, candidate, false, out content, out error);
    }

    private static LanguageMode SelectAutoStorageMode(IEnumerable<string> lines,
        LanguageMode detectedMode, bool usedFallback, bool editorAnnotations)
    {
        if (detectedMode != LanguageMode.Chinese || usedFallback || lines == null)
            return detectedMode;

        foreach (string raw in lines)
        {
            string line = raw ?? string.Empty;
            string display = editorAnnotations ? line.Trim() : line;
            if (editorAnnotations
                && !TrySplitAnnotation(display, out display, out _, out _, out _))
                continue;
            if (display.Length > 0 && CountHanCharacters(display) == 0)
                return LanguageMode.Mixed;
        }
        return detectedMode;
    }

    private static bool IsSupportedMode(LanguageMode mode)
    {
        return mode == LanguageMode.English || mode == LanguageMode.Chinese || mode == LanguageMode.Mixed;
    }

    private static bool TryParseEditorLinesInternal(IEnumerable<string> lines, LanguageMode mode,
        WordListLibraryContent original, bool allowPinyinAnnotations,
        out WordListLibraryContent content, out string error)
    {
        content = null;
        if (!Enum.IsDefined(typeof(LanguageMode), mode))
        {
            error = "Choose a valid word-list language.";
            return false;
        }

        var result = new WordListLibraryContent { LanguageMode = mode };
        var originalQueues = BuildOriginalQueues(original);
        if (lines != null)
        {
            foreach (string raw in lines)
            {
                string line = raw == null ? string.Empty : allowPinyinAnnotations ? raw.Trim() : raw;
                if (line.Length == 0) continue;
                string display;
                string[] syllables;
                bool annotated;
                if (allowPinyinAnnotations)
                {
                    if (!TrySplitAnnotation(line, out display, out syllables, out annotated, out error))
                        return false;
                }
                else
                {
                    display = line;
                    syllables = Array.Empty<string>();
                    annotated = false;
                }

                int hanCount = CountHanCharacters(display);
                OriginalEntry preserved = TakeOriginal(originalQueues, display);

                if (mode == LanguageMode.English)
                {
                    if (annotated || hanCount > 0)
                    {
                        error = "English lists cannot contain Chinese pronunciation annotations or Han characters.";
                        return false;
                    }
                    result.Words.Add(display);
                    continue;
                }

                if (mode == LanguageMode.Chinese)
                {
                    if (hanCount == 0)
                    {
                        error = "Chinese entries need at least one Chinese character.";
                        return false;
                    }
                    ChineseWordEntry chinese;
                    if (!annotated && preserved != null && preserved.chinese != null)
                        chinese = LevelWordListProvider.CloneChineseWords(new List<ChineseWordEntry> { preserved.chinese })[0];
                    else if (!annotated && preserved != null && preserved.mixed != null
                        && TryConvertMixedToChinese(preserved.mixed, display, out chinese))
                    {
                        // Existing typed Mixed data can be narrowed without discarding its pinyin.
                    }
                    else
                    {
                        if (!annotated)
                        {
                            error = "Add one lowercase pinyin syllable per Chinese character using 'display | ni hao'.";
                            return false;
                        }
                        if (!TryMakePhaseEntries(display, syllables, out List<ChinesePhaseEntry> phases, out error))
                            return false;
                        chinese = new ChineseWordEntry { display = display, entries = phases };
                    }

                    if (annotated && preserved?.chinese != null)
                    {
                        if (TryApplyPinyin(preserved.chinese.entries, display, syllables,
                            out List<ChinesePhaseEntry> edited, out error))
                            chinese = new ChineseWordEntry { display = preserved.chinese.display, entries = edited };
                        else if (!TryMakePhaseEntries(display, syllables, out edited, out error))
                            return false;
                        else
                            chinese = new ChineseWordEntry { display = display, entries = edited };
                    }

                    result.ChineseWords.Add(chinese);
                    result.Words.Add(chinese.display);
                    continue;
                }

                if (mode == LanguageMode.Mixed)
                {
                    MixedWordEntry mixed;
                    if (!annotated && preserved != null && preserved.mixed != null)
                        mixed = LevelWordListProvider.CloneMixedWords(new List<MixedWordEntry> { preserved.mixed })[0];
                    else if (!annotated && preserved != null && preserved.chinese != null
                        && TryConvertChineseToMixed(preserved.chinese, display, out mixed))
                    {
                        // Existing typed Chinese data can be widened while retaining its pinyin.
                    }
                    else if (hanCount == 0)
                        mixed = new MixedWordEntry
                        {
                            segments = new List<MixedSegmentData> { new MixedSegmentData { type = "english", text = display } }
                        };
                    else
                    {
                        if (!annotated)
                        {
                            error = "Add one lowercase pinyin syllable per Chinese character using 'display | ni hao'.";
                            return false;
                        }
                        if (preserved?.mixed != null && GetMixedDisplay(preserved.mixed) == display)
                        {
                            mixed = UpdateMixedPinyin(preserved.mixed, display, syllables, out error);
                            if (mixed == null && !TryMakeMixedEntry(display, syllables, out mixed, out error))
                                return false;
                        }
                        else
                        {
                            if (!TryMakeMixedEntry(display, syllables, out mixed, out error)) return false;
                        }
                    }

                    result.MixedWords.Add(mixed);
                    result.Words.Add(GetMixedDisplay(mixed));
                    continue;
                }
            }
        }

        content = result;
        error = null;
        return true;
    }

    private static Dictionary<string, Queue<OriginalEntry>> BuildOriginalQueues(WordListLibraryContent original)
    {
        var queues = new Dictionary<string, Queue<OriginalEntry>>(StringComparer.Ordinal);
        if (original == null) return queues;
        if (original.Words == null) return queues;

        for (int i = 0; i < original.Words.Count; i++)
        {
            ChineseWordEntry chinese = original.ChineseWords != null && i < original.ChineseWords.Count
                ? original.ChineseWords[i] : null;
            MixedWordEntry mixed = original.MixedWords != null && i < original.MixedWords.Count
                ? original.MixedWords[i] : null;
            OriginalEntry typed = null;
            if (original.LanguageMode == LanguageMode.Mixed)
            {
                if (mixed != null) typed = new OriginalEntry { mixed = mixed };
                else if (chinese != null) typed = new OriginalEntry { chinese = chinese };
            }
            else if (original.LanguageMode == LanguageMode.Chinese)
            {
                if (chinese != null) typed = new OriginalEntry { chinese = chinese };
                else if (mixed != null) typed = new OriginalEntry { mixed = mixed };
            }

            if (typed != null)
                Enqueue(queues, typed.chinese != null ? typed.chinese.display : GetMixedDisplay(typed.mixed), typed);
            else if (original.LanguageMode == LanguageMode.English && original.Words[i] != null)
                Enqueue(queues, original.Words[i], new OriginalEntry { english = original.Words[i] });
        }
        return queues;
    }

    private static bool TryConvertChineseToMixed(ChineseWordEntry source, string display, out MixedWordEntry mixed)
    {
        mixed = null;
        if (source == null || !string.Equals(source.display, display, StringComparison.Ordinal)) return false;
        List<string> hanCharacters = GetHanCharacters(display);
        if (source.entries == null || source.entries.Count != hanCharacters.Count) return false;
        for (int i = 0; i < hanCharacters.Count; i++)
            if (source.entries[i] == null
                || !string.Equals(source.entries[i].character, hanCharacters[i], StringComparison.Ordinal))
                return false;

        var clonedEntries = LevelWordListProvider.CloneChineseWords(
            new List<ChineseWordEntry> { source })[0].entries;
        mixed = new MixedWordEntry { segments = new List<MixedSegmentData>() };
        int hanIndex = 0;
        int position = 0;
        while (position < display.Length)
        {
            int codePoint = char.IsSurrogatePair(display, position)
                ? char.ConvertToUtf32(display, position)
                : display[position];
            bool han = IsHan(codePoint);
            int start = position;
            var text = new StringBuilder();
            var entries = new List<ChinesePhaseEntry>();
            while (position < display.Length)
            {
                codePoint = char.IsSurrogatePair(display, position)
                    ? char.ConvertToUtf32(display, position)
                    : display[position];
                if (IsHan(codePoint) != han) break;
                int width = codePoint > 0xFFFF ? 2 : 1;
                string piece = display.Substring(position, width);
                if (han) entries.Add(clonedEntries[hanIndex++]);
                else text.Append(piece);
                position += width;
            }
            if (position == start) position += codePoint > 0xFFFF ? 2 : 1;
            if (han) mixed.segments.Add(new MixedSegmentData { type = "chinese", entries = entries });
            else mixed.segments.Add(new MixedSegmentData { type = "english", text = text.ToString() });
        }
        return true;
    }

    private static bool TryConvertMixedToChinese(MixedWordEntry source, string display, out ChineseWordEntry chinese)
    {
        chinese = null;
        if (source == null || !string.Equals(GetMixedDisplay(source), display, StringComparison.Ordinal)) return false;
        List<string> hanCharacters = GetHanCharacters(display);
        var entries = new List<ChinesePhaseEntry>();
        if (source.segments != null)
        {
            foreach (MixedSegmentData segment in source.segments)
            {
                if (segment == null) return false;
                if (segment.type == "chinese")
                {
                    if (segment.entries == null) return false;
                    foreach (ChinesePhaseEntry entry in segment.entries)
                    {
                        if (entry == null) return false;
                        entries.Add(entry);
                    }
                }
            }
        }
        if (entries.Count != hanCharacters.Count) return false;
        for (int i = 0; i < hanCharacters.Count; i++)
            if (!string.Equals(entries[i].character, hanCharacters[i], StringComparison.Ordinal)) return false;

        chinese = new ChineseWordEntry
        {
            display = display,
            entries = LevelWordListProvider.CloneChineseWords(new List<ChineseWordEntry>
            {
                new ChineseWordEntry { display = display, entries = entries }
            })[0].entries
        };
        return true;
    }

    private static void Enqueue(Dictionary<string, Queue<OriginalEntry>> queues, string key, OriginalEntry value)
    {
        if (key == null) key = string.Empty;
        if (!queues.TryGetValue(key, out Queue<OriginalEntry> queue))
            queues[key] = queue = new Queue<OriginalEntry>();
        queue.Enqueue(value);
    }

    private static OriginalEntry TakeOriginal(Dictionary<string, Queue<OriginalEntry>> queues, string key)
    {
        if (key == null) key = string.Empty;
        return queues.TryGetValue(key, out Queue<OriginalEntry> queue) && queue.Count > 0 ? queue.Dequeue() : null;
    }

    private static bool TrySplitAnnotation(string line, out string display, out string[] syllables,
        out bool annotated, out string error)
    {
        display = line;
        syllables = Array.Empty<string>();
        annotated = false;
        int separator = line.IndexOf('|');
        if (separator < 0)
        {
            error = null;
            return true;
        }
        if (separator != line.LastIndexOf('|'))
        {
            error = "Use one '|' between the display text and pinyin.";
            return false;
        }
        display = line.Substring(0, separator).Trim();
        string pinyin = line.Substring(separator + 1).Trim();
        if (display.Length == 0 || pinyin.Length == 0)
        {
            error = "Add display text and pinyin after the '|' separator.";
            return false;
        }
        syllables = pinyin.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        foreach (string syllable in syllables)
        {
            if (syllable.Length == 0) continue;
            foreach (char c in syllable)
                if (c < 'a' || c > 'z')
                {
                    error = "Pinyin must use lowercase a-z only, with no tone marks.";
                    return false;
                }
        }
        annotated = true;
        error = null;
        return true;
    }

    private static bool TryMakePhaseEntries(string display, string[] syllables,
        out List<ChinesePhaseEntry> entries, out string error)
    {
        entries = new List<ChinesePhaseEntry>();
        List<string> hanCharacters = GetHanCharacters(display);
        if (hanCharacters.Count != syllables.Length)
        {
            error = "Expected " + hanCharacters.Count + " pinyin syllable(s), one per Chinese character.";
            return false;
        }
        for (int i = 0; i < hanCharacters.Count; i++)
            entries.Add(new ChinesePhaseEntry { character = hanCharacters[i], pinyin = syllables[i] });
        error = null;
        return true;
    }

    private static bool TryApplyPinyin(List<ChinesePhaseEntry> original, string display, string[] syllables,
        out List<ChinesePhaseEntry> edited, out string error)
    {
        edited = LevelWordListProvider.CloneChineseWords(new List<ChineseWordEntry>
        {
            new ChineseWordEntry { entries = original }
        })[0].entries;
        List<string> displayCharacters = GetHanCharacters(display);
        if (original == null || original.Count != syllables.Length || original.Count != displayCharacters.Count)
        {
            error = "The pinyin count does not match the Chinese display characters.";
            return false;
        }
        for (int i = 0; i < edited.Count; i++)
        {
            if (edited[i] == null || !string.Equals(edited[i].character, displayCharacters[i], StringComparison.Ordinal))
            {
                error = "The original Chinese character mapping does not match the edited display.";
                return false;
            }
            edited[i].pinyin = syllables[i];
        }
        error = null;
        return true;
    }

    private static bool TryMakeMixedEntry(string display, string[] syllables,
        out MixedWordEntry entry, out string error)
    {
        entry = new MixedWordEntry { segments = new List<MixedSegmentData>() };
        List<string> hanCharacters = GetHanCharacters(display);
        if (hanCharacters.Count != syllables.Length)
        {
            error = "Expected " + hanCharacters.Count + " pinyin syllable(s), one per Chinese character.";
            return false;
        }

        int pinyinIndex = 0;
        int position = 0;
        while (position < display.Length)
        {
            int codePoint = char.IsSurrogatePair(display, position)
                ? char.ConvertToUtf32(display, position)
                : display[position];
            int width = codePoint > 0xFFFF ? 2 : 1;
            bool han = IsHan(codePoint);
            int start = position;
            var phases = new List<ChinesePhaseEntry>();
            var text = new StringBuilder();
            while (position < display.Length)
            {
                codePoint = char.IsSurrogatePair(display, position)
                    ? char.ConvertToUtf32(display, position)
                    : display[position];
                width = codePoint > 0xFFFF ? 2 : 1;
                if (IsHan(codePoint) != han) break;
                string piece = display.Substring(position, width);
                if (han)
                    phases.Add(new ChinesePhaseEntry { character = piece, pinyin = syllables[pinyinIndex++] });
                else text.Append(piece);
                position += width;
            }
            if (position == start) position += width;
            if (han)
                entry.segments.Add(new MixedSegmentData { type = "chinese", entries = phases });
            else
                entry.segments.Add(new MixedSegmentData { type = "english", text = text.ToString() });
        }
        error = null;
        return true;
    }

    private static MixedWordEntry UpdateMixedPinyin(MixedWordEntry source, string display, string[] syllables, out string error)
    {
        var clone = LevelWordListProvider.CloneMixedWords(new List<MixedWordEntry> { source })[0];
        List<string> displayCharacters = GetHanCharacters(display);
        int index = 0;
        if (clone.segments != null)
        {
            foreach (var segment in clone.segments)
            {
                if (segment == null || segment.type != "chinese") continue;
                if (segment.entries == null)
                {
                    error = "The original mixed Chinese segment has no character entries.";
                    return null;
                }
                foreach (var phase in segment.entries)
                {
                    if (phase == null || index >= syllables.Length || index >= displayCharacters.Count
                        || !string.Equals(phase.character, displayCharacters[index], StringComparison.Ordinal))
                    {
                        error = "The existing mixed Chinese character mapping does not match the edited display.";
                        return null;
                    }
                    phase.pinyin = syllables[index++];
                }
            }
        }
        if (index != syllables.Length || index != displayCharacters.Count)
        {
            error = "The pinyin count does not match the edited display's Chinese characters.";
            return null;
        }
        error = null;
        return clone;
    }

    private static string GetMixedDisplay(MixedWordEntry word)
    {
        var sb = new StringBuilder();
        if (word?.segments == null) return string.Empty;
        foreach (var segment in word.segments)
        {
            if (segment == null) continue;
            if (segment.type == "english") sb.Append(segment.text);
            else if (segment.entries != null)
                foreach (var entry in segment.entries)
                    if (entry != null) sb.Append(entry.character);
        }
        return sb.ToString();
    }

    private static int CountHanCharacters(string text) => GetHanCharacters(text).Count;

    private static List<string> GetHanCharacters(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) return result;
        int position = 0;
        while (position < text.Length)
        {
            int codePoint = char.IsSurrogatePair(text, position) ? char.ConvertToUtf32(text, position) : text[position];
            int width = codePoint > 0xFFFF ? 2 : 1;
            if (IsHan(codePoint)) result.Add(text.Substring(position, width));
            position += width;
        }
        return result;
    }

    private static bool IsHan(int codePoint)
    {
        return (codePoint >= 0x3400 && codePoint <= 0x4DBF)
            || (codePoint >= 0x4E00 && codePoint <= 0x9FFF)
            || (codePoint >= 0xF900 && codePoint <= 0xFAFF)
            || (codePoint >= 0x20000 && codePoint <= 0x2A6DF)
            || (codePoint >= 0x2A700 && codePoint <= 0x2B73F)
            || (codePoint >= 0x2B740 && codePoint <= 0x2B81F)
            || (codePoint >= 0x2B820 && codePoint <= 0x2CEAF)
            || (codePoint >= 0x2CEB0 && codePoint <= 0x2EBEF)
            || (codePoint >= 0x2EBF0 && codePoint <= 0x2EE5F)
            || (codePoint >= 0x2F800 && codePoint <= 0x2FA1F)
            || (codePoint >= 0x30000 && codePoint <= 0x3134F)
            || (codePoint >= 0x31350 && codePoint <= 0x323AF);
    }

    private sealed class OriginalEntry
    {
        public string english;
        public ChineseWordEntry chinese;
        public MixedWordEntry mixed;
    }
}
