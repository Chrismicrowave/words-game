using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
    public void LegacyIgnoreProgressionLocksDoesNotBypassChallengeProgressOrCap()
    {
        profile.maxChallengeLevels = 5;
        int unlockedCount = 1;

        profile.ignoreProgressionLocks = true;
        Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(profile, 4, unlockedCount));
        Assert.IsTrue(ChallengeAccessPolicy.CanEnterChallenge(profile, 0, unlockedCount));
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
    public void CompletedChallengeCountUsesPerLevelStarKeysAndRespectsProgressCursor()
    {
        string trackPrefix = "profile_test_" + System.Guid.NewGuid().ToString("N") + "_";
        string unlockedKey = ChallengeProgression.GetUnlockedCountKey(trackPrefix);
        try
        {
            ChallengeProgression.SetUnlockedCountForTrack(trackPrefix, 7);
            ChallengeProgression.SaveStarRating(0, 3, trackPrefix);
            ChallengeProgression.SaveStarRating(2, 0, trackPrefix);
            ChallengeProgression.SaveStarRating(6, 1, trackPrefix);
            ChallengeProgression.SaveStarRating(7, 3, trackPrefix);

            Assert.IsTrue(ChallengeProgression.HasCompletedChallengeForTrack(0, trackPrefix));
            Assert.IsFalse(ChallengeProgression.HasCompletedChallengeForTrack(1, trackPrefix));
            Assert.IsTrue(ChallengeProgression.HasCompletedChallengeForTrack(2, trackPrefix),
                "A persisted zero-star result still represents a completed challenge.");
            Assert.AreEqual(3, ChallengeProgression.GetCompletedChallengesForTrack(trackPrefix),
                "Completion counting is limited to indices below the saved progression cursor.");
        }
        finally
        {
            PlayerPrefs.DeleteKey(unlockedKey);
            for (int challengeIndex = 0; challengeIndex <= 7; challengeIndex++)
                PlayerPrefs.DeleteKey(ChallengeProgression.GetStarRatingKey(trackPrefix, challengeIndex));
            PlayerPrefs.Save();
        }
    }

    [Test]
    public void CompletingFutureChallengeWithMasterUnlockDoesNotCountSkippedLevelsAsCompleted()
    {
        string trackPrefix = "profile_jump_" + System.Guid.NewGuid().ToString("N") + "_";
        string unlockedKey = ChallengeProgression.GetUnlockedCountKey(trackPrefix);
        profile.unlockAllFeatures = true;
        try
        {
            ChallengeProgression.SetUnlockedCountForTrack(trackPrefix, 1);
            ChallengeProgression.UnlockNext(
                completedChallengeIndex: 5,
                trackPrefix: trackPrefix,
                maxUnlockableLevel: profile.EffectiveChallengeCap);
            ChallengeProgression.SaveStarRating(5, stars: 0, trackPrefix: trackPrefix);

            profile.unlockAllFeatures = false;
            int completed = ChallengeProgression.GetCompletedChallengesForTrack(trackPrefix);
            Assert.AreEqual(1, completed);
            Assert.IsFalse(ChallengeProgression.HasCompletedChallengeForTrack(0, trackPrefix));
            Assert.IsFalse(ChallengeProgression.HasCompletedChallengeForTrack(4, trackPrefix));
            Assert.IsTrue(ChallengeProgression.HasCompletedChallengeForTrack(5, trackPrefix));
            Assert.IsFalse(profile.CanOpenCustomLists(completed));
            Assert.IsFalse(profile.CanOpenCommunityLists(completed));
        }
        finally
        {
            PlayerPrefs.DeleteKey(unlockedKey);
            for (int challengeIndex = 0; challengeIndex <= 5; challengeIndex++)
                PlayerPrefs.DeleteKey(ChallengeProgression.GetStarRatingKey(trackPrefix, challengeIndex));
            PlayerPrefs.Save();
        }
    }

    [Test]
    public void LegacyZeroStarLibraryRecordCountsAsCompletionEvenWhenCursorIsStale()
    {
        string trackPrefix = "profile_legacy_zero_" + System.Guid.NewGuid().ToString("N") + "_";
        string unlockedKey = ChallengeProgression.GetUnlockedCountKey(trackPrefix);
        string tempRoot = Path.Combine(Path.GetTempPath(), "ChallengeCompletionMetadata-" + System.Guid.NewGuid().ToString("N"));
        string customDirectory = Path.Combine(tempRoot, "Custom");
        string statePath = Path.Combine(tempRoot, "WordListLibrary.json");
        var providers = new List<LevelWordListProvider>();
        Directory.CreateDirectory(tempRoot);

        try
        {
            for (int challengeIndex = 0; challengeIndex < 6; challengeIndex++)
            {
                string path = Path.Combine(tempRoot, "challenge_" + challengeIndex.ToString("D2") + ".txt");
                File.WriteAllText(path, "first\nsecond");
                providers.Add(new LevelWordListProvider(path));
            }

            var library = new WordListLibraryService(customDirectory, statePath);
            Assert.IsTrue(library.TryRecordRun(providers[5], 12f, 2, 10, 0, out string error), error);
            WordListLibraryItem savedRecord = library.FindByProvider(providers[5]);
            Assert.IsNotNull(savedRecord);
            Assert.IsTrue(savedRecord.CurrentRecord.HasRecord);
            Assert.AreEqual(1, savedRecord.PlayCount);
            Assert.IsFalse(PlayerPrefs.HasKey(ChallengeProgression.GetStarRatingKey(trackPrefix, 5)),
                "This represents a legacy zero-star record with no persisted rating key.");

            // A stale cursor should not hide an actual library completion at a future index.
            PlayerPrefs.SetInt(unlockedKey, 1);
            PlayerPrefs.Save();
            MethodInfo countMethod = typeof(ChallengeProgression).GetMethod(
                "CountCompletedChallengesForTrack", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(countMethod);
            int completed = (int)countMethod.Invoke(null,
                new object[] { trackPrefix, 1, providers, library });

            Assert.AreEqual(1, completed);
            Assert.IsFalse(profile.CanOpenCustomLists(completed));
            Assert.IsFalse(profile.CanOpenCommunityLists(completed));
        }
        finally
        {
            PlayerPrefs.DeleteKey(unlockedKey);
            for (int challengeIndex = 0; challengeIndex <= 5; challengeIndex++)
                PlayerPrefs.DeleteKey(ChallengeProgression.GetStarRatingKey(trackPrefix, challengeIndex));
            PlayerPrefs.Save();
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
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
    public void LegacyIgnoreProgressionLocksDoesNotChangeCapsOrSavedProgress()
    {
        profile.maxChallengeLevels = 5;
        profile.ignoreProgressionLocks = true;
        profile.customUnlockLevels = 5;
        int savedUnlockedCount = 3;

        Assert.AreEqual(5, profile.EffectiveChallengeCap);
        Assert.IsFalse(profile.CanOpenCustomLists(savedUnlockedCount - 1));
        Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(profile, 4, unlockedCount: 1));
        Assert.IsTrue(ChallengeAccessPolicy.CanEnterChallenge(profile, 0, unlockedCount: 1));
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
    public void CommunityRequiresThresholdUnlessMasterUnlockIsEnabled()
    {
        Assert.AreEqual(10, profile.communityUnlockLevels);
        Assert.IsFalse(profile.CanOpenCommunityLists(completedChallenges: 9));
        Assert.IsTrue(profile.CanOpenCommunityLists(completedChallenges: 10));

        profile.ignoreProgressionLocks = true;
        Assert.IsFalse(profile.CanOpenCommunityLists(completedChallenges: 0));
        profile.unlockAllFeatures = true;
        Assert.IsTrue(profile.CanOpenCommunityLists(completedChallenges: 0));
    }

    [Test]
    public void CommunityDisabledRemainsClosedWhenLegacyIgnoreFlagIsSet()
    {
        profile.ignoreProgressionLocks = true;
        profile.communityEnabled = false;
        Assert.IsFalse(profile.CanOpenCommunityLists(completedChallenges: 0));
        Assert.IsFalse(profile.IsCommunityAvailable);
    }

    [Test]
    public void DeveloperMasterUnlockBypassesProgressCommunityDisableAndChallengeCap()
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
        Assert.IsTrue(ChallengeAccessPolicy.CanEnterChallenge(profile, 5, unlockedCount: 1));
        Assert.AreEqual(0, profile.EffectiveChallengeCap,
            "The master unlock must also remove the effective cap used when completion advances progression.");
        Assert.AreEqual(6, ChallengeProgression.CalculateUnlockedCountAfterCompletion(
            completedChallengeIndex: 4, currentUnlockedCount: 5, maxUnlockableLevel: profile.EffectiveChallengeCap));
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

        Assert.IsFalse(profile.CanOpenCommunityLists(completedChallenges: 0));
        Assert.IsTrue(profile.CanOpenCommunityLists(completedChallenges: 10));
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
    public void DevelopmentProfileStartsLockedUntilProgressThresholdsOrExplicitOverride()
    {
        GameConfigProfile source = AssetDatabase.LoadAssetAtPath<GameConfigProfile>(
            "Assets/-Data/Profiles/Development.asset");
        Assert.NotNull(source);

        GameConfigProfile development = Object.Instantiate(source);
        try
        {
            Assert.IsFalse(development.unlockAllFeatures);
            Assert.IsTrue(development.ignoreProgressionLocks,
                "The serialized legacy flag remains set in this asset but no longer grants access.");
            Assert.IsTrue(development.communityEnabled);
            Assert.AreEqual(5, development.customUnlockLevels);
            Assert.AreEqual(10, development.communityUnlockLevels);
            Assert.AreEqual(0, development.EffectiveChallengeCap);

            // A reset progression cursor has the first level open and level two closed.
            Assert.IsTrue(ChallengeAccessPolicy.CanEnterChallenge(development, 0, unlockedCount: 1));
            Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(development, 1, unlockedCount: 1));
            Assert.IsFalse(development.CanOpenCustomLists(completedChallenges: 0));
            Assert.IsFalse(development.CanOpenCommunityLists(completedChallenges: 0));
            Assert.IsFalse(development.CanOpenCustomLists(completedChallenges: 4));
            Assert.IsTrue(development.CanOpenCustomLists(completedChallenges: 5));
            Assert.IsFalse(development.CanOpenCommunityLists(completedChallenges: 9));
            Assert.IsTrue(development.CanOpenCommunityLists(completedChallenges: 10));

            // The serialized legacy flag is ignored, so saved progression remains authoritative.
            development.ignoreProgressionLocks = true;
            development.maxChallengeLevels = 5;
            Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(development, 4, unlockedCount: 1));
            Assert.IsFalse(ChallengeAccessPolicy.CanEnterChallenge(development, 5, unlockedCount: 6));
            Assert.IsFalse(development.CanOpenCustomLists(completedChallenges: 0));
            Assert.IsFalse(development.CanOpenCommunityLists(completedChallenges: 0));

            // Community availability remains a separate switch unless the master override is on.
            development.communityEnabled = false;
            Assert.IsFalse(development.CanOpenCommunityLists(completedChallenges: 0));
            development.unlockAllFeatures = true;
            Assert.IsTrue(development.IsCommunityAvailable);
            Assert.IsTrue(development.CanOpenCommunityLists(completedChallenges: 0));
            Assert.IsTrue(ChallengeAccessPolicy.CanEnterChallenge(development, 5, unlockedCount: 1));
            Assert.AreEqual(0, development.EffectiveChallengeCap);
            Assert.AreEqual(6, ChallengeProgression.CalculateUnlockedCountAfterCompletion(
                completedChallengeIndex: 4, currentUnlockedCount: 5, maxUnlockableLevel: development.EffectiveChallengeCap));
        }
        finally
        {
            Object.DestroyImmediate(development);
        }
    }

    [Test]
    public void ProfileAssetsAndLimitedFixturePreserveEffectivePermissionsAndLimits()
    {
        GameConfigProfile development = AssetDatabase.LoadAssetAtPath<GameConfigProfile>(
            "Assets/-Data/Profiles/Development.asset");
        GameConfigProfile playtest = AssetDatabase.LoadAssetAtPath<GameConfigProfile>(
            "Assets/-Data/Profiles/Playtest.asset");

        Assert.NotNull(development);
        Assert.NotNull(playtest);

        Assert.IsTrue(development.ignoreProgressionLocks);
        Assert.IsFalse(development.unlockAllFeatures);
        Assert.IsTrue(development.communityEnabled);
        Assert.AreEqual(0, development.EffectiveChallengeCap);
        Assert.AreEqual(0, development.maxCustomLists);
        Assert.AreEqual(0, development.maxWordsPerList);

        GameConfigProfile limitedProfile = ScriptableObject.CreateInstance<GameConfigProfile>();
        try
        {
            limitedProfile.maxChallengeLevels = 5;
            limitedProfile.maxCustomLists = 1;
            limitedProfile.maxWordsPerList = 5;
            limitedProfile.customUnlockLevels = 5;
            limitedProfile.communityUnlockLevels = 10;
            limitedProfile.communityEnabled = false;

            Assert.IsFalse(limitedProfile.ignoreProgressionLocks);
            Assert.IsFalse(limitedProfile.unlockAllFeatures);
            Assert.IsFalse(limitedProfile.IsCommunityAvailable);
            Assert.AreEqual(5, limitedProfile.EffectiveChallengeCap);
            Assert.AreEqual(1, limitedProfile.maxCustomLists);
            Assert.AreEqual(5, limitedProfile.maxWordsPerList);
            Assert.IsFalse(limitedProfile.CanOpenCustomLists(4));
            Assert.IsTrue(limitedProfile.CanOpenCustomLists(5));
            Assert.IsFalse(limitedProfile.CanOpenCommunityLists(9));
            Assert.IsFalse(limitedProfile.CanOpenCommunityLists(10));
        }
        finally
        {
            Object.DestroyImmediate(limitedProfile);
        }

        Assert.IsFalse(playtest.ignoreProgressionLocks);
        Assert.IsTrue(playtest.unlockAllFeatures);
        Assert.IsTrue(playtest.communityEnabled);
        Assert.AreEqual(0, playtest.EffectiveChallengeCap);
        Assert.AreEqual(0, playtest.maxCustomLists);
        Assert.AreEqual(0, playtest.maxWordsPerList);
        Assert.IsTrue(playtest.CanOpenCustomLists(4));
        Assert.IsTrue(playtest.CanOpenCustomLists(5));
        Assert.IsTrue(playtest.CanOpenCommunityLists(9));
        Assert.IsTrue(playtest.CanOpenCommunityLists(10));
    }

    [Test]
    public void DebugAnyKeyCompletion_IsDisabledByDefaultAndEnabledInLocalTestingProfiles()
    {
        Assert.IsFalse(profile.debugAnyKeyCompletesWord);

        GameConfigProfile development = AssetDatabase.LoadAssetAtPath<GameConfigProfile>(
            "Assets/-Data/Profiles/Development.asset");
        GameConfigProfile playtest = AssetDatabase.LoadAssetAtPath<GameConfigProfile>(
            "Assets/-Data/Profiles/Playtest.asset");
        GameConfigProfile release = AssetDatabase.LoadAssetAtPath<GameConfigProfile>(
            "Assets/-Data/Profiles/Release.asset");

        Assert.NotNull(development);
        Assert.NotNull(playtest);
        Assert.NotNull(release);
        Assert.IsTrue(development.debugAnyKeyCompletesWord);
        Assert.IsTrue(playtest.debugAnyKeyCompletesWord);
        Assert.IsFalse(release.debugAnyKeyCompletesWord);
    }

    [Test]
    public void SteamDiagnosticsFlagsDefaultOffAndProfilesUseExpectedBuildPurpose()
    {
        Assert.AreEqual(BuildPurpose.LocalTesting, profile.buildPurpose);
        Assert.IsFalse(profile.enableSteamDiagnostics);
        Assert.IsFalse(profile.allowEditorSteamTestTools);

        GameConfigProfile development = AssetDatabase.LoadAssetAtPath<GameConfigProfile>(
            "Assets/-Data/Profiles/Development.asset");
        GameConfigProfile playtest = AssetDatabase.LoadAssetAtPath<GameConfigProfile>(
            "Assets/-Data/Profiles/Playtest.asset");
        GameConfigProfile release = AssetDatabase.LoadAssetAtPath<GameConfigProfile>(
            "Assets/-Data/Profiles/Release.asset");

        Assert.NotNull(development);
        Assert.NotNull(playtest);
        Assert.NotNull(release);

        Assert.AreEqual(BuildPurpose.LocalTesting, development.buildPurpose);
        Assert.AreEqual(BuildPurpose.LocalTesting, playtest.buildPurpose);
        Assert.IsTrue(development.enableSteamDiagnostics);
        Assert.IsTrue(playtest.enableSteamDiagnostics);
        Assert.IsFalse(development.allowEditorSteamTestTools);
        Assert.IsFalse(playtest.allowEditorSteamTestTools);

        Assert.AreEqual(BuildPurpose.PlayerRelease, release.buildPurpose);
        Assert.IsFalse(release.enableSteamDiagnostics);
        Assert.IsFalse(release.allowEditorSteamTestTools);
    }

    [Test]
    public void ReleaseProfileDisablesDevelopmentFeaturesAndUsesGenericPlayerName()
    {
        GameConfigProfile release = AssetDatabase.LoadAssetAtPath<GameConfigProfile>(
            "Assets/-Data/Profiles/Release.asset");
        GameConfig config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/-Data/GameConfig.asset");

        Assert.NotNull(release);
        Assert.NotNull(config);
        Assert.AreEqual(BuildPurpose.PlayerRelease, release.buildPurpose);
        Assert.IsFalse(release.enableSteamDiagnostics);
        Assert.IsFalse(release.allowEditorSteamTestTools);
        Assert.IsFalse(release.unlockAllFeatures);
        Assert.IsFalse(release.ignoreProgressionLocks);
        Assert.IsFalse(release.debugAnyKeyCompletesWord);
        Assert.IsFalse(release.resetLocalDataOnFirstRun);
        Assert.IsFalse(release.resetAllLocalDataOnNextEditorPlay);
        Assert.IsFalse(release.resetAllLocalDataOnFirstBuildRun);
        Assert.AreEqual(GameResetTargets.None, release.resetTargets);
        Assert.AreEqual(0, release.maxChallengeLevels);
        Assert.AreEqual(0, release.maxCustomLists);
        Assert.AreEqual(0, release.maxWordsPerList);
        Assert.AreEqual(5, release.customUnlockLevels);
        Assert.IsTrue(release.communityEnabled);
        Assert.AreEqual(10, release.communityUnlockLevels);
        Assert.NotNull(release.defaults);
        Assert.AreEqual("Player", release.defaults.playerName);
        Assert.NotNull(config.activeProfile);
        Assert.AreEqual("Development", config.activeProfile.name,
            "Adding the release profile must not change the project's active profile.");

        Assert.IsNull(typeof(GameConfigProfile).GetField("steamId"));
        Assert.IsNull(typeof(GameConfigProfile).GetField("currentAccountId"));
        Assert.IsNull(typeof(GameConfigProfile).GetField("steamPassword"));
    }
}
