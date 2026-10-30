using NUnit.Framework;
using System.IO;
using UnityEngine;

[TestFixture]
public class GameConfigProfileTests
{
    private GameConfigProfile profile;

    [SetUp]
    public void SetUp()
    {
        profile = ScriptableObject.CreateInstance<GameConfigProfile>();
    }

    [TearDown]
    public void TearDown()
    {
        if (profile != null)
            Object.DestroyImmediate(profile);
    }

    [Test]
    public void DemoFinalChallengeCompletionCountsTowardCustomWithoutUnlockingNextLevel()
    {
        int countAfterFiveClears = ChallengeProgression.CalculateUnlockedCountAfterCompletion(
            completedChallengeIndex: 4,
            currentUnlockedCount: 5,
            maxUnlockableLevel: 5);

        Assert.AreEqual(6, countAfterFiveClears);
        Assert.IsTrue(ChallengeProgression.IsChallengeIndexUnlocked(4, countAfterFiveClears, 5));
        Assert.IsFalse(ChallengeProgression.IsChallengeIndexUnlocked(5, countAfterFiveClears, 5));
        Assert.IsTrue(profile.CanOpenCustomLists(countAfterFiveClears - 1));
    }

    [Test]
    public void UncappedProfileUnlocksNextLevelThroughExistingProgressionCursor()
    {
        int countAfterFiveClears = ChallengeProgression.CalculateUnlockedCountAfterCompletion(
            completedChallengeIndex: 4,
            currentUnlockedCount: 5,
            maxUnlockableLevel: 0);

        Assert.AreEqual(6, countAfterFiveClears);
        Assert.IsTrue(ChallengeProgression.IsChallengeIndexUnlocked(5, countAfterFiveClears, 0));
    }

    [TestCase(5, 5)]
    [TestCase(-1, 5)]
    public void CappedChallengeUnlocksNeverExposeAnIndexOutsideTheCap(int index, int cap)
    {
        Assert.IsFalse(ChallengeProgression.IsChallengeIndexUnlocked(index, int.MaxValue, cap));
    }

    [Test]
    public void DemoPolicyBlocksSavedChallengeBeyondItsCap()
    {
        profile.isDemo = true;
        profile.maxChallengeLevels = 5;

        Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(
            profile, challengeIndex: 5, unlockedCount: 6));
        Assert.IsTrue(ChallengeAccessPolicy.CanEnterChallenge(
            profile, challengeIndex: 4, unlockedCount: 5));
    }

    [Test]
    public void ChallengeOverridesPermitEntryWithoutChangingProgressCount()
    {
        profile.isDemo = true;
        profile.maxChallengeLevels = 5;
        int unlockedCount = 1;

        profile.unlockAllChallenges = true;
        Assert.IsTrue(ChallengeAccessPolicy.CanEnterChallenge(profile, 5, unlockedCount));
        Assert.AreEqual(1, unlockedCount);

        profile.unlockAllChallenges = false;
        profile.unlockAllFeatures = true;
        Assert.IsTrue(ChallengeAccessPolicy.CanEnterChallenge(profile, 5, unlockedCount));
        Assert.AreEqual(1, unlockedCount);
    }

    [Test]
    public void LegacyPolicyFallbackRespectsSerializedCapAndUnlockOverride()
    {
        Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(
            null, challengeIndex: 5, unlockedCount: 6,
            legacyMaxChallengeLevels: 5, legacyUnlockAllChallenges: false));
        Assert.IsTrue(ChallengeAccessPolicy.CanEnterChallenge(
            null, challengeIndex: 5, unlockedCount: 1,
            legacyMaxChallengeLevels: 5, legacyUnlockAllChallenges: true));
    }

    [Test]
    public void TrackKeysAreIndependentAndChineseLookingCustomFilesAreNotChallenges()
    {
        string oldTrackPrefix = ChallengeProgression.TrackPrefix;
        string englishKey = ChallengeProgression.GetUnlockedCountKey(ChallengeTrackResolver.EnglishTrackPrefix);
        string chineseKey = ChallengeProgression.GetUnlockedCountKey(ChallengeTrackResolver.ChineseTrackPrefix);
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "ChallengeTrackResolverTests");
        string challengeDirectory = Path.Combine(temporaryRoot, "StreamingAssets", "Levels");
        string challengeCnPath = Path.Combine(challengeDirectory, "level_cn_05.txt");
        string customCnPath = Path.Combine(temporaryRoot, "Custom", "list_cn.txt");

        Assert.AreNotEqual(englishKey, chineseKey);
        Assert.AreEqual("cn_", ChallengeTrackResolver.GetTrackPrefixForChallengePath(challengeCnPath, challengeDirectory));
        Assert.IsNull(ChallengeTrackResolver.GetTrackPrefixForChallengePath(customCnPath, challengeDirectory));
        Assert.AreEqual(oldTrackPrefix, ChallengeProgression.TrackPrefix);
    }

    [Test]
    public void UnlockAllChangesOnlyEffectiveGatesAndDoesNotChangeTheProgressSnapshot()
    {
        profile.isDemo = true;
        profile.maxChallengeLevels = 5;
        profile.unlockAllFeatures = true;
        profile.customUnlockLevels = 5;
        int savedUnlockedCount = 3;

        Assert.AreEqual(0, profile.EffectiveChallengeCap);
        Assert.IsTrue(profile.IsFeatureAvailable(false));
        Assert.IsTrue(profile.CanOpenCustomLists(savedUnlockedCount - 1));
        Assert.AreEqual(3, savedUnlockedCount);
    }

    [Test]
    public void IndividualDemoFeatureOverrideDoesNotOpenCustomTab()
    {
        profile.isDemo = true;
        profile.customUnlockLevels = 5;
        profile.unlockCreateLists = true;

        Assert.IsTrue(profile.IsFeatureAvailable(profile.unlockCreateLists));
        Assert.IsFalse(profile.CanOpenCustomLists(completedChallenges: 0));

        profile.unlockCustomLists = true;
        Assert.IsTrue(profile.CanOpenCustomLists(completedChallenges: 0));
    }
}
