using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class TxtWordListImporter
{
    public static FileWordListProvider ImportFromTxt(string txtPath)
    {
        string[] lines = File.ReadAllLines(txtPath);
        var words = new List<string>();
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                words.Add(trimmed);
        }

        string displayName = Path.GetFileNameWithoutExtension(txtPath);
        string jsonPath = Path.Combine(
            FileWordListProvider.GetWordListDirectory(),
            displayName + ".json"
        );

        var provider = new FileWordListProvider(jsonPath);
        provider.ResetToEnglish(); // clear stale Chinese/Mixed data from any previous import at this path
        provider.SetName(displayName);
        provider.SetWords(words);
        provider.Save();

        return provider;
    }

    public static void ExportToTxt(IWordListProvider provider, string txtPath)
    {
        File.WriteAllLines(txtPath, provider.GetWords());
    }
}
