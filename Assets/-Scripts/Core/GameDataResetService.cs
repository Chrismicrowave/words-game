using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public enum GameResetPreferenceKind
{
    Int,
    Float,
    String
}

public interface IGameResetPreferenceStore
{
    bool HasKey(string key);
    int GetInt(string key);
    float GetFloat(string key);
    string GetString(string key);
    void SetInt(string key, int value);
    void SetFloat(string key, float value);
    void SetString(string key, string value);
    void DeleteKey(string key);
    void Save();
}

public interface IGameResetFileOperations
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    FileAttributes GetAttributes(string path);
    void CreateDirectory(string path);
    void MoveFile(string source, string destination);
    void MoveDirectory(string source, string destination);
    void WriteAllText(string path, string text);
    string ReadAllText(string path);
    void DeleteFile(string path);
}

[Serializable]
public sealed class GameResetPreferenceSnapshot
{
    public string key;
    public GameResetPreferenceKind kind;
    public bool existed;
    public int intValue;
    public float floatValue;
    public string stringValue;
}

[Serializable]
public sealed class GameResetPathSnapshot
{
    public string originalPath;
    public string backupPath;
    public bool isDirectory;
    public bool existed;
}

[Serializable]
public sealed class GameResetManifest
{
    public int schemaVersion = 1;
    public string createdUtc;
    public string targets;
    public List<GameResetPreferenceSnapshot> preferences = new List<GameResetPreferenceSnapshot>();
    public List<GameResetPathSnapshot> paths = new List<GameResetPathSnapshot>();
}

public sealed class GameResetPlan
{
    public GameResetTargets Targets { get; internal set; }
    public string PersistentRoot { get; internal set; }
    public string BackupDirectory { get; internal set; }
    public string Error { get; internal set; }
    public IReadOnlyList<GameResetPreferenceSnapshot> Preferences
    {
        get
        {
            var copies = new List<GameResetPreferenceSnapshot>(preferences.Count);
            foreach (GameResetPreferenceSnapshot item in preferences)
                copies.Add(new GameResetPreferenceSnapshot { key = item.key, kind = item.kind, existed = item.existed, intValue = item.intValue, floatValue = item.floatValue, stringValue = item.stringValue });
            return copies.AsReadOnly();
        }
    }

    public IReadOnlyList<GameResetPathSnapshot> Paths
    {
        get
        {
            var copies = new List<GameResetPathSnapshot>(paths.Count);
            foreach (GameResetPathSnapshot item in paths)
                copies.Add(new GameResetPathSnapshot { originalPath = item.originalPath, backupPath = item.backupPath, isDirectory = item.isDirectory, existed = item.existed });
            return copies.AsReadOnly();
        }
    }
    public bool IsValid => string.IsNullOrEmpty(Error) && Targets != GameResetTargets.None;

    internal readonly List<GameResetPreferenceSnapshot> preferences = new List<GameResetPreferenceSnapshot>();
    internal readonly List<GameResetPathSnapshot> paths = new List<GameResetPathSnapshot>();
    internal IGameResetPreferenceStore preferenceStore;
    internal IGameResetFileOperations fileOperations;
    internal string markerPath;
    internal string markerContent;
}

public sealed class GameResetResult
{
    public bool Succeeded { get; internal set; }
    public bool AlreadyApplied { get; internal set; }
    public bool RollbackSucceeded { get; internal set; }
    public string Message { get; internal set; }
    public string BackupDirectory { get; internal set; }
    public string RollbackError { get; internal set; }
}

public struct GameResetChallengeCounts
{
    public int englishCount;
    public int chineseCount;

    public GameResetChallengeCounts(int englishCount, int chineseCount)
    {
        this.englishCount = Math.Max(0, englishCount);
        this.chineseCount = Math.Max(0, chineseCount);
    }
}

public static class GameDataResetService
{
    private const string BackupFolderName = ".GameDataResetBackups";
    private const string MarkerFolderName = ".GameDataResetMarkers";
    private const int MinimumLegacyChallengeStarIndex = 99;

    private static readonly IGameResetFileOperations SystemFiles = new SystemGameResetFileOperations();

    public static GameResetPlan Preview(GameResetTargets targets)
    {
        return Preview(targets, Application.persistentDataPath, new UnityGameResetPreferenceStore(), GetKnownChallengeCounts(Application.streamingAssetsPath));
    }

    public static GameResetPlan Preview(
        GameResetTargets targets,
        string persistentRoot,
        IGameResetPreferenceStore preferences,
        GameResetChallengeCounts challengeCounts,
        IGameResetFileOperations fileOperations = null)
    {
        var plan = new GameResetPlan
        {
            Targets = targets,
            preferenceStore = preferences,
            fileOperations = fileOperations ?? SystemFiles
        };

        try
        {
            ValidateTargets(targets);
            if (preferences == null)
                throw new ArgumentNullException(nameof(preferences));
            if (string.IsNullOrWhiteSpace(persistentRoot))
                throw new ArgumentException("A persistent data root is required.", nameof(persistentRoot));

            plan.PersistentRoot = Path.GetFullPath(persistentRoot);
            ValidateRoot(plan.PersistentRoot, plan.fileOperations);
            plan.BackupDirectory = CreateBackupDirectoryPath(plan.PersistentRoot);
            AddSelectedPreferences(plan, challengeCounts);
            AddSelectedPaths(plan);
            ValidatePlanPaths(plan);
        }
        catch (Exception exception)
        {
            plan.Error = exception.GetType().Name + ": " + exception.Message;
        }

        return plan;
    }

    public static GameResetResult Execute(GameResetTargets targets)
    {
        return Execute(Preview(targets));
    }

    public static GameResetResult Execute(GameResetPlan plan)
    {
        return ExecuteCore(plan, null);
    }

    public static GameResetResult ApplyFirstRunReset(GameConfigProfile profile)
    {
        return ApplyFirstRunReset(
            profile,
            Application.persistentDataPath,
            new UnityGameResetPreferenceStore(),
            GetKnownChallengeCounts(Application.streamingAssetsPath),
            SystemFiles);
    }

    public static GameResetResult ApplyFirstRunReset(
        GameConfigProfile profile,
        string persistentRoot,
        IGameResetPreferenceStore preferences,
        GameResetChallengeCounts challengeCounts,
        IGameResetFileOperations fileOperations = null)
    {
        if (profile == null)
            return Failure("No GameConfigProfile is assigned.");
        if (!profile.resetLocalDataOnFirstRun)
            return new GameResetResult { Succeeded = true, RollbackSucceeded = true, Message = "First-run reset is disabled for this profile." };
        if (string.IsNullOrWhiteSpace(profile.profileId) || string.IsNullOrWhiteSpace(profile.resetRevision))
            return Failure("First-run reset requires a profileId and resetRevision.");

        string root = persistentRoot;
        string markerPath;
        var files = fileOperations ?? SystemFiles;
        try
        {
            ValidateRoot(Path.GetFullPath(root), files);
            markerPath = GetMarkerPath(root, profile.profileId, profile.resetRevision);
            ValidatePathWithinRoot(Path.GetFullPath(root), markerPath, files, true);
            if (files.FileExists(markerPath))
            {
                string marker = files.ReadAllText(markerPath);
                if (string.Equals(marker, BuildMarkerContent(profile.profileId, profile.resetRevision), StringComparison.Ordinal))
                    return new GameResetResult { Succeeded = true, AlreadyApplied = true, RollbackSucceeded = true, Message = "This profile reset revision was already applied." };
                return Failure("The first-run reset marker exists but does not match the selected profile revision. No data was changed.");
            }
        }
        catch (Exception exception)
        {
            return Failure("Could not inspect the first-run reset marker: " + exception.Message);
        }

        var plan = Preview(profile.resetTargets, root, preferences, challengeCounts, files);
        if (!plan.IsValid)
            return Failure("First-run reset preview failed: " + plan.Error);

        plan.markerPath = markerPath;
        plan.markerContent = BuildMarkerContent(profile.profileId, profile.resetRevision);
        return ExecuteCore(plan, () => WriteMarkerAtomically(plan));
    }

    public static GameResetChallengeCounts GetKnownChallengeCounts(string streamingAssetsPath)
    {
        int english = 0;
        int chinese = 0;
        try
        {
            string directory = Path.Combine(streamingAssetsPath ?? "", "Levels");
            if (Directory.Exists(directory))
            {
                foreach (string path in Directory.GetFiles(directory, "*.txt"))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (name.StartsWith("challenge_cn_", StringComparison.OrdinalIgnoreCase))
                        chinese++;
                    else if (name.StartsWith("challenge_", StringComparison.OrdinalIgnoreCase))
                        english++;
                }
            }
        }
        catch
        {
            // The legacy range below still clears existing supported indices if the content directory is unavailable.
        }

        return new GameResetChallengeCounts(english, chinese);
    }

    private static GameResetResult ExecuteCore(GameResetPlan plan, Action afterLocalMutations)
    {
        if (plan == null)
            return Failure("No reset plan was supplied.");
        if (!plan.IsValid)
            return Failure("Reset preview is invalid: " + plan.Error);

        var deletedPreferences = new List<GameResetPreferenceSnapshot>();
        bool backupCreated = false;
        try
        {
            ValidatePlanPaths(plan);
            if (!PlanStillCurrent(plan))
                return Failure("Data changed after preview. Preview again before resetting.");
            if (!string.IsNullOrEmpty(plan.markerPath)
                && (plan.fileOperations.FileExists(plan.markerPath) || plan.fileOperations.DirectoryExists(plan.markerPath)))
                return Failure("The first-run reset marker appeared after preview. No data was changed.");

            plan.fileOperations.CreateDirectory(Path.GetDirectoryName(plan.BackupDirectory));
            plan.fileOperations.CreateDirectory(plan.BackupDirectory);
            backupCreated = true;

            var manifest = new GameResetManifest
            {
                createdUtc = DateTime.UtcNow.ToString("o"),
                targets = plan.Targets.ToString()
            };
            foreach (GameResetPreferenceSnapshot preference in plan.preferences)
                if (preference.existed)
                    manifest.preferences.Add(ClonePreference(preference));
            foreach (GameResetPathSnapshot path in plan.paths)
                if (path.existed)
                    manifest.paths.Add(new GameResetPathSnapshot { originalPath = path.originalPath, backupPath = path.backupPath, isDirectory = path.isDirectory, existed = true });
            plan.fileOperations.WriteAllText(Path.Combine(plan.BackupDirectory, "manifest.json"), JsonUtility.ToJson(manifest, true));

            foreach (GameResetPathSnapshot path in plan.paths)
            {
                bool exists = path.isDirectory ? plan.fileOperations.DirectoryExists(path.originalPath) : plan.fileOperations.FileExists(path.originalPath);
                if (!exists)
                    continue;
                plan.fileOperations.CreateDirectory(Path.GetDirectoryName(path.backupPath));
                if (path.isDirectory)
                    plan.fileOperations.MoveDirectory(path.originalPath, path.backupPath);
                else
                    plan.fileOperations.MoveFile(path.originalPath, path.backupPath);
            }

            foreach (GameResetPreferenceSnapshot preference in plan.preferences)
            {
                if (!preference.existed)
                    continue;
                deletedPreferences.Add(preference);
                plan.preferenceStore.DeleteKey(preference.key);
            }
            plan.preferenceStore.Save();
            afterLocalMutations?.Invoke();

            return new GameResetResult
            {
                Succeeded = true,
                RollbackSucceeded = true,
                Message = "Reset completed. A recoverable backup was created.",
                BackupDirectory = plan.BackupDirectory
            };
        }
        catch (Exception exception)
        {
            string rollbackError = Rollback(plan, deletedPreferences, plan.markerPath);
            return new GameResetResult
            {
                Succeeded = false,
                RollbackSucceeded = string.IsNullOrEmpty(rollbackError),
                Message = "Reset failed: " + exception.GetType().Name + ": " + exception.Message,
                BackupDirectory = backupCreated ? plan.BackupDirectory : null,
                RollbackError = rollbackError
            };
        }
    }

    private static string Rollback(GameResetPlan plan, List<GameResetPreferenceSnapshot> deletedPreferences, string markerPath)
    {
        var errors = new List<string>();
        if (!string.IsNullOrEmpty(markerPath))
        {
            try
            {
                if (plan.fileOperations.FileExists(markerPath))
                    plan.fileOperations.DeleteFile(markerPath);
                string markerTemp = markerPath + ".tmp";
                if (plan.fileOperations.FileExists(markerTemp))
                    plan.fileOperations.DeleteFile(markerTemp);
            }
            catch (Exception exception) { errors.Add("Marker: " + exception.Message); }
        }

        for (int i = deletedPreferences.Count - 1; i >= 0; i--)
        {
            GameResetPreferenceSnapshot preference = deletedPreferences[i];
            try { RestorePreference(plan.preferenceStore, preference); }
            catch (Exception exception) { errors.Add(preference.key + ": " + exception.Message); }
        }
        try { plan.preferenceStore.Save(); }
        catch (Exception exception) { errors.Add("PlayerPrefs.Save: " + exception.Message); }

        for (int i = plan.paths.Count - 1; i >= 0; i--)
        {
            GameResetPathSnapshot path = plan.paths[i];
            try
            {
                bool backupExists = path.isDirectory
                    ? plan.fileOperations.DirectoryExists(path.backupPath)
                    : plan.fileOperations.FileExists(path.backupPath);
                if (!backupExists)
                    continue;

                bool originalExists = path.isDirectory
                    ? plan.fileOperations.DirectoryExists(path.originalPath)
                    : plan.fileOperations.FileExists(path.originalPath);
                if (originalExists)
                    throw new IOException("Original path is occupied; backup retained at " + path.backupPath);

                string parent = Path.GetDirectoryName(path.originalPath);
                if (!string.IsNullOrEmpty(parent))
                    plan.fileOperations.CreateDirectory(parent);
                if (path.isDirectory)
                    plan.fileOperations.MoveDirectory(path.backupPath, path.originalPath);
                else
                    plan.fileOperations.MoveFile(path.backupPath, path.originalPath);
            }
            catch (Exception exception) { errors.Add(path.originalPath + ": " + exception.Message); }
        }

        return errors.Count == 0 ? "" : string.Join(" | ", errors);
    }

    private static void AddSelectedPreferences(GameResetPlan plan, GameResetChallengeCounts challengeCounts)
    {
        AddPreferenceGroup(plan, GameResetTargets.AudioSettings,
            Pref(SettingsManager.KeyMasterVolume, GameResetPreferenceKind.Float),
            Pref(SettingsManager.KeySFXVolume, GameResetPreferenceKind.Float),
            Pref(SettingsManager.KeyBGMVolume, GameResetPreferenceKind.Float));
        AddPreferenceGroup(plan, GameResetTargets.DisplaySettings,
            Pref(SettingsManager.KeyFullscreen, GameResetPreferenceKind.Int),
            Pref(SettingsManager.KeyResolution, GameResetPreferenceKind.Int));
        AddPreferenceGroup(plan, GameResetTargets.VisualEffects,
            Pref(SettingsManager.KeyCRTFilter, GameResetPreferenceKind.Int),
            Pref(SettingsManager.KeyScreenShake, GameResetPreferenceKind.Int));
        AddPreferenceGroup(plan, GameResetTargets.InputHints, Pref(SettingsManager.KeyActionPrompts, GameResetPreferenceKind.Int));
        AddPreferenceGroup(plan, GameResetTargets.UiLanguage, Pref(SettingsManager.KeyUILanguage, GameResetPreferenceKind.String));
        AddPreferenceGroup(plan, GameResetTargets.Pinyin, Pref(SettingsManager.KeyShowPinyin, GameResetPreferenceKind.Int));
        AddPreferenceGroup(plan, GameResetTargets.PlayerName, Pref(SettingsManager.KeyPlayerName, GameResetPreferenceKind.String));
        AddPreferenceGroup(plan, GameResetTargets.PanelVisibility,
            Pref(SettingsManager.KeyWordsPanelOn, GameResetPreferenceKind.Int),
            Pref(SettingsManager.KeyTimerPanelOn, GameResetPreferenceKind.Int),
            Pref(SettingsManager.KeyInfoPanelOn, GameResetPreferenceKind.Int));
        AddPreferenceGroup(plan, GameResetTargets.NavigationMemory,
            Pref(SettingsManager.KeyActiveTab, GameResetPreferenceKind.String),
            Pref("LevelPanel_LastPath", GameResetPreferenceKind.String),
            Pref("LevelPanel_LastTab", GameResetPreferenceKind.String),
            Pref("MyListPath", GameResetPreferenceKind.String));
        AddPreferenceGroup(plan, GameResetTargets.MyLists, Pref("MyListPath", GameResetPreferenceKind.String));

        int englishMaxIndex = Math.Max(MinimumLegacyChallengeStarIndex, challengeCounts.englishCount - 1);
        int chineseMaxIndex = Math.Max(MinimumLegacyChallengeStarIndex, challengeCounts.chineseCount - 1);
        AddChallengeProgress(plan, GameResetTargets.EnglishChallengeProgress, "", englishMaxIndex);
        AddChallengeProgress(plan, GameResetTargets.ChineseChallengeProgress, "cn_", chineseMaxIndex);
    }

    private static void AddChallengeProgress(GameResetPlan plan, GameResetTargets target, string prefix, int maxIndex)
    {
        if (!Includes(plan.Targets, target))
            return;
        AddPreference(plan, Pref("ChallengeUnlockedCount_" + prefix, GameResetPreferenceKind.Int));
        for (int index = 0; index <= maxIndex; index++)
            AddPreference(plan, Pref("ChallengeStar_" + prefix + index, GameResetPreferenceKind.Int));
    }

    private static void AddPreferenceGroup(GameResetPlan plan, GameResetTargets target, params PreferenceKey[] keys)
    {
        if (!Includes(plan.Targets, target))
            return;
        foreach (PreferenceKey key in keys)
            AddPreference(plan, key);
    }

    private static void AddPreference(GameResetPlan plan, PreferenceKey pref)
    {
        if (plan.preferences.Exists(item => string.Equals(item.key, pref.Key, StringComparison.Ordinal)))
            return;

        bool exists = plan.preferenceStore.HasKey(pref.Key);
        var snapshot = new GameResetPreferenceSnapshot { key = pref.Key, kind = pref.Kind, existed = exists };
        if (exists)
        {
            switch (pref.Kind)
            {
                case GameResetPreferenceKind.Int: snapshot.intValue = plan.preferenceStore.GetInt(pref.Key); break;
                case GameResetPreferenceKind.Float: snapshot.floatValue = plan.preferenceStore.GetFloat(pref.Key); break;
                case GameResetPreferenceKind.String: snapshot.stringValue = plan.preferenceStore.GetString(pref.Key); break;
                default: throw new ArgumentOutOfRangeException();
            }
        }
        plan.preferences.Add(snapshot);
    }

    private static void AddSelectedPaths(GameResetPlan plan)
    {
        if (Includes(plan.Targets, GameResetTargets.TimeRecords))
            AddPath(plan, "ListTimeRecords.json", false);
        if (Includes(plan.Targets, GameResetTargets.MyLists))
            AddPath(plan, "WordLists", true);
        if (Includes(plan.Targets, GameResetTargets.CustomLists))
            AddPath(plan, Path.Combine("Levels", "Custom"), true);
        if (Includes(plan.Targets, GameResetTargets.LocalAchievements))
            AddPath(plan, "WordAchievementProfiles", true);
    }

    private static void AddPath(GameResetPlan plan, string relativePath, bool isDirectory)
    {
        string fullPath = Path.GetFullPath(Path.Combine(plan.PersistentRoot, relativePath));
        string backupPath = Path.GetFullPath(Path.Combine(plan.BackupDirectory, relativePath));
        bool directoryExists = plan.fileOperations.DirectoryExists(fullPath);
        bool fileExists = plan.fileOperations.FileExists(fullPath);
        plan.paths.Add(new GameResetPathSnapshot
        {
            originalPath = fullPath,
            backupPath = backupPath,
            isDirectory = isDirectory,
            existed = directoryExists || fileExists
        });
    }

    private static void ValidatePlanPaths(GameResetPlan plan)
    {
        ValidateRoot(plan.PersistentRoot, plan.fileOperations);
        string backupRoot = Path.Combine(plan.PersistentRoot, BackupFolderName);
        ValidatePathWithinRoot(plan.PersistentRoot, backupRoot, plan.fileOperations, true);
        if (plan.fileOperations.DirectoryExists(plan.BackupDirectory) || plan.fileOperations.FileExists(plan.BackupDirectory))
            throw new IOException("The generated backup destination already exists.");
        foreach (GameResetPathSnapshot path in plan.paths)
        {
            ValidatePathWithinRoot(plan.PersistentRoot, path.originalPath, plan.fileOperations, true);
            if (IsWithinPath(path.originalPath, backupRoot))
                throw new InvalidOperationException("A reset target intersects the backup directory.");
            ValidatePathWithinRoot(plan.PersistentRoot, path.backupPath, plan.fileOperations, false);
            if (path.isDirectory && plan.fileOperations.FileExists(path.originalPath))
                throw new IOException("A file exists where a reset directory is expected: " + path.originalPath);
            if (!path.isDirectory && plan.fileOperations.DirectoryExists(path.originalPath))
                throw new IOException("A directory exists where a reset file is expected: " + path.originalPath);
        }
        if (!string.IsNullOrEmpty(plan.markerPath))
            ValidatePathWithinRoot(plan.PersistentRoot, plan.markerPath, plan.fileOperations, false);
    }

    private static void ValidateRoot(string root, IGameResetFileOperations files)
    {
        string fullRoot = Path.GetFullPath(root);
        if (files.DirectoryExists(fullRoot))
            RejectReparsePoint(fullRoot, files);
    }

    private static void ValidatePathWithinRoot(string root, string candidate, IGameResetFileOperations files, bool rejectExistingReparseAncestors)
    {
        string fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        string fullCandidate = Path.GetFullPath(candidate);
        StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullCandidate.StartsWith(fullRoot, comparison))
            throw new UnauthorizedAccessException("Reset path is outside the injected persistent data root: " + fullCandidate);

        if (!rejectExistingReparseAncestors)
            return;
        string current = fullCandidate;
        string trimmedRoot = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (current.Length > trimmedRoot.Length)
        {
            if (files.FileExists(current) || files.DirectoryExists(current))
                RejectReparsePoint(current, files);
            current = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(current))
                break;
        }
    }

    private static void RejectReparsePoint(string path, IGameResetFileOperations files)
    {
        if ((files.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Reset refuses to operate through a reparse point: " + path);
    }

    private static bool PlanStillCurrent(GameResetPlan plan)
    {
        foreach (GameResetPreferenceSnapshot item in plan.preferences)
        {
            if (plan.preferenceStore.HasKey(item.key) != item.existed)
                return false;
            if (!item.existed)
                continue;
            switch (item.kind)
            {
                case GameResetPreferenceKind.Int:
                    if (plan.preferenceStore.GetInt(item.key) != item.intValue) return false;
                    break;
                case GameResetPreferenceKind.Float:
                    if (!plan.preferenceStore.GetFloat(item.key).Equals(item.floatValue)) return false;
                    break;
                case GameResetPreferenceKind.String:
                    if (!string.Equals(plan.preferenceStore.GetString(item.key), item.stringValue, StringComparison.Ordinal)) return false;
                    break;
            }
        }

        foreach (GameResetPathSnapshot item in plan.paths)
        {
            bool current = item.isDirectory
                ? plan.fileOperations.DirectoryExists(item.originalPath)
                : plan.fileOperations.FileExists(item.originalPath);
            bool snapshotted = plan.fileOperations.FileExists(item.backupPath) || plan.fileOperations.DirectoryExists(item.backupPath);
            if (current != item.existed || snapshotted)
                return false;
        }
        return true;
    }

    private static string CreateBackupDirectoryPath(string root)
    {
        return Path.Combine(root, BackupFolderName, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N"));
    }

    private static string GetMarkerPath(string root, string profileId, string revision)
    {
        string markerDirectory = Path.Combine(Path.GetFullPath(root), MarkerFolderName);
        return Path.Combine(markerDirectory, ComputeIdentityHash(profileId + "\n" + revision) + ".json");
    }

    private static string BuildMarkerContent(string profileId, string revision)
    {
        return JsonUtility.ToJson(new ResetMarker { profileId = profileId, resetRevision = revision });
    }

    private static void WriteMarkerAtomically(GameResetPlan plan)
    {
        string directory = Path.GetDirectoryName(plan.markerPath);
        plan.fileOperations.CreateDirectory(directory);
        string temporaryPath = plan.markerPath + ".tmp";
        plan.fileOperations.WriteAllText(temporaryPath, plan.markerContent);
        plan.fileOperations.MoveFile(temporaryPath, plan.markerPath);
    }

    private static string ComputeIdentityHash(string input)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? ""));
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
                builder.Append(value.ToString("x2"));
            return builder.ToString();
        }
    }

    private static bool Includes(GameResetTargets targets, GameResetTargets value) => (targets & value) == value;

    private static void ValidateTargets(GameResetTargets targets)
    {
        const GameResetTargets valid = GameResetTargets.AllLocal;
        if (targets == GameResetTargets.None || (targets & ~valid) != 0)
            throw new ArgumentOutOfRangeException(nameof(targets), "Select at least one known local reset target.");
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (!path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            && !path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            return path + Path.DirectorySeparatorChar;
        return path;
    }

    private static bool IsWithinPath(string path, string parent)
    {
        string normalizedParent = EnsureTrailingSeparator(Path.GetFullPath(parent));
        StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return Path.GetFullPath(path).StartsWith(normalizedParent, comparison);
    }

    private static GameResetPreferenceSnapshot ClonePreference(GameResetPreferenceSnapshot item)
    {
        return new GameResetPreferenceSnapshot
        {
            key = item.key,
            kind = item.kind,
            existed = item.existed,
            intValue = item.intValue,
            floatValue = item.floatValue,
            stringValue = item.stringValue
        };
    }

    private static void RestorePreference(IGameResetPreferenceStore preferences, GameResetPreferenceSnapshot item)
    {
        switch (item.kind)
        {
            case GameResetPreferenceKind.Int: preferences.SetInt(item.key, item.intValue); break;
            case GameResetPreferenceKind.Float: preferences.SetFloat(item.key, item.floatValue); break;
            case GameResetPreferenceKind.String: preferences.SetString(item.key, item.stringValue ?? ""); break;
            default: throw new ArgumentOutOfRangeException();
        }
    }

    private static PreferenceKey Pref(string key, GameResetPreferenceKind kind) => new PreferenceKey(key, kind);

    private static GameResetResult Failure(string message)
    {
        return new GameResetResult { Succeeded = false, RollbackSucceeded = true, Message = message };
    }

    [Serializable]
    private sealed class ResetMarker
    {
        public string profileId;
        public string resetRevision;
    }

    private readonly struct PreferenceKey
    {
        public readonly string Key;
        public readonly GameResetPreferenceKind Kind;
        public PreferenceKey(string key, GameResetPreferenceKind kind) { Key = key; Kind = kind; }
    }

    private sealed class UnityGameResetPreferenceStore : IGameResetPreferenceStore
    {
        public bool HasKey(string key) => PlayerPrefs.HasKey(key);
        public int GetInt(string key) => PlayerPrefs.GetInt(key);
        public float GetFloat(string key) => PlayerPrefs.GetFloat(key);
        public string GetString(string key) => PlayerPrefs.GetString(key);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void SetFloat(string key, float value) => PlayerPrefs.SetFloat(key, value);
        public void SetString(string key, string value) => PlayerPrefs.SetString(key, value);
        public void DeleteKey(string key) => PlayerPrefs.DeleteKey(key);
        public void Save() => PlayerPrefs.Save();
    }

    private sealed class SystemGameResetFileOperations : IGameResetFileOperations
    {
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void MoveFile(string source, string destination) => File.Move(source, destination);
        public void MoveDirectory(string source, string destination) => Directory.Move(source, destination);
        public void WriteAllText(string path, string text) => File.WriteAllText(path, text);
        public string ReadAllText(string path) => File.ReadAllText(path);
        public void DeleteFile(string path) => File.Delete(path);
    }
}
