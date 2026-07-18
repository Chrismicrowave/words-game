using System.Collections.Generic;
using System.Text;

/// <summary>
/// Utility for measuring visual display width of mixed Chinese/English text.
/// Chinese chars = 1 unit, English letters = 0.5 unit.
/// Used to trim over-long words from word lists before gameplay.
/// </summary>
public static class DisplayWidthUtil
{
    /// <summary>Max letters for pure English words.</summary>
    public const int MaxEnglishLetters = 140;

    /// <summary>Max display-width units for Chinese/Mixed words (CN=1, EN=0.5).</summary>
    public const int MaxDisplayUnits = 48;

    /// <summary>
    /// Returns the visual display width of text:
    /// CJK char = 1.0 unit, everything else = 0.5 unit.
    /// </summary>
    public static float GetWidth(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        float w = 0f;
        foreach (char c in text)
        {
            w += IsCjk(c) ? 1f : 0.5f;
        }
        return w;
    }

    /// <summary>
    /// Returns true if the text exceeds the allowed limit for its language mode.
    /// </summary>
    public static bool IsOverLimit(string text, LanguageMode mode)
    {
        if (string.IsNullOrEmpty(text)) return true;

        if (mode == LanguageMode.English)
            return text.Length > MaxEnglishLetters;

        // Chinese or Mixed
        return GetWidth(text) > MaxDisplayUnits;
    }

    /// <summary>
    /// Filters a word list to remove entries exceeding the display limit.
    /// Returns a new list; original is untouched.
    /// </summary>
    public static List<string> FilterWords(List<string> words, LanguageMode mode)
    {
        var result = new List<string>(words.Count);
        foreach (var w in words)
        {
            if (!IsOverLimit(w, mode))
                result.Add(w);
        }
        return result;
    }

    private static bool IsCjk(char c)
    {
        // CJK Unified Ideographs
        return c >= 0x4E00 && c <= 0x9FFF;
    }
}
