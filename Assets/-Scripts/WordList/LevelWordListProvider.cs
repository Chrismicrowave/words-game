using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// IWordListProvider backed by a plain .txt file (one word per line).
/// Used for challenge levels (read-only) and custom levels (read-write).
/// </summary>
public class LevelWordListProvider : IWordListProvider
{
    public string DisplayName { get; private set; }
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

        var lines = File.ReadAllLines(FilePath);
        words = new List<string>();
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                words.Add(trimmed);
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

    public static string GetChallengeDirectory()
    {
        return Path.Combine(Application.streamingAssetsPath, "Levels");
    }

    public static string GetCustomDirectory()
    {
        return Path.Combine(Application.persistentDataPath, "Levels", "Custom");
    }

    /// <summary>
    /// Scans the given directory for .txt files and returns LevelWordListProvider instances.
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
