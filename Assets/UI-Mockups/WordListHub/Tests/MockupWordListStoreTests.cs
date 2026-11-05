using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
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
        public void CommunityPreviewFixtures_excludeLocalLibraryFixtures_andRetainDiscoveryData()
        {
            MockupWordListStore community = MockupWordListStore.CreateCommunityPreviewFixtures();

            Assert.That(community.QueryLibrary(new MockupLibraryQuery()), Is.Empty);
            Assert.That(community.FindById("mock-list-everyday-english"), Is.Null);
            Assert.That(community.QueryDiscoveryWithFilters(new MockupDiscoveryQuery()), Is.Not.Empty);

            community.ResetFixtures();
            Assert.That(community.QueryLibrary(new MockupLibraryQuery()), Is.Empty,
                "Resetting Community Preview must not reseed local Library fixtures.");
        }

        [Test]
        public void WordListHubMockupScene_hasSerializedBindingsAndInactiveSceneTemplates()
        {
            Scene scene = SceneManager.GetSceneByPath(MockupScenePath);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
                scene = EditorSceneManager.OpenScene(MockupScenePath, OpenSceneMode.Additive);

            try
            {
                WordListHubMockupController controller = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<WordListHubMockupController>(true))
                    .Single();
                WordListHubSceneBindings bindings = controller.GetComponent<WordListHubSceneBindings>();

                Assert.That(bindings, Is.Not.Null);
                Assert.That(bindings.card, Is.Not.Null);
                Assert.That(bindings.hubRoot, Is.Not.Null);
                Assert.That(bindings.navigationRoot, Is.Not.Null);
                Assert.That(bindings.footer, Is.Not.Null);
                Assert.That(bindings.pages, Has.Count.EqualTo(4));
                Assert.That(bindings.pages.Select(page => page.page).Distinct().Count(), Is.EqualTo(4));
                Assert.That(bindings.pages.Select(page => page.root).Distinct().Count(), Is.EqualTo(4));
                Assert.That(bindings.pages.All(page => page.root != null), Is.True);

                Assert.That(bindings.library.root, Is.Not.Null);
                Assert.That(bindings.library.listContent, Is.Not.Null);
                Assert.That(bindings.library.detailPane, Is.Not.Null);
                Assert.That(bindings.library.listScroll, Is.Not.Null);
                Assert.That(bindings.library.listScroll.content, Is.SameAs(bindings.library.listContent));
                Assert.That(bindings.library.groups, Has.Count.EqualTo(3));
                Assert.That(bindings.library.authoredRows, Is.Not.Empty);
                Assert.That(bindings.library.rowTemplate, Is.Not.Null);
                Assert.That(bindings.library.rowTemplate.gameObject.activeSelf, Is.False,
                    "The authored Library template must stay inactive in the saved scene.");
                Assert.That(bindings.library.rowTemplate.GetComponent<WordListHubRuntimeInstance>(), Is.Null,
                    "The template must not carry the runtime instance marker.");

                Assert.That(bindings.community.root, Is.Not.Null);
                Assert.That(bindings.community.listContent, Is.Not.Null);
                Assert.That(bindings.community.detailPane, Is.Not.Null);
                Assert.That(bindings.community.listScroll, Is.Not.Null);
                Assert.That(bindings.community.listScroll.content, Is.SameAs(bindings.community.listContent));
                Assert.That(bindings.community.rowTemplate, Is.Not.Null);
                Assert.That(bindings.community.rowTemplate.gameObject.activeSelf, Is.False);
                Assert.That(bindings.community.rowTemplate.GetComponent<WordListHubRuntimeInstance>(), Is.Null);

                Assert.That(bindings.achievements.root, Is.Not.Null);
                Assert.That(bindings.achievements.cardContent, Is.Not.Null);
                Assert.That(bindings.achievements.cardTemplate, Is.Not.Null);
                Assert.That(bindings.achievements.cardTemplate.gameObject.activeSelf, Is.False);
                Assert.That(bindings.achievements.cardTemplate.GetComponent<WordListHubRuntimeInstance>(), Is.Null);
                Assert.That(bindings.modal.root, Is.Not.Null);
                Assert.That(bindings.modal.root.activeSelf, Is.False);

                SerializedObject serialized = new SerializedObject(controller);
                Assert.That(serialized.FindProperty("authoredLayoutOverrides"), Is.Null,
                    "Scene layout must live in the Scene instead of serialized runtime overrides.");

                foreach (Button button in GetBoundButtons(bindings).Where(button => button != null).Distinct())
                {
                    Assert.That(button.onClick.GetPersistentEventCount(), Is.Zero,
                        button.name + " must not have persistent Inspector callbacks competing with runtime binding.");
                }
                foreach (TMP_Dropdown dropdown in GetBoundDropdowns(bindings)
                    .Where(dropdown => dropdown != null).Distinct())
                {
                    Assert.That(dropdown.onValueChanged.GetPersistentEventCount(), Is.Zero,
                        dropdown.name + " must not have persistent Inspector callbacks competing with runtime binding.");
                }
                foreach (TMP_InputField input in GetBoundInputs(bindings).Where(input => input != null).Distinct())
                {
                    Assert.That(input.onValueChanged.GetPersistentEventCount(), Is.Zero,
                        input.name + " must not have persistent Inspector callbacks competing with runtime binding.");
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
        public void LocalListLanguageIsDetectedFromDisplayLettersAndModeOnlyChangesDoNotCreateRevisions()
        {
            Assert.That(store.TryCreateList("Neutral input", MockupLanguage.Chinese,
                new[] { "123 !!!" }, string.Empty, string.Empty,
                out MockupWordList item, out string createError), Is.True, createError);
            Assert.That(item.Language, Is.EqualTo(MockupLanguage.English),
                "A new neutral-only list falls back to English instead of honoring a manual mode choice.");
            Assert.That(item.DetectedLanguage, Is.EqualTo(MockupLanguage.English));

            MockupWordListEdit chinese = store.CreateEditDraft(item.Id);
            chinese.Language = MockupLanguage.English;
            chinese.Words = new List<string> { "苹果 | ping guo" };
            MockupEditResult chineseResult = store.SaveEdit(item.Id, chinese);
            Assert.That(chineseResult.ContentChanged, Is.True);
            Assert.That(item.Language, Is.EqualTo(MockupLanguage.Chinese));
            Assert.That(item.DetectedLanguage, Is.EqualTo(MockupLanguage.Chinese),
                "Latin pinyin after a valid annotation is not display-language evidence.");

            MockupWordListEdit mixed = store.CreateEditDraft(item.Id);
            mixed.Language = MockupLanguage.Chinese;
            mixed.Words = new List<string> { "tea 你 | ni" };
            MockupEditResult mixedResult = store.SaveEdit(item.Id, mixed);
            Assert.That(mixedResult.ContentChanged, Is.True);
            Assert.That(item.Language, Is.EqualTo(MockupLanguage.Mixed));
            int revision = item.ContentRevision;

            MockupWordListEdit metadataOnly = store.CreateEditDraft(item.Id);
            metadataOnly.Language = MockupLanguage.Chinese;
            metadataOnly.Title = "Detected mode is advisory";
            MockupEditResult metadataResult = store.SaveEdit(item.Id, metadataOnly);
            Assert.That(metadataResult.ContentChanged, Is.False);
            Assert.That(item.Language, Is.EqualTo(MockupLanguage.Mixed));
            Assert.That(item.ContentRevision, Is.EqualTo(revision));
        }

        [Test]
        public void DiscoveryFilters_composeLanguageDownloadAndMetadataSearch()
        {
            var expectedByLanguage = new Dictionary<MockupLanguageFilter, string[]>
            {
                { MockupLanguageFilter.All, new[]
                    { "mock-ugc-night-market", "mock-ugc-cafe-abroad", "mock-ugc-weekend-garden", "mock-ugc-airport-mix" } },
                { MockupLanguageFilter.English, new[] { "mock-ugc-weekend-garden" } },
                { MockupLanguageFilter.Chinese, new[] { "mock-ugc-night-market" } },
                { MockupLanguageFilter.Mixed, new[] { "mock-ugc-cafe-abroad", "mock-ugc-airport-mix" } }
            };

            foreach (KeyValuePair<MockupLanguageFilter, string[]> expected in expectedByLanguage)
            {
                IReadOnlyList<MockupDiscoveryItem> results = store.QueryDiscoveryWithFilters(
                    new MockupDiscoveryQuery { LanguageFilter = expected.Key });
                CollectionAssert.AreEqual(expected.Value, results.Select(item => item.RemoteItemId));
            }

            var allDownloads = store.QueryDiscoveryWithFilters(new MockupDiscoveryQuery
            {
                DownloadFilter = MockupDiscoveryDownloadFilter.All
            });
            CollectionAssert.AreEqual(
                new[] { "mock-ugc-night-market", "mock-ugc-cafe-abroad", "mock-ugc-weekend-garden", "mock-ugc-airport-mix" },
                allDownloads.Select(item => item.RemoteItemId));

            var downloaded = store.QueryDiscoveryWithFilters(new MockupDiscoveryQuery
            {
                DownloadFilter = MockupDiscoveryDownloadFilter.Downloaded
            });
            CollectionAssert.AreEqual(new[] { "mock-ugc-night-market", "mock-ugc-cafe-abroad" },
                downloaded.Select(item => item.RemoteItemId));

            var notDownloaded = store.QueryDiscoveryWithFilters(new MockupDiscoveryQuery
            {
                DownloadFilter = MockupDiscoveryDownloadFilter.NotDownloaded
            });
            CollectionAssert.AreEqual(new[] { "mock-ugc-weekend-garden", "mock-ugc-airport-mix" },
                notDownloaded.Select(item => item.RemoteItemId));

            IReadOnlyList<MockupDiscoveryItem> combined = store.QueryDiscoveryWithFilters(new MockupDiscoveryQuery
            {
                SearchText = "  phrases  ",
                LanguageFilter = MockupLanguageFilter.Mixed,
                DownloadFilter = MockupDiscoveryDownloadFilter.NotDownloaded,
                SortMode = MockupDiscoverySortMode.WordCount,
                SortDirection = MockupSortDirection.Descending
            });
            CollectionAssert.AreEqual(new[] { "mock-ugc-airport-mix" },
                combined.Select(item => item.RemoteItemId));
            Assert.That(combined.All(item => item.Language == MockupLanguage.Mixed && !item.IsDownloaded), Is.True);
        }

        [Test]
        public void DiscoverySorts_keepDefaultInsertionOrderAndBreakTiesDeterministically()
        {
            MockupDiscoveryItem cafe = store.FindDiscoveryItem("mock-ugc-cafe-abroad");
            MockupDiscoveryItem garden = store.FindDiscoveryItem("mock-ugc-weekend-garden");
            SetDiscoveryProperty(cafe, nameof(MockupDiscoveryItem.Title), "Shared Title");
            SetDiscoveryProperty(garden, nameof(MockupDiscoveryItem.Title), "Shared Title");
            SetDiscoveryProperty(cafe, nameof(MockupDiscoveryItem.LikeCount), garden.LikeCount);
            SetDiscoveryProperty(cafe, nameof(MockupDiscoveryItem.WordCount), garden.WordCount);

            string[] ids(MockupDiscoveryQuery query) => store.QueryDiscoveryWithFilters(query)
                .Select(item => item.RemoteItemId).ToArray();

            CollectionAssert.AreEqual(
                new[] { "mock-ugc-night-market", "mock-ugc-cafe-abroad", "mock-ugc-weekend-garden", "mock-ugc-airport-mix" },
                ids(new MockupDiscoveryQuery()));
            CollectionAssert.AreEqual(
                new[] { "mock-ugc-airport-mix", "mock-ugc-weekend-garden", "mock-ugc-cafe-abroad", "mock-ugc-night-market" },
                ids(new MockupDiscoveryQuery { SortDirection = MockupSortDirection.Descending }));

            CollectionAssert.AreEqual(
                new[] { "mock-ugc-airport-mix", "mock-ugc-night-market", "mock-ugc-cafe-abroad", "mock-ugc-weekend-garden" },
                ids(new MockupDiscoveryQuery { SortMode = MockupDiscoverySortMode.Name }));
            CollectionAssert.AreEqual(
                new[] { "mock-ugc-cafe-abroad", "mock-ugc-weekend-garden", "mock-ugc-night-market", "mock-ugc-airport-mix" },
                ids(new MockupDiscoveryQuery
                {
                    SortMode = MockupDiscoverySortMode.Name,
                    SortDirection = MockupSortDirection.Descending
                }));

            CollectionAssert.AreEqual(
                new[] { "mock-ugc-cafe-abroad", "mock-ugc-weekend-garden", "mock-ugc-night-market", "mock-ugc-airport-mix" },
                ids(new MockupDiscoveryQuery { SortMode = MockupDiscoverySortMode.Likes }));
            CollectionAssert.AreEqual(
                new[] { "mock-ugc-airport-mix", "mock-ugc-night-market", "mock-ugc-cafe-abroad", "mock-ugc-weekend-garden" },
                ids(new MockupDiscoveryQuery
                {
                    SortMode = MockupDiscoverySortMode.Likes,
                    SortDirection = MockupSortDirection.Descending
                }));

            CollectionAssert.AreEqual(
                new[] { "mock-ugc-cafe-abroad", "mock-ugc-weekend-garden", "mock-ugc-airport-mix", "mock-ugc-night-market" },
                ids(new MockupDiscoveryQuery { SortMode = MockupDiscoverySortMode.WordCount }));
            CollectionAssert.AreEqual(
                new[] { "mock-ugc-night-market", "mock-ugc-airport-mix", "mock-ugc-cafe-abroad", "mock-ugc-weekend-garden" },
                ids(new MockupDiscoveryQuery
                {
                    SortMode = MockupDiscoverySortMode.WordCount,
                    SortDirection = MockupSortDirection.Descending
                }));
        }

        [Test]
        public void LegacyDiscoverySearchAndDownloadChangesRespectCommunityVisibility()
        {
            CollectionAssert.AreEqual(new[] { "mock-ugc-night-market" },
                store.QueryDiscovery("night market").Select(item => item.RemoteItemId));
            CollectionAssert.AreEqual(new[] { "mock-ugc-airport-mix" },
                store.QueryDiscovery("Airport phrases").Select(item => item.RemoteItemId));
            CollectionAssert.AreEqual(new[] { "mock-ugc-weekend-garden" },
                store.QueryDiscovery("GreenType").Select(item => item.RemoteItemId));

            CollectionAssert.AreEqual(new[] { "mock-ugc-night-market", "mock-ugc-cafe-abroad" },
                store.QueryDiscoveryWithFilters(new MockupDiscoveryQuery
                {
                    DownloadFilter = MockupDiscoveryDownloadFilter.Downloaded
                }).Select(item => item.RemoteItemId));

            MockupWordList downloaded = store.Download("mock-ugc-weekend-garden");
            Assert.That(downloaded.RemoteItemId, Is.EqualTo("mock-ugc-weekend-garden"));
            CollectionAssert.AreEqual(
                new[] { "mock-ugc-night-market", "mock-ugc-cafe-abroad", "mock-ugc-weekend-garden" },
                store.QueryDiscoveryWithFilters(new MockupDiscoveryQuery
                {
                    DownloadFilter = MockupDiscoveryDownloadFilter.Downloaded
                }).Select(item => item.RemoteItemId));
            CollectionAssert.AreEqual(new[] { "mock-ugc-airport-mix" },
                store.QueryDiscoveryWithFilters(new MockupDiscoveryQuery
                {
                    DownloadFilter = MockupDiscoveryDownloadFilter.NotDownloaded
                }).Select(item => item.RemoteItemId));

            Assert.That(store.HideForCurrentUser("mock-ugc-weekend-garden").Succeeded, Is.True);
            Assert.That(store.TryReportAndHide("mock-ugc-cafe-abroad").Succeeded, Is.True);
            Assert.That(store.TryResolveReport("mock-ugc-cafe-abroad", true).Succeeded, Is.True);
            CollectionAssert.AreEqual(new[] { "mock-ugc-night-market", "mock-ugc-airport-mix" },
                store.QueryDiscovery().Select(item => item.RemoteItemId));
            Assert.That(store.QueryDiscoveryWithFilters(new MockupDiscoveryQuery
            {
                DownloadFilter = MockupDiscoveryDownloadFilter.Downloaded
            }).All(item => !item.IsHiddenByCurrentUser && !item.IsRemovedFromCommunity), Is.True);
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
        public void FindCommunityDownload_usesRemoteIdentityAndReturnsItsPersonalRecord()
        {
            const string remoteItemId = "mock-ugc-weekend-garden";
            Assert.That(store.FindCommunityDownload(remoteItemId), Is.Null);

            MockupWordList downloaded = store.Download(remoteItemId);
            Assert.That(store.FindCommunityDownload(remoteItemId), Is.SameAs(downloaded));
            Assert.That(downloaded.PersonalStars, Is.Null);
            Assert.That(downloaded.BestTimeSeconds, Is.Null);

            store.CompleteRun(downloaded.Id, 2, 12.5f);

            MockupWordList record = store.FindCommunityDownload(remoteItemId);
            Assert.That(record, Is.SameAs(downloaded));
            Assert.That(record.PersonalStars, Is.EqualTo(2));
            Assert.That(record.BestTimeSeconds, Is.EqualTo(12.5f));
            Assert.That(store.FindCommunityDownload("missing-community-item"), Is.Null);
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
                new[] { " apple ", "", "   ", "早上 Morning! | zao shang", "Keep.Case?" });

            CollectionAssert.AreEqual(new[] { "apple", "早上 Morning! | zao shang", "Keep.Case?" }, item.Words);
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
        public void PinyinAnnotations_validatePerHanCharacterAndFormatWithoutChangingEnglishText()
        {
            string error;
            Assert.That(MockupWordContent.TryValidateWordLines(new[]
            {
                "apple — 苹果 | ping guo",
                "行 | hang",
                "行 | xing",
                "A|B"
            }, null, out error), Is.True, error);

            Assert.That(MockupWordContent.FormatForDetail("apple — 苹果 | ping guo"),
                Is.EqualTo("apple — 苹(ping) 果(guo)"));
            Assert.That(MockupWordContent.FormatForDetail("行 | hang"), Is.EqualTo("行(hang)"));
            Assert.That(MockupWordContent.FormatForDetail("行 | xing"), Is.EqualTo("行(xing)"));
            Assert.That(MockupWordContent.FormatForDetail("你，好! | ni hao"), Is.EqualTo("你(ni)，好(hao)!"));
            Assert.That(MockupWordContent.FormatForDetail("A|B"), Is.EqualTo("A|B"));
            Assert.That(MockupWordContent.FormatForDetail("苹果"), Is.EqualTo("苹果 (pinyin not set)"));
        }

        [Test]
        public void StackedPresentationPartsPreserveMixedOrderAndMarkLegacyReadingsUnknown()
        {
            MockupWordContent.GetPresentationParts(
                "before 你，好! after 苹果 | ni hao ping guo",
                out string displayText,
                out string[] pinyin,
                out bool hasUnannotatedHan);

            Assert.That(displayText, Is.EqualTo("before 你，好! after 苹果"));
            Assert.That(pinyin, Is.EqualTo(new[] { "ni", "hao", "ping", "guo" }));
            Assert.That(hasUnannotatedHan, Is.False);

            MockupWordContent.GetPresentationParts("苹果", out displayText, out pinyin, out hasUnannotatedHan);
            Assert.That(displayText, Is.EqualTo("苹果"));
            Assert.That(pinyin, Is.Empty);
            Assert.That(hasUnannotatedHan, Is.True);

            MockupWordContent.GetPresentationParts("A|B", out displayText, out pinyin, out hasUnannotatedHan);
            Assert.That(displayText, Is.EqualTo("A|B"));
            Assert.That(pinyin, Is.Empty);
            Assert.That(hasUnannotatedHan, Is.False);
        }

        [TestCase("苹果 | ping", "Line 1: expected 2 pinyin syllable(s)")]
        [TestCase("苹果 | PING guo", "Line 1: pinyin must use lowercase a-z")]
        [TestCase("苹果 | pǐng guǒ", "Line 1: pinyin must use lowercase a-z")]
        [TestCase("苹果 | ping guo | extra", "Line 1: use one '|'")]
        public void PinyinAnnotations_reportInvalidLines(string word, string expectedError)
        {
            string error;
            Assert.That(MockupWordContent.TryValidateWordLines(new[] { word }, null, out error), Is.False);
            Assert.That(error, Does.Contain(expectedError));
        }

        [Test]
        public void PinyinAnnotationText_reportsOriginalLineNumberAndAllowsUnchangedLegacyHanzi()
        {
            string error;
            Assert.That(MockupWordContent.TryValidateWordText("apple\n\n苹果 | ping", null, out error), Is.False);
            Assert.That(error, Does.StartWith("Line 3:"));
            Assert.That(MockupWordContent.TryValidateWordLines(new[] { "苹果" }, null, out error), Is.False);
            Assert.That(MockupWordContent.TryValidateWordLines(new[] { "苹果" }, new[] { "苹果" }, out error),
                Is.True, error);

            Assert.Throws<System.ArgumentException>(() => store.CreateList(
                "Missing pinyin", MockupLanguage.Chinese, new[] { "苹果" }));
        }

        [Test]
        public void ExistingLegacyHanziCanStayUntouchedButCannotBeAddedAgain()
        {
            MockupWordList legacy = store.FindById("mock-list-hsk-starter");
            legacy.Words[0] = "苹果";
            MockupWordListEdit metadataDraft = store.CreateEditDraft(legacy.Id);
            metadataDraft.Title += " Updated";

            MockupEditResult metadataResult = store.SaveEdit(legacy.Id, metadataDraft);
            Assert.That(metadataResult.ContentChanged, Is.False);
            Assert.That(legacy.Words[0], Is.EqualTo("苹果"));

            MockupEditResult copyResult = store.SaveOwnedContentAsNewList(
                legacy.Id, store.CreateEditDraft(legacy.Id));
            Assert.That(copyResult.Item.Words[0], Is.EqualTo("苹果"));

            MockupWordListEdit duplicateDraft = store.CreateEditDraft(legacy.Id);
            duplicateDraft.Words[1] = "苹果";
            Assert.Throws<System.ArgumentException>(() =>
                store.SaveOwnedContentAsNewList(legacy.Id, duplicateDraft));
        }

        [Test]
        public void AllSeededChineseAndMixedLibraryAndCommunityLinesHavePinyin()
        {
            foreach (MockupWordList item in store.QueryLibrary(new MockupLibraryQuery()))
            {
                Assert.That(MockupWordContent.TryValidateWordLines(item.Words, null, out string error),
                    Is.True, item.Title + ": " + error);
            }

            foreach (MockupDiscoveryItem item in store.DiscoveryItems)
            {
                Assert.That(MockupWordContent.TryValidateWordLines(item.Words, null, out string error),
                    Is.True, item.Title + ": " + error);
            }
        }

        [Test]
        public void PinyinOnlyEditIsGameplayContentAndCopiesDownloadsKeepTheExactAnnotation()
        {
            MockupWordList onePhrase = store.CreateList(
                "One phrase", MockupLanguage.Chinese, new[] { "你好吗 | ni hao ma" });
            Assert.That(onePhrase.WordCount, Is.EqualTo(1));

            MockupWordList item = store.CreateList("Polyphonic", MockupLanguage.Chinese, new[] { "行 | hang" });
            store.CompleteRun(item.Id, 4, 21f);
            MockupWordListEdit pronunciationDraft = store.CreateEditDraft(item.Id);
            pronunciationDraft.Words[0] = "行 | xing";

            MockupEditResult editResult = store.SaveEdit(item.Id, pronunciationDraft);

            Assert.That(editResult.ContentChanged, Is.True);
            Assert.That(item.ContentRevision, Is.EqualTo(2));
            Assert.That(item.Words, Is.EqualTo(new[] { "行 | xing" }));
            Assert.That(item.PreviousContentRecords, Has.Count.EqualTo(1));
            Assert.That(item.PreviousContentRecords[0].PersonalStars, Is.EqualTo(4));
            Assert.That(item.PersonalStars, Is.Null);

            MockupEditResult copyResult = store.SaveOwnedContentAsNewList(
                item.Id, store.CreateEditDraft(item.Id));
            Assert.That(copyResult.Item.Words, Is.EqualTo(new[] { "行 | xing" }));

            MockupDiscoveryItem remote = store.FindDiscoveryItem("mock-ugc-airport-mix");
            MockupWordList downloaded = store.Download(remote.RemoteItemId);
            CollectionAssert.AreEqual(remote.Words, downloaded.Words);
            Assert.That(downloaded.Words.All(word => MockupWordContent.TryValidateWordLines(
                new[] { word }, null, out string _)), Is.True);

            MockupWordList communityCopy = store.EditCommunityCopy(downloaded.Id);
            CollectionAssert.AreEqual(remote.Words, communityCopy.Words);
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
        public void DeletePublishedList_requiresUnpublishAndDeletesOnlyTheLocalList()
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
            Assert.That(item.RemoteItemId, Is.Null);
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
        public void OwnedCopy_createsIndependentMyList_withoutRemoteIdentityOrRecords()
        {
            MockupWordList source = store.FindById("mock-list-travel-copy");
            store.ToggleFavorite(source.Id);
            store.CompleteRun(source.Id, 4, 61f);

            MockupWordListEdit contentEdit = store.CreateEditDraft(source.Id);
            contentEdit.Words.Add("source revision");
            store.SaveEdit(source.Id, contentEdit);
            store.CompleteRun(source.Id, 3, 72f);

            string sourceTitle = source.Title;
            string sourceDescription = source.Description;
            string sourceTags = source.Tags;
            List<string> sourceWords = new List<string>(source.Words);
            int? sourceStars = source.PersonalStars;
            float? sourceBestTime = source.BestTimeSeconds;
            int sourcePlayCount = source.PlayCount;
            int sourceHistoryCount = source.PreviousContentRecords.Count;
            bool sourceFavorite = source.IsFavorite;
            System.DateTime sourceModifiedAt = source.ModifiedAt;

            MockupWordListEdit draft = store.CreateEditDraft(source.Id);
            draft.Title = "Travel Phrases Copy";
            draft.Description = "An independent local copy.";
            draft.Tags = "travel,copy";

            MockupEditResult result = store.SaveOwnedContentAsNewList(source.Id, draft);
            MockupWordList copy = result.Item;

            Assert.That(result.CreatedNewList, Is.True);
            Assert.That(result.ContentChanged, Is.False);
            Assert.That(result.ShowRecordWarning, Is.False);
            Assert.That(copy.Id, Is.Not.EqualTo(source.Id));
            Assert.That(copy.Title, Is.EqualTo(draft.Title));
            Assert.That(copy.Description, Is.EqualTo(draft.Description));
            Assert.That(copy.Tags, Is.EqualTo(draft.Tags));
            Assert.That(copy.Language, Is.EqualTo(source.Language));
            Assert.That(copy.Words, Is.EqualTo(sourceWords));
            Assert.That(copy.WordCount, Is.EqualTo(copy.Words.Count));
            Assert.That(copy.Ownership, Is.EqualTo(MockupOwnership.Owned));
            Assert.That(copy.Group, Is.EqualTo(MockupLibraryGroup.MyLists));
            Assert.That(copy.IsReadOnly, Is.False);
            Assert.That(copy.HasOfflineCache, Is.True);
            Assert.That(copy.HasPublishedBinding, Is.False);
            Assert.That(copy.RemoteItemId, Is.Null);
            Assert.That(copy.RemoteAuthor, Is.Null);
            Assert.That(copy.IsPubliclyVisible, Is.False);
            Assert.That(copy.DownloadStatus, Is.EqualTo(MockupDownloadStatus.None));
            Assert.That(copy.PublicationStatus, Is.EqualTo(MockupPublicationStatus.NotPublished));
            Assert.That(copy.ContentRevision, Is.EqualTo(1));
            Assert.That(copy.LastPlayedAt, Is.Null);
            Assert.That(copy.PersonalStars, Is.Null);
            Assert.That(copy.BestTimeSeconds, Is.Null);
            Assert.That(copy.PlayCount, Is.Zero);
            Assert.That(copy.IsFavorite, Is.False);
            Assert.That(copy.PreviousContentRecords, Is.Empty);

            Assert.That(source.Title, Is.EqualTo(sourceTitle));
            Assert.That(source.Description, Is.EqualTo(sourceDescription));
            Assert.That(source.Tags, Is.EqualTo(sourceTags));
            Assert.That(source.Words, Is.EqualTo(sourceWords));
            Assert.That(source.PersonalStars, Is.EqualTo(sourceStars));
            Assert.That(source.BestTimeSeconds, Is.EqualTo(sourceBestTime));
            Assert.That(source.PlayCount, Is.EqualTo(sourcePlayCount));
            Assert.That(source.PreviousContentRecords, Has.Count.EqualTo(sourceHistoryCount));
            Assert.That(source.IsFavorite, Is.EqualTo(sourceFavorite));
            Assert.That(source.ModifiedAt, Is.EqualTo(sourceModifiedAt));
            Assert.That(source.Group, Is.EqualTo(MockupLibraryGroup.MyLists));
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
        public void UnpublishedMetadataOnlyEdit_doesNotWarnOrChangePersonalRecords()
        {
            MockupWordList item = store.FindById("mock-list-everyday-english");
            List<string> oldWords = new List<string>(item.Words);
            int? oldStars = item.PersonalStars;
            float? oldBestTime = item.BestTimeSeconds;
            int oldRevision = item.ContentRevision;
            System.DateTime oldContentModifiedAt = item.ContentModifiedAt;
            System.DateTime oldModifiedAt = item.ModifiedAt;

            MockupWordListEdit draft = store.CreateEditDraft(item.Id);
            draft.Title = "Everyday English Plus";
            draft.Description = "Updated explanatory text.";
            draft.Tags = "punctuation,english,practice";
            MockupEditResult result = store.SaveEdit(item.Id, draft);

            Assert.That(result.ContentChanged, Is.False);
            Assert.That(result.ShowRecordWarning, Is.False);
            Assert.That(item.Title, Is.EqualTo("Everyday English Plus"));
            Assert.That(item.Tags, Is.EqualTo("punctuation,english,practice"));
            Assert.That(item.Words, Is.EqualTo(oldWords));
            Assert.That(item.WordCount, Is.EqualTo(item.Words.Count));
            Assert.That(item.PersonalStars, Is.EqualTo(oldStars));
            Assert.That(item.BestTimeSeconds, Is.EqualTo(oldBestTime));
            Assert.That(item.ContentRevision, Is.EqualTo(oldRevision));
            Assert.That(item.ContentModifiedAt, Is.EqualTo(oldContentModifiedAt));
            Assert.That(item.ModifiedAt, Is.Not.EqualTo(oldModifiedAt));
            Assert.That(item.PublicationStatus, Is.EqualTo(MockupPublicationStatus.NotPublished));
            Assert.That(item.Group, Is.EqualTo(MockupLibraryGroup.MyLists));
            Assert.That(item.PreviousContentRecords, Is.Empty);
        }

        [Test]
        public void UnpublishingClearsPublishedIdentityAndRestoresLocalEditing()
        {
            MockupWordList item = store.FindById("mock-list-punctuation");
            string oldRemoteId = item.RemoteItemId;
            string oldTitle = item.Title;
            string oldDescription = item.Description;
            string oldTags = item.Tags;
            MockupLanguage oldLanguage = item.Language;
            List<string> oldWords = new List<string>(item.Words);
            System.DateTime oldModifiedAt = item.ModifiedAt;
            System.DateTime oldContentModifiedAt = item.ContentModifiedAt;
            int oldRevision = item.ContentRevision;
            int? oldStars = item.PersonalStars;
            float? oldBestTime = item.BestTimeSeconds;

            AssertPublishedEditsRejected(item);
            MockupEditResult publishedNoOp = store.SaveEdit(item.Id, store.CreateEditDraft(item.Id));
            Assert.That(publishedNoOp.Item, Is.SameAs(item));
            Assert.That(publishedNoOp.ContentChanged, Is.False);
            Assert.That(item.PublicationStatus, Is.EqualTo(MockupPublicationStatus.Published));

            store.Unpublish(item.Id);
            Assert.That(item.RemoteItemId, Is.Null);
            Assert.That(item.PublicationStatus, Is.EqualTo(MockupPublicationStatus.NotPublished));
            Assert.That(item.IsPubliclyVisible, Is.False);
            Assert.That(item.Group, Is.EqualTo(MockupLibraryGroup.MyLists));

            Assert.That(item.Title, Is.EqualTo(oldTitle));
            Assert.That(item.Description, Is.EqualTo(oldDescription));
            Assert.That(item.Tags, Is.EqualTo(oldTags));
            Assert.That(item.Language, Is.EqualTo(oldLanguage));
            Assert.That(item.Words, Is.EqualTo(oldWords));
            Assert.That(item.ModifiedAt, Is.EqualTo(oldModifiedAt));
            Assert.That(item.ContentModifiedAt, Is.EqualTo(oldContentModifiedAt));
            Assert.That(item.ContentRevision, Is.EqualTo(oldRevision));
            Assert.That(item.PersonalStars, Is.EqualTo(oldStars));
            Assert.That(item.BestTimeSeconds, Is.EqualTo(oldBestTime));
            Assert.That(item.PreviousContentRecords, Is.Empty);

            MockupWordListEdit metadataDraft = store.CreateEditDraft(item.Id);
            metadataDraft.Title = "Punctuation After Unpublish";
            metadataDraft.Description = "An editable local list.";
            metadataDraft.Tags = "punctuation,local";
            MockupEditResult metadataResult = store.SaveEdit(item.Id, metadataDraft);
            Assert.That(metadataResult.ContentChanged, Is.False);
            Assert.That(metadataResult.ShowRecordWarning, Is.False);
            Assert.That(item.Title, Is.EqualTo(metadataDraft.Title));
            Assert.That(item.Description, Is.EqualTo(metadataDraft.Description));
            Assert.That(item.Tags, Is.EqualTo(metadataDraft.Tags));
            Assert.That(item.Words, Is.EqualTo(oldWords));
            Assert.That(item.PersonalStars, Is.EqualTo(oldStars));
            Assert.That(item.BestTimeSeconds, Is.EqualTo(oldBestTime));

            MockupWordListEdit contentDraft = store.CreateEditDraft(item.Id);
            contentDraft.Language = MockupLanguage.Chinese;
            contentDraft.Words[0] = "改 | gai";
            MockupEditResult contentResult = store.SaveEdit(item.Id, contentDraft);
            Assert.That(contentResult.ContentChanged, Is.True);
            Assert.That(contentResult.ShowRecordWarning, Is.True);
            Assert.That(item.Language, Is.EqualTo(MockupLanguage.Chinese));
            Assert.That(item.Words[0], Is.EqualTo("改 | gai"));
            Assert.That(item.ContentRevision, Is.EqualTo(oldRevision + 1));
            Assert.That(item.PersonalStars, Is.Null);
            Assert.That(item.BestTimeSeconds, Is.Null);
            Assert.That(item.PreviousContentRecords, Has.Count.EqualTo(1));
            Assert.That(item.PreviousContentRecords[0].ContentRevision, Is.EqualTo(oldRevision));
            Assert.That(item.PreviousContentRecords[0].PersonalStars, Is.EqualTo(oldStars));
            Assert.That(item.PreviousContentRecords[0].BestTimeSeconds, Is.EqualTo(oldBestTime));

            MockupWordList republished = store.Publish(item.Id);
            Assert.That(republished, Is.SameAs(item));
            Assert.That(item.RemoteItemId, Is.Not.Null.And.Not.Empty);
            Assert.That(item.RemoteItemId, Is.Not.EqualTo(oldRemoteId));
            Assert.That(item.PublicationStatus, Is.EqualTo(MockupPublicationStatus.Published));
            Assert.That(item.Group, Is.EqualTo(MockupLibraryGroup.MyPublished));
            Assert.That(item.PreviousContentRecords, Has.Count.EqualTo(1));
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
            draft.Title = "Punctuation Practice New Version";
            draft.Description = "A separately authored content version.";
            draft.Tags = "punctuation,new-version";
            draft.Words.Add("new punctuation word");
            MockupEditResult result = store.SavePublishedContentAsNewList(source.Id, draft);

            Assert.That(result.ContentChanged, Is.True);
            Assert.That(result.ShowRecordWarning, Is.True);
            Assert.That(result.CreatedNewList, Is.True);
            Assert.That(result.Item.Id, Is.Not.EqualTo(source.Id));
            Assert.That(result.Item.RemoteItemId, Is.Null);
            Assert.That(result.Item.PublicationStatus, Is.EqualTo(MockupPublicationStatus.NotPublished));
            Assert.That(result.Item.Title, Is.EqualTo(draft.Title));
            Assert.That(result.Item.Description, Is.EqualTo(draft.Description));
            Assert.That(result.Item.Tags, Is.EqualTo(draft.Tags));
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
        public void PublishedCopy_allowsUnchangedOrMetadataOnlyCopiesAndKeepsSourceIntact()
        {
            MockupWordList source = store.FindById("mock-list-punctuation");
            string oldRemoteId = source.RemoteItemId;
            string oldTitle = source.Title;
            string oldDescription = source.Description;
            string oldTags = source.Tags;
            MockupLanguage oldLanguage = source.Language;
            List<string> oldWords = new List<string>(source.Words);
            int? oldStars = source.PersonalStars;
            float? oldTime = source.BestTimeSeconds;

            MockupWordListEdit unchangedDraft = store.CreateEditDraft(source.Id);
            MockupEditResult unchangedCopyResult = store.SavePublishedContentAsNewList(source.Id, unchangedDraft);
            Assert.That(unchangedCopyResult.ContentChanged, Is.False);
            Assert.That(unchangedCopyResult.CreatedNewList, Is.True);
            Assert.That(unchangedCopyResult.ShowRecordWarning, Is.False);
            Assert.That(unchangedCopyResult.Item.Id, Is.Not.EqualTo(source.Id));
            Assert.That(unchangedCopyResult.Item.RemoteItemId, Is.Null);
            Assert.That(unchangedCopyResult.Item.Words, Is.EqualTo(oldWords));
            Assert.That(unchangedCopyResult.Item.Language, Is.EqualTo(oldLanguage));

            MockupWordListEdit metadataDraft = store.CreateEditDraft(source.Id);
            metadataDraft.Title = "Punctuation Practice Copy";
            metadataDraft.Description = "A local copy with updated notes.";
            metadataDraft.Tags = "punctuation,local-copy";
            MockupEditResult metadataCopyResult = store.SavePublishedContentAsNewList(source.Id, metadataDraft);
            Assert.That(metadataCopyResult.ContentChanged, Is.False);
            Assert.That(metadataCopyResult.CreatedNewList, Is.True);
            Assert.That(metadataCopyResult.Item.Id, Is.Not.EqualTo(source.Id));
            Assert.That(metadataCopyResult.Item.RemoteItemId, Is.Null);
            Assert.That(metadataCopyResult.Item.PublicationStatus, Is.EqualTo(MockupPublicationStatus.NotPublished));
            Assert.That(metadataCopyResult.Item.Group, Is.EqualTo(MockupLibraryGroup.MyLists));
            Assert.That(metadataCopyResult.Item.Title, Is.EqualTo(metadataDraft.Title));
            Assert.That(metadataCopyResult.Item.Description, Is.EqualTo(metadataDraft.Description));
            Assert.That(metadataCopyResult.Item.Tags, Is.EqualTo(metadataDraft.Tags));
            Assert.That(metadataCopyResult.Item.Words, Is.EqualTo(oldWords));
            Assert.That(metadataCopyResult.Item.Language, Is.EqualTo(oldLanguage));

            Assert.That(source.RemoteItemId, Is.EqualTo(oldRemoteId));
            Assert.That(source.Title, Is.EqualTo(oldTitle));
            Assert.That(source.Description, Is.EqualTo(oldDescription));
            Assert.That(source.Tags, Is.EqualTo(oldTags));
            Assert.That(source.Language, Is.EqualTo(oldLanguage));
            Assert.That(source.Words, Is.EqualTo(oldWords));
            Assert.That(source.PersonalStars, Is.EqualTo(oldStars));
            Assert.That(source.BestTimeSeconds, Is.EqualTo(oldTime));
            Assert.That(source.PublicationStatus, Is.EqualTo(MockupPublicationStatus.Published));
            Assert.That(source.IsPubliclyVisible, Is.True);
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
        public void Unpublish_requiresOnline_andRepublishCreatesANewCommunityIdentity()
        {
            MockupWordList item = store.FindById("mock-list-travel-copy");
            MockupWordList published = store.Publish(item.Id);
            string remoteId = published.RemoteItemId;
            int? stars = item.PersonalStars;
            float? bestTime = item.BestTimeSeconds;
            int historyCount = item.PreviousContentRecords.Count;
            List<string> words = new List<string>(item.Words);
            store.SetOnline(false);

            MockupActionResult offline = store.TryUnpublish(item.Id);
            Assert.That(offline.Succeeded, Is.False);
            Assert.That(offline.List, Is.SameAs(item));
            Assert.That(item.IsPubliclyVisible, Is.True);
            Assert.That(item.PublicationStatus, Is.EqualTo(MockupPublicationStatus.Published));
            Assert.That(item.RemoteItemId, Is.EqualTo(remoteId));
            Assert.That(item.Group, Is.EqualTo(MockupLibraryGroup.MyPublished));

            store.SetOnline(true);
            MockupActionResult unpublished = store.TryUnpublish(item.Id);
            Assert.That(unpublished.Succeeded, Is.True);
            Assert.That(unpublished.List, Is.SameAs(item));
            Assert.That(item.IsPubliclyVisible, Is.False);
            Assert.That(item.PublicationStatus, Is.EqualTo(MockupPublicationStatus.NotPublished));
            Assert.That(item.RemoteItemId, Is.Null);
            Assert.That(item.Group, Is.EqualTo(MockupLibraryGroup.MyLists));
            Assert.That(item.PersonalStars, Is.EqualTo(stars));
            Assert.That(item.BestTimeSeconds, Is.EqualTo(bestTime));
            Assert.That(item.PreviousContentRecords, Has.Count.EqualTo(historyCount));
            Assert.That(item.Words, Is.EqualTo(words));

            MockupActionResult republished = store.TryPublish(item.Id);
            Assert.That(republished.Succeeded, Is.True);
            Assert.That(republished.List, Is.SameAs(item));
            Assert.That(item.RemoteItemId, Is.Not.Null.And.Not.Empty);
            Assert.That(item.RemoteItemId, Is.Not.EqualTo(remoteId));
            Assert.That(item.Id, Is.EqualTo("mock-list-travel-copy"));
            Assert.That(item.Words, Is.EqualTo(words));
            Assert.That(item.WordCount, Is.EqualTo(item.Words.Count));
            Assert.That(item.IsPubliclyVisible, Is.True);
            Assert.That(item.PublicationStatus, Is.EqualTo(MockupPublicationStatus.Published));
            Assert.That(item.Group, Is.EqualTo(MockupLibraryGroup.MyPublished));
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

        private void AssertPublishedEditsRejected(MockupWordList item)
        {
            MockupWordListEdit titleDraft = store.CreateEditDraft(item.Id);
            titleDraft.Title += " changed";
            Assert.Throws<System.InvalidOperationException>(() => store.SaveEdit(item.Id, titleDraft));

            MockupWordListEdit descriptionDraft = store.CreateEditDraft(item.Id);
            descriptionDraft.Description += " changed";
            Assert.Throws<System.InvalidOperationException>(() => store.SaveEdit(item.Id, descriptionDraft));

            MockupWordListEdit tagsDraft = store.CreateEditDraft(item.Id);
            tagsDraft.Tags += ",changed";
            Assert.Throws<System.InvalidOperationException>(() => store.SaveEdit(item.Id, tagsDraft));

            MockupWordListEdit wordsDraft = store.CreateEditDraft(item.Id);
            wordsDraft.Words[0] += " changed";
            Assert.Throws<System.InvalidOperationException>(() => store.SaveEdit(item.Id, wordsDraft));

            MockupWordListEdit languageDraft = store.CreateEditDraft(item.Id);
            languageDraft.Language = MockupLanguage.Chinese;
            MockupEditResult languageOnlyResult = store.SaveEdit(item.Id, languageDraft);
            Assert.That(languageOnlyResult.ContentChanged, Is.False,
                "The read-only language display cannot change stored content by itself.");
            Assert.That(item.Language, Is.Not.EqualTo(MockupLanguage.Chinese));
        }

        private static void SetDiscoveryProperty<T>(MockupDiscoveryItem item, string propertyName, T value)
        {
            System.Reflection.PropertyInfo property = typeof(MockupDiscoveryItem).GetProperty(propertyName);
            Assert.That(property, Is.Not.Null, "Missing discovery property: " + propertyName);
            property.SetValue(item, value, null);
        }

        private void AssertViewContainsOnly(MockupLibraryView view, MockupLibraryGroup expectedGroup)
        {
            IReadOnlyList<MockupWordList> items = store.QueryLibrary(new MockupLibraryQuery { View = view });
            Assert.That(items.All(item => item.Group == expectedGroup), Is.True);
            Assert.That(items, Has.Count.EqualTo(store.GetGroupCount(expectedGroup)));
        }

        private static IEnumerable<Button> GetBoundButtons(WordListHubSceneBindings bindings)
        {
            yield return bindings.navChallenges.button;
            yield return bindings.navLibrary.button;
            yield return bindings.navCommunity.button;
            yield return bindings.navAchievements.button;
            yield return bindings.resetMockData.button;
            yield return bindings.library.allLists.button;
            yield return bindings.library.favorites.button;
            yield return bindings.library.createList.button;
            foreach (WordListHubSceneBindings.GroupBinding group in bindings.library.groups)
                yield return group.button;
            foreach (WordListHubSceneBindings.LibraryRowBinding row in bindings.library.authoredRows)
            {
                yield return row.button;
                yield return row.favoriteButton;
            }
            yield return bindings.library.delete.button;
            yield return bindings.library.play.button;
            yield return bindings.library.edit.button;
            yield return bindings.library.copyEditWords.button;
            yield return bindings.library.publish.button;
            yield return bindings.library.unpublish.button;
            foreach (WordListHubSceneBindings.DiscoveryRowBinding row in bindings.community.authoredRows)
                yield return row.button;
            yield return bindings.community.like.button;
            yield return bindings.community.download.button;
            yield return bindings.community.play.button;
            yield return bindings.community.report.button;
            yield return bindings.challenges.dailyChallenge.button;
            yield return bindings.challenges.weeklyChallenge.button;
            yield return bindings.modal.newList.saveButton;
            yield return bindings.modal.newList.cancelButton;
            yield return bindings.modal.editList.saveButton;
            yield return bindings.modal.editList.cancelButton;
            yield return bindings.modal.contentChange.confirmButton;
            yield return bindings.modal.contentChange.cancelButton;
            yield return bindings.modal.deleteList.confirmButton;
            yield return bindings.modal.deleteList.cancelButton;
            yield return bindings.modal.playResult.confirmButton;
            yield return bindings.modal.playResult.cancelButton;
        }

        private static IEnumerable<TMP_Dropdown> GetBoundDropdowns(WordListHubSceneBindings bindings)
        {
            yield return bindings.library.languageFilter;
            yield return bindings.library.playedFilter;
            yield return bindings.library.sortFilter;
            yield return bindings.library.directionFilter;
            yield return bindings.community.connectionState;
            yield return bindings.achievements.filter;
            yield return bindings.modal.newList.languageDropdown;
            yield return bindings.modal.editList.languageDropdown;
        }

        private static IEnumerable<TMP_InputField> GetBoundInputs(WordListHubSceneBindings bindings)
        {
            yield return bindings.library.searchInput;
            yield return bindings.community.searchInput;
            yield return bindings.modal.newList.titleInput;
            yield return bindings.modal.newList.descriptionInput;
            yield return bindings.modal.newList.tagsInput;
            yield return bindings.modal.newList.wordsInput;
            yield return bindings.modal.editList.titleInput;
            yield return bindings.modal.editList.descriptionInput;
            yield return bindings.modal.editList.tagsInput;
            yield return bindings.modal.editList.wordsInput;
        }
    }
}
