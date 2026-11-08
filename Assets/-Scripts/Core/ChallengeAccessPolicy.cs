using System;

/// <summary>
/// Pure policy for entering a challenge. Callers supply the progression count for the
/// challenge's own track so UI browsing cannot change which saved track is consulted.
/// </summary>
public static class ChallengeAccessPolicy
{
    public static bool CanEnterChallenge(
        GameConfigProfile profile,
        int challengeIndex,
        int unlockedCount,
        int legacyMaxChallengeLevels = 0,
        bool legacyUnlockAllChallenges = false)
    {
        if (challengeIndex < 0)
            return false;

        if (profile != null && profile.unlockAllFeatures)
            return true;

        int challengeCap = profile != null
            ? profile.EffectiveChallengeCap
            : Math.Max(0, legacyMaxChallengeLevels);
        if (challengeCap > 0 && challengeIndex >= challengeCap)
            return false;

        if (profile != null)
        {
            return ChallengeProgression.IsChallengeIndexUnlocked(
                challengeIndex, unlockedCount, challengeCap);
        }

        // The legacy override parameter is retained for callers, but unprofiled
        // challenges always follow their configured cap and saved progression.
        return ChallengeProgression.IsChallengeIndexUnlocked(
            challengeIndex, unlockedCount, challengeCap);
    }
}
