using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class FileWordListProvider : IWordListProvider
{
    public string DisplayName { get; private set; }
    public bool IsEditable => true;
    public string FilePath { get; private set; }

    private List<string> words = new List<string>();

    [Serializable]
    private class WordListData
    {
        public string name;
        public List<string> words;
        public string createdAt;
    }

    public FileWordListProvider(string filePath)
    {
        FilePath = filePath;
        Load();
    }

    public List<string> GetWords()
    {
        return new List<string>(words);
    }

    public void SetWords(List<string> newWords)
    {
        words = new List<string>(newWords);
    }

    public void SetName(string name)
    {
        DisplayName = name;
    }

    public void Save()
    {
        var data = new WordListData
        {
            name = DisplayName,
            words = words,
            createdAt = DateTime.UtcNow.ToString("o")
        };

        string dir = Path.GetDirectoryName(FilePath);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(FilePath, json);
    }

    public void Load()
    {
        if (!File.Exists(FilePath))
        {
            DisplayName = "New List";
            words = new List<string>();
            return;
        }

        string json = File.ReadAllText(FilePath);
        var data = JsonUtility.FromJson<WordListData>(json);
        DisplayName = data.name ?? "Untitled";
        words = data.words ?? new List<string>();
    }

    public static string GetWordListDirectory()
    {
        return Path.Combine(Application.persistentDataPath, "WordLists");
    }

    public static List<string> GetAllWordListFiles()
    {
        string dir = GetWordListDirectory();
        if (!Directory.Exists(dir))
            return new List<string>();

        var files = new List<string>(Directory.GetFiles(dir, "*.json"));
        return files;
    }
}
