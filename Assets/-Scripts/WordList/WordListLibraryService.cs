using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>Detached, typed content snapshot used by the Library and editor bridge.</summary>
[Serializable]
public sealed class WordListLibraryContent
{
    public LanguageMode LanguageMode;
    public List<string> Words = new List<string>();
    public List<ChineseWordEntry> ChineseWords = new List<ChineseWordEntry>();
    public List<MixedWordEntry> MixedWords = new List<MixedWordEntry>();

    public WordListLibraryContent Clone()
    {
        return new WordListLibraryContent
        {
            LanguageMode = LanguageMode,
            Words = Words != null ? new List<string>(Words) : new List<string>(),
            ChineseWords = LevelWordListProvider.CloneChineseWords(ChineseWords),
            MixedWords = LevelWordListProvider.CloneMixedWords(MixedWords)
        };
    }

    public static WordListLibraryContent FromProvider(LevelWordListProvider provider)
    {
        if (provider == null) return null;
        return new WordListLibraryContent
        {
            LanguageMode = provider.LanguageMode,
            Words = provider.GetWords(),
            ChineseWords = provider.GetChineseWords(),
            MixedWords = provider.GetMixedWords()
        };
    }
}

/// <summary>Runtime projection of a local list plus sidecar metadata and records.</summary>
public sealed class WordListLibraryItem
{
    public string LocalListId { get; internal set; }
    public string FilePath { get; internal set; }
    public string Title { get; internal set; }
    public string Description { get; internal set; }
    public List<string> Tags { get; internal set; }
    public bool IsFavorite { get; internal set; }
    public bool IsEditable { get; internal set; }
    public bool IsChallenge { get; internal set; }
    public LanguageMode LanguageMode { get; internal set; }
    public int ContentRevision { get; internal set; }
    public string ContentHash { get; internal set; }
    public string AddedAtUtc { get; internal set; }
    public string ModifiedAtUtc { get; internal set; }
    public string ContentModifiedAtUtc { get; internal set; }
    public string LastPlayedAtUtc { get; internal set; }
    public int PlayCount { get; internal set; }
    public WordListRevisionRecord CurrentRecord { get; internal set; }
    public List<WordListRevisionRecord> ArchivedRecords { get; internal set; }
    public WordListLibraryContent Content { get; internal set; }
    public LevelWordListProvider Provider { get; internal set; }
}

[Serializable]
public sealed class WordListLibraryState
{
    public int schemaVersion = 1;
    public List<WordListLibraryEntry> lists = new List<WordListLibraryEntry>();
}

[Serializable]
public sealed class WordListLibraryEntry
{
    public string localListId;
    public string filePath;
    public string title;
    public string description;
    public List<string> tags = new List<string>();
    public bool isFavorite;
    public string addedAtUtc;
    public string modifiedAtUtc;
    public string contentModifiedAtUtc;
    public string lastPlayedAtUtc;
    public int playCount;
    public string contentHash;
    public int contentRevision;
    public WordListRevisionRecord currentRecord;
    public List<WordListRevisionRecord> archivedRecords = new List<WordListRevisionRecord>();
}

[Serializable]
public sealed class WordListRevisionRecord
{
    public int contentRevision;
    public string contentHash;
    public int highestStars;
    public WordListRunSummary latest;
    public WordListRunSummary best;

    public bool HasRecord => latest != null && latest.hasResult;
}

[Serializable]
public sealed class WordListRunSummary
{
    public bool hasResult;
    public float totalTimeSeconds;
    public int phaseCount;
    public int errors;
    public int stars;
    public string completedAtUtc;
}

/// <summary>
/// Production sidecar for custom-list metadata and version-scoped records. Content remains
/// in the existing Levels/Custom files; this class does not depend on Core or the mock UI.
/// </summary>
public sealed class WordListLibraryService
{
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
    private static WordListLibraryService shared;

    public static WordListLibraryService Shared
    {
        get
        {
            if (shared == null)
                shared = new WordListLibraryService(
                    LevelWordListProvider.GetCustomDirectory(),
                    Path.Combine(Application.persistentDataPath, "WordListLibrary.json"));
            return shared;
        }
    }

    private readonly string customDirectory;
    private readonly string statePath;
    private readonly Func<DateTime> utcNow;
    private readonly Dictionary<string, LevelWordListProvider> providers =
        new Dictionary<string, LevelWordListProvider>(StringComparer.Ordinal);
    private LevelWordListProvider activeProvider;
    private WordListLibraryState state;
    private bool stateWritable = true;
    private string sidecarLoadError;
    private string activeListId;
    private string recoverySourcePath;

    public event Action<WordListLibraryItem> ContentChanged;

    public string SidecarLoadError => sidecarLoadError;

    public WordListLibraryService(string customDirectory, string statePath, Func<DateTime> utcNow = null)
    {
        if (string.IsNullOrEmpty(customDirectory)) throw new ArgumentException("A custom-list directory is required.", nameof(customDirectory));
        if (string.IsNullOrEmpty(statePath)) throw new ArgumentException("A sidecar path is required.", nameof(statePath));
        this.customDirectory = Path.GetFullPath(customDirectory);
        this.statePath = Path.GetFullPath(statePath);
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        state = LoadState();
    }

    /// <summary>Returns only owned custom lists, scanned in deterministic filename order.</summary>
    public IReadOnlyList<WordListLibraryItem> GetAll()
    {
        providers.Clear();
        var result = new List<WordListLibraryItem>();
        if (!Directory.Exists(customDirectory)) return result;

        string[] files = Directory.GetFiles(customDirectory, "*.txt");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            var scannedProvider = new LevelWordListProvider(file, true);
            var provider = scannedProvider;
            if (!provider.IsValid || !string.IsNullOrEmpty(provider.IdentityPersistenceError))
            {
                if (!string.IsNullOrEmpty(provider.IdentityPersistenceError))
                    Debug.LogWarning("[WordListLibrary] Could not persist the custom-list identity for " + file + ": " + provider.IdentityPersistenceError);
                continue;
            }

            string id = provider.GetListKey();
            if (!seenIds.Add(id))
            {
                // Keep the first deterministic file for the old ID; never merge content.
                if (IsActivePath(file) && activeProvider != null
                    && string.Equals(activeProvider.GetListKey(), id, StringComparison.Ordinal))
                {
                    activeProvider.AssignNewUUID();
                    provider = activeProvider;
                    activeListId = provider.GetListKey();
                }
                else
                    provider.AssignNewUUID();

                if (!provider.TrySave(out string saveError))
                {
                    Debug.LogWarning("[WordListLibrary] Could not repair duplicate identity for " + file + ": " + saveError);
                    continue;
                }
                id = provider.GetListKey();
                seenIds.Add(id);
            }

            if (IsActivePath(file) && activeProvider != null
                && string.Equals(activeProvider.GetListKey(), id, StringComparison.Ordinal))
                provider = activeProvider;

            providers[id] = provider;
            WordListLibraryEntry entry = FindEntry(id);
            if (entry == null)
            {
                entry = CreateEntry(provider, id, ComputeContentHash(WordListLibraryContent.FromProvider(provider)));
                state.lists.Add(entry);
                if (!PersistState())
                {
                    state.lists.Remove(entry);
                    Debug.LogWarning("[WordListLibrary] Could not save the new list index: " + sidecarLoadError);
                    continue;
                }
            }
            else
            {
                if (!SyncEntryToProvider(entry, provider, id, true))
                {
                    providers.Remove(id);
                    continue;
                }
                entry = FindEntry(id);
            }
            result.Add(Project(entry, provider));
        }
        return result;
    }

    public WordListLibraryItem FindById(string localListId)
    {
        if (string.IsNullOrEmpty(localListId)) return null;
        GetAll();
        WordListLibraryEntry entry = FindEntry(localListId);
        LevelWordListProvider provider;
        return entry != null && providers.TryGetValue(localListId, out provider) ? Project(entry, provider) : null;
    }

    /// <summary>Registers an existing provider, including read-only challenges outside GetAll().</summary>
    public WordListLibraryItem GetOrRegister(LevelWordListProvider provider)
    {
        if (provider == null || !provider.IsValid) return null;
        if (provider.IsEditable && !string.IsNullOrEmpty(provider.IdentityPersistenceError))
        {
            sidecarLoadError = "The custom word list identity could not be saved: " + provider.IdentityPersistenceError;
            return null;
        }
        provider = ResolveProvider(provider.GetListKey(), provider.FilePath) ?? provider;
        string id = provider.GetListKey();
        WordListLibraryEntry entry = FindEntry(id);
        if (entry == null)
        {
            entry = CreateEntry(provider, id, ComputeContentHash(WordListLibraryContent.FromProvider(provider)));
            state.lists.Add(entry);
            if (!PersistState())
            {
                state.lists.Remove(entry);
                return null;
            }
        }
        else if (!SyncEntryToProvider(entry, provider, id, true))
            return null;
        entry = FindEntry(id);

        if (provider.IsEditable) providers[id] = provider;
        return Project(entry, provider);
    }

    public WordListLibraryItem FindByProvider(LevelWordListProvider provider)
    {
        if (provider == null) return null;
        provider = ResolveProvider(provider.GetListKey(), provider.FilePath) ?? provider;
        WordListLibraryEntry entry = FindEntry(provider.GetListKey());
        if (entry == null) return null;
        return Project(entry, provider);
    }

    public void SetActiveListId(string localListId) => activeListId = localListId;

    public WordListLibraryItem SetActiveProvider(LevelWordListProvider provider)
    {
        activeProvider = provider;
        if (provider == null)
        {
            activeListId = null;
            return null;
        }
        activeListId = provider.GetListKey();
        WordListLibraryItem item = GetOrRegister(provider);
        providers[activeListId] = provider;
        return item;
    }

    public void ClearActiveProvider(LevelWordListProvider expectedProvider = null)
    {
        if (expectedProvider != null && !ReferenceEquals(expectedProvider, activeProvider)) return;
        if (activeProvider != null)
        {
            LevelWordListProvider previous = activeProvider;
            string id = previous.GetListKey();
            activeProvider = null;
            if (providers.TryGetValue(id, out LevelWordListProvider registered)
                && ReferenceEquals(registered, previous))
                providers.Remove(id);
        }
        activeListId = null;
    }

    public void ClearActiveListId(string localListId = null)
    {
        if (localListId == null || string.Equals(activeListId, localListId, StringComparison.Ordinal))
            activeListId = null;
    }

    public bool TryCreateList(string title, LanguageMode mode, out WordListLibraryItem item, out string error)
    {
        return TryCreateList(title, new WordListLibraryContent { LanguageMode = mode }, out item, out error);
    }

    /// <summary>Creates a list and content in one operation; no temporary placeholder list is exposed.</summary>
    public bool TryCreateList(string title, WordListLibraryContent content,
        out WordListLibraryItem item, out string error)
    {
        item = null;
        if (!CanWriteState(out error)) return false;
        if (!TryValidateTitle(title, out string normalizedTitle, out error)) return false;
        if (!TryValidateContent(content, out WordListLibraryContent normalizedContent, out error)) return false;

        string id = Guid.NewGuid().ToString("N");
        string path = Path.Combine(customDirectory, "list_" + id + ".txt");
        var provider = new LevelWordListProvider(path, true);
        provider.SetDisplayName(normalizedTitle);
        provider.AssignNewUUID();
        // Keep path and local ID generated from one GUID for a new list.
        provider.TrySetUUID(id);
        provider.SetContent(normalizedContent.LanguageMode, normalizedContent.Words,
            normalizedContent.ChineseWords, normalizedContent.MixedWords);
        if (!provider.TrySave(out error)) return false;

        string actualId = provider.GetListKey();
        var entry = CreateEntry(provider, actualId, ComputeContentHash(normalizedContent));
        entry.title = normalizedTitle;
        entry.description = string.Empty;
        entry.contentRevision = 1;
        state.lists.Add(entry);
        if (!PersistState())
        {
            state.lists.Remove(entry);
            PreserveUnregisteredFile(path);
            error = sidecarLoadError ?? "Could not save the Library index.";
            return false;
        }

        providers[actualId] = provider;
        item = Project(entry, provider);
        error = null;
        return true;
    }

    public bool TryImportList(string sourcePath, out WordListLibraryItem item, out string error)
    {
        item = null;
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            error = "The source word list could not be found.";
            return false;
        }
        var source = new LevelWordListProvider(sourcePath, false);
        if (!source.IsValid)
        {
            error = source.LoadError;
            return false;
        }
        return TryDuplicateProvider(source, out item, out error);
    }

    public bool TryDuplicateList(string localListId, out WordListLibraryItem item, out string error)
    {
        WordListLibraryItem source = FindById(localListId);
        if (source == null)
        {
            item = null;
            error = "The source word list could not be found.";
            return false;
        }
        return TryDuplicateProvider(source.Provider, out item, out error);
    }

    public bool TryDuplicateProvider(LevelWordListProvider source, out WordListLibraryItem item, out string error)
    {
        item = null;
        if (!CanWriteState(out error)) return false;
        if (source == null || !source.IsValid)
        {
            error = source?.LoadError ?? "The source word list could not be read.";
            return false;
        }
        var content = WordListLibraryContent.FromProvider(source);
        // A copy preserves the source's exact structured content, including recoverable
        // legacy pronunciation gaps. New edits still pass strict validation below.
        WordListLibraryContent normalizedContent = content.Clone();

        string id = Guid.NewGuid().ToString("N");
        string path = Path.Combine(customDirectory, "list_" + id + ".txt");
        var copy = new LevelWordListProvider(path, true);
        copy.SetDisplayName(source.DisplayName + " Copy", source.DisplayNameZh);
        copy.TrySetUUID(id);
        copy.SetContent(normalizedContent.LanguageMode, normalizedContent.Words,
            normalizedContent.ChineseWords, normalizedContent.MixedWords);
        if (!copy.TrySave(out error)) return false;

        var entry = CreateEntry(copy, copy.GetListKey(), ComputeContentHash(normalizedContent));
        entry.title = copy.DisplayName;
        state.lists.Add(entry);
        if (!PersistState())
        {
            state.lists.Remove(entry);
            PreserveUnregisteredFile(path);
            error = sidecarLoadError ?? "Could not save the Library index.";
            return false;
        }

        providers[entry.localListId] = copy;
        item = Project(entry, copy);
        error = null;
        return true;
    }

    public bool TryUpdateMetadata(string localListId, string title, string description,
        IEnumerable<string> tags, bool favorite, out WordListLibraryItem item, out string error)
    {
        return TrySaveListEdit(localListId, title, description, tags, favorite, null, out item, out error);
    }

    /// <summary>
    /// Validates metadata and optional content before writing either. Metadata-only updates do not
    /// rewrite the canonical file or change its content revision/hash.
    /// </summary>
    public bool TrySaveListEdit(string localListId, string title, string description,
        IEnumerable<string> tags, bool favorite, WordListLibraryContent contentOrNull,
        out WordListLibraryItem item, out string error)
    {
        item = null;
        if (!CanWriteState(out error)) return false;
        GetAll(); // Ensure the current file object is cached; active provider identity is retained by the scan.
        WordListLibraryEntry entry = FindEntry(localListId);
        LevelWordListProvider provider;
        if (entry == null || !providers.TryGetValue(localListId, out provider)
            || !provider.IsEditable || !File.Exists(provider.FilePath))
        {
            error = "The custom word list is unavailable or read-only.";
            return false;
        }
        if (!TryValidateTitle(title, out string normalizedTitle, out error)) return false;
        List<string> normalizedTags = NormalizeTags(tags);
        string normalizedDescription = description == null ? string.Empty : description.Trim();

        WordListLibraryContent normalizedContent = null;
        bool contentChanged = false;
        byte[] oldFileBytes = null;
        WordListLibraryContent oldContent = WordListLibraryContent.FromProvider(provider);
        string oldHash = ComputeContentHash(oldContent);
        if (contentOrNull != null)
        {
            if (!TryValidateContent(contentOrNull, out normalizedContent, out error)) return false;
            string newHash = ComputeContentHash(normalizedContent);
            contentChanged = !string.Equals(oldHash, newHash, StringComparison.Ordinal);
            if (contentChanged)
            {
                try { oldFileBytes = File.ReadAllBytes(provider.FilePath); }
                catch (Exception ex) { error = ex.Message; return false; }
            }
        }

        WordListLibraryEntry oldEntry = CloneEntry(entry);
        bool metadataChanged = !string.Equals(entry.title ?? string.Empty, normalizedTitle, StringComparison.Ordinal)
            || !string.Equals(entry.description ?? string.Empty, normalizedDescription, StringComparison.Ordinal)
            || entry.isFavorite != favorite
            || !StringListsEqual(entry.tags, normalizedTags);
        if (!contentChanged && !metadataChanged)
        {
            provider.SetDisplayName(normalizedTitle);
            item = Project(entry, provider);
            error = null;
            return true;
        }

        if (contentChanged)
        {
            provider.SetContent(normalizedContent.LanguageMode, normalizedContent.Words,
                normalizedContent.ChineseWords, normalizedContent.MixedWords);
            if (!provider.TrySave(out error))
            {
                provider.SetContent(oldContent.LanguageMode, oldContent.Words, oldContent.ChineseWords, oldContent.MixedWords);
                return false;
            }

            ArchiveCurrentRecord(entry);
            entry.contentRevision = Math.Max(1, entry.contentRevision + 1);
            entry.contentHash = ComputeContentHash(normalizedContent);
            entry.currentRecord = NewRevisionRecord(entry.contentRevision, entry.contentHash);
            entry.contentModifiedAtUtc = NowUtc();
        }

        entry.title = normalizedTitle;
        entry.description = normalizedDescription;
        entry.tags = normalizedTags;
        entry.isFavorite = favorite;
        entry.modifiedAtUtc = NowUtc();
        entry.filePath = provider.FilePath;
        if (!PersistState())
        {
            ReplaceEntry(oldEntry);
            if (contentChanged)
            {
                try { RestoreFile(provider.FilePath, oldFileBytes); }
                catch (Exception restoreError)
                {
                    error = (sidecarLoadError ?? "Could not save the Library index.")
                        + " Content rollback failed; the original file is retained at .bak: " + restoreError.Message;
                    provider.SetContent(oldContent.LanguageMode, oldContent.Words, oldContent.ChineseWords, oldContent.MixedWords);
                    return false;
                }
                provider.SetContent(oldContent.LanguageMode, oldContent.Words, oldContent.ChineseWords, oldContent.MixedWords);
            }
            error = sidecarLoadError ?? "Could not save the Library index.";
            return false;
        }

        provider.SetDisplayName(normalizedTitle);
        item = Project(entry, provider);
        if (contentChanged) RaiseContentChanged(item);
        error = null;
        return true;
    }

    public bool TryDeleteList(string localListId, out string error)
    {
        if (!CanWriteState(out error)) return false;
        if (string.Equals(activeListId, localListId, StringComparison.Ordinal))
        {
            error = "Select another list before deleting the active list.";
            return false;
        }
        WordListLibraryEntry entry = FindEntry(localListId);
        LevelWordListProvider provider;
        if (entry == null || !providers.TryGetValue(localListId, out provider)
            || !provider.IsEditable || LevelWordListProvider.IsChallengeFilePath(provider.FilePath))
        {
            error = "The custom word list is unavailable or read-only.";
            return false;
        }

        string archivedPath = provider.FilePath + ".deleted." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        bool moved = false;
        try
        {
            if (File.Exists(provider.FilePath))
            {
                File.Move(provider.FilePath, archivedPath);
                moved = true;
            }
            state.lists.Remove(entry);
            if (!PersistState())
            {
                state.lists.Add(entry);
                if (moved) File.Move(archivedPath, provider.FilePath);
                error = sidecarLoadError ?? "Could not save the Library index.";
                return false;
            }
            providers.Remove(localListId);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            if (moved && !File.Exists(provider.FilePath) && File.Exists(archivedPath))
                try { File.Move(archivedPath, provider.FilePath); } catch { }
            error = ex.Message;
            return false;
        }
    }

    public bool TryRecordRun(LevelWordListProvider provider, float timeSeconds, int phaseCount,
        int errors, int stars, out string error)
    {
        if (!CanWriteState(out error)) return false;
        if (provider == null || !provider.IsValid || !File.Exists(provider.FilePath))
        {
            error = "The completed word list no longer exists; no record was written.";
            return false;
        }
        if (timeSeconds < 0 || phaseCount < 0 || errors < 0 || stars < 0 || stars > 3)
        {
            error = "The completed run data is invalid.";
            return false;
        }

        WordListLibraryItem current = GetOrRegister(provider);
        if (current == null)
        {
            error = sidecarLoadError ?? "Could not register the completed word list.";
            return false;
        }
        WordListLibraryEntry entry = FindEntry(current.LocalListId);
        WordListLibraryEntry previousEntry = CloneEntry(entry);
        WordListRevisionRecord record = EnsureCurrentRecord(entry);
        string now = NowUtc();
        record.latest = new WordListRunSummary
        {
            hasResult = true,
            totalTimeSeconds = timeSeconds,
            phaseCount = phaseCount,
            errors = errors,
            stars = stars,
            completedAtUtc = now
        };
        record.highestStars = Math.Max(record.highestStars, stars);
        if (record.best == null || !record.best.hasResult || timeSeconds < record.best.totalTimeSeconds)
        {
            record.best = CloneSummary(record.latest);
        }
        entry.playCount++;
        entry.lastPlayedAtUtc = now;
        entry.modifiedAtUtc = now;
        if (!PersistState())
        {
            ReplaceEntry(previousEntry);
            error = sidecarLoadError ?? "Could not save the completed run record.";
            return false;
        }
        error = null;
        return true;
    }

    public bool MarkPlayed(LevelWordListProvider provider, out string error)
    {
        if (!CanWriteState(out error)) return false;
        if (provider == null || !provider.IsValid || !File.Exists(provider.FilePath))
        {
            error = "The word list no longer exists.";
            return false;
        }
        WordListLibraryItem current = GetOrRegister(provider);
        if (current == null) { error = sidecarLoadError ?? "Could not register the word list."; return false; }
        WordListLibraryEntry entry = FindEntry(current.LocalListId);
        WordListLibraryEntry previousEntry = CloneEntry(entry);
        entry.lastPlayedAtUtc = NowUtc();
        entry.modifiedAtUtc = entry.lastPlayedAtUtc;
        if (!PersistState())
        {
            ReplaceEntry(previousEntry);
            error = sidecarLoadError ?? "Could not save play history.";
            return false;
        }
        error = null;
        return true;
    }

    private void RaiseContentChanged(WordListLibraryItem item)
    {
        Action<WordListLibraryItem> listeners = ContentChanged;
        if (listeners == null) return;
        foreach (Delegate listener in listeners.GetInvocationList())
        {
            try { ((Action<WordListLibraryItem>)listener)(item); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }

    public static string ComputeContentHash(WordListLibraryContent content)
    {
        if (content == null) return string.Empty;
        var canonical = new StringBuilder();
        AppendField(canonical, content.LanguageMode.ToString());
        AppendStringList(canonical, content.Words);
        AppendChineseWords(canonical, content.ChineseWords);
        AppendMixedWords(canonical, content.MixedWords);
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
            var result = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash) result.Append(value.ToString("x2"));
            return result.ToString();
        }
    }

    private static void AppendStringList(StringBuilder sb, List<string> values)
    {
        AppendField(sb, values != null ? values.Count.ToString() : "-1");
        if (values != null) foreach (string value in values) AppendField(sb, value);
    }

    private static void AppendChineseWords(StringBuilder sb, List<ChineseWordEntry> values)
    {
        AppendField(sb, values != null ? values.Count.ToString() : "-1");
        if (values == null) return;
        foreach (var word in values)
        {
            AppendField(sb, word == null ? null : word.display);
            AppendField(sb, word?.entries != null ? word.entries.Count.ToString() : "-1");
            if (word?.entries == null) continue;
            foreach (var phase in word.entries)
            {
                AppendField(sb, phase == null ? null : phase.character);
                AppendField(sb, phase == null ? null : phase.pinyin);
            }
        }
    }

    private static void AppendMixedWords(StringBuilder sb, List<MixedWordEntry> values)
    {
        AppendField(sb, values != null ? values.Count.ToString() : "-1");
        if (values == null) return;
        foreach (var word in values)
        {
            AppendField(sb, word?.segments != null ? word.segments.Count.ToString() : "-1");
            if (word?.segments == null) continue;
            foreach (var segment in word.segments)
            {
                AppendField(sb, segment == null ? null : segment.type);
                AppendField(sb, segment == null ? null : segment.text);
                AppendField(sb, segment?.entries != null ? segment.entries.Count.ToString() : "-1");
                if (segment?.entries == null) continue;
                foreach (var phase in segment.entries)
                {
                    AppendField(sb, phase == null ? null : phase.character);
                    AppendField(sb, phase == null ? null : phase.pinyin);
                }
            }
        }
    }

    private static void AppendField(StringBuilder sb, string value)
    {
        if (value == null) sb.Append("-1:");
        else sb.Append(value.Length).Append(':').Append(value);
        sb.Append('|');
    }

    private WordListLibraryState LoadState()
    {
        bool hasAnySidecar = File.Exists(statePath) || File.Exists(statePath + ".bak")
            || File.Exists(statePath + ".replaced") || Directory.Exists(Path.GetDirectoryName(statePath));
        if (!hasAnySidecar) return NewState();

        var candidates = new List<string> { statePath, statePath + ".bak", statePath + ".replaced" };
        string stateDirectory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrEmpty(stateDirectory) && Directory.Exists(stateDirectory))
        {
            string prefix = Path.GetFileName(statePath) + ".replaced.";
            foreach (string candidate in Directory.GetFiles(stateDirectory, prefix + "*"))
                candidates.Add(candidate);
            candidates.Sort((left, right) =>
            {
                if (string.Equals(left, statePath, StringComparison.Ordinal)) return -1;
                if (string.Equals(right, statePath, StringComparison.Ordinal)) return 1;
                return string.CompareOrdinal(right, left);
            });
        }

        Exception lastError = null;
        foreach (string candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var loaded = JsonUtility.FromJson<WordListLibraryState>(File.ReadAllText(candidate));
                if (loaded == null || loaded.schemaVersion != 1 || loaded.lists == null)
                    throw new FormatException("The Library sidecar has an unsupported or invalid schema.");
                if (!PathsEqual(candidate, statePath))
                {
                    recoverySourcePath = candidate;
                    sidecarLoadError = "Recovered Library metadata from " + Path.GetFileName(candidate) + ".";
                }
                return loaded;
            }
            catch (Exception ex) { lastError = ex; }
        }

        bool hasCandidateFiles = candidates.Any(File.Exists);
        if (hasCandidateFiles)
        {
            sidecarLoadError = "Could not read Library metadata; existing sidecar files were left untouched: "
                + (lastError != null ? lastError.Message : "no recoverable sidecar was found.");
            stateWritable = false;
        }
        return NewState();
    }

    private bool PersistState()
    {
        if (!stateWritable)
        {
            if (string.IsNullOrEmpty(sidecarLoadError)) sidecarLoadError = "Library metadata is read-only because the existing sidecar is corrupt.";
            return false;
        }
        try
        {
            string directory = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = statePath + ".tmp";
            string backup = statePath + ".bak";
            File.WriteAllText(temp, JsonUtility.ToJson(state, true), Utf8NoBom);
            if (!File.Exists(statePath)) File.Move(temp, statePath);
            else if (!string.IsNullOrEmpty(recoverySourcePath))
            {
                string preservedPrimary = statePath + ".recovered-corrupt." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                File.Move(statePath, preservedPrimary);
                try { File.Move(temp, statePath); }
                catch
                {
                    if (!File.Exists(statePath)) File.Move(preservedPrimary, statePath);
                    throw;
                }
            }
            else
            {
                File.Copy(statePath, backup, true);
                try { File.Replace(temp, statePath, null); }
                catch (PlatformNotSupportedException)
                {
                    string replaced = statePath + ".replaced";
                    if (File.Exists(replaced))
                        File.Move(replaced, replaced + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
                    File.Move(statePath, replaced);
                    try { File.Move(temp, statePath); }
                    catch { if (!File.Exists(statePath)) File.Move(replaced, statePath); throw; }
                }
            }
            recoverySourcePath = null;
            sidecarLoadError = null;
            return true;
        }
        catch (Exception ex)
        {
            sidecarLoadError = ex.Message;
            return false;
        }
        finally
        {
            string temp = statePath + ".tmp";
            if (File.Exists(temp)) try { File.Delete(temp); } catch { }
        }
    }

    private bool CanWriteState(out string error)
    {
        if (!stateWritable)
        {
            error = sidecarLoadError ?? "Library metadata is read-only because the existing sidecar is corrupt.";
            return false;
        }
        error = null;
        return true;
    }

    private WordListLibraryEntry CreateEntry(LevelWordListProvider provider, string id, string hash)
    {
        string now = NowUtc();
        return new WordListLibraryEntry
        {
            localListId = id,
            filePath = provider.FilePath,
            title = provider.DisplayName,
            description = string.Empty,
            tags = new List<string>(),
            addedAtUtc = now,
            modifiedAtUtc = now,
            contentModifiedAtUtc = now,
            contentHash = hash,
            contentRevision = 1,
            currentRecord = NewRevisionRecord(1, hash),
            archivedRecords = new List<WordListRevisionRecord>()
        };
    }

    private bool SyncEntryToProvider(WordListLibraryEntry entry, LevelWordListProvider provider, string id, bool archiveChangedContent)
    {
        if (entry == null || provider == null) return false;
        WordListLibraryEntry before = CloneEntry(entry);
        string currentHash = ComputeContentHash(WordListLibraryContent.FromProvider(provider));
        if (string.IsNullOrEmpty(entry.contentHash))
        {
            entry.contentHash = currentHash;
            if (entry.contentRevision <= 0) entry.contentRevision = 1;
            if (entry.currentRecord == null) entry.currentRecord = NewRevisionRecord(entry.contentRevision, currentHash);
        }
        else if (!string.Equals(entry.contentHash, currentHash, StringComparison.Ordinal))
        {
            if (archiveChangedContent) ArchiveCurrentRecord(entry);
            entry.contentRevision = Math.Max(1, entry.contentRevision + 1);
            entry.contentHash = currentHash;
            entry.contentModifiedAtUtc = NowUtc();
            entry.currentRecord = NewRevisionRecord(entry.contentRevision, currentHash);
            entry.modifiedAtUtc = entry.contentModifiedAtUtc;
        }
        entry.filePath = provider.FilePath;
        if (string.IsNullOrEmpty(entry.title)) entry.title = provider.DisplayName;
        if (!string.IsNullOrEmpty(entry.title)) provider.SetDisplayName(entry.title);
        if (!EntriesEqual(before, entry) && !PersistState())
        {
            ReplaceEntry(before);
            provider.SetDisplayName(before.title);
            Debug.LogWarning("[WordListLibrary] Could not synchronize metadata: " + sidecarLoadError);
            return false;
        }
        return true;
    }

    private static bool EntriesEqual(WordListLibraryEntry first, WordListLibraryEntry second)
    {
        return string.Equals(JsonUtility.ToJson(first), JsonUtility.ToJson(second), StringComparison.Ordinal);
    }

    private WordListLibraryItem Project(WordListLibraryEntry entry, LevelWordListProvider provider)
    {
        return new WordListLibraryItem
        {
            LocalListId = entry.localListId,
            FilePath = provider.FilePath,
            Title = string.IsNullOrEmpty(entry.title) ? provider.DisplayName : entry.title,
            Description = entry.description ?? string.Empty,
            Tags = entry.tags != null ? new List<string>(entry.tags) : new List<string>(),
            IsFavorite = entry.isFavorite,
            IsEditable = provider.IsEditable,
            IsChallenge = !provider.IsEditable,
            LanguageMode = provider.LanguageMode,
            ContentRevision = entry.contentRevision,
            ContentHash = entry.contentHash,
            AddedAtUtc = entry.addedAtUtc,
            ModifiedAtUtc = entry.modifiedAtUtc,
            ContentModifiedAtUtc = entry.contentModifiedAtUtc,
            LastPlayedAtUtc = entry.lastPlayedAtUtc,
            PlayCount = entry.playCount,
            CurrentRecord = CloneRevision(entry.currentRecord),
            ArchivedRecords = CloneRevisions(entry.archivedRecords),
            Content = WordListLibraryContent.FromProvider(provider),
            Provider = provider
        };
    }

    private WordListLibraryEntry FindEntry(string id)
    {
        return state.lists.FirstOrDefault(item => item != null && string.Equals(item.localListId, id, StringComparison.Ordinal));
    }

    private bool IsActivePath(string path)
    {
        return activeProvider != null && PathsEqual(activeProvider.FilePath, path);
    }

    private static bool PathsEqual(string first, string second)
    {
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private LevelWordListProvider ResolveProvider(string localListId, string path)
    {
        if (activeProvider != null && string.Equals(activeListId, localListId, StringComparison.Ordinal)
            && PathsEqual(activeProvider.FilePath, path))
            return activeProvider;
        if (providers.TryGetValue(localListId, out LevelWordListProvider cached)
            && PathsEqual(cached.FilePath, path))
            return cached;
        return null;
    }

    private WordListRevisionRecord EnsureCurrentRecord(WordListLibraryEntry entry)
    {
        if (entry.currentRecord == null)
            entry.currentRecord = NewRevisionRecord(entry.contentRevision, entry.contentHash);
        return entry.currentRecord;
    }

    private static WordListRevisionRecord NewRevisionRecord(int revision, string hash)
    {
        return new WordListRevisionRecord { contentRevision = revision, contentHash = hash };
    }

    private static void ArchiveCurrentRecord(WordListLibraryEntry entry)
    {
        if (entry.currentRecord == null || !entry.currentRecord.HasRecord) return;
        if (entry.archivedRecords == null) entry.archivedRecords = new List<WordListRevisionRecord>();
        entry.archivedRecords.Add(CloneRevision(entry.currentRecord));
    }

    private string NowUtc()
    {
        DateTime now = utcNow();
        if (now.Kind != DateTimeKind.Utc) now = now.ToUniversalTime();
        return now.ToString("o");
    }

    private static bool TryValidateTitle(string title, out string normalized, out string error)
    {
        normalized = title == null ? string.Empty : title.Trim();
        if (normalized.Length == 0)
        {
            error = "Enter a title for this word list.";
            return false;
        }
        error = null;
        return true;
    }

    private static bool TryValidateContent(WordListLibraryContent source,
        out WordListLibraryContent normalized, out string error)
    {
        normalized = null;
        if (source == null || !Enum.IsDefined(typeof(LanguageMode), source.LanguageMode))
        {
            error = "Choose a valid word-list language.";
            return false;
        }
        normalized = source.Clone();
        if (normalized.Words == null) normalized.Words = new List<string>();
        if (normalized.ChineseWords == null) normalized.ChineseWords = new List<ChineseWordEntry>();
        if (normalized.MixedWords == null) normalized.MixedWords = new List<MixedWordEntry>();
        if (normalized.LanguageMode == LanguageMode.English)
        {
            if (normalized.ChineseWords.Count > 0 || normalized.MixedWords.Count > 0)
            {
                error = "English lists cannot carry Chinese or mixed structures.";
                normalized = null;
                return false;
            }
            foreach (string word in normalized.Words)
            {
                if (string.IsNullOrWhiteSpace(word) || ContainsHanCharacters(word))
                {
                    error = "English entries must be nonempty and cannot contain Han characters.";
                    normalized = null;
                    return false;
                }
            }
        }
        else if (normalized.LanguageMode == LanguageMode.Chinese)
        {
            if (normalized.MixedWords.Count > 0 || normalized.ChineseWords.Count != normalized.Words.Count)
            {
                error = "Chinese lists need one structured Chinese entry for every displayed word.";
                normalized = null;
                return false;
            }
            for (int i = 0; i < normalized.ChineseWords.Count; i++)
            {
                ChineseWordEntry entry = normalized.ChineseWords[i];
                if (entry == null || string.IsNullOrEmpty(entry.display) || entry.entries == null
                    || !string.Equals(entry.display, normalized.Words[i], StringComparison.Ordinal))
                {
                    error = "Chinese display text must match its structured character and pinyin entries.";
                    normalized = null;
                    return false;
                }
                if (!ValidateChineseEntries(entry.display, entry.entries, out error))
                {
                    error = "Chinese word " + (i + 1) + " ('" + entry.display + "'): " + error;
                    normalized = null;
                    return false;
                }
            }
        }
        else
        {
            if (normalized.ChineseWords.Count > 0 || normalized.MixedWords.Count != normalized.Words.Count)
            {
                error = "Mixed lists need one structured mixed entry for every displayed word.";
                normalized = null;
                return false;
            }
            for (int i = 0; i < normalized.MixedWords.Count; i++)
            {
                MixedWordEntry entry = normalized.MixedWords[i];
                if (entry == null || entry.segments == null || entry.segments.Count == 0)
                {
                    error = "Mixed words need structured segments.";
                    normalized = null;
                    return false;
                }
                var display = new StringBuilder();
                foreach (MixedSegmentData segment in entry.segments)
                {
                    if (segment == null || (segment.type != "english" && segment.type != "chinese"))
                    {
                        error = "Mixed word segments have an unknown type.";
                        normalized = null;
                        return false;
                    }
                    if (segment.type == "english")
                    {
                        string text = segment.text ?? string.Empty;
                        if (ContainsHanCharacters(text))
                        {
                            error = "Mixed word " + (i + 1) + ", segment: English text cannot contain Han characters outside pinyin data.";
                            normalized = null;
                            return false;
                        }
                        display.Append(text);
                    }
                    else
                    {
                        if (segment.entries == null || segment.entries.Count == 0)
                        {
                            error = "Mixed word " + (i + 1) + ", segment: Chinese text needs structured character/pinyin entries.";
                            normalized = null;
                            return false;
                        }
                        foreach (ChinesePhaseEntry phase in segment.entries)
                        {
                            if (phase == null || !IsSingleHanCharacter(phase.character) || !IsValidPinyin(phase.pinyin))
                            {
                                error = "Mixed word " + (i + 1) + ", Chinese character " + (segment.entries.IndexOf(phase) + 1)
                                    + " needs one Han character and a lowercase a-z pinyin syllable.";
                                normalized = null;
                                return false;
                            }
                            display.Append(phase.character);
                        }
                    }
                }
                if (display.Length == 0 || !string.Equals(display.ToString(), normalized.Words[i], StringComparison.Ordinal))
                {
                    error = "Mixed word " + (i + 1) + " display text must match its ordered English and Chinese segments.";
                    normalized = null;
                    return false;
                }
            }
        }
        error = null;
        return true;
    }

    private static bool ValidateChineseEntries(string display, List<ChinesePhaseEntry> entries, out string error)
    {
        List<string> characters = GetHanCodePoints(display);
        if (characters.Count == 0 || characters.Count != entries.Count)
        {
            error = "Chinese display characters must match the number of structured entries.";
            return false;
        }
        for (int i = 0; i < characters.Count; i++)
        {
            if (!IsHanString(characters[i]) || entries[i] == null
                || !string.Equals(entries[i].character, characters[i], StringComparison.Ordinal)
                || !IsValidPinyin(entries[i].pinyin))
            {
                error = "Each Chinese display character needs a matching Han character and lowercase pinyin syllable.";
                return false;
            }
        }
        error = null;
        return true;
    }

    private static bool ContainsHanCharacters(string text)
    {
        foreach (string point in GetCodePoints(text))
            if (IsHanString(point)) return true;
        return false;
    }

    private static bool IsSingleHanCharacter(string text)
    {
        List<string> points = GetCodePoints(text);
        return points.Count == 1 && IsHanString(points[0]);
    }

    private static bool IsHanString(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        int codePoint = char.IsSurrogatePair(text, 0) ? char.ConvertToUtf32(text, 0) : text[0];
        return IsHanCodePoint(codePoint);
    }

    private static List<string> GetCodePoints(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) return result;
        for (int index = 0; index < text.Length;)
        {
            int width = char.IsSurrogatePair(text, index) ? 2 : 1;
            result.Add(text.Substring(index, width));
            index += width;
        }
        return result;
    }

    private static List<string> GetHanCodePoints(string text)
    {
        var result = new List<string>();
        foreach (string point in GetCodePoints(text))
            if (IsHanString(point)) result.Add(point);
        return result;
    }

    private static bool IsValidPinyin(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (char c in value) if (c < 'a' || c > 'z') return false;
        return true;
    }

    private static bool IsHanCodePoint(int codePoint)
    {
        return (codePoint >= 0x3400 && codePoint <= 0x4DBF)
            || (codePoint >= 0x4E00 && codePoint <= 0x9FFF)
            || (codePoint >= 0xF900 && codePoint <= 0xFAFF)
            || (codePoint >= 0x20000 && codePoint <= 0x2A6DF)
            || (codePoint >= 0x2A700 && codePoint <= 0x2B73F)
            || (codePoint >= 0x2B740 && codePoint <= 0x2B81F)
            || (codePoint >= 0x2B820 && codePoint <= 0x2CEAF)
            || (codePoint >= 0x2CEB0 && codePoint <= 0x2EBEF)
            || (codePoint >= 0x2EBF0 && codePoint <= 0x2EE5F)
            || (codePoint >= 0x2F800 && codePoint <= 0x2FA1F)
            || (codePoint >= 0x30000 && codePoint <= 0x3134F)
            || (codePoint >= 0x31350 && codePoint <= 0x323AF);
    }

    private static List<string> NormalizeTags(IEnumerable<string> tags)
    {
        var result = new List<string>();
        if (tags == null) return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string tag in tags)
        {
            string value = tag == null ? string.Empty : tag.Trim();
            if (value.Length > 0 && seen.Add(value)) result.Add(value);
        }
        return result;
    }

    private static bool StringListsEqual(List<string> first, List<string> second)
    {
        first = first ?? new List<string>();
        second = second ?? new List<string>();
        if (first.Count != second.Count) return false;
        for (int i = 0; i < first.Count; i++)
            if (!string.Equals(first[i], second[i], StringComparison.Ordinal)) return false;
        return true;
    }

    private static WordListLibraryState NewState() => new WordListLibraryState { schemaVersion = 1, lists = new List<WordListLibraryEntry>() };

    private static WordListLibraryEntry CloneEntry(WordListLibraryEntry source)
    {
        return JsonUtility.FromJson<WordListLibraryEntry>(JsonUtility.ToJson(source));
    }

    private void ReplaceEntry(WordListLibraryEntry replacement)
    {
        int index = state.lists.FindIndex(item => item != null && item.localListId == replacement.localListId);
        if (index >= 0) state.lists[index] = replacement;
    }

    private static WordListRunSummary CloneSummary(WordListRunSummary source)
    {
        return source == null ? null : JsonUtility.FromJson<WordListRunSummary>(JsonUtility.ToJson(source));
    }

    private static WordListRevisionRecord CloneRevision(WordListRevisionRecord source)
    {
        return source == null ? null : JsonUtility.FromJson<WordListRevisionRecord>(JsonUtility.ToJson(source));
    }

    private static List<WordListRevisionRecord> CloneRevisions(List<WordListRevisionRecord> source)
    {
        var result = new List<WordListRevisionRecord>();
        if (source != null) foreach (var record in source) result.Add(CloneRevision(record));
        return result;
    }

    private static void RestoreFile(string path, byte[] bytes)
    {
        if (bytes == null) return;
        string restore = path + ".rollback";
        File.WriteAllBytes(restore, bytes);
        if (File.Exists(path))
        {
            string backup = path + ".bak";
            if (!File.Exists(backup)) File.Copy(path, backup);
            try { File.Replace(restore, path, null); }
            catch (PlatformNotSupportedException)
            {
                string displaced = path + ".displaced";
                File.Move(path, displaced);
                try { File.Move(restore, path); }
                catch { if (!File.Exists(path)) File.Move(displaced, path); throw; }
            }
        }
        else File.Move(restore, path);
        if (File.Exists(restore)) File.Delete(restore);
    }

    private static void PreserveUnregisteredFile(string path)
    {
        if (!File.Exists(path)) return;
        string preserved = path + ".unregistered." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        try { File.Move(path, preserved); }
        catch (Exception ex) { Debug.LogWarning("[WordListLibrary] Could not preserve failed new list: " + ex.Message); }
    }
}
