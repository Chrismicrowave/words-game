using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks which challenge levels the player has unlocked and their star ratings.
/// Progress is persisted in PlayerPrefs and survives restarts.
/// Supports separate tracks via TrackPrefix (e.g. "cn_" for Chinese levels).
/// </summary>
public static class ChallengeProgression
{
    /// <summary>Track key prefix for separate progression sets ("" = English, "cn_" = Chinese).</summary>
    public static string TrackPrefix { get; set; } = "";

    private static string PrefKey => GetUnlockedCountKey(TrackPrefix);

    public static string GetUnlockedCountKey(string trackPrefix) =>
        $"ChallengeUnlockedCount_{NormalizeTrackPrefix(trackPrefix)}";

    public static string GetStarRatingKey(string trackPrefix, int challengeIndex) =>
        $"ChallengeStar_{NormalizeTrackPrefix(trackPrefix)}{challengeIndex}";

    private static string NormalizeTrackPrefix(string trackPrefix) => trackPrefix ?? "";

    /// <summary>How many challenges are currently unlocked (1-based count).</summary>
    public static int UnlockedCount
    {
        get => GetUnlockedCountForTrack(TrackPrefix);
        set => SetUnlockedCountForTrack(TrackPrefix, value);
    }

    public static int GetUnlockedCountForTrack(string trackPrefix) =>
        Mathf.Max(1, PlayerPrefs.GetInt(GetUnlockedCountKey(trackPrefix), 1));

    /// <summary>Returns whether this challenge has a persisted PlayerPrefs completion marker, including zero stars.</summary>
    public static bool HasCompletedChallengeForTrack(int challengeIndex, string trackPrefix) =>
        challengeIndex >= 0 && PlayerPrefs.HasKey(GetStarRatingKey(trackPrefix, challengeIndex));

    /// <summary>Counts per-challenge completion markers and saved run records for this track.</summary>
    public static int GetCompletedChallengesForTrack(string trackPrefix)
    {
        int unlockedCount = GetUnlockedCountForTrack(trackPrefix);
        List<LevelWordListProvider> trackProviders;
        try
        {
            trackProviders = ChallengeTrackResolver.FilterByTrack(
                LevelWordListProvider.ScanDirectory(LevelWordListProvider.GetChallengeDirectory()),
                trackPrefix);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[ChallengeProgression] Could not scan challenge files while counting completed levels; "
                + "falling back to saved completion markers. " + exception.Message);
            return CountCompletedChallengesForTrack(trackPrefix, unlockedCount, null, null);
        }

        WordListLibraryService library = null;
        try
        {
            library = WordListLibraryService.Shared;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[ChallengeProgression] Could not load challenge run metadata while counting completed levels; "
                + "falling back to saved completion markers. " + exception.Message);
        }

        return CountCompletedChallengesForTrack(trackPrefix, unlockedCount, trackProviders, library);
    }

    internal static int CountCompletedChallengesForTrack(
        string trackPrefix,
        int unlockedCount,
        List<LevelWordListProvider> trackProviders,
        WordListLibraryService library)
    {
        int providerCount = trackProviders != null ? trackProviders.Count : 0;
        int completionRange = Math.Max(Math.Max(1, unlockedCount), providerCount);
        int completedCount = 0;
        for (int challengeIndex = 0; challengeIndex < completionRange; challengeIndex++)
        {
            bool hasCompletion = HasCompletedChallengeForTrack(challengeIndex, trackPrefix);
            if (!hasCompletion && challengeIndex < providerCount && library != null)
            {
                try
                {
                    WordListLibraryItem item = library.FindByProvider(trackProviders[challengeIndex]);
                    hasCompletion = item != null
                        && (item.PlayCount > 0 || (item.CurrentRecord != null && item.CurrentRecord.HasRecord));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[ChallengeProgression] Could not read saved run metadata for challenge index "
                        + challengeIndex + "; saved completion markers remain available. " + exception.Message);
                }
            }

            if (hasCompletion)
                completedCount++;
        }
        return completedCount;
    }

    public static void SetUnlockedCountForTrack(string trackPrefix, int value)
    {
        PlayerPrefs.SetInt(GetUnlockedCountKey(trackPrefix), Mathf.Max(1, value));
        PlayerPrefs.Save();
    }

    /// <summary>Max levels that can be unlocked (0 = no cap). Used in demo mode.</summary>
    public static int MaxUnlockableLevel { get; set; } = 0;

    /// <summary>Check if a specific challenge index (0-based) is unlocked.</summary>
    public static bool IsUnlocked(int challengeIndex) =>
        IsChallengeIndexUnlocked(challengeIndex, UnlockedCount, MaxUnlockableLevel);

    /// <summary>
    /// Pure unlock check used by progression and EditMode tests. The stored count may
    /// include one extra step after the final capped level has been completed.
    /// </summary>
    public static bool IsChallengeIndexUnlocked(int challengeIndex, int unlockedCount, int maxUnlockableLevel)
    {
        return challengeIndex >= 0
            && challengeIndex < unlockedCount
            && (maxUnlockableLevel <= 0 || challengeIndex < maxUnlockableLevel);
    }

    /// <summary>
    /// Unlock next challenge after completing the given one (0-based index).
    /// Completing challenge 0 unlocks challenge 1; replaying 0 doesn't re-unlock.
    /// When MaxUnlockableLevel is set, caps the unlock at that level.
    /// </summary>
    public static void UnlockNext(int completedChallengeIndex)
    {
        UnlockNext(completedChallengeIndex, TrackPrefix, MaxUnlockableLevel);
    }

    public static void UnlockNext(int completedChallengeIndex, string trackPrefix, int maxUnlockableLevel)
    {
        int currentCount = GetUnlockedCountForTrack(trackPrefix);
        int next = CalculateUnlockedCountAfterCompletion(
            completedChallengeIndex, currentCount, maxUnlockableLevel);
        if (next > currentCount)
            SetUnlockedCountForTrack(trackPrefix, next);
    }

    /// <summary>
    /// Calculates the next 1-based progress cursor without changing PlayerPrefs.
    /// A capped final completion records cap+1 so it can satisfy Custom's clear-count
    /// gate, while IsChallengeIndexUnlocked still prevents opening the next level.
    /// </summary>
    public static int CalculateUnlockedCountAfterCompletion(
        int completedChallengeIndex, int currentUnlockedCount, int maxUnlockableLevel)
    {
        long next = (long)completedChallengeIndex + 2L;
        if (next < 1L) next = 1L;
        if (maxUnlockableLevel > 0)
            next = Math.Min(next, (long)maxUnlockableLevel + 1L);

        long result = Math.Max(Math.Max(1L, currentUnlockedCount), next);
        return result > int.MaxValue ? int.MaxValue : (int)result;
    }

    /// <summary>Reset all progress back to level 1 only (unlock + stars + last level).</summary>
    public static void ResetAll()
    {
        // Reset both tracks
        ResetTrack("");
        ResetTrack("cn_");

        // The Library sidecar also records the first-play marker and challenge run
        // summaries. Keep this public reset entry point aligned with the selected-
        // track reset service so direct callers cannot leave those records behind.
        WordListLibraryService library = WordListLibraryService.Shared;
        library.ReloadAfterExternalReset();
        foreach (string prefix in new[] { "", "cn_" })
        {
            if (!library.ResetChallengeProgressForTrack(prefix, out string error))
                Debug.LogWarning("[ChallengeProgression] Could not reset "
                    + (prefix == "cn_" ? "Chinese" : "English")
                    + " Library challenge history: " + error);
        }

        // Clear last selected level so game starts at level 1
        PlayerPrefs.DeleteKey("LevelPanel_LastPath");
        // Clear all time records (challenge + custom best times)
        ListTimeManager.ClearAll();
        PlayerPrefs.Save();
    }

    private static void ResetTrack(string prefix)
    {
        // The saved cursor can extend beyond the legacy 100-level range when
        // master unlock is enabled. Capture it before resetting the cursor.
        int maxStarIndex = Math.Max(99,
            PlayerPrefs.GetInt(GetUnlockedCountKey(prefix), 1) - 1);
        PlayerPrefs.SetInt($"ChallengeUnlockedCount_{prefix}", 1);
        // Clear all star ratings
        for (int i = 0; i <= maxStarIndex; i++)
        {
            string key = $"ChallengeStar_{prefix}{i}";
            if (PlayerPrefs.HasKey(key))
                PlayerPrefs.DeleteKey(key);
        }
    }

    // ── Star rating ────────────────────────────────────────────────────────────

    /// <summary>Calculate stars (0-3) from total error count.</summary>
    public static int CalculateStars(int totalErrors)
    {
        if (totalErrors == 0) return 3;
        if (totalErrors < 5)  return 2;
        if (totalErrors < 10) return 1;
        return 0;
    }

    /// <summary>Save star rating for a challenge by index (0-based).</summary>
    public static void SaveStarRating(int challengeIndex, int stars)
    {
        SaveStarRating(challengeIndex, stars, TrackPrefix);
    }

    public static void SaveStarRating(int challengeIndex, int stars, string trackPrefix)
    {
        string key = GetStarRatingKey(trackPrefix, challengeIndex);
        int current = PlayerPrefs.GetInt(key, 0);
        if (!PlayerPrefs.HasKey(key) || stars > current) // record first completion, including zero stars, and only improve thereafter
        {
            PlayerPrefs.SetInt(key, stars);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Get the saved star rating for a challenge (0-3; use HasCompletedChallengeForTrack to distinguish unrated from zero-star).</summary>
    public static int GetStarRating(int challengeIndex) =>
        GetStarRating(challengeIndex, TrackPrefix);

    public static int GetStarRating(int challengeIndex, string trackPrefix) =>
        PlayerPrefs.GetInt(GetStarRatingKey(trackPrefix, challengeIndex), 0);

    /// <summary>Builds a star string with filled + empty stars, e.g. ★★☆ for 2/3.</summary>
    public static string GetStarDisplay(int stars)
    {
        char[] result = { '☆', '☆', '☆' };
        for (int i = 0; i < stars && i < 3; i++)
            result[i] = '★';
        return new string(result);
    }
}
