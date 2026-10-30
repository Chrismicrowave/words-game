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
        public void DeleteFile(string path) => File.Delete(path);
    }
}
