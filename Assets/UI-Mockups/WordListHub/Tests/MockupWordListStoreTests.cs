using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Words.UiMockups.WordListHub.Tests
{
    [TestFixture]
    public sealed class MockupWordListStoreTests
    {
        private const string MockupScenePath = "Assets/Scenes/UI-Mockups/WordListHubMockup.unity";

        private MockupWordListStore store;

        [SetUp]
        public void SetUp()
        {
            store = MockupWordListStore.CreateWithFixtures();
        }

        [Test]
        public void WordListHubMockupScene_usesSlicedImagesEverywhere()
        {
            Scene scene = SceneManager.GetSceneByPath(MockupScenePath);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
                scene = EditorSceneManager.OpenScene(MockupScenePath, OpenSceneMode.Additive);

            try
            {
                Image[] images = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Image>(true))
                    .ToArray();

                Assert.That(images, Is.Not.Empty);
                Assert.That(images.All(image => image.type == Image.Type.Sliced), Is.True,
                    "Every mockup Image must remain sliced, including inactive and generated UI.");
            }
            finally
            {
                if (openedForTest)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void WordListHubMockupScene_detailUsesHeaderRecordAndWordColumns()
        {
            Scene scene = SceneManager.GetSceneByPath(MockupScenePath);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
                scene = EditorSceneManager.OpenScene(MockupScenePath, OpenSceneMode.Additive);

            try
            {
                Transform detail = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .FirstOrDefault(transform => transform.name == "LibraryDetailPane");
                Assert.That(detail, Is.Not.Null, "The library detail pane must exist in the saved mockup scene.");

                RectTransform header = RequireRect(detail, "DetailHeaderPanel");
                RectTransform record = RequireRect(detail, "DetailRecordPanel");
                RectTransform words = RequireRect(detail, "DetailWordsPanel");
                RectTransform status = RequireRect(detail, "DetailStatus");

                AssertRect(header, new Vector2(0f, 160f), new Vector2(680f, 124f));
                AssertRect(record, new Vector2(-170f, -38f), new Vector2(320f, 220f));
                AssertRect(words, new Vector2(170f, -38f), new Vector2(320f, 220f));
                AssertRect(status, new Vector2(0f, -159f), new Vector2(650f, 18f));

                Rect headerRect = GetAnchoredRect(header);
                Rect recordRect = GetAnchoredRect(record);
                Rect wordsRect = GetAnchoredRect(words);
                Rect statusRect = GetAnchoredRect(status);
                Assert.That(headerRect.Overlaps(recordRect), Is.False);
                Assert.That(headerRect.Overlaps(wordsRect), Is.False);
                Assert.That(recordRect.Overlaps(wordsRect), Is.False);
                Assert.That(statusRect.Overlaps(recordRect), Is.False);
                Assert.That(statusRect.Overlaps(wordsRect), Is.False);

                foreach (string path in new[]
                {
                    "DetailHeaderPanel/DetailTitle",
                    "DetailHeaderPanel/DetailDescription",
                    "DetailHeaderPanel/DetailMetadata",
                    "DetailRecordPanel/RecordStars",
                    "DetailRecordPanel/RecordTime",
                    "DetailWordsPanel/DetailWordsScroll/Viewport/Content/DetailWords",
                    "DetailStatus"
                })
                {
                    Transform textTransform = detail.Find(path);
                    Assert.That(textTransform, Is.Not.Null, path + " must exist.");
                    TMPro.TextMeshProUGUI text = textTransform.GetComponent<TMPro.TextMeshProUGUI>();
                    Assert.That(text, Is.Not.Null, path + " must contain TMP text.");
                    Assert.That(text.text, Is.Not.Empty, path + " must contain preview text.");
                }
            }
            finally
            {
                if (openedForTest)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void WordListHubMockupScene_usesRequestedControlHeightsAndDropdownScroll()
        {
            Scene scene = SceneManager.GetSceneByPath(MockupScenePath);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
                scene = EditorSceneManager.OpenScene(MockupScenePath, OpenSceneMode.Additive);

            try
            {
                Transform navigation = RequireNamedTransform(scene, "GlobalNavigation");
                Assert.That(navigation.GetComponent<RectTransform>().sizeDelta.y, Is.EqualTo(75f));
                foreach (string name in new[]
                {
                    "Nav_Challenges",
                    "Nav_WordLibrary",
                    "Nav_Community",
                    "Nav_Achievements",
                    "ResetMockData"
                })
                {
                    Assert.That(RequireRect(navigation, name).sizeDelta.y, Is.EqualTo(63f), name);
                }

                Transform page = RequireNamedTransform(scene, "PageContent");
                string[] orderedControls =
                {
                    "CreateList",
                    "View_All",
                    "View_Favorites",
                    "LibrarySearch",
                    "Filter_Language",
                    "Filter_Played",
                    "Filter_Sort",
                    "Filter_Direction"
                };
                float previousX = float.NegativeInfinity;
                foreach (string name in orderedControls)
                {
                    float currentX = RequireRect(page, name).anchoredPosition.x;
                    Assert.That(currentX, Is.GreaterThan(previousX), name + " must preserve its left-to-right control order.");
                    previousX = currentX;
                }
                Assert.That(RequireRect(page, "CreateList").anchoredPosition.x, Is.EqualTo(-645f));

                foreach (string name in new[]
                {
                    "Filter_Language",
                    "Filter_Played",
                    "Filter_Sort",
                    "Filter_Direction"
                })
                {
                    Transform filter = page.Find(name);
                    TMPro.TMP_Dropdown dropdown = filter.GetComponent<TMPro.TMP_Dropdown>();
                    Assert.That(dropdown, Is.Not.Null, name);

                    Transform template = filter.Find("Template");
                    RectTransform templateRect = template.GetComponent<RectTransform>();
                    float expectedHeight = Mathf.Min(
                        720f,
                        (dropdown.options.Count * 20f + 4f) * 3f);
                    Assert.That(templateRect.sizeDelta.y, Is.EqualTo(expectedHeight), name + " popup height");

                    ScrollRect scroll = template.GetComponent<ScrollRect>();
                    Assert.That(scroll, Is.Not.Null, name + " must scroll");
                    Assert.That(scroll.vertical, Is.True, name);
                    Assert.That(scroll.horizontal, Is.False, name);

                    Transform item = template.Find("Viewport/Content/Item");
                    Assert.That(item.GetComponent<RectTransform>().sizeDelta.y, Is.EqualTo(40f));
                    LayoutElement itemLayout = item.GetComponent<LayoutElement>();
                    Assert.That(itemLayout.minHeight, Is.EqualTo(40f));
                    Assert.That(itemLayout.preferredHeight, Is.EqualTo(40f));
                    Assert.That(item.Find("Item Label").GetComponent<TMPro.TextMeshProUGUI>().fontSize, Is.EqualTo(20f));
                }
            }
            finally
            {
                if (openedForTest)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void LibraryGroups_areMutuallyExclusive_andCoverEveryFixtureOnce()
        {
            IReadOnlyList<MockupWordList> all = store.QueryLibrary(
                new MockupLibraryQuery { View = MockupLibraryView.All });

            Assert.That(all, Has.Count.EqualTo(12));
            Assert.That(all.Select(item => item.Id).Distinct().Count(), Is.EqualTo(all.Count));
            Assert.That(all.All(item => item.Words != null && item.WordCount == item.Words.Count), Is.True);
            Assert.That(store.DiscoveryItems.All(item => item.Words != null && item.WordCount == item.Words.Count), Is.True);
            Assert.That(store.GetGroupCount(MockupLibraryGroup.MyLists), Is.EqualTo(6));
            Assert.That(store.GetGroupCount(MockupLibraryGroup.CommunityDownloads), Is.EqualTo(3));
            Assert.That(store.GetGroupCount(MockupLibraryGroup.MyPublished), Is.EqualTo(3));
            Assert.That(store.GetGroupCount(MockupLibraryGroup.MyLists)
                + store.GetGroupCount(MockupLibraryGroup.CommunityDownloads)
                + store.GetGroupCount(MockupLibraryGroup.MyPublished), Is.EqualTo(all.Count));

            AssertViewContainsOnly(MockupLibraryView.MyLists, MockupLibraryGroup.MyLists);
            AssertViewContainsOnly(MockupLibraryView.CommunityDownloads, MockupLibraryGroup.CommunityDownloads);
            AssertViewContainsOnly(MockupLibraryView.MyPublished, MockupLibraryGroup.MyPublished);

            var groupedIds = new HashSet<string>();
            foreach (MockupLibraryView view in new[]
                     {
                         MockupLibraryView.MyLists,
                         MockupLibraryView.CommunityDownloads,
                         MockupLibraryView.MyPublished
                     })
            {
                foreach (MockupWordList item in store.QueryLibrary(new MockupLibraryQuery { View = view }))
                    Assert.That(groupedIds.Add(item.Id), Is.True, "An item appeared in more than one library group.");
            }

            Assert.That(groupedIds.SetEquals(all.Select(item => item.Id)), Is.True);
        }

        [Test]
        public void FavoritesView_crossesGroups_andTogglingDoesNotMoveAnItem()
        {
            IReadOnlyList<MockupWordList> favorites = store.QueryLibrary(
                new MockupLibraryQuery { View = MockupLibraryView.Favorites });

            Assert.That(favorites, Has.Count.EqualTo(6));
            Assert.That(favorites.Select(item => item.Group).Distinct().Count(), Is.EqualTo(3));

            MockupWordList unstarred = store.FindById("mock-list-shop-signs");
            MockupLibraryGroup originalGroup = unstarred.Group;
            Assert.That(store.ToggleFavorite(unstarred.Id), Is.True);
            Assert.That(store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.Favorites })
                .Any(item => item.Id == unstarred.Id), Is.True);
            Assert.That(unstarred.Group, Is.EqualTo(originalGroup));

            Assert.That(store.ToggleFavorite(unstarred.Id), Is.False);
            Assert.That(store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.Favorites })
                .Any(item => item.Id == unstarred.Id), Is.False);
        }

        [Test]
        public void SearchLanguageAndPlayedFilters_compose()
        {
            IReadOnlyList<MockupWordList> results = store.QueryLibrary(new MockupLibraryQuery
            {
                SearchText = "HSK",
                LanguageFilter = MockupLanguageFilter.Chinese,
                PlayedFilter = MockupPlayedFilter.Unplayed
            });

            Assert.That(results.Select(item => item.Id), Is.EquivalentTo(new[] { "mock-list-mini-hsk" }));
            Assert.That(results.All(item => item.Language == MockupLanguage.Chinese), Is.True);
            Assert.That(results.All(item => !item.HasBeenPlayed), Is.True);
        }

        [Test]
        public void Fixtures_coverLongMixedTextEmptyListsAndLockedAchievements()
        {
            MockupWordList longMixed = store.FindById("mock-list-long-mixed");
            MockupWordList empty = store.FindById("mock-list-empty");

            Assert.That(longMixed.Language, Is.EqualTo(MockupLanguage.Mixed));
            Assert.That(longMixed.WordCount, Is.EqualTo(72));
            Assert.That(longMixed.Words, Has.Count.EqualTo(72));
            Assert.That(longMixed.Title, Does.Contain("城市通勤"));
            Assert.That(longMixed.Description, Does.Contain("换乘"));
            Assert.That(longMixed.Words.Any(word => word.Contains(" — ") && word.Contains("苹果")), Is.True);
            Assert.That(longMixed.PersonalStars, Is.EqualTo(5));
            Assert.That(longMixed.AddedAt, Is.Not.EqualTo(longMixed.ModifiedAt));
            Assert.That(empty.WordCount, Is.Zero);
            Assert.That(empty.HasBeenPlayed, Is.False);
            Assert.That(empty.PersonalStars, Is.Null);
            Assert.That(empty.Words, Is.Empty);
            Assert.That(store.Achievements.Any(item => item.Status == MockupAchievementStatus.Locked), Is.True);
        }

        [TestCase(MockupSortMode.PersonalStars, MockupSortDirection.Ascending)]
        [TestCase(MockupSortMode.PersonalStars, MockupSortDirection.Descending)]
        [TestCase(MockupSortMode.RecentlyPlayed, MockupSortDirection.Ascending)]
        [TestCase(MockupSortMode.RecentlyPlayed, MockupSortDirection.Descending)]
        public void ScoreAndPlaySorts_keepMissingValuesAtTheEndInBothDirections(
            MockupSortMode sortMode,
            MockupSortDirection direction)
        {
            IReadOnlyList<MockupWordList> results = store.QueryLibrary(new MockupLibraryQuery
            {
                SortMode = sortMode,
                SortDirection = direction
            });

            bool hasValue(MockupWordList item) => sortMode == MockupSortMode.PersonalStars
                ? item.PersonalStars.HasValue
                : item.LastPlayedAt.HasValue;

            int firstMissing = results.ToList().FindIndex(item => !hasValue(item));
            Assert.That(firstMissing, Is.GreaterThanOrEqualTo(0), "Fixtures should include a missing value.");
            Assert.That(results.Skip(firstMissing).All(item => !hasValue(item)), Is.True,
                "Items without a score or play date must form the trailing block.");
            Assert.That(results.Take(firstMissing).All(hasValue), Is.True);

            List<MockupWordList> valued = results.Take(firstMissing).ToList();
            if (sortMode == MockupSortMode.PersonalStars)
            {
                IEnumerable<int> scores = valued.Select(item => item.PersonalStars.Value);
                CollectionAssert.AreEqual(
                    direction == MockupSortDirection.Ascending
                        ? scores.OrderBy(value => value).ToArray()
                        : scores.OrderByDescending(value => value).ToArray(),
                    scores.ToArray());
            }
            else
            {
                IEnumerable<System.DateTime> playDates = valued.Select(item => item.LastPlayedAt.Value);
                CollectionAssert.AreEqual(
                    direction == MockupSortDirection.Ascending
                        ? playDates.OrderBy(value => value).ToArray()
                        : playDates.OrderByDescending(value => value).ToArray(),
                    playDates.ToArray());
            }
        }

        [Test]
        public void Publishing_movesTheSameList_withoutCreatingDuplicates()
        {
            MockupWordList item = store.FindById("mock-list-travel-copy");
            string originalId = item.Id;
            int initialLocalCount = store.QueryLibrary(
                new MockupLibraryQuery { View = MockupLibraryView.All }).Count;
            int initialMyListsCount = store.GetGroupCount(MockupLibraryGroup.MyLists);
            int initialMyPublishedCount = store.GetGroupCount(MockupLibraryGroup.MyPublished);

            MockupWordList published = store.Publish(item.Id);
            string remoteId = published.RemoteItemId;

            Assert.That(published, Is.SameAs(item));
            Assert.That(published.Id, Is.EqualTo(originalId));
            Assert.That(published.Group, Is.EqualTo(MockupLibraryGroup.MyPublished));
            Assert.That(published.IsPubliclyVisible, Is.True);
            Assert.That(store.GetGroupCount(MockupLibraryGroup.MyLists), Is.EqualTo(initialMyListsCount - 1));
            Assert.That(store.GetGroupCount(MockupLibraryGroup.MyPublished), Is.EqualTo(initialMyPublishedCount + 1));
            Assert.That(store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.All }),
                Has.Count.EqualTo(initialLocalCount));

            MockupWordList publishedAgain = store.Publish(item.Id);
            Assert.That(publishedAgain, Is.SameAs(item));
            Assert.That(publishedAgain.Id, Is.EqualTo(originalId));
            Assert.That(publishedAgain.RemoteItemId, Is.EqualTo(remoteId));
            Assert.That(store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.All })
                .Count(entry => entry.Id == originalId), Is.EqualTo(1));
            Assert.That(store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.All }),
                Has.Count.EqualTo(initialLocalCount));
        }

        [Test]
        public void DownloadingSameRemoteItemTwice_isIdempotent()
        {
            int initialCount = store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.All }).Count;
            MockupWordList first = store.Download("mock-ugc-weekend-garden");
            MockupWordList second = store.Download("mock-ugc-weekend-garden");

            Assert.That(second, Is.SameAs(first));
            Assert.That(store.FindDiscoveryItem("mock-ugc-weekend-garden").IsDownloaded, Is.True);
            Assert.That(store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.All }),
                Has.Count.EqualTo(initialCount + 1));
            Assert.That(store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.All })
                .Count(item => item.RemoteItemId == "mock-ugc-weekend-garden"), Is.EqualTo(1));
            Assert.That(first.Group, Is.EqualTo(MockupLibraryGroup.CommunityDownloads));
            Assert.That(first.Ownership, Is.EqualTo(MockupOwnership.CommunityDownloaded));
            Assert.That(first.IsReadOnly, Is.True);
            Assert.That(first.RemoteAuthor, Is.EqualTo("GreenType"));
            Assert.That(first.RemoteItemId, Is.EqualTo("mock-ugc-weekend-garden"));
            Assert.That(first.Words, Has.Count.EqualTo(first.WordCount));
            CollectionAssert.AreEqual(store.FindDiscoveryItem(first.RemoteItemId).Words, first.Words);
            Assert.That(store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.CommunityDownloads })
                .Any(item => item.Id == first.Id), Is.True);
            Assert.That(store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.MyLists })
                .Any(item => item.Id == first.Id), Is.False);
        }

        [Test]
        public void DownloadingAnExistingCommunityItem_returnsItsExistingLibraryEntry()
        {
            MockupWordList existing = store.FindById("mock-list-cafe-abroad");
            int initialCount = store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.All }).Count;

            MockupWordList downloaded = store.Download("mock-ugc-cafe-abroad");

            Assert.That(downloaded, Is.SameAs(existing));
            Assert.That(downloaded.Group, Is.EqualTo(MockupLibraryGroup.CommunityDownloads));
            Assert.That(store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.All }),
                Has.Count.EqualTo(initialCount));
            Assert.That(store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.MyLists })
                .Any(item => item.Id == existing.Id), Is.False);
            Assert.That(store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.CommunityDownloads })
                .Any(item => item.Id == existing.Id), Is.True);
            Assert.That(store.FindDiscoveryItem("mock-ugc-cafe-abroad").IsDownloaded, Is.True);
        }

        [Test]
        public void PlayDiscoveryItem_downloadsOnceAndAddsCommunitySourceToCommunityDownloads()
        {
            int initialCount = store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.All }).Count;

            MockupPlayResult first = store.PlayDiscoveryItem("mock-ugc-weekend-garden");
            MockupPlayResult second = store.PlayDiscoveryItem("mock-ugc-weekend-garden");

            Assert.That(first.Succeeded, Is.True);
            Assert.That(first.DownloadedNow, Is.True);
            Assert.That(first.List, Is.Not.Null);
            Assert.That(first.List.IsReadOnly, Is.True);
            Assert.That(first.List.Group, Is.EqualTo(MockupLibraryGroup.CommunityDownloads));
            Assert.That(first.List.RemoteAuthor, Is.EqualTo("GreenType"));
            Assert.That(first.List.HasOfflineCache, Is.True);
            Assert.That(first.Message, Does.Contain("mock play"));
            Assert.That(second.List, Is.SameAs(first.List));
            Assert.That(second.DownloadedNow, Is.False);
            Assert.That(store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.All }),
                Has.Count.EqualTo(initialCount + 1));
            Assert.That(store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.CommunityDownloads })
                .Any(item => item.Id == first.List.Id), Is.True);
            Assert.That(store.QueryLibrary(new MockupLibraryQuery { View = MockupLibraryView.MyLists })
                .Any(item => item.Id == first.List.Id), Is.False);
        }

        [Test]
        public void CompleteRun_updatesMockScoreDatesAndAchievementProgress()
        {
            MockupWordList item = store.FindById("mock-list-empty");

            Assert.That(store.CompleteRun(item.Id, 4, 42f), Is.True);

            Assert.That(item.PlayCount, Is.EqualTo(1));
            Assert.That(item.LastPlayedAt, Is.Not.Null);
            Assert.That(item.PersonalStars, Is.EqualTo(4));
            Assert.That(item.BestTimeSeconds, Is.EqualTo(42f));
            MockupAchievement twoWords = store.Achievements.Single(achievement => achievement.Id == "COMPLETE_TWO_WORDS");
            Assert.That(twoWords.Status, Is.EqualTo(MockupAchievementStatus.Unlocked));
            Assert.That(twoWords.ProgressText, Is.EqualTo("2 / 2"));
            Assert.That(store.Achievements.Any(achievement => achievement.Status == MockupAchievementStatus.Locked), Is.True);
        }

        [Test]
        public void CreateAndEditWords_trimsSkipsBlankLinesAndKeepsTextMeaning()
        {
            MockupWordList item = store.CreateList("Typed words", MockupLanguage.Mixed,
                new[] { " apple ", "", "   ", "早上 Morning!", "Keep.Case?" });

            CollectionAssert.AreEqual(new[] { "apple", "早上 Morning!", "Keep.Case?" }, item.Words);
            Assert.That(item.WordCount, Is.EqualTo(item.Words.Count));

            MockupWordListEdit draft = store.CreateEditDraft(item.Id);
            draft.Words = new List<string> { " apple ", "", "NEW-word?" };
            MockupEditResult result = store.SaveEdit(item.Id, draft);

            Assert.That(result.ContentChanged, Is.True);
            CollectionAssert.AreEqual(new[] { "apple", "NEW-word?" }, item.Words);
            Assert.That(item.WordCount, Is.EqualTo(item.Words.Count));
        }

        [Test]
        public void CreateListWithWords_requiresAtLeastOneNonBlankWord()
        {
            Assert.Throws<System.ArgumentException>(() => store.CreateList(
                "Blank input", MockupLanguage.English, new[] { "", "  " }));
        }

        [Test]
        public void WordSequenceComparison_isOrdinalAndClearsThePreviousRecord()
        {
            MockupWordList item = store.CreateList("Case check", MockupLanguage.English, new[] { "Case?" });
            store.CompleteRun(item.Id, 4, 18f);
            MockupWordListEdit draft = store.CreateEditDraft(item.Id);
            draft.Words = new List<string> { "case?" };

            MockupEditResult result = store.SaveEdit(item.Id, draft);

            Assert.That(result.ContentChanged, Is.True);
            Assert.That(item.Words, Is.EqualTo(new[] { "case?" }));
            Assert.That(item.WordCount, Is.EqualTo(item.Words.Count));
            Assert.That(item.PersonalStars, Is.Null);
            Assert.That(item.PreviousContentRecords, Has.Count.EqualTo(1));
        }

        [Test]
        public void OfflinePlayRequiresCache_butCachedCommunityListsRemainPlayable()
        {
            store.SetOnline(false);

            MockupPlayResult uncached = store.PlayDiscoveryItem("mock-ugc-weekend-garden");
            MockupPlayResult cached = store.PlayDiscoveryItem("mock-ugc-night-market");
            MockupActionResult download = store.TryDownload("mock-ugc-weekend-garden");

            Assert.That(uncached.Succeeded, Is.False);
            Assert.That(uncached.Message, Does.Contain("not cached"));
            Assert.That(cached.Succeeded, Is.True);
            Assert.That(cached.List.HasOfflineCache, Is.True);
            Assert.That(download.Succeeded, Is.False);
            Assert.That(download.Message, Does.Contain("online"));
        }

        [Test]
        public void PlayLibraryItem_playsOwnedListsAndCommunityCopiesOffline()
        {
            MockupWordList local = store.FindById("mock-list-everyday-english");
            MockupWordList community = store.FindById("mock-list-night-market");
            MockupWordList copy = store.EditCommunityCopy(community.Id);
            store.SetOnline(false);

            MockupPlayResult localPlay = store.PlayLibraryItem(local.Id);
            MockupPlayResult communityPlay = store.PlayLibraryItem(community.Id);
            MockupPlayResult copyPlay = store.PlayLibraryItem(copy.Id);

            Assert.That(localPlay.Succeeded, Is.True);
            Assert.That(communityPlay.Succeeded, Is.True);
            Assert.That(copyPlay.Succeeded, Is.True);
            Assert.That(communityPlay.List.IsReadOnly, Is.True);
            Assert.That(copyPlay.List.IsReadOnly, Is.False);
        }

        [Test]
        public void PlayLibraryItem_keepsCachedDownloadPlayableAfterReportHideAndRemoval()
        {
            MockupActionResult download = store.TryDownload("mock-ugc-weekend-garden");
            MockupWordList cached = download.List;
            Assert.That(download.Succeeded, Is.True);

            Assert.That(store.TryReportAndHide(cached.RemoteItemId).Succeeded, Is.True);
            Assert.That(store.QueryDiscovery().Any(item => item.RemoteItemId == cached.RemoteItemId), Is.False);
            Assert.That(store.TryResolveReport(cached.RemoteItemId, true).Succeeded, Is.True);
            Assert.That(store.FindDiscoveryItem(cached.RemoteItemId).IsRemovedFromCommunity, Is.True);

            store.SetOnline(false);
            MockupPlayResult result = store.PlayLibraryItem(cached.Id);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.List, Is.SameAs(cached));
            Assert.That(result.List.HasOfflineCache, Is.True);
            Assert.That(result.List.RemoteItemId, Is.EqualTo(cached.RemoteItemId));
        }

        [Test]
        public void OfflineLikeAndReportFailClearly_withoutChangingCommunityState()
        {
            MockupDiscoveryItem item = store.FindDiscoveryItem("mock-ugc-weekend-garden");
            int originalLikes = item.LikeCount;
            store.SetOnline(false);

            MockupActionResult like = store.TryToggleLike(item.RemoteItemId);
            MockupActionResult report = store.TryReportAndHide(item.RemoteItemId);
            MockupActionResult review = store.TryResolveReport(item.RemoteItemId, false);

            Assert.That(like.Succeeded, Is.False);
            Assert.That(like.Message, Does.Contain("online"));
            Assert.That(report.Succeeded, Is.False);
            Assert.That(report.Message, Does.Contain("online"));
            Assert.That(review.Succeeded, Is.False);
            Assert.That(review.Message, Does.Contain("online"));
            Assert.That(item.IsLiked, Is.False);
            Assert.That(item.LikeCount, Is.EqualTo(originalLikes));
            Assert.That(item.IsHiddenByCurrentUser, Is.False);
        }

        [Test]
        public void LikeAndReportAreIdempotent_andReportHidesOnlyForCurrentUser()
        {
            MockupDiscoveryItem item = store.FindDiscoveryItem("mock-ugc-weekend-garden");
            int originalLikes = item.LikeCount;

            Assert.That(store.SetLiked(item.RemoteItemId, true), Is.True);
            Assert.That(store.SetLiked(item.RemoteItemId, true), Is.True);
            Assert.That(item.LikeCount, Is.EqualTo(originalLikes + 1));
            Assert.That(store.ToggleLike(item.RemoteItemId), Is.False);
            Assert.That(item.LikeCount, Is.EqualTo(originalLikes));

            Assert.That(store.TryReportAndHide(item.RemoteItemId).Succeeded, Is.True);
            Assert.That(store.TryReportAndHide(item.RemoteItemId).Succeeded, Is.True);
            Assert.That(item.IsHiddenByCurrentUser, Is.True);
            Assert.That(item.IsRemovedFromCommunity, Is.False);
            Assert.That(item.ReportStatus, Is.EqualTo(MockupReportStatus.PendingReview));
            Assert.That(item.MockReportCount, Is.EqualTo(1));
            Assert.That(store.QueryDiscovery().Any(candidate => candidate.RemoteItemId == item.RemoteItemId), Is.False);
            Assert.That(store.QueryDiscovery().Any(candidate => candidate.RemoteItemId == "mock-ugc-airport-mix"), Is.True);

            MockupActionResult cleared = store.ResolveReport(item.RemoteItemId, false);
            Assert.That(cleared.Succeeded, Is.True);
            Assert.That(item.ReportStatus, Is.EqualTo(MockupReportStatus.Cleared));
            Assert.That(item.IsRemovedFromCommunity, Is.False);
            Assert.That(item.IsHiddenByCurrentUser, Is.True);
            Assert.That(store.UnhideForCurrentUser(item.RemoteItemId).Succeeded, Is.True);
            Assert.That(store.QueryDiscovery().Any(candidate => candidate.RemoteItemId == item.RemoteItemId), Is.True);

            MockupDiscoveryItem removed = store.FindDiscoveryItem("mock-ugc-airport-mix");
            Assert.That(store.TryReportAndHide(removed.RemoteItemId).Succeeded, Is.True);
            Assert.That(store.ResolveReport(removed.RemoteItemId, true).Succeeded, Is.True);
            Assert.That(removed.ReportStatus, Is.EqualTo(MockupReportStatus.RemovedFromCommunity));
            Assert.That(store.QueryDiscovery().Any(candidate => candidate.RemoteItemId == removed.RemoteItemId), Is.False);
            Assert.That(store.UnhideForCurrentUser(removed.RemoteItemId).Succeeded, Is.False);
        }

        [Test]
        public void UpdatingDownloadedList_metadataOnlyPreservesWordsAndRecords()
        {
            MockupWordList item = store.FindById("mock-list-cafe-abroad");
            List<string> oldWords = new List<string>(item.Words);
            int oldRevision = item.ContentRevision;
            int oldStars = item.PersonalStars.Value;
            float oldTime = item.BestTimeSeconds.Value;

            store.SetOnline(false);
            MockupActionResult offline = store.TryUpdateDownloadedItem(item.Id);
            Assert.That(offline.Succeeded, Is.False);
            CollectionAssert.AreEqual(oldWords, item.Words);
            Assert.That(item.ContentRevision, Is.EqualTo(oldRevision));
            Assert.That(item.PersonalStars, Is.EqualTo(oldStars));

            store.SetOnline(true);
            MockupActionResult update = store.TryUpdateDownloadedItem(item.Id);
            Assert.That(update.Succeeded, Is.True);
            Assert.That(item.WordCount, Is.EqualTo(16));
            CollectionAssert.AreEqual(oldWords, item.Words);
            Assert.That(item.ContentRevision, Is.EqualTo(oldRevision));
            Assert.That(item.PersonalStars, Is.EqualTo(oldStars));
            Assert.That(item.BestTimeSeconds, Is.EqualTo(oldTime));
            Assert.That(item.PreviousContentRecords, Is.Empty);
            Assert.That(item.DownloadStatus, Is.EqualTo(MockupDownloadStatus.None));
            Assert.That(item.HasOfflineCache, Is.True);
        }

        [Test]
        public void DeleteCommunityList_removesOnlyLocalCopyAndAllowsRedownload()
        {
            MockupWordList downloaded = store.FindById("mock-list-cafe-abroad");
            MockupDiscoveryItem remote = store.FindDiscoveryItem(downloaded.RemoteItemId);
            Assert.That(remote.IsDownloaded, Is.True);

            MockupActionResult removed = store.TryDeleteList(downloaded.Id);

            Assert.That(removed.Succeeded, Is.True);
            Assert.That(store.FindById(downloaded.Id), Is.Null);
            Assert.That(remote.IsDownloaded, Is.False);
            Assert.That(remote.RemoteItemId, Is.EqualTo(downloaded.RemoteItemId));

            MockupActionResult redownloaded = store.TryDownload(remote.RemoteItemId);
            Assert.That(redownloaded.Succeeded, Is.True);
            Assert.That(redownloaded.List.Id, Is.Not.EqualTo(downloaded.Id));
            Assert.That(remote.IsDownloaded, Is.True);
        }

        [Test]
        public void DeletePublishedList_requiresExplicitUnpublishAndNeverDeletesRemoteItem()
        {
            MockupWordList item = store.FindById("mock-list-punctuation");
            string remoteId = item.RemoteItemId;

            MockupActionResult blocked = store.TryDeleteList(item.Id);

            Assert.That(blocked.Succeeded, Is.False);
            Assert.That(store.FindById(item.Id), Is.SameAs(item));
            Assert.That(item.IsPubliclyVisible, Is.True);
            Assert.That(item.RemoteItemId, Is.EqualTo(remoteId));

            store.Unpublish(item.Id);
            MockupActionResult removed = store.TryDeleteList(item.Id);

            Assert.That(removed.Succeeded, Is.True);
            Assert.That(store.FindById(item.Id), Is.Null);
            Assert.That(item.IsPubliclyVisible, Is.False);
            Assert.That(item.RemoteItemId, Is.EqualTo(remoteId));
        }

        [Test]
        public void EditingCommunityCopy_createsOwnedUnattributedEntry_andKeepsOriginalReadOnly()
        {
            MockupWordList source = store.FindById("mock-list-night-market");
            MockupWordList copy = store.EditCommunityCopy(source.Id);

            Assert.That(copy.Id, Is.Not.EqualTo(source.Id));
            Assert.That(copy.Ownership, Is.EqualTo(MockupOwnership.Owned));
            Assert.That(copy.Group, Is.EqualTo(MockupLibraryGroup.MyLists));
            Assert.That(copy.IsReadOnly, Is.False);
            Assert.That(copy.RemoteItemId, Is.Null);
            Assert.That(copy.RemoteAuthor, Is.Null);
            Assert.That(copy.Words, Is.EqualTo(source.Words));
            Assert.That(copy.WordCount, Is.EqualTo(copy.Words.Count));
            Assert.That(copy.DownloadStatus, Is.EqualTo(MockupDownloadStatus.None));
            Assert.That(copy.PublicationStatus, Is.EqualTo(MockupPublicationStatus.NotPublished));

            Assert.That(source.IsReadOnly, Is.True);
            Assert.That(source.Group, Is.EqualTo(MockupLibraryGroup.CommunityDownloads));
            Assert.That(source.RemoteAuthor, Is.EqualTo("MapleWords"));
        }

        [Test]
        public void DiscardingAnEditDraft_leavesTheLibraryItemUnchanged()
        {
            MockupWordList item = store.FindById("mock-list-travel-copy");
            string oldTitle = item.Title;
            string oldDescription = item.Description;
            List<string> oldWords = new List<string>(item.Words);
            int oldWordCount = item.WordCount;
            int oldRevision = item.ContentRevision;
            System.DateTime oldModifiedAt = item.ModifiedAt;
            System.DateTime oldContentModifiedAt = item.ContentModifiedAt;

            MockupWordListEdit discardedDraft = store.CreateEditDraft(item.Id);
            discardedDraft.Title = "Unsaved title";
            discardedDraft.Description = "Unsaved description";
            discardedDraft.Words[0] = "Unsaved word";
            discardedDraft.WordCount++;
            discardedDraft.GameplayContentChanged = true;
            // Cancel is represented by discarding the draft: the store changes only on SaveEdit.

            Assert.That(item.Title, Is.EqualTo(oldTitle));
            Assert.That(item.Description, Is.EqualTo(oldDescription));
            Assert.That(item.Words, Is.EqualTo(oldWords));
            Assert.That(item.WordCount, Is.EqualTo(oldWordCount));
            Assert.That(item.ContentRevision, Is.EqualTo(oldRevision));
            Assert.That(item.ModifiedAt, Is.EqualTo(oldModifiedAt));
            Assert.That(item.ContentModifiedAt, Is.EqualTo(oldContentModifiedAt));
        }

        [Test]
        public void MetadataOnlyEdit_doesNotWarnOrChangePersonalRecords()
        {
            MockupWordList item = store.FindById("mock-list-punctuation");
            List<string> oldWords = new List<string>(item.Words);
            int? oldStars = item.PersonalStars;
            float? oldBestTime = item.BestTimeSeconds;
            int oldRevision = item.ContentRevision;
            System.DateTime oldContentModifiedAt = item.ContentModifiedAt;
            System.DateTime oldModifiedAt = item.ModifiedAt;

            MockupWordListEdit draft = store.CreateEditDraft(item.Id);
            draft.Title = "Punctuation Practice Plus";
            draft.Description = "Updated explanatory text.";
            draft.Tags = "punctuation,english,practice";
            MockupEditResult result = store.SaveEdit(item.Id, draft);

            Assert.That(result.ContentChanged, Is.False);
            Assert.That(result.ShowRecordWarning, Is.False);
            Assert.That(item.Title, Is.EqualTo("Punctuation Practice Plus"));
            Assert.That(item.Tags, Is.EqualTo("punctuation,english,practice"));
            Assert.That(item.Words, Is.EqualTo(oldWords));
            Assert.That(item.WordCount, Is.EqualTo(item.Words.Count));
            Assert.That(item.PersonalStars, Is.EqualTo(oldStars));
            Assert.That(item.BestTimeSeconds, Is.EqualTo(oldBestTime));
            Assert.That(item.ContentRevision, Is.EqualTo(oldRevision));
            Assert.That(item.ContentModifiedAt, Is.EqualTo(oldContentModifiedAt));
            Assert.That(item.ModifiedAt, Is.Not.EqualTo(oldModifiedAt));
            Assert.That(item.PublicationStatus, Is.EqualTo(MockupPublicationStatus.UpdateNeeded));
            Assert.That(item.Group, Is.EqualTo(MockupLibraryGroup.MyPublished));
            Assert.That(item.PreviousContentRecords, Is.Empty);
        }

        [Test]
        public void PublishedContentEdit_createsANewListAndLeavesOldPublishedVersionIntact()
        {
            MockupWordList source = store.FindById("mock-list-punctuation");
            string oldRemoteId = source.RemoteItemId;
            int oldWordCount = source.WordCount;
            int? oldStars = source.PersonalStars;
            float? oldTime = source.BestTimeSeconds;

            MockupWordListEdit draft = store.CreateEditDraft(source.Id);
            draft.Words.Add("new punctuation word");
            MockupEditResult result = store.SavePublishedContentAsNewList(source.Id, draft);

            Assert.That(result.ContentChanged, Is.True);
            Assert.That(result.ShowRecordWarning, Is.True);
            Assert.That(result.CreatedNewList, Is.True);
            Assert.That(result.Item.Id, Is.Not.EqualTo(source.Id));
            Assert.That(result.Item.RemoteItemId, Is.Null);
            Assert.That(result.Item.PublicationStatus, Is.EqualTo(MockupPublicationStatus.NotPublished));
            Assert.That(result.Item.ContentRevision, Is.EqualTo(1));
            Assert.That(result.Item.Words, Is.EqualTo(draft.Words));
            Assert.That(result.Item.WordCount, Is.EqualTo(result.Item.Words.Count));
            Assert.That(result.Item.PersonalStars, Is.Null);
            Assert.That(result.Item.BestTimeSeconds, Is.Null);
            Assert.That(result.Item.Group, Is.EqualTo(MockupLibraryGroup.MyLists));

            MockupWordList newPublished = store.Publish(result.Item.Id);
            Assert.That(newPublished.RemoteItemId, Is.Not.EqualTo(oldRemoteId));
            Assert.That(newPublished.IsPubliclyVisible, Is.True);

            Assert.That(source.WordCount, Is.EqualTo(oldWordCount));
            Assert.That(source.RemoteItemId, Is.EqualTo(oldRemoteId));
            Assert.That(source.PublicationStatus, Is.EqualTo(MockupPublicationStatus.Published));
            Assert.That(source.IsPubliclyVisible, Is.True);
            Assert.That(source.PersonalStars, Is.EqualTo(oldStars));
            Assert.That(source.BestTimeSeconds, Is.EqualTo(oldTime));
        }

        [Test]
        public void LegacyWordCountEdit_materializesWordsAndKeepsCountConsistent()
        {
            MockupWordList item = store.FindById("mock-list-everyday-english");
            MockupWordListEdit draft = store.CreateEditDraft(item.Id);
            draft.WordCount++;

            MockupEditResult result = store.SaveEdit(item.Id, draft);

            Assert.That(result.ContentChanged, Is.True);
            Assert.That(item.WordCount, Is.EqualTo(draft.WordCount));
            Assert.That(item.WordCount, Is.EqualTo(item.Words.Count));
            Assert.That(item.Words.All(word => !string.IsNullOrWhiteSpace(word)), Is.True);
        }

        [Test]
        public void SaveEdit_rejectsGameplayChangesToAlreadyPublishedContent()
        {
            MockupWordList source = store.FindById("mock-list-punctuation");
            int wordCount = source.WordCount;
            int revision = source.ContentRevision;
            MockupWordListEdit draft = store.CreateEditDraft(source.Id);
            draft.Words.Add("new published word");

            Assert.Throws<System.InvalidOperationException>(() => store.SaveEdit(source.Id, draft));
            Assert.That(source.WordCount, Is.EqualTo(wordCount));
            Assert.That(source.ContentRevision, Is.EqualTo(revision));
            Assert.That(source.PublicationStatus, Is.EqualTo(MockupPublicationStatus.Published));
        }

        [Test]
        public void Unpublish_requiresOnline_andKeepsTheOldPublishedEntry()
        {
            MockupWordList item = store.FindById("mock-list-travel-copy");
            MockupWordList published = store.Publish(item.Id);
            string remoteId = published.RemoteItemId;
            store.SetOnline(false);

            MockupActionResult offline = store.TryUnpublish(item.Id);
            Assert.That(offline.Succeeded, Is.False);
            Assert.That(item.IsPubliclyVisible, Is.True);

            store.SetOnline(true);
            MockupWordList unpublished = store.Unpublish(item.Id);
            Assert.That(unpublished, Is.SameAs(item));
            Assert.That(item.IsPubliclyVisible, Is.False);
            Assert.That(item.PublicationStatus, Is.EqualTo(MockupPublicationStatus.Unpublished));
            Assert.That(item.RemoteItemId, Is.EqualTo(remoteId));
            Assert.That(item.Group, Is.EqualTo(MockupLibraryGroup.MyPublished));

            List<string> wordsBeforeRepublish = new List<string>(item.Words);
            MockupActionResult republished = store.TryPublish(item.Id);
            Assert.That(republished.Succeeded, Is.True);
            Assert.That(republished.List, Is.SameAs(item));
            Assert.That(item.RemoteItemId, Is.EqualTo(remoteId));
            Assert.That(item.Words, Is.EqualTo(wordsBeforeRepublish));
            Assert.That(item.WordCount, Is.EqualTo(item.Words.Count));
            Assert.That(item.IsPubliclyVisible, Is.True);
            Assert.That(item.PublicationStatus, Is.EqualTo(MockupPublicationStatus.Published));
        }

        [Test]
        public void GameplayContentEdit_warnsAndArchivesPriorPersonalRecord()
        {
            MockupWordList item = store.FindById("mock-list-everyday-english");
            int oldStars = item.PersonalStars.Value;
            float oldBestTime = item.BestTimeSeconds.Value;
            int oldRevision = item.ContentRevision;

            MockupWordListEdit draft = store.CreateEditDraft(item.Id);
            draft.Words.Add("afterthought");
            MockupEditResult result = store.SaveEdit(item.Id, draft);

            Assert.That(result.ContentChanged, Is.True);
            Assert.That(result.ShowRecordWarning, Is.True);
            Assert.That(item.PersonalStars, Is.Null);
            Assert.That(item.BestTimeSeconds, Is.Null);
            Assert.That(item.ContentRevision, Is.EqualTo(oldRevision + 1));
            Assert.That(item.WordCount, Is.EqualTo(item.Words.Count));
            Assert.That(item.ContentModifiedAt, Is.EqualTo(item.ModifiedAt));
            Assert.That(item.PreviousContentRecords, Has.Count.EqualTo(1));
            Assert.That(item.PreviousContentRecords[0].ContentRevision, Is.EqualTo(oldRevision));
            Assert.That(item.PreviousContentRecords[0].PersonalStars, Is.EqualTo(oldStars));
            Assert.That(item.PreviousContentRecords[0].BestTimeSeconds, Is.EqualTo(oldBestTime));
            Assert.That(item.PublicationStatus, Is.EqualTo(MockupPublicationStatus.NotPublished));
        }

        private static RectTransform RequireRect(Transform root, string path)
        {
            Transform child = root.Find(path);
            Assert.That(child, Is.Not.Null, path + " must exist.");
            RectTransform rect = child.GetComponent<RectTransform>();
            Assert.That(rect, Is.Not.Null, path + " must have a RectTransform.");
            return rect;
        }

        private static Transform RequireNamedTransform(Scene scene, string name)
        {
            Transform transform = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(candidate => candidate.name == name);
            Assert.That(transform, Is.Not.Null, name + " must exist.");
            return transform;
        }

        private static void AssertRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            Assert.That(rect.anchoredPosition, Is.EqualTo(position));
            Assert.That(rect.sizeDelta, Is.EqualTo(size));
        }

        private static Rect GetAnchoredRect(RectTransform rect)
        {
            return new Rect(
                rect.anchoredPosition.x - rect.sizeDelta.x * rect.pivot.x,
                rect.anchoredPosition.y - rect.sizeDelta.y * rect.pivot.y,
                rect.sizeDelta.x,
                rect.sizeDelta.y);
        }

        private void AssertViewContainsOnly(MockupLibraryView view, MockupLibraryGroup expectedGroup)
        {
            IReadOnlyList<MockupWordList> items = store.QueryLibrary(new MockupLibraryQuery { View = view });
            Assert.That(items.All(item => item.Group == expectedGroup), Is.True);
            Assert.That(items, Has.Count.EqualTo(store.GetGroupCount(expectedGroup)));
        }
    }
}
