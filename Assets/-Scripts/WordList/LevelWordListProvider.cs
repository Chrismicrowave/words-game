using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// IWordListProvider backed by a .txt file.
///
/// If the file content starts with '{' it is parsed as JSON:
///   { "name": "First Steps", "nameZh": "起步", "words": [...] }
/// Otherwise it is treated as a plain word-per-line text file.
///
/// Challenge levels are read-only JSON-format .txt files.
/// Custom levels are read-write plain .txt files (one word per line).
/// </summary>
public class LevelWordListProvider : IWordListProvider
{
    public string DisplayName { get; private set; }
    public string DisplayNameZh { get; private set; }
    public LanguageMode LanguageMode { get; private set; } = LanguageMode.English;
    public bool IsEditable { get; private set; }
    public string FilePath { get; private set; }

    private List<string> words = new List<string>();

    public LevelWordListProvider(string filePath, bool isEditable = false)
    {
        FilePath = filePath;
        IsEditable = isEditable;
        DisplayName = Path.GetFileNameWithoutExtension(filePath);
        Load();
    }

    public List<string> GetWords() => new List<string>(words);

    public List<ChineseWordEntry> GetChineseWords() => null;

    public List<MixedWordEntry> GetMixedWords() => null;

    public void SetWords(List<string> newWords)
    {
        words = new List<string>(newWords);
        if (IsEditable)
            Save();
    }

    public void Save()
    {
        string dir = Path.GetDirectoryName(FilePath);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllLines(FilePath, words);
    }

    public void DeleteFile()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }

    private void Load()
    {
        if (!File.Exists(FilePath))
        {
            words = new List<string>();
            return;
        }

        string raw = File.ReadAllText(FilePath);
        string trimmed = raw.TrimStart();

        if (trimmed.Length > 0 && trimmed[0] == '{')
        {
            // JSON format
            var data = JsonUtility.FromJson<LevelJsonData>(raw);
            if (data != null)
            {
                DisplayName = data.name ?? DisplayName;
                DisplayNameZh = data.nameZh;
                words = data.words != null ? new List<string>(data.words) : new List<string>();
            }
        }
        else
        {
            // Plain text: one word per line
            words = new List<string>();
            var lines = raw.Split('\n');
            foreach (string line in lines)
            {
                string word = line.Trim();
                if (!string.IsNullOrEmpty(word))
                    words.Add(word);
            }
        }

        // Auto-detect language mode
        bool hasChinese = false;
        foreach (string w in words)
        {
            if (PinyinLookup.ContainsChinese(w))
            {
                hasChinese = true;
                break;
            }
        }
        LanguageMode = hasChinese ? LanguageMode.Mixed : LanguageMode.English;
    }

    [System.Serializable]
    private class LevelJsonData
    {
        public string name;
        public string nameZh;
        public string[] words;
    }

    public static string GetChallengeDirectory()
    {
        return Path.Combine(Application.streamingAssetsPath, "Levels");
    }

    public static string GetCustomDirectory()
    {
        return Path.Combine(Application.persistentDataPath, "Levels", "Custom");
    }

    /// <summary>
    /// Scans the given directory for .txt files and returns
    /// LevelWordListProvider instances.
    /// </summary>
    public static List<LevelWordListProvider> ScanDirectory(string directory, bool isEditable = false)
    {
        var providers = new List<LevelWordListProvider>();
        if (!Directory.Exists(directory)) return providers;

        var files = Directory.GetFiles(directory, "*.txt");
        System.Array.Sort(files);
        foreach (var f in files)
            providers.Add(new LevelWordListProvider(f, isEditable));
        return providers;
    }

    /// <summary>
    /// Creates a new empty custom level txt file with a unique name.
    /// Returns the provider, or null if creation fails.
    /// </summary>
    public static LevelWordListProvider CreateNewCustom()
    {
        string dir = GetCustomDirectory();
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Find next available name
        int n = 1;
        string path;
        do
        {
            path = Path.Combine(dir, $"new_list_{n}.txt");
            n++;
        } while (File.Exists(path));

        File.WriteAllText(path, "");
        return new LevelWordListProvider(path, true);
    }
}
