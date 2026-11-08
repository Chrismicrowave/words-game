using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Words.UiMockups.WordListHub.Tests
{
    public sealed class WordListHubCommunityProjectionTests
    {
        [Test]
        public void RemoteSummaryProjectionKeepsWordCountAndVoteStateWithoutPreviewWords()
        {
            MockupDiscoveryItem item = MockupWordListProjection.CreateDiscoveryItem(
                "12345", "Garden", "A small list", MockupLanguage.Mixed,
                Array.Empty<string>(), "Author", MockupDiscoveryStatus.New,
                wordCount: 24, likeCount: 8, isLiked: true, isDownloaded: false);

            Assert.AreEqual(24, item.WordCount);
            Assert.AreEqual(0, item.Words.Count, "A summary result must not invent the detail preview payload.");
            Assert.AreEqual(8, item.LikeCount);
            Assert.IsTrue(item.IsLiked);
            Assert.IsFalse(item.IsDownloaded);
        }

        [Test]
        public void FailedPublicationWithRemoteBindingStaysInMyPublished()
        {
            DateTime now = DateTime.UtcNow;
            MockupWordList owned = MockupWordListProjection.CreateWordList(
                "local-1", "Garden", "", "", MockupLanguage.English,
                new[] { "seed" }, false, now, now, now, null, null, null, 1, 0);

            MockupWordListProjection.WithPublication(owned,
                new MockupCommunityPublicationInfo("local-1", "Garden", "12345",
                    "hash", MockupCommunityPublicationState.Failed, false,
                    "Upload failed; publication outcome needs review."));

            Assert.IsTrue(owned.HasPublishedBinding);
            Assert.AreEqual(MockupLibraryGroup.MyPublished, owned.Group);
        }

        [Test]
        public void UnplayedChallengeStaysPrivateEvenWhenItsProgressionGateIsOpen()
        {
            DateTime now = DateTime.UtcNow;
            MockupWordList unplayed = MockupWordListProjection.CreateWordList(
                "challenge-unplayed", "private title", "private description", string.Empty,
                MockupLanguage.English, new[] { "private word" }, false,
                now, now, now, null, null, null, 1, 0,
                isChallenge: true, isChallengeUnlocked: true, challengeNumber: 4);
            MockupWordList completedByRun = MockupWordListProjection.CreateWordList(
                "challenge-played", "visible title", string.Empty, string.Empty,
                MockupLanguage.English, new[] { "visible word" }, false,
                now, now, now, null, null, null, 1, 1,
                isChallenge: true, isChallengeUnlocked: true, challengeNumber: 4);
            MockupWordList enteredButUncompleted = MockupWordListProjection.CreateWordList(
                "challenge-entered", "entered title", string.Empty, string.Empty,
                MockupLanguage.English, new[] { "entered word" }, false,
                now, now, now, now, null, null, 1, 0,
                isChallenge: true, isChallengeUnlocked: true, challengeNumber: 4);
            MockupWordList masterOverride = MockupWordListProjection.CreateWordList(
                "challenge-master", "master title", string.Empty, string.Empty,
                MockupLanguage.English, new[] { "master word" }, false,
                now, now, now, null, null, null, 1, 0,
                isChallenge: true, isChallengeUnlocked: true, challengeNumber: 4,
                revealChallengeDetails: true);

            Assert.IsFalse(unplayed.HasCompletedChallenge,
                "An open progression gate, including a development override, is not a completed run.");
            Assert.IsFalse(unplayed.CanRevealChallengeDetails,
                "An unplayed challenge stays private even when its progression gate is open.");
            Assert.IsTrue(enteredButUncompleted.HasBeenPlayed);
            Assert.IsFalse(enteredButUncompleted.HasCompletedChallenge,
                "Entering a challenge does not count as a completed run.");
            Assert.IsTrue(enteredButUncompleted.CanRevealChallengeDetails);
            Assert.IsFalse(masterOverride.HasBeenPlayed);
            Assert.AreEqual(0, masterOverride.PlayCount);
            Assert.IsFalse(masterOverride.HasCompletedChallenge);
            Assert.IsTrue(masterOverride.CanRevealChallengeDetails,
                "The explicit master override reveals contents without recording a play.");
            Assert.IsTrue(completedByRun.HasCompletedChallenge);
            Assert.IsTrue(completedByRun.CanRevealChallengeDetails);
        }

        [Test]
        public void ProductionBackendProjectionUsesLastPlayedAndMasterOverrideWithoutInventingCompletion()
        {
            Type backendType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("WordListHubProductionBackend", false))
                .FirstOrDefault(type => type != null);
            Assert.That(backendType, Is.Not.Null);
            MethodInfo project = backendType.GetMethod("Project", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(project, Is.Not.Null);
            DateTime now = DateTime.UtcNow;

            WordListLibraryItem enteredItem = CreateProjectionSource("entered", now, now);
            MockupWordList entered = (MockupWordList)project.Invoke(null,
                new object[] { enteredItem, true, true, null, null, 2, false, false });
            Assert.That(entered.LastPlayedAt, Is.Not.Null,
                "The production projection carries the persisted MarkPlayed timestamp.");
            Assert.That(entered.PlayCount, Is.Zero);
            Assert.That(entered.HasBeenPlayed, Is.True);
            Assert.That(entered.HasCompletedChallenge, Is.False);
            Assert.That(entered.CanRevealChallengeDetails, Is.True);

            WordListLibraryItem untouchedItem = CreateProjectionSource("unplayed", now, null);
            MockupWordList untouched = (MockupWordList)project.Invoke(null,
                new object[] { untouchedItem, true, true, null, null, 2, false, false });
            Assert.That(untouched.HasBeenPlayed, Is.False);
            Assert.That(untouched.HasCompletedChallenge, Is.False);
            Assert.That(untouched.CanRevealChallengeDetails, Is.False);

            MockupWordList overrideProjection = (MockupWordList)project.Invoke(null,
                new object[] { untouchedItem, true, true, null, null, 2, false, true });
            Assert.That(overrideProjection.LastPlayedAt, Is.Null);
            Assert.That(overrideProjection.PlayCount, Is.Zero);
            Assert.That(overrideProjection.HasCompletedChallenge, Is.False);
            Assert.That(overrideProjection.CanRevealChallengeDetails, Is.True);

            MockupWordList overrideDisabledAgain = (MockupWordList)project.Invoke(null,
                new object[] { untouchedItem, true, true, null, null, 2, false, false });
            Assert.That(overrideDisabledAgain.CanRevealChallengeDetails, Is.False,
                "Turning the override back off does not persist a reveal marker.");
            Assert.That(untouchedItem.LastPlayedAtUtc, Is.Null);
            Assert.That(untouchedItem.PlayCount, Is.Zero);
        }

        [Test]
        public void LaterProgressionCursorDoesNotCompleteAnUnplayedEarlierChallenge()
        {
            Type backendType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("WordListHubProductionBackend", false))
                .FirstOrDefault(type => type != null);
            Assert.That(backendType, Is.Not.Null);
            MethodInfo hasCompletionEvidence = backendType.GetMethod(
                "HasChallengeCompletionEvidence", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(hasCompletionEvidence, Is.Not.Null);

            int cursorAfterLaterCompletion = ChallengeProgression.CalculateUnlockedCountAfterCompletion(
                completedChallengeIndex: 2, currentUnlockedCount: 1, maxUnlockableLevel: 0);
            Assert.That(cursorAfterLaterCompletion - 1, Is.GreaterThan(0),
                "Completing a later level advances the aggregate cursor past earlier levels.");

            string testTrackPrefix = "hub_completion_evidence_" + Guid.NewGuid().ToString("N") + "_";
            string cursorKey = ChallengeProgression.GetUnlockedCountKey(testTrackPrefix);
            string zeroStarKey = ChallengeProgression.GetStarRatingKey(testTrackPrefix, 0);
            try
            {
                ChallengeProgression.SetUnlockedCountForTrack(testTrackPrefix, cursorAfterLaterCompletion);
                WordListLibraryItem skippedEarlierChallenge = CreateProjectionSource(
                    "skipped-earlier", DateTime.UtcNow, null);
                bool hasCompletion = (bool)hasCompletionEvidence.Invoke(null,
                    new object[] { skippedEarlierChallenge, 0, testTrackPrefix });
                Assert.That(hasCompletion, Is.False,
                    "An advanced aggregate cursor cannot supply per-challenge completion evidence.");

                ChallengeProgression.SaveStarRating(0, 0, testTrackPrefix);
                Assert.That((bool)hasCompletionEvidence.Invoke(null,
                    new object[] { skippedEarlierChallenge, 0, testTrackPrefix }), Is.True,
                    "A persisted zero-star completion marker counts even without library metadata.");

                var recordedZeroStarChallenge = CreateProjectionSource(
                    "recorded-zero-stars", DateTime.UtcNow, null);
                SetNonPublicProperty(recordedZeroStarChallenge, "CurrentRecord",
                    new WordListRevisionRecord
                    {
                        latest = new WordListRunSummary { hasResult = true, stars = 0 }
                    });
                PlayerPrefs.DeleteKey(zeroStarKey);
                Assert.That((bool)hasCompletionEvidence.Invoke(null,
                    new object[] { recordedZeroStarChallenge, 0, testTrackPrefix }), Is.True,
                    "A real saved run record counts even without a progression rating.");
            }
            finally
            {
                PlayerPrefs.DeleteKey(cursorKey);
                PlayerPrefs.DeleteKey(zeroStarKey);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void AvailableUnlockMilestonesRemainPreviewedBeforeProgressionCompletes()
        {
            Type backendType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("WordListHubProductionBackend", false))
                .FirstOrDefault(type => type != null);
            Assert.That(backendType, Is.Not.Null,
                "The production backend must be loaded for its no-scene policy helper test.");

            MethodInfo milestoneVisible = backendType.GetMethod("IsUnlockMilestoneVisible",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo rewardLabel = backendType.GetMethod("GetUnlockRewardLabel",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(milestoneVisible, Is.Not.Null);
            Assert.That(rewardLabel, Is.Not.Null);

            bool customAvailable = (bool)milestoneVisible.Invoke(null, new object[] { 5, true });
            bool communityAvailable = (bool)milestoneVisible.Invoke(null, new object[] { 10, true });
            bool communityDisabled = (bool)milestoneVisible.Invoke(null, new object[] { 10, false });
            Assert.IsTrue(customAvailable,
                "A profile master unlock must not suppress the visible Custom milestone preview.");
            Assert.IsTrue(communityAvailable,
                "An enabled Community feature must keep its milestone preview visible under a bypass.");
            Assert.IsFalse(communityDisabled,
                "Community milestones stay hidden when the Community feature itself is unavailable.");
            Assert.IsFalse((bool)milestoneVisible.Invoke(null, new object[] { 0, true }),
                "A zero requirement does not create a level-zero reward.");

            string customLabel = (string)rewardLabel.Invoke(null,
                new object[] { 5, 5, customAvailable, 10, communityAvailable });
            string communityLabel = (string)rewardLabel.Invoke(null,
                new object[] { 10, 5, customAvailable, 10, communityAvailable });
            string unavailableCommunityLabel = (string)rewardLabel.Invoke(null,
                new object[] { 10, 5, customAvailable, 10, communityDisabled });
            Assert.AreEqual("Unlock Library", customLabel);
            Assert.AreEqual("Unlock Community", communityLabel);
            Assert.AreEqual(string.Empty, unavailableCommunityLabel);
        }

        private static WordListLibraryItem CreateProjectionSource(
            string localId, DateTime now, DateTime? lastPlayedAt)
        {
            var item = new WordListLibraryItem();
            SetNonPublicProperty(item, "LocalListId", localId);
            SetNonPublicProperty(item, "Title", "Challenge " + localId);
            SetNonPublicProperty(item, "Description", string.Empty);
            SetNonPublicProperty(item, "LanguageMode", LanguageMode.English);
            SetNonPublicProperty(item, "ContentRevision", 1);
            SetNonPublicProperty(item, "AddedAtUtc", now.ToString("O"));
            SetNonPublicProperty(item, "ModifiedAtUtc", now.ToString("O"));
            SetNonPublicProperty(item, "ContentModifiedAtUtc", now.ToString("O"));
            SetNonPublicProperty(item, "LastPlayedAtUtc",
                lastPlayedAt.HasValue ? lastPlayedAt.Value.ToString("O") : null);
            SetNonPublicProperty(item, "PlayCount", 0);
            SetNonPublicProperty(item, "Content", new WordListLibraryContent
            {
                LanguageMode = LanguageMode.English,
                Words = new System.Collections.Generic.List<string> { "word" }
            });
            return item;
        }

        private static void SetNonPublicProperty(object target, string propertyName, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, propertyName + " should exist.");
            MethodInfo setter = property.GetSetMethod(true);
            Assert.That(setter, Is.Not.Null, propertyName + " should be writable in the fixture.");
            setter.Invoke(target, new[] { value });
        }
    }
}
