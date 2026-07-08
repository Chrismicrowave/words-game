using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// IWordListProvider backed by a .txt (one word per line) or .json file.
/// JSON format: { "name": "First Steps", "nameZh": "起步", "words": [...] }
/// Used for challenge levels (read-only JSON) and custom levels (read-write .txt).
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

        string ext = Path.GetExtension(FilePath).ToLower();
        if (ext == ".json")
        {
            // JSON files are read-only challenges — Save() should not be called
            // for them (IsEditable is false), but handle gracefully.
            var data = new LevelJsonData
            {
                name = DisplayName,
                nameZh = DisplayNameZh,
                words = words.ToArray()
            };
            File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
        }
        else
        {
            File.WriteAllLines(FilePath, words);
        }
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

        string ext = Path.GetExtension(FilePath).ToLower();
        if (ext == ".json")
        {
            string json = File.ReadAllText(FilePath);
            var data = JsonUtility.FromJson<LevelJsonData>(json);
            if (data != null)
            {
                DisplayName = data.name ?? DisplayName;
                DisplayNameZh = data.nameZh;
                words = data.words != null ? new List<string>(data.words) : new List<string>();
            }
        }
        else
        {
            // Original .txt behavior: one word per line
            var lines = File.ReadAllLines(FilePath);
            words = new List<string>();
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    words.Add(trimmed);
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
    /// Scans the given directory for .txt and .json files and
    /// returns LevelWordListProvider instances.
    /// </summary>
    public static List<LevelWordListProvider> ScanDirectory(string directory, bool isEditable = false)
    {
        var providers = new List<LevelWordListProvider>();
        if (!Directory.Exists(directory)) return providers;

        var files = new List<string>();
        files.AddRange(Directory.GetFiles(directory, "*.txt"));
        files.AddRange(Directory.GetFiles(directory, "*.json"));
        files.Sort();
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
