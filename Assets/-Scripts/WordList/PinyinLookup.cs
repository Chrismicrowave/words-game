using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Loads a JSON character→pinyin table from a TextAsset and provides lookup.
/// Polyphonic characters return the most common reading; unknown characters return "".
/// </summary>
public static class PinyinLookup
{
    private static Dictionary<char, string> _table;

    private static void EnsureLoaded()
    {
        if (_table != null) return;

        _table = new Dictionary<char, string>();
        var asset = Resources.Load<TextAsset>("PinyinLookup");
        if (asset == null)
        {
            Debug.LogWarning("[PinyinLookup] PinyinLookup.json not found in Resources.");
            return;
        }

        // Manual parse of flat {"char":"pinyin",...} JSON — avoids JsonUtility limitations
        string json = asset.text.Trim().Trim('{', '}');
        foreach (string pair in json.Split(','))
        {
            string p = pair.Trim();
            if (p.Length < 5) continue; // minimum: "x":"y"
            int colon = p.IndexOf(':');
            if (colon < 0) continue;

            string keyPart = p.Substring(0, colon).Trim().Trim('"');
            string valPart = p.Substring(colon + 1).Trim().Trim('"');

            if (keyPart.Length == 1 && valPart.Length > 0)
                _table[keyPart[0]] = valPart;
        }

        Debug.Log($"[PinyinLookup] Loaded {_table.Count} entries.");
    }

    /// <summary>Returns pinyin for a single Chinese character, or "xxx" if unknown (signals user to fix it).</summary>
    public static string Get(char c) { EnsureLoaded(); return _table.TryGetValue(c, out var p) ? p : "xxx"; }

    /// <summary>Returns true if the string contains any CJK Unified Ideograph characters.</summary>
    public static bool ContainsChinese(string text)
    {
        foreach (char c in text)
            if (IsChinese(c)) return true;
        return false;
    }

    public static bool IsChinese(char c) => c >= '\u4e00' && c <= '\u9fff';

    /// <summary>
    /// Splits text into segments: each run of CJK chars is one segment, each run of non-CJK is one segment.
    /// Returns list of (isChinese, text) tuples.
    /// </summary>
    public static List<(bool isChinese, string text)> Segment(string text)
    {
        var result = new List<(bool, string)>();
        if (string.IsNullOrEmpty(text)) return result;

        var sb = new System.Text.StringBuilder();
        bool curChinese = IsChinese(text[0]);

        foreach (char c in text)
        {
            bool thisChinese = IsChinese(c);
            if (thisChinese != curChinese)
            {
                if (sb.Length > 0) result.Add((curChinese, sb.ToString()));
                sb.Clear();
                curChinese = thisChinese;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) result.Add((curChinese, sb.ToString()));
        return result;
    }

    public static bool HasNonAscii(string text)
    {
        foreach (char c in text) if (c > 127) return true;
        return false;
    }
}
