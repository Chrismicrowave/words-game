using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>Infers the supported list language from display text without changing that text.</summary>
public static class WordListLanguageDetector
{
    public static bool TryDetectDisplayLines(IEnumerable<string> rawDisplays, LanguageMode fallback,
        out LanguageMode mode, out bool usedFallback, out string error)
    {
        return TryDetect(rawDisplays, false, fallback, out mode, out usedFallback, out error);
    }

    public static bool TryDetectEditorLines(IEnumerable<string> editorLines, LanguageMode fallback,
        out LanguageMode mode, out bool usedFallback, out string error)
    {
        return TryDetect(editorLines, true, fallback, out mode, out usedFallback, out error);
    }

    private static bool TryDetect(IEnumerable<string> lines, bool stripPinyin, LanguageMode fallback,
        out LanguageMode mode, out bool usedFallback, out string error)
    {
        mode = IsSupportedMode(fallback) ? fallback : LanguageMode.English;
        usedFallback = false;
        error = null;
        if (!IsSupportedMode(fallback))
        {
            error = "Choose a valid fallback word-list language.";
            return false;
        }

        bool foundHan = false;
        bool foundLatin = false;
        if (lines != null)
        {
            foreach (string raw in lines)
            {
                string text = raw ?? string.Empty;
                if (stripPinyin) text = StripValidPinyinSuffix(text);
                int index = 0;
                while (index < text.Length)
                {
                    int codePoint = char.IsSurrogatePair(text, index)
                        ? char.ConvertToUtf32(text, index)
                        : text[index];
                    int width = codePoint > 0xFFFF ? 2 : 1;
                    UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(text, index);
                    if (IsLetterCategory(category))
                    {
                        if (IsHan(codePoint)) foundHan = true;
                        else if (IsLatin(codePoint)) foundLatin = true;
                        else
                        {
                            error = "Only English (Latin) and Chinese (Han) letters are supported in word lists.";
                            return false;
                        }
                    }
                    // Digits, punctuation, symbols, whitespace, combining marks and formatting
                    // characters do not determine a list's language.
                    index += width;
                }
            }
        }

        if (foundHan && foundLatin) mode = LanguageMode.Mixed;
        else if (foundHan) mode = LanguageMode.Chinese;
        else if (foundLatin) mode = LanguageMode.English;
        else
        {
            mode = fallback;
            usedFallback = true;
        }
        return true;
    }

    private static string StripValidPinyinSuffix(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;
        int separator = line.IndexOf('|');
        if (separator < 0 || separator != line.LastIndexOf('|')) return line;
        string display = line.Substring(0, separator).Trim();
        string annotation = line.Substring(separator + 1).Trim();
        if (display.Length == 0 || annotation.Length == 0) return line;

        bool hasLetter = false;
        for (int i = 0; i < annotation.Length; i++)
        {
            char c = annotation[i];
            if (c >= 'a' && c <= 'z') hasLetter = true;
            else if (c != ' ' && c != '\t' && c != '\r' && c != '\n') return line;
        }
        return hasLetter ? display : line;
    }

    private static bool IsSupportedMode(LanguageMode mode)
    {
        return mode == LanguageMode.English || mode == LanguageMode.Chinese || mode == LanguageMode.Mixed;
    }

    private static bool IsLetterCategory(UnicodeCategory category)
    {
        return category == UnicodeCategory.UppercaseLetter
            || category == UnicodeCategory.LowercaseLetter
            || category == UnicodeCategory.TitlecaseLetter
            || category == UnicodeCategory.ModifierLetter
            || category == UnicodeCategory.OtherLetter;
    }

    private static bool IsLatin(int codePoint)
    {
        // Product-supported Latin includes ASCII, Latin-1/Extended-A/B and the common
        // Extended Additional/Extended-C/D/E/F/G blocks plus fullwidth Latin forms.
        return (codePoint >= 0x0041 && codePoint <= 0x024F)
            || (codePoint >= 0x1D00 && codePoint <= 0x1D7F)
            || (codePoint >= 0x1E00 && codePoint <= 0x1EFF)
            || (codePoint >= 0x2C60 && codePoint <= 0x2C7F)
            || (codePoint >= 0xA720 && codePoint <= 0xA7FF)
            || (codePoint >= 0xAB30 && codePoint <= 0xAB6F)
            || (codePoint >= 0x10780 && codePoint <= 0x107BF)
            || (codePoint >= 0x1DF00 && codePoint <= 0x1DFFF)
            || (codePoint >= 0xFF21 && codePoint <= 0xFF3A)
            || (codePoint >= 0xFF41 && codePoint <= 0xFF5A);
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
}
