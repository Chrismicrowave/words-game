using UnityEngine;

/// <summary>
/// Tracks which challenge levels the player has unlocked and their star ratings.
/// Progress is persisted in PlayerPrefs and survives restarts.
/// </summary>
public static class ChallengeProgression
{
    private const string PrefKey = "ChallengeUnlockedCount";

    /// <summary>How many challenges are currently unlocked (1-based count).</summary>
    public static int UnlockedCount
    {
        get => Mathf.Max(1, PlayerPrefs.GetInt(PrefKey, 1));
        set
        {
            PlayerPrefs.SetInt(PrefKey, Mathf.Max(1, value));
            PlayerPrefs.Save();
        }
    }

    /// <summary>Check if a specific challenge index (0-based) is unlocked.</summary>
    public static bool IsUnlocked(int challengeIndex) =>
        challengeIndex < UnlockedCount;

    /// <summary>
    /// Unlock next challenge after completing the given one (0-based index).
    /// Completing challenge 0 unlocks challenge 1; replaying 0 doesn't re-unlock.
    /// </summary>
    public static void UnlockNext(int completedChallengeIndex)
    {
        int next = completedChallengeIndex + 2; // 0-based: complete 0 → unlock up to 1
        if (next > UnlockedCount)
            UnlockedCount = next;
    }

    /// <summary>Reset all progress back to level 1 only (unlock + stars).</summary>
    public static void ResetAll()
    {
        UnlockedCount = 1;
        // Clear all star ratings (keys ChallengeStar_0 through ChallengeStar_N)
        for (int i = 0; i < 100; i++)
        {
            string key = $"ChallengeStar_{i}";
            if (PlayerPrefs.HasKey(key))
                PlayerPrefs.DeleteKey(key);
        }
        PlayerPrefs.Save();
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
        string key = $"ChallengeStar_{challengeIndex}";
        int current = PlayerPrefs.GetInt(key, 0);
        if (stars > current) // only overwrite if better
        {
            PlayerPrefs.SetInt(key, stars);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Get saved star rating for a challenge (0-3, 0 = unrated).</summary>
    public static int GetStarRating(int challengeIndex) =>
        PlayerPrefs.GetInt($"ChallengeStar_{challengeIndex}", 0);

    /// <summary>Builds a star string with filled + empty stars, e.g. ★★☆ for 2/3.</summary>
    public static string GetStarDisplay(int stars)
    {
        char[] result = { '☆', '☆', '☆' };
        for (int i = 0; i < stars && i < 3; i++)
            result[i] = '★';
        return new string(result);
    }
}
