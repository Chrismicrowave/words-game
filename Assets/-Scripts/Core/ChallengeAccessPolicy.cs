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

        if (profile != null)
        {
            if (profile.unlockAllFeatures || profile.unlockAllChallenges)
                return true;

            return ChallengeProgression.IsChallengeIndexUnlocked(
                challengeIndex, unlockedCount, profile.EffectiveChallengeCap);
        }

        if (legacyUnlockAllChallenges)
            return true;

        return ChallengeProgression.IsChallengeIndexUnlocked(
            challengeIndex, unlockedCount, Math.Max(0, legacyMaxChallengeLevels));
    }
}
