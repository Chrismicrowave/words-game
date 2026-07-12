using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
///
/// UUID (custom lists only):
///   A persistent UUID is stored as "// u:xxxxxxxx" header line in the file.
///   This survives rename, edit, and duplicate operations.
///   Used as the key for ListTimeManager time records.
/// </summary>
public class LevelWordListProvider : IWordListProvider
{
    public string DisplayName { get; private set; }
    public string DisplayNameZh { get; private set; }
    public LanguageMode LanguageMode { get; private set; } = LanguageMode.English;
    public bool IsEditable { get; private set; }
    public string FilePath { get; private set; }

    private List<string> words = new List<string>();
    private List<MixedWordEntry> mixedWords = new List<MixedWordEntry>();
    private string uuid; // null for challenge lists

    public LevelWordListProvider(string filePath, bool isEditable = false)
    {
        FilePath = filePath;
        IsEditable = isEditable;
        DisplayName = Path.GetFileNameWithoutExtension(filePath);
        Load();
    }

    public List<string> GetWords() => new List<string>(words);

    public List<ChineseWordEntry> GetChineseWords() => null;

    public List<MixedWordEntry> GetMixedWords() =>
        mixedWords != null ? new List<MixedWordEntry>(mixedWords) : null;

    public void SetMixedWords(List<MixedWordEntry> mw) =>
        mixedWords = new List<MixedWordEntry>(mw);

    public void SetLanguageMode(LanguageMode mode) =>
        LanguageMode = mode;

    /// <summary>
    /// Returns the stable key used by ListTimeManager for time records.
    /// Challenge: "chg_{filename}"   Custom: "cst_{uuid}"
    /// If no UUID exists yet (upgraded from old format), generates one on the spot.
    /// </summary>
    public string GetListKey()
    {
        if (IsEditable)
        {
            if (string.IsNullOrEmpty(uuid))
                EnsureUUID();
            return "cst_" + uuid;
        }
        return "chg_" + Path.GetFileName(FilePath);
    }

    /// <summary>Returns the UUID, or null for challenge lists.</summary>
    public string GetUUID() => uuid;

    /// <summary>
    /// Generates a new UUID for custom lists that were created before the UUID system
    /// or were imported without one. Writes it to the file header immediately.
    /// </summary>
    private void EnsureUUID()
    {
        uuid = Guid.NewGuid().ToString("N").Substring(0, 12);
    }

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

        if (mixedWords != null && mixedWords.Count > 0)
        {
            // Save as JSON with mixed word data
            var data = new LevelJsonData
            {
                name = DisplayName,
                nameZh = DisplayNameZh,
                words = words.ToArray(),
                mixedWords = mixedWords
            };
            string json = JsonUtility.ToJson(data, true);
            // Prepend UUID header for custom lists
            if (IsEditable && !string.IsNullOrEmpty(uuid))
                File.WriteAllText(FilePath, "// u:" + uuid + "\n" + json);
            else
                File.WriteAllText(FilePath, json);
        }
        else
        {
            // Plain text: one word per line
            var sb = new System.Text.StringBuilder();
            if (IsEditable && !string.IsNullOrEmpty(uuid))
                sb.AppendLine("// u:" + uuid);
            foreach (string w in words)
                sb.AppendLine(w);
            File.WriteAllText(FilePath, sb.ToString());
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

        string raw = File.ReadAllText(FilePath);
        string trimmed = raw.TrimStart();

        if (trimmed.Length > 0 && trimmed[0] == '{')
        {
            // JSON format (challenge levels)
            var data = JsonUtility.FromJson<LevelJsonData>(raw);
            if (data != null)
            {
                DisplayName = data.name ?? DisplayName;
                DisplayNameZh = data.nameZh;
                words = data.words != null ? new List<string>(data.words) : new List<string>();
                mixedWords = data.mixedWords ?? new List<MixedWordEntry>();
            }
        }
        else
        {
            // Plain text: one word per line, with optional UUID header
            words = new List<string>();
            var lines = raw.Split('\n');
            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine))
                    continue;

                // Check for UUID header line
                if (trimmedLine.StartsWith("// u:"))
                {
                    uuid = trimmedLine.Substring(5).Trim(); // after "// u:"
                    continue;
                }

                // Skip other comment lines
                if (trimmedLine.StartsWith("//"))
                    continue;

                words.Add(trimmedLine);
            }
        }

        // Auto-generate UUID for custom lists that don't have one (upgraded from old format)
        if (IsEditable && string.IsNullOrEmpty(uuid))
        {
            EnsureUUID();
            Save(); // write UUID header back to file
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
        public List<MixedWordEntry> mixedWords;
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
    /// Creates a new empty custom level txt file with a unique name and UUID.
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

        // Generate UUID and write initial content with header
        string newUuid = Guid.NewGuid().ToString("N").Substring(0, 12); // short UUID
        File.WriteAllText(path, "// u:" + newUuid + "\nhello world");

        return new LevelWordListProvider(path, true);
    }

    /// <summary>
    /// Creates a duplicate of this list with a new UUID but same words.
    /// Returns the new provider, or null on failure.
    /// </summary>
    public LevelWordListProvider Duplicate()
    {
        string dir = GetCustomDirectory();
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Find next available name based on source
        string baseName = Path.GetFileNameWithoutExtension(FilePath);
        int n = 1;
        string newPath;
        do
        {
            newPath = Path.Combine(dir, $"{baseName}_copy_{n}.txt");
            n++;
        } while (File.Exists(newPath));

        // Write with same words but NEW UUID
        string newUuid = Guid.NewGuid().ToString("N").Substring(0, 12);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// u:" + newUuid);
        foreach (string w in words)
            sb.AppendLine(w);
        File.WriteAllText(newPath, sb.ToString());

        return new LevelWordListProvider(newPath, true);
    }
}
