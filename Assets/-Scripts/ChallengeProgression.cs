using UnityEngine;

/// <summary>
/// Tracks which challenge levels the player has unlocked.
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

    /// <summary>Unlock the next challenge after completing the current one.</summary>
    public static void UnlockNext() =>
        UnlockedCount = UnlockedCount + 1;

    /// <summary>Reset all progress back to level 1 only.</summary>
    public static void Reset() => UnlockedCount = 1;
}
