using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class DailyWordListProvider : IWordListProvider
{
    public string DisplayName { get; private set; }
    public bool IsEditable => false;
    public string FilePath { get; private set; }

    private List<string> words = new List<string>();

    [Serializable]
    private class DailyListData
    {
        public string name;
        public List<string> words;
        public string date;
    }

    public DailyWordListProvider(string filePath)
    {
        FilePath = filePath;
        Load();
    }

    public List<string> GetWords() => new List<string>(words);

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
    }

    public static string GetDailyListDirectory() =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "DailyLists"));
}
