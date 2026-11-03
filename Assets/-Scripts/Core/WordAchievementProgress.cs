using System;
using System.Globalization;
using System.IO;
using UnityEngine;

public static class WordAchievementApiNames
{
    public const string CompleteFirstWord = "COMPLETE_FIRST_WORD";
    public const string CompleteTwoWords = "COMPLETE_TWO_WORDS";
    public const string CompleteWordWithD = "COMPLETE_WORD_WITH_D";
}

public struct WordAchievementProgressSnapshot
{
    public int CompletedWordCount { get; }
    public bool HasCompletedWordWithD { get; }

    public bool CompleteFirstWordUnlocked => CompletedWordCount >= 1;
    public bool CompleteTwoWordsUnlocked => CompletedWordCount >= 2;
    public bool CompleteWordWithDUnlocked => HasCompletedWordWithD;

    public WordAchievementProgressSnapshot(int completedWordCount, bool hasCompletedWordWithD)
    {
        CompletedWordCount = Math.Max(0, Math.Min(2, completedWordCount));
        HasCompletedWordWithD = hasCompletedWordWithD;
    }
}

public enum WordAchievementProfileActivationResult
{
    UnboundProfile,
    PreviousSteamProfile,
    SteamProfile,
    ClaimedUnboundProfile,
    UnboundClaimConflict
}

public struct WordAchievementProgressRecordResult
{
    public bool Accepted { get; }
    public bool PersistenceSucceeded { get; }
    public string StorageError { get; }
    public WordAchievementProgressSnapshot Snapshot { get; }

    public WordAchievementProgressRecordResult(
        bool accepted,
        bool persistenceSucceeded,
        string storageError,
        WordAchievementProgressSnapshot snapshot)
    {
        Accepted = accepted;
        PersistenceSucceeded = persistenceSucceeded;
        StorageError = storageError ?? "";
        Snapshot = snapshot;
    }
}

/// <summary>
/// Stores the first two successful word completions and whether a completed input target contained ASCII D.
/// Steam integration is intentionally kept outside this class.
/// </summary>
public sealed class WordAchievementProgress
{
    private readonly WordAchievementProgressStore store;
    private WordAchievementProgressSnapshot snapshot;
    private Guid currentAttempt;
    private Guid lastRecordedAttempt;
    private bool persistencePending;
    private string lastPersistenceError = "";

    public WordAchievementProgressSnapshot Snapshot => snapshot;
    public string ActiveProfileId => store.ActiveProfileId;
    public bool HasPendingPersistence => persistencePending;
    public string LastPersistenceError => lastPersistenceError;

    public WordAchievementProgress(string storageDirectory)
    {
        store = new WordAchievementProgressStore(storageDirectory);
    }

    /// <summary>
    /// Activates a profile for a recognized SteamID, or falls back to the last known profile/unbound profile.
    /// steamId must be null when Steam identity is unavailable.
    /// </summary>
    public WordAchievementProfileActivationResult ActivateProfile(string steamId)
    {
        if (persistencePending && !RetryPersistence())
            throw new IOException("Cannot switch achievement profiles while the previous profile has an unsaved progress snapshot: " + lastPersistenceError);

        WordAchievementProgressSnapshot activatedSnapshot = store.ActivateProfile(steamId, out WordAchievementProfileActivationResult result);
        snapshot = activatedSnapshot;
        persistencePending = false;
        lastPersistenceError = "";
        return result;
    }

    /// <summary>Call whenever a new word phase attempt is loaded or explicitly restarted.</summary>
    public void BeginPhaseAttempt()
    {
        currentAttempt = Guid.NewGuid();
    }

    /// <summary>
    /// Records a real successful word completion once for the current loaded attempt.
    /// Empty or non-typable targets are ignored. Repeating the same word in a new attempt counts again.
    /// </summary>
    public WordAchievementProgressRecordResult RecordCompletedWord(string targetText)
    {
        if (currentAttempt == Guid.Empty || !HasTypableCharacter(targetText))
            return new WordAchievementProgressRecordResult(false, !persistencePending, lastPersistenceError, snapshot);

        if (currentAttempt == lastRecordedAttempt)
        {
            bool retrySucceeded = RetryPersistence();
            return new WordAchievementProgressRecordResult(false, retrySucceeded, lastPersistenceError, snapshot);
        }

        lastRecordedAttempt = currentAttempt;

        int completedWordCount = Math.Min(2, snapshot.CompletedWordCount + 1);
        bool hasCompletedWordWithD = snapshot.HasCompletedWordWithD || ContainsAsciiD(targetText);
        snapshot = new WordAchievementProgressSnapshot(completedWordCount, hasCompletedWordWithD);

        persistencePending = true;
        bool saved = RetryPersistence();
        return new WordAchievementProgressRecordResult(true, saved, lastPersistenceError, snapshot);
    }

    /// <summary>Retries writing the current snapshot without changing achievement progress.</summary>
    public bool RetryPersistence()
    {
        if (!persistencePending)
        {
            lastPersistenceError = "";
            return true;
        }

        bool saved = store.TrySave(snapshot, out lastPersistenceError);
        persistencePending = !saved;
        return saved;
    }

    private static bool HasTypableCharacter(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (char character in text)
        {
            if (char.IsLetterOrDigit(character))
                return true;
        }

        return false;
    }

    private static bool ContainsAsciiD(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (char character in text)
        {
            if (character == 'd' || character == 'D')
                return true;
        }

        return false;
    }
}

/// <summary>
/// Local per-SteamID progress files plus one first-login unbound profile.
/// Uses an atomic rename to claim the unbound file; it never merges profiles.
/// </summary>
internal sealed class WordAchievementProgressStore
{
    private const int CurrentSchemaVersion = 1;
    private const string UnboundFileName = "unbound.json";
    private const string ProfileMarkerFileName = "active-profile.json";

    [Serializable]
    private sealed class StoredProgress
    {
        public int schemaVersion = CurrentSchemaVersion;
        public int completedWordCount;
        public bool hasCompletedWordWithD;
    }

    [Serializable]
    private sealed class ProfileMarker
    {
        public int schemaVersion = CurrentSchemaVersion;
        public string activeSteamId = "";
        public bool unboundClaimAttempted;
        public string unboundClaimTargetSteamId = "";
    }

    private readonly string profilesDirectory;
    private StoredProgress activeProgress;
    private string activePath;

    public string ActiveProfileId { get; private set; } = "";

    public WordAchievementProgressStore(string profilesDirectory)
    {
        if (string.IsNullOrWhiteSpace(profilesDirectory))
            throw new ArgumentException("A progress directory is required.", nameof(profilesDirectory));

        this.profilesDirectory = Path.GetFullPath(profilesDirectory);
    }

    public WordAchievementProgressSnapshot ActivateProfile(
        string steamId,
        out WordAchievementProfileActivationResult activationResult)
    {
        ProfileMarker marker = ReadMarker();

        if (TryNormalizeSteamId(steamId, out string normalizedSteamId))
        {
            string steamPath = GetSteamProfilePath(normalizedSteamId);
            activationResult = WordAchievementProfileActivationResult.SteamProfile;
            ProfileMarker updatedMarker = marker;

            if (!marker.unboundClaimAttempted)
            {
                string unboundPath = GetUnboundPath();
                if (File.Exists(unboundPath))
                {
                    if (!File.Exists(steamPath))
                    {
                        try
                        {
                            File.Move(unboundPath, steamPath);
                            activationResult = WordAchievementProfileActivationResult.ClaimedUnboundProfile;
                        }
                        catch (IOException)
                        {
                            if (!File.Exists(steamPath))
                                throw;

                            activationResult = WordAchievementProfileActivationResult.UnboundClaimConflict;
                        }
                    }
                    else
                    {
                        activationResult = WordAchievementProfileActivationResult.UnboundClaimConflict;
                    }
                }

                updatedMarker.unboundClaimAttempted = true;
                updatedMarker.unboundClaimTargetSteamId = normalizedSteamId;
            }

            StoredProgress nextProgress = ReadProgress(steamPath);
            updatedMarker.activeSteamId = normalizedSteamId;
            WriteMarker(updatedMarker);

            activeProgress = nextProgress;
            activePath = steamPath;
            ActiveProfileId = normalizedSteamId;
            return ToSnapshot(nextProgress);
        }

        if (TryNormalizeSteamId(marker.activeSteamId, out string previousSteamId))
        {
            string previousPath = GetSteamProfilePath(previousSteamId);
            StoredProgress previousProgress = ReadProgress(previousPath);
            activePath = previousPath;
            ActiveProfileId = previousSteamId;
            activeProgress = previousProgress;
            activationResult = WordAchievementProfileActivationResult.PreviousSteamProfile;
            return ToSnapshot(previousProgress);
        }
        else
        {
            string unboundPath = GetUnboundPath();
            StoredProgress unboundProgress = ReadProgress(unboundPath);
            activePath = unboundPath;
            ActiveProfileId = "unbound";
            activeProgress = unboundProgress;
            activationResult = WordAchievementProfileActivationResult.UnboundProfile;
            return ToSnapshot(unboundProgress);
        }
    }

    public bool TrySave(WordAchievementProgressSnapshot snapshot, out string storageError)
    {
        if (string.IsNullOrEmpty(activePath) || activeProgress == null)
        {
            storageError = "No progress profile has been activated.";
            return false;
        }

        activeProgress.completedWordCount = Math.Max(0, Math.Min(2, snapshot.CompletedWordCount));
        activeProgress.hasCompletedWordWithD = snapshot.HasCompletedWordWithD;

        try
        {
            WriteJsonAtomically(activePath, JsonUtility.ToJson(activeProgress, true));
            storageError = "";
            return true;
        }
        catch (Exception exception)
        {
            storageError = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    private ProfileMarker ReadMarker()
    {
        string path = Path.Combine(profilesDirectory, ProfileMarkerFileName);
        if (!File.Exists(path))
            return new ProfileMarker();

        string json = File.ReadAllText(path);
        ProfileMarker marker = JsonUtility.FromJson<ProfileMarker>(json);
        if (marker == null || marker.schemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported or invalid achievement profile marker.");

        return marker;
    }

    private void WriteMarker(ProfileMarker marker)
    {
        Directory.CreateDirectory(profilesDirectory);
        WriteJsonAtomically(Path.Combine(profilesDirectory, ProfileMarkerFileName), JsonUtility.ToJson(marker, true));
    }

    private StoredProgress ReadProgress(string path)
    {
        if (!File.Exists(path))
            return new StoredProgress();

        string json = File.ReadAllText(path);
        StoredProgress progress = JsonUtility.FromJson<StoredProgress>(json);
        if (progress == null || progress.schemaVersion != CurrentSchemaVersion
            || progress.completedWordCount < 0 || progress.completedWordCount > 2)
            throw new InvalidDataException("Unsupported or invalid word achievement progress file: " + path);

        return progress;
    }

    private string GetSteamProfilePath(string steamId)
    {
        return Path.Combine(profilesDirectory, "steam-" + steamId + ".json");
    }

    private string GetUnboundPath()
    {
        return Path.Combine(profilesDirectory, UnboundFileName);
    }

    private static WordAchievementProgressSnapshot ToSnapshot(StoredProgress progress)
    {
        return new WordAchievementProgressSnapshot(progress.completedWordCount, progress.hasCompletedWordWithD);
    }

    private static bool TryNormalizeSteamId(string steamId, out string normalizedSteamId)
    {
        normalizedSteamId = "";
        if (!ulong.TryParse(steamId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed) || parsed == 0)
            return false;

        normalizedSteamId = parsed.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static void WriteJsonAtomically(string path, string json)
    {
        string directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException("Progress file has no parent directory.");

        Directory.CreateDirectory(directory);
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, json);

        if (File.Exists(path))
            File.Replace(temporaryPath, path, null);
        else
            File.Move(temporaryPath, path);
    }
}
