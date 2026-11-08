using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class GameDataResetServiceTests
{
    private string persistentRoot;
    private MemoryPreferences preferences;
    private ResetFileOperations files;
    private GameConfigProfile temporaryProfile;

    [SetUp]
    public void SetUp()
    {
        persistentRoot = Path.Combine(Path.GetTempPath(), "StickyWordsResetTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(persistentRoot);
        preferences = new MemoryPreferences();
        files = new ResetFileOperations();
    }

    [TearDown]
    public void TearDown()
    {
        if (!string.IsNullOrEmpty(persistentRoot)
            && Path.GetFileName(persistentRoot).StartsWith("StickyWordsResetTest-", StringComparison.Ordinal)
            && Directory.Exists(persistentRoot))
            Directory.Delete(persistentRoot, true);
        if (temporaryProfile != null)
            UnityEngine.Object.DestroyImmediate(temporaryProfile);
        temporaryProfile = null;
    }

    [Test]
    public void Preview_ListsOnlySelectedKeysAndRootOwnedPaths()
    {
        string userListPath = Path.Combine(persistentRoot, "WordLists", "user-list.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userListPath));
        File.WriteAllText(userListPath, "leave user data in place");
        preferences.SetFloat(SettingsManager.KeyMasterVolume, 0.4f);
        preferences.SetInt(SettingsManager.KeyFullscreen, 1);
        preferences.SetString("LevelPanel_LastPath", "challenge_01.txt");
        preferences.SetString("MyListPath", userListPath);

        GameResetPlan plan = GameDataResetService.Preview(
            GameResetTargets.AudioSettings | GameResetTargets.NavigationMemory | GameResetTargets.TimeRecords,
            persistentRoot,
            preferences,
            new GameResetChallengeCounts(19, 20),
            files);

        Assert.IsTrue(plan.IsValid, plan.Error);
        CollectionAssert.AreEquivalent(
            new[]
            {
                SettingsManager.KeyMasterVolume,
                SettingsManager.KeySFXVolume,
                SettingsManager.KeyBGMVolume,
                "LevelPanel_LastPath",
                SettingsManager.KeyActiveTab,
                "LevelPanel_LastTab",
                "MyListPath"
            },
            plan.Preferences.Select(item => item.key).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { Path.Combine(persistentRoot, "ListTimeRecords.json") },
            plan.Paths.Select(item => item.originalPath).ToArray());
        Assert.IsTrue(plan.BackupDirectory.StartsWith(persistentRoot, StringComparison.OrdinalIgnoreCase));

        GameResetPlan navigationOnly = GameDataResetService.Preview(
            GameResetTargets.NavigationMemory,
            persistentRoot,
            preferences,
            new GameResetChallengeCounts(19, 20),
            files);
        Assert.IsTrue(navigationOnly.IsValid, navigationOnly.Error);
        Assert.IsEmpty(navigationOnly.Paths, "Navigation memory should not move the WordLists folder.");
        Assert.AreEqual(1, navigationOnly.Preferences.Count(item => item.key == "MyListPath"));

        GameResetResult result = GameDataResetService.Execute(navigationOnly);
        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.IsFalse(preferences.HasKey("MyListPath"));
        Assert.IsTrue(File.Exists(userListPath), "The selected list remains on disk when only navigation memory is reset.");
    }

    [Test]
    public void Preview_ChallengeProgressKeepsLegacyRangeAndExtendsPastNinetyNine()
    {
        GameResetPlan plan = GameDataResetService.Preview(
            GameResetTargets.EnglishChallengeProgress | GameResetTargets.ChineseChallengeProgress,
            persistentRoot,
            preferences,
            new GameResetChallengeCounts(102, 20),
            files);

        string[] keys = plan.Preferences.Select(item => item.key).ToArray();
        CollectionAssert.Contains(keys, "ChallengeUnlockedCount_");
        CollectionAssert.Contains(keys, "ChallengeStar_99");
        CollectionAssert.Contains(keys, "ChallengeStar_101");
        CollectionAssert.Contains(keys, "ChallengeUnlockedCount_cn_");
        CollectionAssert.Contains(keys, "ChallengeStar_cn_99");
        Assert.IsFalse(keys.Contains("ChallengeStar_102"));
    }

    [Test]
    public void Preview_ChallengeProgressIncludesSavedCursorBeyondCurrentContentCount()
    {
        preferences.SetInt("ChallengeUnlockedCount_", 151);
        preferences.SetInt("ChallengeUnlockedCount_cn_", 131);
        GameResetPlan plan = GameDataResetService.Preview(
            GameResetTargets.EnglishChallengeProgress | GameResetTargets.ChineseChallengeProgress,
            persistentRoot,
            preferences,
            new GameResetChallengeCounts(2, 2),
            files);

        Assert.IsTrue(plan.IsValid, plan.Error);
        string[] keys = plan.Preferences.Select(item => item.key).ToArray();
        CollectionAssert.Contains(keys, "ChallengeStar_150");
        CollectionAssert.Contains(keys, "ChallengeStar_cn_130");
        CollectionAssert.DoesNotContain(keys, "ChallengeStar_151");
        CollectionAssert.DoesNotContain(keys, "ChallengeStar_cn_131");
    }

    [Test]
    public void Execute_EnglishChallengeResetClearsOnlyEnglishLibraryHistoryAndKeepsRecoveryBackup()
    {
        string challengeDirectory = Path.Combine(persistentRoot, "StreamingAssets", "Levels");
        string customDirectory = Path.Combine(persistentRoot, "Levels", "Custom");
        Directory.CreateDirectory(challengeDirectory);
        Directory.CreateDirectory(customDirectory);
        string sidecarPath = Path.Combine(persistentRoot, "WordListLibrary.json");
        var state = new WordListLibraryState();
        state.lists.Add(CreateProgressEntry("en-id", Path.Combine(challengeDirectory, "challenge_01.txt"), 3));
        state.lists.Add(CreateProgressEntry("cn-id", Path.Combine(challengeDirectory, "challenge_cn_01.txt"), 2));
        state.lists.Add(CreateProgressEntry("custom-id", Path.Combine(customDirectory, "challenge_cn_notes.txt"), 1));
        string originalJson = JsonUtility.ToJson(state, true);
        File.WriteAllText(sidecarPath, originalJson);
        File.WriteAllText(sidecarPath + ".bak", originalJson);

        var library = new WordListLibraryService(customDirectory, sidecarPath);
        GameResetPlan plan = GameDataResetService.Preview(
            GameResetTargets.EnglishChallengeProgress,
            persistentRoot,
            preferences,
            new GameResetChallengeCounts(1, 1),
            files,
            library,
            challengeDirectory);

        Assert.IsTrue(plan.IsValid, plan.Error);
        Assert.IsTrue(plan.Preferences.All(item => !item.existed), "The Library sidecar alone must be enough to plan this reset.");
        CollectionAssert.Contains(plan.MetadataFiles.Select(item => Path.GetFileName(item.originalPath)).ToArray(), "WordListLibrary.json");
        Assert.IsFalse(plan.Paths.Any(item => item.originalPath == sidecarPath), "Selective reset must preserve the active sidecar path.");

        GameResetResult result = GameDataResetService.Execute(plan);
        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.IsTrue(File.Exists(sidecarPath));
        GameResetManifest manifest = JsonUtility.FromJson<GameResetManifest>(
            File.ReadAllText(Path.Combine(result.BackupDirectory, "manifest.json")));
        Assert.IsTrue(manifest.metadataFiles.Any(item => item.originalPath == sidecarPath));
        Assert.AreEqual(originalJson, File.ReadAllText(Path.Combine(
            result.BackupDirectory, "LibraryMetadata", "WordListLibrary.json")), "The original metadata remains recoverable.");

        WordListLibraryState reset = JsonUtility.FromJson<WordListLibraryState>(File.ReadAllText(sidecarPath));
        WordListLibraryEntry english = reset.lists.Single(item => item.localListId == "en-id");
        WordListLibraryEntry chinese = reset.lists.Single(item => item.localListId == "cn-id");
        WordListLibraryEntry custom = reset.lists.Single(item => item.localListId == "custom-id");
        AssertEntryProgressCleared(english);
        AssertEntryProgressPresent(chinese, 2);
        AssertEntryProgressPresent(custom, 1);
        Assert.AreEqual("title-en-id", english.title);
        Assert.AreEqual("description-en-id", english.description);
        Assert.IsTrue(english.isFavorite);
        Assert.AreEqual("modified-en-id", english.modifiedAtUtc, "Selective reset preserves metadata timestamps.");
        Assert.AreEqual(1, english.contentRevision);
        Assert.AreEqual("hash-en-id", english.contentHash);
        Assert.AreEqual(File.ReadAllText(sidecarPath), File.ReadAllText(sidecarPath + ".bak"),
            "The old primary backup must not restore reset history.");
    }

    [Test]
    public void Preview_AllLocalIncludesLibrarySidecarAndKnownRecoveryFilesWithoutChangingLegacyBits()
    {
        Assert.AreEqual(1 << 14, (int)GameResetTargets.LocalAchievements);
        Assert.AreEqual(1 << 15, (int)GameResetTargets.WordListLibraryMetadata);
        Assert.AreEqual((1 << 16) - 1, (int)GameResetTargets.AllLocal);

        File.WriteAllText(Path.Combine(persistentRoot, "WordListLibrary.json.bak"), "backup");
        File.WriteAllText(Path.Combine(persistentRoot, "WordListLibrary.json.replaced.20261108120000"), "replaced");
        File.WriteAllText(Path.Combine(persistentRoot, "WordListLibrary.json.recovered-corrupt.20261108120000"), "recovery");
        File.WriteAllText(Path.Combine(persistentRoot, "WordListLibrary.json.user-copy"), "unrelated");

        GameResetPlan plan = GameDataResetService.Preview(
            GameResetTargets.AllLocal,
            persistentRoot,
            preferences,
            new GameResetChallengeCounts(19, 20),
            files);

        Assert.IsTrue(plan.IsValid, plan.Error);
        string[] libraryPaths = plan.Paths
            .Select(item => item.originalPath)
            .Where(path => Path.GetFileName(path).StartsWith("WordListLibrary.json", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .ToArray();
        CollectionAssert.AreEquivalent(
            new[]
            {
                "WordListLibrary.json",
                "WordListLibrary.json.bak",
                "WordListLibrary.json.tmp",
                "WordListLibrary.json.replaced",
                "WordListLibrary.json.replaced.20261108120000",
                "WordListLibrary.json.recovered-corrupt.20261108120000"
            },
            libraryPaths);
        Assert.IsFalse(libraryPaths.Contains("WordListLibrary.json.user-copy"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Execute_AllLocalMovesLibrarySidecarsAndRollbackRestoresThem(bool injectMoveFailure)
    {
        string[] names =
        {
            "WordListLibrary.json",
            "WordListLibrary.json.bak",
            "WordListLibrary.json.tmp",
            "WordListLibrary.json.replaced",
            "WordListLibrary.json.replaced.20261108120000",
            "WordListLibrary.json.recovered-corrupt.20261108120000"
        };
        foreach (string name in names)
            File.WriteAllText(Path.Combine(persistentRoot, name), "contents for " + name);
        string unrelatedPath = Path.Combine(persistentRoot, "WordListLibrary.json.user-copy");
        File.WriteAllText(unrelatedPath, "leave unrelated files untouched");
        files.ThrowAfterFileMove = injectMoveFailure;

        GameResetPlan plan = GameDataResetService.Preview(
            GameResetTargets.AllLocal,
            persistentRoot,
            preferences,
            new GameResetChallengeCounts(19, 20),
            files);
        GameResetResult result = GameDataResetService.Execute(plan);

        Assert.AreEqual(!injectMoveFailure, result.Succeeded, result.Message);
        Assert.IsTrue(result.RollbackSucceeded, result.RollbackError);
        Assert.IsTrue(File.Exists(unrelatedPath));
        foreach (string name in names)
        {
            string source = Path.Combine(persistentRoot, name);
            string backup = Path.Combine(result.BackupDirectory, name);
            if (injectMoveFailure)
            {
                Assert.IsTrue(File.Exists(source), name + " should be restored after rollback.");
                Assert.AreEqual("contents for " + name, File.ReadAllText(source));
            }
            else
            {
                Assert.IsFalse(File.Exists(source), name + " should be moved out of the active persistent root.");
                Assert.IsTrue(File.Exists(backup), name + " should have a recoverable backup.");
            }
        }
    }

    [Test]
    public void Execute_MovesSelectedDataAndBacksUpTypedPreferenceManifest()
    {
        string listsDirectory = Path.Combine(persistentRoot, "WordLists");
        Directory.CreateDirectory(listsDirectory);
        File.WriteAllText(Path.Combine(listsDirectory, "mylist.json"), "{\"words\":[\"hello\"]}");
        File.WriteAllText(Path.Combine(persistentRoot, "ListTimeRecords.json"), "time-records");
        string customDirectory = Path.Combine(persistentRoot, "Levels", "Custom");
        Directory.CreateDirectory(customDirectory);
        File.WriteAllText(Path.Combine(customDirectory, "custom-list-02.txt"), "kept by this operation");
        string achievementsDirectory = Path.Combine(persistentRoot, "WordAchievementProfiles");
        Directory.CreateDirectory(achievementsDirectory);
        File.WriteAllText(Path.Combine(achievementsDirectory, "steam-123.json"), "{} ");
        preferences.SetString("MyListPath", Path.Combine(listsDirectory, "mylist.json"));
        preferences.SetString("LevelPanel_LastPath", Path.Combine(customDirectory, "custom-list-02.txt"));

        GameResetPlan plan = GameDataResetService.Preview(
            GameResetTargets.NavigationMemory | GameResetTargets.MyLists | GameResetTargets.TimeRecords,
            persistentRoot,
            preferences,
            new GameResetChallengeCounts(19, 20),
            files);
        GameResetResult result = GameDataResetService.Execute(plan);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.IsTrue(File.Exists(Path.Combine(result.BackupDirectory, "ListTimeRecords.json")));
        Assert.IsTrue(File.Exists(Path.Combine(result.BackupDirectory, "WordLists", "mylist.json")));
        Assert.IsFalse(Directory.Exists(listsDirectory));
        Assert.IsFalse(File.Exists(Path.Combine(persistentRoot, "ListTimeRecords.json")));
        Assert.IsTrue(File.Exists(Path.Combine(customDirectory, "custom-list-02.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(achievementsDirectory, "steam-123.json")));
        Assert.IsFalse(preferences.HasKey("MyListPath"));
        Assert.IsFalse(preferences.HasKey("LevelPanel_LastPath"));
        Assert.AreEqual(1, plan.Preferences.Count(item => item.key == "MyListPath"), "Overlapping reset scopes must snapshot the shared key once.");

        GameResetManifest manifest = JsonUtility.FromJson<GameResetManifest>(
            File.ReadAllText(Path.Combine(result.BackupDirectory, "manifest.json")));
        GameResetPreferenceSnapshot myListPathBackup = manifest.preferences.Single(item => item.key == "MyListPath");
        Assert.AreEqual(GameResetPreferenceKind.String, myListPathBackup.kind);
        Assert.AreEqual(Path.Combine(persistentRoot, "WordLists"), manifest.paths.Single(item => item.isDirectory && item.originalPath == Path.Combine(persistentRoot, "WordLists")).originalPath);
    }

    [Test]
    public void Execute_DeleteFailureRestoresFilesAndPreferences()
    {
        string listsDirectory = Path.Combine(persistentRoot, "WordLists");
        Directory.CreateDirectory(listsDirectory);
        string listPath = Path.Combine(listsDirectory, "mylist.json");
        File.WriteAllText(listPath, "keep this during rollback");
        preferences.SetString("MyListPath", listPath);
        preferences.ThrowAfterDeletingKey = "MyListPath";

        GameResetPlan plan = GameDataResetService.Preview(
            GameResetTargets.MyLists,
            persistentRoot,
            preferences,
            new GameResetChallengeCounts(19, 20),
            files);
        GameResetResult result = GameDataResetService.Execute(plan);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.RollbackSucceeded, result.RollbackError);
        Assert.IsTrue(File.Exists(listPath));
        Assert.AreEqual(listPath, preferences.GetString("MyListPath"));
        Assert.IsNotEmpty(result.BackupDirectory);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Execute_MoveFailureAfterMoveRestoresFileOrDirectory(bool directory)
    {
        string path;
        GameResetTargets target;
        if (directory)
        {
            path = Path.Combine(persistentRoot, "WordLists");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "user-list.json"), "preserve on rollback");
            target = GameResetTargets.MyLists;
            files.ThrowAfterDirectoryMove = true;
        }
        else
        {
            path = Path.Combine(persistentRoot, "ListTimeRecords.json");
            File.WriteAllText(path, "preserve on rollback");
            target = GameResetTargets.TimeRecords;
            files.ThrowAfterFileMove = true;
        }

        GameResetPlan plan = GameDataResetService.Preview(
            target, persistentRoot, preferences, new GameResetChallengeCounts(19, 20), files);
        GameResetResult result = GameDataResetService.Execute(plan);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.RollbackSucceeded, result.RollbackError);
        if (directory)
            Assert.AreEqual("preserve on rollback", File.ReadAllText(Path.Combine(path, "user-list.json")));
        else
            Assert.AreEqual("preserve on rollback", File.ReadAllText(path));
    }

    [Test]
    public void Preview_RejectsInjectedReparsePointAtOwnedTarget()
    {
        string listsDirectory = Path.Combine(persistentRoot, "WordLists");
        Directory.CreateDirectory(listsDirectory);
        File.WriteAllText(Path.Combine(listsDirectory, "user-list.json"), "must not move");
        files.ReparsePointPath = listsDirectory;

        GameResetPlan plan = GameDataResetService.Preview(
            GameResetTargets.MyLists, persistentRoot, preferences, new GameResetChallengeCounts(19, 20), files);

        Assert.IsFalse(plan.IsValid);
        StringAssert.Contains("reparse point", plan.Error);
        Assert.IsTrue(File.Exists(Path.Combine(listsDirectory, "user-list.json")));
    }

    [Test]
    public void FirstRunReset_IsRevisionScopedAndMarkerFailureRollsBack()
    {
        temporaryProfile = ScriptableObject.CreateInstance<GameConfigProfile>();
        var profile = temporaryProfile;
        profile.profileId = "playtest-profile";
        profile.resetRevision = "rev-1";
        profile.resetLocalDataOnFirstRun = true;
        profile.resetTargets = GameResetTargets.PanelVisibility;
        preferences.SetInt(SettingsManager.KeyWordsPanelOn, 1);

        GameResetResult first = GameDataResetService.ApplyFirstRunReset(
            profile, persistentRoot, preferences, new GameResetChallengeCounts(19, 20), files);
        Assert.IsTrue(first.Succeeded, first.Message);
        Assert.IsFalse(preferences.HasKey(SettingsManager.KeyWordsPanelOn));

        preferences.SetInt(SettingsManager.KeyWordsPanelOn, 1);
        GameResetResult second = GameDataResetService.ApplyFirstRunReset(
            profile, persistentRoot, preferences, new GameResetChallengeCounts(19, 20), files);
        Assert.IsTrue(second.Succeeded, second.Message);
        Assert.IsTrue(second.AlreadyApplied);
        Assert.IsTrue(preferences.HasKey(SettingsManager.KeyWordsPanelOn));

        profile.resetRevision = "rev-2";
        files.ThrowAfterMarkerMove = true;
        GameResetResult failed = GameDataResetService.ApplyFirstRunReset(
            profile, persistentRoot, preferences, new GameResetChallengeCounts(19, 20), files);
        Assert.IsFalse(failed.Succeeded);
        Assert.IsTrue(failed.RollbackSucceeded, failed.RollbackError);
        Assert.IsTrue(preferences.HasKey(SettingsManager.KeyWordsPanelOn));
        string markerDirectory = Path.Combine(persistentRoot, ".GameDataResetMarkers");
        Assert.IsTrue(Directory.Exists(markerDirectory));
        string[] markers = Directory.GetFiles(markerDirectory, "*.json");
        Assert.AreEqual(1, markers.Length, "The successful rev-1 marker should remain after rev-2 rolls back.");
        string retainedMarker = File.ReadAllText(markers[0]);
        StringAssert.Contains("playtest-profile", retainedMarker);
        StringAssert.Contains("rev-1", retainedMarker);
        StringAssert.DoesNotContain("rev-2", retainedMarker);
        Assert.IsFalse(Directory.Exists(markerDirectory) && Directory.GetFiles(markerDirectory, "*.tmp").Length > 0);
    }

    [Test]
    public void FirstRunReset_InvalidRootPathReturnsFailureInsteadOfThrowing()
    {
        temporaryProfile = ScriptableObject.CreateInstance<GameConfigProfile>();
        temporaryProfile.profileId = "invalid-root-profile";
        temporaryProfile.resetRevision = "rev-1";
        temporaryProfile.resetLocalDataOnFirstRun = true;
        temporaryProfile.resetTargets = GameResetTargets.PanelVisibility;

        GameResetResult result = null;
        Assert.DoesNotThrow(() => result = GameDataResetService.ApplyFirstRunReset(
            temporaryProfile, "\0invalid-root", preferences, new GameResetChallengeCounts(19, 20), files));
        Assert.IsNotNull(result);
        Assert.IsFalse(result.Succeeded);
    }

    private sealed class MemoryPreferences : IGameResetPreferenceStore
    {
        private readonly Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.Ordinal);
        public string ThrowAfterDeletingKey { get; set; }

        public bool HasKey(string key) => values.ContainsKey(key);
        public int GetInt(string key) => (int)values[key];
        public float GetFloat(string key) => (float)values[key];
        public string GetString(string key) => (string)values[key];
        public void SetInt(string key, int value) => values[key] = value;
        public void SetFloat(string key, float value) => values[key] = value;
        public void SetString(string key, string value) => values[key] = value;
        public void DeleteKey(string key)
        {
            values.Remove(key);
            if (string.Equals(key, ThrowAfterDeletingKey, StringComparison.Ordinal))
            {
                ThrowAfterDeletingKey = null;
                throw new IOException("Injected preference deletion failure after removal.");
            }
        }
        public void Save() { }
    }

    private static WordListLibraryEntry CreateProgressEntry(string id, string path, int stars)
    {
        var summary = new WordListRunSummary
        {
            hasResult = true,
            totalTimeSeconds = 25f,
            phaseCount = 2,
            errors = 1,
            stars = stars,
            completedAtUtc = "2026-11-01T00:00:00.0000000Z"
        };
        return new WordListLibraryEntry
        {
            localListId = id,
            filePath = path,
            title = "title-" + id,
            description = "description-" + id,
            tags = new List<string> { "keep" },
            isFavorite = true,
            addedAtUtc = "added-" + id,
            modifiedAtUtc = "modified-" + id,
            contentModifiedAtUtc = "content-modified-" + id,
            lastPlayedAtUtc = "played-" + id,
            playCount = 4,
            contentHash = "hash-" + id,
            contentRevision = 1,
            currentRecord = new WordListRevisionRecord
            {
                contentRevision = 1,
                contentHash = "hash-" + id,
                highestStars = stars,
                latest = summary,
                best = summary
            },
            archivedRecords = new List<WordListRevisionRecord>
            {
                new WordListRevisionRecord
                {
                    contentRevision = 0,
                    contentHash = "old-hash-" + id,
                    highestStars = 1,
                    latest = summary,
                    best = summary
                }
            }
        };
    }

    private static void AssertEntryProgressCleared(WordListLibraryEntry entry)
    {
        Assert.IsNull(entry.lastPlayedAtUtc);
        Assert.AreEqual(0, entry.playCount);
        Assert.AreEqual(0, entry.currentRecord.highestStars);
        Assert.IsNull(entry.currentRecord.latest);
        Assert.IsNull(entry.currentRecord.best);
        Assert.IsEmpty(entry.archivedRecords);
    }

    private static void AssertEntryProgressPresent(WordListLibraryEntry entry, int stars)
    {
        Assert.AreEqual("played-" + entry.localListId, entry.lastPlayedAtUtc);
        Assert.AreEqual(4, entry.playCount);
        Assert.AreEqual(stars, entry.currentRecord.highestStars);
        Assert.IsTrue(entry.currentRecord.latest.hasResult);
        Assert.AreEqual(1, entry.archivedRecords.Count);
    }

    private sealed class ResetFileOperations : IGameResetFileOperations
    {
        public bool ThrowAfterMarkerMove { get; set; }
        public bool ThrowAfterFileMove { get; set; }
        public bool ThrowAfterDirectoryMove { get; set; }
        public string ReparsePointPath { get; set; }
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public FileAttributes GetAttributes(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            return !string.IsNullOrEmpty(ReparsePointPath)
                && string.Equals(Path.GetFullPath(path), Path.GetFullPath(ReparsePointPath), StringComparison.OrdinalIgnoreCase)
                ? attributes | FileAttributes.ReparsePoint
                : attributes;
        }
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void MoveFile(string source, string destination)
        {
            File.Move(source, destination);
            if (ThrowAfterMarkerMove && destination.Contains(".GameDataResetMarkers"))
            {
                ThrowAfterMarkerMove = false;
                throw new IOException("Injected failure after marker move.");
            }
            if (ThrowAfterFileMove && destination.Contains(".GameDataResetBackups"))
            {
                ThrowAfterFileMove = false;
                throw new IOException("Injected failure after file move.");
            }
        }
        public void MoveDirectory(string source, string destination)
        {
            Directory.Move(source, destination);
            if (ThrowAfterDirectoryMove && destination.Contains(".GameDataResetBackups"))
            {
                ThrowAfterDirectoryMove = false;
                throw new IOException("Injected failure after directory move.");
            }
        }
        public void WriteAllText(string path, string text) => File.WriteAllText(path, text);
        public string ReadAllText(string path) => File.ReadAllText(path);
        public string[] GetFiles(string directory, string searchPattern) => Directory.Exists(directory) ? Directory.GetFiles(directory, searchPattern) : Array.Empty<string>();
        public void DeleteFile(string path) => File.Delete(path);
    }
}
