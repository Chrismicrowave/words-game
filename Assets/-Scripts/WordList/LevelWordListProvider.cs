using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// IWordListProvider backed by a .txt file. Plain English lists stay plain text;
/// Chinese and mixed lists use the backward-compatible JSON-in-TXT format.
/// Challenge files under StreamingAssets/Levels are always treated as read-only.
/// </summary>
public class LevelWordListProvider : IWordListProvider
{
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

    public string DisplayName { get; private set; }
    public string DisplayNameZh { get; private set; }
    public LanguageMode LanguageMode { get; private set; } = LanguageMode.English;
    public bool IsEditable { get; private set; }
    public string FilePath { get; private set; }
    public string LoadError { get; private set; }
    public string IdentityPersistenceError { get; private set; }
    public bool IsValid => string.IsNullOrEmpty(LoadError);

    private List<string> words = new List<string>();
    private List<ChineseWordEntry> chineseWords = new List<ChineseWordEntry>();
    private List<MixedWordEntry> mixedWords = new List<MixedWordEntry>();
    private string uuid; // null for challenge lists

    public LevelWordListProvider(string filePath, bool isEditable = false)
    {
        FilePath = Path.GetFullPath(filePath);
        IsEditable = isEditable && !IsChallengePath(FilePath);
        DisplayName = Path.GetFileNameWithoutExtension(filePath);
        Load();
    }

    public List<string> GetWords() => new List<string>(words);
    public List<ChineseWordEntry> GetChineseWords() => CloneChineseWords(chineseWords);
    public List<MixedWordEntry> GetMixedWords() => CloneMixedWords(mixedWords);

    /// <summary>Replaces the in-memory content. Call TrySave after all fields are set.</summary>
    public void SetContent(LanguageMode mode, List<string> newWords,
        List<ChineseWordEntry> newChineseWords, List<MixedWordEntry> newMixedWords)
    {
        LanguageMode = mode;
        words = newWords != null ? new List<string>(newWords) : new List<string>();
        chineseWords = CloneChineseWords(newChineseWords);
        mixedWords = CloneMixedWords(newMixedWords);

        if (mode == LanguageMode.Chinese && chineseWords.Count > 0)
            words = GetChineseDisplays(chineseWords);
        else if (mode == LanguageMode.Mixed && mixedWords.Count > 0)
            words = GetMixedDisplays(mixedWords);
    }

    public void SetWords(List<string> newWords)
    {
        words = newWords != null ? new List<string>(newWords) : new List<string>();
    }

    public void SetChineseWords(List<ChineseWordEntry> newChineseWords)
    {
        chineseWords = CloneChineseWords(newChineseWords);
    }

    public void SetMixedWords(List<MixedWordEntry> newMixedWords)
    {
        mixedWords = CloneMixedWords(newMixedWords);
    }

    public void SetLanguageMode(LanguageMode mode) => LanguageMode = mode;

    public void SetDisplayName(string name, string nameZh = null)
    {
        if (!string.IsNullOrEmpty(name)) DisplayName = name;
        if (nameZh != null) DisplayNameZh = nameZh;
    }

    /// <summary>
    /// Stable key used by legacy ListTimeManager and the library sidecar.
    /// This getter never writes or migrates an ID.
    /// </summary>
    public string GetListKey()
    {
        if (IsEditable && !string.IsNullOrEmpty(uuid))
            return "cst_" + uuid;
        return "chg_" + Path.GetFileName(FilePath);
    }

    public string GetUUID() => uuid;

    /// <summary>Assigns a new full-length identity to a newly created or copied custom list.</summary>
    public void AssignNewUUID() => uuid = Guid.NewGuid().ToString("N");

    /// <summary>Sets a validated existing identity without changing the content file.</summary>
    public bool TrySetUUID(string value)
    {
        if (!IsValidId(value)) return false;
        uuid = value;
        return true;
    }

    public bool TrySave(out string error)
    {
        error = null;
        if (!IsValid)
        {
            error = LoadError;
            return false;
        }
        if (!IsEditable || IsChallengePath(FilePath))
        {
            error = "This word list is read-only.";
            return false;
        }
        if (string.IsNullOrEmpty(uuid)) AssignNewUUID();

        try
        {
            string dir = Path.GetDirectoryName(FilePath);
            if (string.IsNullOrEmpty(dir))
                throw new IOException("The word list path has no parent directory.");
            Directory.CreateDirectory(dir);

            string body;
            if (LanguageMode == LanguageMode.English && chineseWords.Count == 0 && mixedWords.Count == 0)
            {
                var sb = new StringBuilder();
                foreach (string word in words)
                    sb.AppendLine(word ?? string.Empty);
                body = sb.ToString();
            }
            else
            {
                var data = new LevelJsonData
                {
                    name = DisplayName,
                    nameZh = DisplayNameZh,
                    languageMode = LanguageMode.ToString(),
                    words = words.ToArray(),
                    chineseWords = CloneChineseWords(chineseWords),
                    mixedWords = CloneMixedWords(mixedWords)
                };
                body = JsonUtility.ToJson(data, true);
            }

            WriteRecoverable(FilePath, "// u:" + uuid + "\n" + body);
            LoadError = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Compatibility wrapper for existing callers that do not return save status.</summary>
    public void Save()
    {
        if (!TrySave(out string error))
            Debug.LogWarning("[LevelWordListProvider] Save failed: " + error);
    }

    /// <summary>Deletes only an editable custom path; challenges cannot be deleted through this provider.</summary>
    public void DeleteFile()
    {
        if (IsEditable && !IsChallengePath(FilePath) && File.Exists(FilePath))
            File.Delete(FilePath);
    }

    private void Load()
    {
        if (!File.Exists(FilePath))
        {
            words = new List<string>();
            chineseWords = new List<ChineseWordEntry>();
            mixedWords = new List<MixedWordEntry>();
            return;
        }

        string raw;
        try
        {
            raw = File.ReadAllText(FilePath);
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            return;
        }

        string trimmed = raw.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        string jsonContent = trimmed;
        bool hadIdHeader = false;
        if (jsonContent.StartsWith("// u:", StringComparison.Ordinal))
        {
            int nl = jsonContent.IndexOf('\n');
            if (nl >= 0)
            {
                string candidate = jsonContent.Substring(5, nl - 5).Trim();
                if (IsValidId(candidate))
                    uuid = candidate;
                jsonContent = jsonContent.Substring(nl + 1).TrimStart();
                hadIdHeader = true;
            }
            else
            {
                string candidate = jsonContent.Substring(5).Trim();
                if (IsValidId(candidate)) uuid = candidate;
                jsonContent = string.Empty;
                hadIdHeader = true;
            }
        }

        if (jsonContent.Length > 0 && jsonContent[0] == '{')
        {
            try
            {
                var data = JsonUtility.FromJson<LevelJsonData>(jsonContent);
                if (data == null)
                    throw new FormatException("The word list JSON is empty or invalid.");

                DisplayName = data.name ?? DisplayName;
                DisplayNameZh = data.nameZh;
                words = data.words != null ? new List<string>(data.words) : new List<string>();
                chineseWords = CloneChineseWords(data.chineseWords);
                mixedWords = CloneMixedWords(data.mixedWords);

                bool hasLanguageModeField = HasJsonProperty(jsonContent, "languageMode");
                if (hasLanguageModeField)
                {
                    if (!Enum.TryParse(data.languageMode, false, out LanguageMode parsedMode)
                        || !Enum.IsDefined(typeof(LanguageMode), parsedMode))
                        throw new FormatException("The word list declares an unknown languageMode.");
                    LanguageMode = parsedMode;
                }
                else if (mixedWords.Count > 0)
                    LanguageMode = LanguageMode.Mixed;
                else if (chineseWords.Count > 0)
                    LanguageMode = LanguageMode.Chinese;
                else
                    LanguageMode = ContainsChinese(words) ? LanguageMode.Mixed : LanguageMode.English;

                if (LanguageMode == LanguageMode.Chinese && chineseWords.Count > 0)
                    words = GetChineseDisplays(chineseWords);
                else if (LanguageMode == LanguageMode.Mixed && mixedWords.Count > 0)
                    words = GetMixedDisplays(mixedWords);

                if (IsEditable && string.IsNullOrEmpty(uuid))
                {
                    AssignNewUUID();
                    // Preserve legacy JSON bytes and structure while adding the identity header.
                    try { WriteRecoverable(FilePath, "// u:" + uuid + "\n" + jsonContent); }
                    catch (Exception ex) { IdentityPersistenceError = ex.Message; }
                }
                return;
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
                words = new List<string>();
                chineseWords = new List<ChineseWordEntry>();
                mixedWords = new List<MixedWordEntry>();
                return;
            }
        }

        words = new List<string>();
        chineseWords = new List<ChineseWordEntry>();
        mixedWords = new List<MixedWordEntry>();
        string[] lines = raw.Split('\n');
        foreach (string line in lines)
        {
            string value = line.Trim().TrimStart('\uFEFF');
            if (string.IsNullOrEmpty(value)) continue;
            if (value.StartsWith("// u:", StringComparison.Ordinal))
            {
                string candidate = value.Substring(5).Trim();
                if (IsValidId(candidate)) uuid = candidate;
                hadIdHeader = true;
                continue;
            }
            if (value.StartsWith("//", StringComparison.Ordinal)) continue;
            words.Add(value);
        }

        LanguageMode = ContainsChinese(words) ? LanguageMode.Mixed : LanguageMode.English;
        if (IsEditable && string.IsNullOrEmpty(uuid))
        {
            AssignNewUUID();
            // Keep the original text byte content after the new header.
            try { WriteRecoverable(FilePath, "// u:" + uuid + "\n" + raw); }
            catch (Exception ex) { IdentityPersistenceError = ex.Message; }
        }
    }

    private static bool HasJsonProperty(string json, string propertyName)
    {
        string token = "\"" + propertyName + "\"";
        int index = 0;
        while ((index = json.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            index += token.Length;
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
            if (index < json.Length && json[index] == ':') return true;
        }
        return false;
    }

    private static bool IsValidId(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 8 || value.Length > 32) return false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
    }

    private static bool ContainsChinese(List<string> values)
    {
        if (values == null) return false;
        foreach (string value in values)
            if (PinyinLookup.ContainsChinese(value)) return true;
        return false;
    }

    private static List<string> GetChineseDisplays(List<ChineseWordEntry> entries)
    {
        var result = new List<string>();
        foreach (var entry in entries) result.Add(entry != null ? entry.display ?? string.Empty : string.Empty);
        return result;
    }

    private static List<string> GetMixedDisplays(List<MixedWordEntry> entries)
    {
        var result = new List<string>();
        foreach (var entry in entries)
        {
            var sb = new StringBuilder();
            if (entry?.segments != null)
            {
                foreach (var segment in entry.segments)
                {
                    if (segment == null) continue;
                    if (segment.type == "english") sb.Append(segment.text);
                    else if (segment.entries != null)
                        foreach (var phase in segment.entries)
                            if (phase != null) sb.Append(phase.character);
                }
            }
            result.Add(sb.ToString());
        }
        return result;
    }

    public static List<ChineseWordEntry> CloneChineseWords(List<ChineseWordEntry> source)
    {
        var result = new List<ChineseWordEntry>();
        if (source == null) return result;
        foreach (var word in source)
        {
            if (word == null) { result.Add(null); continue; }
            var clone = new ChineseWordEntry { display = word.display, entries = new List<ChinesePhaseEntry>() };
            if (word.entries != null)
                foreach (var entry in word.entries)
                    clone.entries.Add(entry == null ? null : new ChinesePhaseEntry { character = entry.character, pinyin = entry.pinyin });
            result.Add(clone);
        }
        return result;
    }

    public static List<MixedWordEntry> CloneMixedWords(List<MixedWordEntry> source)
    {
        var result = new List<MixedWordEntry>();
        if (source == null) return result;
        foreach (var word in source)
        {
            if (word == null) { result.Add(null); continue; }
            var clone = new MixedWordEntry { segments = new List<MixedSegmentData>() };
            if (word.segments != null)
                foreach (var segment in word.segments)
                {
                    if (segment == null) { clone.segments.Add(null); continue; }
                    clone.segments.Add(new MixedSegmentData
                    {
                        type = segment.type,
                        text = segment.text,
                        entries = ClonePhaseEntries(segment.entries)
                    });
                }
            result.Add(clone);
        }
        return result;
    }

    private static List<ChinesePhaseEntry> ClonePhaseEntries(List<ChinesePhaseEntry> source)
    {
        var result = new List<ChinesePhaseEntry>();
        if (source == null) return result;
        foreach (var entry in source)
            result.Add(entry == null ? null : new ChinesePhaseEntry { character = entry.character, pinyin = entry.pinyin });
        return result;
    }

    public static bool IsChallengeFilePath(string path)
    {
        return IsWithinDirectory(path, GetChallengeDirectory());
    }

    private static bool IsChallengePath(string path) => IsChallengeFilePath(path);

    private static bool IsWithinDirectory(string path, string directory)
    {
        try
        {
            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullDir = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(fullPath, fullDir, StringComparison.OrdinalIgnoreCase)) return true;
            string prefix = fullDir + Path.DirectorySeparatorChar;
            if (!prefix.Contains("\\") && Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
                prefix = fullDir + Path.AltDirectorySeparatorChar;
            return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void WriteRecoverable(string path, string content)
    {
        string tempPath = path + ".tmp";
        string backupPath = path + ".bak";
        File.WriteAllText(tempPath, content, Utf8NoBom);
        if (!File.Exists(path))
        {
            File.Move(tempPath, path);
            return;
        }

        File.Copy(path, backupPath, true);
        try
        {
            File.Replace(tempPath, path, null);
        }
        catch (PlatformNotSupportedException)
        {
            File.Delete(path);
            File.Move(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public static string GetChallengeDirectory() => Path.Combine(Application.streamingAssetsPath, "Levels");
    public static string GetCustomDirectory() => Path.Combine(Application.persistentDataPath, "Levels", "Custom");

    public static void SeedCustomDirectoryIfEmpty()
    {
        string dir = GetCustomDirectory();
        if (Directory.Exists(dir) && Directory.GetFiles(dir, "*.txt").Length > 0) return;
        string srcPath = Path.Combine(Application.streamingAssetsPath, "DefaultCustom", "custom-list-01.txt");
        if (!File.Exists(srcPath)) return;
        Directory.CreateDirectory(dir);
        string destPath = Path.Combine(dir, "custom-list-01.txt");
        File.WriteAllText(destPath, "// u:" + Guid.NewGuid().ToString("N") + "\n" + File.ReadAllText(srcPath), Utf8NoBom);
    }

    public static List<LevelWordListProvider> ScanDirectory(string directory, bool isEditable = false)
    {
        var providers = new List<LevelWordListProvider>();
        if (!Directory.Exists(directory)) return providers;
        var files = Directory.GetFiles(directory, "*.txt");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (string file in files) providers.Add(new LevelWordListProvider(file, isEditable));
        return providers;
    }

    public static LevelWordListProvider CreateNewCustom()
    {
        string dir = GetCustomDirectory();
        Directory.CreateDirectory(dir);
        int n = 1;
        string path;
        do { path = Path.Combine(dir, $"new_list_{n++}.txt"); } while (File.Exists(path));
        var provider = new LevelWordListProvider(path, true);
        provider.AssignNewUUID();
        provider.SetContent(LanguageMode.English, new List<string> { "hello world" }, null, null);
        return provider.TrySave(out _) ? new LevelWordListProvider(path, true) : null;
    }

    public LevelWordListProvider Duplicate()
    {
        string dir = GetCustomDirectory();
        Directory.CreateDirectory(dir);
        string baseName = Path.GetFileNameWithoutExtension(FilePath);
        int n = 1;
        string path;
        do { path = Path.Combine(dir, $"{baseName}_copy_{n++}.txt"); } while (File.Exists(path));
        var copy = new LevelWordListProvider(path, true);
        copy.DisplayName = DisplayName + " Copy";
        copy.DisplayNameZh = DisplayNameZh;
        copy.AssignNewUUID();
        copy.SetContent(LanguageMode, words, chineseWords, mixedWords);
        return copy.TrySave(out _) ? new LevelWordListProvider(path, true) : null;
    }

    [Serializable]
    private class LevelJsonData
    {
        public string name;
        public string nameZh;
        public string languageMode;
        public string[] words;
        public List<ChineseWordEntry> chineseWords;
        public List<MixedWordEntry> mixedWords;
    }
}
