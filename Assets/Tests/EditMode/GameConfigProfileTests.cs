using NUnit.Framework;
using System.IO;
using UnityEditor;
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
    public void FinalCappedChallengeCompletionCountsTowardCustomWithoutUnlockingNextLevel()
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
    public void ConfiguredChallengeCapBlocksSavedProgressBeyondItsLimit()
    {
        profile.maxChallengeLevels = 5;

        Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(
            profile, challengeIndex: 5, unlockedCount: 6));
        Assert.IsTrue(ChallengeAccessPolicy.CanEnterChallenge(
            profile, challengeIndex: 4, unlockedCount: 5));
    }

    [Test]
    public void IgnoreProgressionLocksBypassesProgressButNotChallengeCap()
    {
        profile.maxChallengeLevels = 5;
        int unlockedCount = 1;

        profile.ignoreProgressionLocks = true;
        Assert.IsTrue(ChallengeAccessPolicy.CanEnterChallenge(profile, 4, unlockedCount));
        Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(profile, 5, int.MaxValue));
        Assert.AreEqual(1, unlockedCount);

        profile.ignoreProgressionLocks = false;
        Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(profile, 4, unlockedCount));
        Assert.AreEqual(1, unlockedCount);
    }

    [Test]
    public void LegacyPolicyFallbackKeepsProgressionGateAndChallengeCap()
    {
        Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(
            null, challengeIndex: 5, unlockedCount: 6,
            legacyMaxChallengeLevels: 5, legacyUnlockAllChallenges: false));
        Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(
            null, challengeIndex: 5, unlockedCount: int.MaxValue,
            legacyMaxChallengeLevels: 5, legacyUnlockAllChallenges: true));
        Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(
            null, challengeIndex: 4, unlockedCount: 1,
            legacyMaxChallengeLevels: 5, legacyUnlockAllChallenges: true));
        Assert.IsTrue(ChallengeAccessPolicy.CanEnterChallenge(
            null, challengeIndex: 4, unlockedCount: 5,
            legacyMaxChallengeLevels: 5, legacyUnlockAllChallenges: false));
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
    public void PreferredTrackUsesLocaleOnlyWhenChineseContentIsAllowed()
    {
        Assert.AreEqual(ChallengeTrackResolver.ChineseTrackPrefix,
            ChallengeTrackResolver.ResolvePreferredTrackPrefix(true, "zh-Hans"));
        Assert.AreEqual(ChallengeTrackResolver.EnglishTrackPrefix,
            ChallengeTrackResolver.ResolvePreferredTrackPrefix(false, "zh-Hans"));
        Assert.AreEqual(ChallengeTrackResolver.EnglishTrackPrefix,
            ChallengeTrackResolver.ResolvePreferredTrackPrefix(true, "en"));
    }

    [Test]
    public void IgnoreProgressionLocksDoesNotChangeCapsOrSavedProgress()
    {
        profile.maxChallengeLevels = 5;
        profile.ignoreProgressionLocks = true;
        profile.customUnlockLevels = 5;
        int savedUnlockedCount = 3;

        Assert.AreEqual(5, profile.EffectiveChallengeCap);
        Assert.IsTrue(profile.CanOpenCustomLists(savedUnlockedCount - 1));
        Assert.IsTrue(ChallengeAccessPolicy.CanEnterChallenge(profile, 4, unlockedCount: 1));
        Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(profile, 5, unlockedCount: int.MaxValue));
        Assert.AreEqual(3, savedUnlockedCount);
    }

    [Test]
    public void CustomUnlockUsesOneGateForAllCustomActions()
    {
        profile.customUnlockLevels = 5;
        Assert.IsFalse(profile.CanOpenCustomLists(completedChallenges: 4));
        Assert.IsTrue(profile.CanOpenCustomLists(completedChallenges: 5));
    }

    [Test]
    public void CommunityRequiresThresholdUnlessProgressionLocksAreIgnored()
    {
        Assert.AreEqual(10, profile.communityUnlockLevels);
        Assert.IsFalse(profile.CanOpenCommunityLists(completedChallenges: 9));
        Assert.IsTrue(profile.CanOpenCommunityLists(completedChallenges: 10));

        profile.ignoreProgressionLocks = true;
        Assert.IsTrue(profile.CanOpenCommunityLists(completedChallenges: 0));
    }

    [Test]
    public void CommunityDisabledRemainsClosedWhenProgressionLocksAreIgnored()
    {
        profile.ignoreProgressionLocks = true;
        profile.communityEnabled = false;
        Assert.IsFalse(profile.CanOpenCommunityLists(completedChallenges: 0));
    }

    [Test]
    public void DeveloperMasterUnlockBypassesProgressAndCommunityDisableButNotChallengeCap()
    {
        profile.unlockAllFeatures = true;
        profile.ignoreProgressionLocks = false;
        profile.communityEnabled = false;
        profile.customUnlockLevels = 50;
        profile.communityUnlockLevels = 50;
        profile.maxChallengeLevels = 5;

        Assert.IsTrue(profile.CanOpenCustomLists(completedChallenges: 0));
        Assert.IsTrue(profile.IsCommunityAvailable);
        Assert.IsTrue(profile.CanOpenCommunityLists(completedChallenges: 0));
        Assert.IsTrue(ChallengeAccessPolicy.CanEnterChallenge(profile, 4, unlockedCount: 1));
        Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(profile, 5, unlockedCount: int.MaxValue));
    }

    [Test]
    public void ZeroCommunityThresholdOpensImmediately()
    {
        profile.communityUnlockLevels = 0;
        Assert.IsTrue(profile.CanOpenCommunityLists(completedChallenges: 0));
    }

    [Test]
    public void CommunityAvailabilityDoesNotChangeChallengeCap()
    {
        profile.maxChallengeLevels = 5;
        profile.communityUnlockLevels = 10;
        profile.ignoreProgressionLocks = true;

        Assert.IsTrue(profile.CanOpenCommunityLists(completedChallenges: 0));
        Assert.AreEqual(5, profile.EffectiveChallengeCap);
        Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(
            profile, challengeIndex: 5, unlockedCount: 11));
    }

    [Test]
    public void NegativeThresholdAndProgressInputsClampToZero()
    {
        profile.communityUnlockLevels = -3;
        Assert.IsTrue(profile.CanOpenCommunityLists(completedChallenges: -1));
        profile.customUnlockLevels = 5;
        Assert.IsFalse(profile.CanOpenCustomLists(completedChallenges: -1));
    }

    [Test]
    public void ZeroChallengeCapLeavesProgressionAsTheOnlyChallengeGate()
    {
        profile.maxChallengeLevels = 0;

        Assert.AreEqual(0, profile.EffectiveChallengeCap);
        Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(profile, 5, unlockedCount: 1));
        Assert.IsTrue(ChallengeAccessPolicy.CanEnterChallenge(profile, 5, unlockedCount: 6));
    }

    [Test]
    public void MigratedProfileAssetsPreserveEffectivePermissionsAndLimits()
    {
        GameConfigProfile development = AssetDatabase.LoadAssetAtPath<GameConfigProfile>(
            "Assets/-Data/Profiles/Development.asset");
        GameConfigProfile demo = AssetDatabase.LoadAssetAtPath<GameConfigProfile>(
            "Assets/-Data/Profiles/Demo.asset");
        GameConfigProfile playtest = AssetDatabase.LoadAssetAtPath<GameConfigProfile>(
            "Assets/-Data/Profiles/Playtest.asset");

        Assert.NotNull(development);
        Assert.NotNull(demo);
        Assert.NotNull(playtest);

        Assert.IsTrue(development.ignoreProgressionLocks);
        Assert.IsTrue(development.unlockAllFeatures);
        Assert.IsTrue(development.communityEnabled);
        Assert.AreEqual(0, development.EffectiveChallengeCap);
        Assert.AreEqual(0, development.maxCustomLists);
        Assert.AreEqual(0, development.maxWordsPerList);
        Assert.IsTrue(development.CanOpenCustomLists(0));
        Assert.IsTrue(development.CanOpenCommunityLists(0));

        Assert.IsFalse(demo.ignoreProgressionLocks);
        Assert.IsFalse(demo.unlockAllFeatures);
        Assert.IsFalse(demo.communityEnabled);
        Assert.IsFalse(demo.IsCommunityAvailable);
        Assert.AreEqual(5, demo.EffectiveChallengeCap);
        Assert.AreEqual(1, demo.maxCustomLists);
        Assert.AreEqual(5, demo.maxWordsPerList);
        Assert.IsFalse(demo.CanOpenCustomLists(4));
        Assert.IsTrue(demo.CanOpenCustomLists(5));
        Assert.IsFalse(demo.CanOpenCommunityLists(9));
        Assert.IsFalse(demo.CanOpenCommunityLists(10));

        Assert.IsFalse(playtest.ignoreProgressionLocks);
        Assert.IsFalse(playtest.unlockAllFeatures);
        Assert.IsTrue(playtest.communityEnabled);
        Assert.AreEqual(0, playtest.EffectiveChallengeCap);
        Assert.AreEqual(0, playtest.maxCustomLists);
        Assert.AreEqual(0, playtest.maxWordsPerList);
        Assert.IsFalse(playtest.CanOpenCustomLists(4));
        Assert.IsTrue(playtest.CanOpenCustomLists(5));
        Assert.IsFalse(playtest.CanOpenCommunityLists(9));
        Assert.IsTrue(playtest.CanOpenCommunityLists(10));
    }
}
