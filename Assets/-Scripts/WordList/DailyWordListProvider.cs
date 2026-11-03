using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class DailyWordListProvider : IWordListProvider
{
    public string DisplayName { get; private set; }
    public LanguageMode LanguageMode { get; private set; } = LanguageMode.English;
    public bool IsEditable => false;
    public string FilePath { get; private set; }

    private List<string> words = new List<string>();
    private List<ChineseWordEntry> chineseWords = new List<ChineseWordEntry>();
    private List<MixedWordEntry> mixedWords = new List<MixedWordEntry>();

    [Serializable]
    private class DailyListData
    {
        public string name;
        public string languageMode;
        public List<string> words;
        public List<ChineseWordEntry> chineseWords;
        public List<MixedWordEntry> mixedWords;
        public string date;
    }

    public DailyWordListProvider(string filePath)
    {
        FilePath = filePath;
        Load();
    }

    public List<string> GetWords() => new List<string>(words);
    public List<ChineseWordEntry> GetChineseWords() => chineseWords != null ? new List<ChineseWordEntry>(chineseWords) : null;
    public List<MixedWordEntry> GetMixedWords() => mixedWords != null ? new List<MixedWordEntry>(mixedWords) : null;

    private void Load()
    {
        if (!File.Exists(FilePath))
        {
            DisplayName = "Daily (not found)";
            words = new List<string>();
            return;
        }

        string json = File.ReadAllText(FilePath);
        var data = JsonUtility.FromJson<DailyListData>(json);
        DisplayName = data.name ?? "Daily";
        words = data.words ?? new List<string>();
        chineseWords = data.chineseWords ?? new List<ChineseWordEntry>();
        mixedWords = data.mixedWords ?? new List<MixedWordEntry>();

        if (!string.IsNullOrEmpty(data.languageMode) &&
            System.Enum.TryParse<LanguageMode>(data.languageMode, out var mode))
            LanguageMode = mode;
        else
            LanguageMode = LanguageMode.English;

        // Rebuild display words from structured data when the JSON omits the words array
        if (words.Count == 0)
        {
            if (chineseWords.Count > 0)
            {
                foreach (var cw in chineseWords)
                {
                    if (!string.IsNullOrEmpty(cw.display))
                        words.Add(cw.display);
                    else if (cw.entries != null)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var e in cw.entries) sb.Append(e.character);
                        words.Add(sb.ToString());
                    }
                }
            }
            else if (mixedWords.Count > 0)
            {
                foreach (var mw in mixedWords)
                {
                    if (mw.segments == null) continue;
                    var sb = new System.Text.StringBuilder();
                    foreach (var seg in mw.segments)
                    {
                        if (seg.type == "english")
                            sb.Append(seg.text);
                        else if (seg.entries != null)
                            foreach (var e in seg.entries) sb.Append(e.character);
                    }
                    words.Add(sb.ToString());
                }
            }
        }
    }

    public static string GetDailyListDirectory() =>
        Path.Combine(Application.streamingAssetsPath, "DailyLists");
}
