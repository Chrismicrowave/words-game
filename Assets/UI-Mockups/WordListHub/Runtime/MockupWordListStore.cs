using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Words.UiMockups.WordListHub
{
    /// <summary>
    /// Disposable mock-only state. It uses no files, PlayerPrefs, Steam APIs, or game
    /// managers. Construct a new store (or call ResetFixtures) when the mock scene opens.
    /// </summary>
    public sealed class MockupWordListStore
    {
        private readonly List<MockupWordList> libraryEntries = new List<MockupWordList>();
        private readonly List<MockupDiscoveryItem> discoveryItems = new List<MockupDiscoveryItem>();
        private readonly List<MockupAchievement> achievements = new List<MockupAchievement>();
        private int nextMockListNumber;
        private int nextPublishedNumber;
        private int completedRuns;
        private bool isOnline = true;

        public IReadOnlyList<MockupDiscoveryItem> DiscoveryItems => discoveryItems.AsReadOnly();
        public IReadOnlyList<MockupDiscoveryItem> VisibleDiscoveryItems =>
            discoveryItems.Where(item => !item.IsHiddenByCurrentUser && !item.IsRemovedFromCommunity)
                .ToList().AsReadOnly();
        public IReadOnlyList<MockupAchievement> Achievements => achievements.AsReadOnly();
        public bool IsOnline => isOnline;

        public static MockupWordListStore CreateWithFixtures()
        {
            return new MockupWordListStore();
        }

        public MockupWordListStore()
        {
            ResetFixtures();
        }

        public void ResetFixtures()
        {
            libraryEntries.Clear();
            discoveryItems.Clear();
            achievements.Clear();
            nextMockListNumber = 1;
            nextPublishedNumber = 1;
            completedRuns = 1;
            isOnline = true;
            SeedLibraryFixtures();
            SeedDiscoveryFixtures();
            SeedAchievementFixtures();
        }

        public IReadOnlyList<MockupWordList> QueryLibrary(MockupLibraryQuery query)
        {
            query = query ?? new MockupLibraryQuery();
            IEnumerable<MockupWordList> results = libraryEntries;

            switch (query.View)
            {
                case MockupLibraryView.Favorites:
                    results = results.Where(item => item.IsFavorite);
                    break;
                case MockupLibraryView.MyLists:
                    results = results.Where(item => item.Group == MockupLibraryGroup.MyLists);
                    break;
                case MockupLibraryView.CommunityDownloads:
                    results = results.Where(item => item.Group == MockupLibraryGroup.CommunityDownloads);
                    break;
                case MockupLibraryView.MyPublished:
                    results = results.Where(item => item.Group == MockupLibraryGroup.MyPublished);
                    break;
            }

            if (!string.IsNullOrWhiteSpace(query.SearchText))
            {
                string term = query.SearchText.Trim();
                results = results.Where(item =>
                    Contains(item.Title, term) || Contains(item.Description, term));
            }

            if (query.LanguageFilter != MockupLanguageFilter.All)
                results = results.Where(item => item.Language.ToString() == query.LanguageFilter.ToString());

            if (query.PlayedFilter == MockupPlayedFilter.Played)
                results = results.Where(item => item.HasBeenPlayed);
            else if (query.PlayedFilter == MockupPlayedFilter.Unplayed)
                results = results.Where(item => !item.HasBeenPlayed);

            return Sort(results, query.SortMode, query.SortDirection).ToList().AsReadOnly();
        }

        public IReadOnlyList<MockupDiscoveryItem> QueryDiscovery(string searchText = null)
        {
            IEnumerable<MockupDiscoveryItem> results = VisibleDiscoveryItems;
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                string term = searchText.Trim();
                results = results.Where(item => Contains(item.Title, term)
                    || Contains(item.Description, term)
                    || Contains(item.RemoteAuthor, term));
            }

            return results.ToList().AsReadOnly();
        }

        public void SetOnline(bool online)
        {
            isOnline = online;
        }

        public int GetGroupCount(MockupLibraryGroup group)
        {
            return libraryEntries.Count(item => item.Group == group);
        }

        public MockupWordList FindById(string mockId)
        {
            return libraryEntries.FirstOrDefault(item => item.Id == mockId);
        }

        public MockupDiscoveryItem FindDiscoveryItem(string remoteItemId)
        {
            return discoveryItems.FirstOrDefault(item => item.RemoteItemId == remoteItemId);
        }

        public MockupWordListEdit CreateEditDraft(string mockId)
        {
            MockupWordList item = RequireList(mockId);
            return new MockupWordListEdit
            {
                Title = item.Title,
                Description = item.Description,
                Tags = item.Tags,
                Language = item.Language,
                Words = item.Words == null ? new List<string>() : new List<string>(item.Words),
                WordCount = item.WordCount
            };
        }

        public bool ToggleFavorite(string mockId)
        {
            MockupWordList item = RequireList(mockId);
            item.IsFavorite = !item.IsFavorite;
            return item.IsFavorite;
        }

        /// <summary>Changes only the local preview state and never submits a community vote.</summary>
        public bool ToggleLike(string remoteItemId)
        {
            MockupDiscoveryItem item = RequireDiscoveryItem(remoteItemId);
            return SetLiked(remoteItemId, !item.IsLiked);
        }

        public bool SetLiked(string remoteItemId, bool liked)
        {
            MockupDiscoveryItem item = RequireDiscoveryItem(remoteItemId);
            RequireOnline("Like a community list");
            if (item.IsLiked == liked)
                return item.IsLiked;

            item.IsLiked = liked;
            item.LikeCount = Math.Max(0, item.LikeCount + (liked ? 1 : -1));
            return item.IsLiked;
        }

        public MockupActionResult TryToggleLike(string remoteItemId)
        {
            MockupDiscoveryItem item = RequireDiscoveryItem(remoteItemId);
            if (!isOnline)
                return new MockupActionResult(false, "You need to be online to like a community list.",
                    discoveryItem: item);

            bool liked = SetLiked(remoteItemId, !item.IsLiked);
            return new MockupActionResult(true, liked ? "List liked." : "Like removed.",
                discoveryItem: item);
        }

        public MockupActionResult TryReportAndHide(string remoteItemId)
        {
            MockupDiscoveryItem item = RequireDiscoveryItem(remoteItemId);
            if (!isOnline)
                return new MockupActionResult(false, "You need to be online to send a report.",
                    discoveryItem: item);

            item.IsHiddenByCurrentUser = true;
            if (!item.IsReportedByCurrentUser)
            {
                item.IsReportedByCurrentUser = true;
                item.MockReportCount++;
                item.ReportStatus = MockupReportStatus.PendingReview;
            }

            return new MockupActionResult(true, "List hidden for you and report submitted for mock review.",
                discoveryItem: item);
        }

        public MockupActionResult HideForCurrentUser(string remoteItemId)
        {
            MockupDiscoveryItem item = RequireDiscoveryItem(remoteItemId);
            item.IsHiddenByCurrentUser = true;
            return new MockupActionResult(true, "List hidden from your community view.",
                discoveryItem: item);
        }

        public MockupActionResult UnhideForCurrentUser(string remoteItemId)
        {
            MockupDiscoveryItem item = RequireDiscoveryItem(remoteItemId);
            if (item.IsRemovedFromCommunity)
                return new MockupActionResult(false, "This list was removed from the community by mock review.",
                    discoveryItem: item);

            item.IsHiddenByCurrentUser = false;
            return new MockupActionResult(true, "List restored to your community view.",
                discoveryItem: item);
        }

        public MockupActionResult ResolveReport(string remoteItemId, bool removeFromCommunity)
        {
            RequireOnline("review a community report");
            MockupDiscoveryItem item = RequireDiscoveryItem(remoteItemId);
            if (item.ReportStatus != MockupReportStatus.PendingReview)
                throw new InvalidOperationException("This mock community item has no pending report to review.");

            if (removeFromCommunity)
            {
                item.IsRemovedFromCommunity = true;
                item.ReportStatus = MockupReportStatus.RemovedFromCommunity;
                return new MockupActionResult(true, "Developer mock review removed this list from the game community.",
                    discoveryItem: item);
            }

            item.ReportStatus = MockupReportStatus.Cleared;
            return new MockupActionResult(true, "Developer mock review cleared the report.",
                discoveryItem: item);
        }

        public MockupActionResult TryResolveReport(string remoteItemId, bool removeFromCommunity)
        {
            MockupDiscoveryItem item = RequireDiscoveryItem(remoteItemId);
            if (!isOnline)
                return new MockupActionResult(false, "You need to be online to review a community report.",
                    discoveryItem: item);
            return ResolveReport(remoteItemId, removeFromCommunity);
        }

        /// <summary>
        /// Adds a read-only community original. Downloading the same remote ID twice
        /// returns the existing entry and does not create a duplicate.
        /// </summary>
        public MockupWordList Download(string remoteItemId)
        {
            RequireOnline("Download a community list");
            MockupDiscoveryItem remote = RequireDiscoveryItem(remoteItemId);
            MockupWordList existing = libraryEntries.FirstOrDefault(item =>
                item.Ownership == MockupOwnership.CommunityDownloaded
                && item.RemoteItemId == remoteItemId);
            if (existing != null)
            {
                existing.HasOfflineCache = true;
                remote.IsDownloaded = true;
                return existing;
            }

            DateTime now = DateTime.UtcNow;
            var downloaded = new MockupWordList
            {
                Id = NextMockId(),
                Title = remote.Title,
                Description = remote.Description,
                Language = remote.Language,
                Words = new List<string>(remote.Words ?? SampleWords(remote.Language, remote.WordCount)),
                WordCount = remote.Words == null ? remote.WordCount : remote.Words.Count,
                AddedAt = now,
                ModifiedAt = now,
                ContentModifiedAt = now,
                Ownership = MockupOwnership.CommunityDownloaded,
                HasOfflineCache = true,
                RemoteItemId = remote.RemoteItemId,
                RemoteAuthor = remote.RemoteAuthor,
                DownloadStatus = MockupDownloadStatus.RecentlyAdded,
                PublicationStatus = MockupPublicationStatus.NotPublished
            };

            libraryEntries.Add(downloaded);
            remote.IsDownloaded = true;
            return downloaded;
        }

        public MockupActionResult TryDownload(string remoteItemId)
        {
            MockupDiscoveryItem item = RequireDiscoveryItem(remoteItemId);
            if (!isOnline)
                return new MockupActionResult(false, "You need to be online to download a community list.",
                    discoveryItem: item);

            MockupWordList downloaded = Download(remoteItemId);
            return new MockupActionResult(true, "List downloaded and available offline.",
                list: downloaded,
                discoveryItem: item);
        }

        public MockupPlayResult PlayDiscoveryItem(string remoteItemId)
        {
            MockupDiscoveryItem remote = RequireDiscoveryItem(remoteItemId);
            MockupWordList existing = libraryEntries.FirstOrDefault(item =>
                item.Ownership == MockupOwnership.CommunityDownloaded
                && item.RemoteItemId == remoteItemId);

            if (existing == null && !isOnline)
                return new MockupPlayResult(false, "This list is not cached. Connect to download it before playing.");
            if (existing != null && !existing.HasOfflineCache && !isOnline)
                return new MockupPlayResult(false, "This list is not cached. Connect to download it before playing.", existing);

            bool downloadedNow = false;
            if (existing == null)
            {
                existing = Download(remoteItemId);
                downloadedNow = true;
            }
            else if (!HasSameGameplayContent(existing, remote))
            {
                return new MockupPlayResult(false,
                    "This community item has different words under the same ID. The cached copy was kept; play it from Community Downloads.",
                    existing);
            }

            existing.HasOfflineCache = true;
            remote.IsDownloaded = true;
            string message = downloadedNow
                ? "List downloaded to Community Downloads. This is a mock play action; no game scene was launched."
                : "Cached list is ready in Community Downloads. This is a mock play action; no game scene was launched.";
            return new MockupPlayResult(true, message, existing, downloadedNow);
        }

        /// <summary>
        /// Starts the mock flow from an item already in My Lists. It does not query
        /// community visibility, so a hidden or removed community source remains
        /// playable from its local cached copy.
        /// </summary>
        public MockupPlayResult PlayLibraryItem(string mockId)
        {
            MockupWordList item = RequireList(mockId);
            if (item.Ownership == MockupOwnership.CommunityDownloaded && !item.HasOfflineCache)
            {
                if (!isOnline)
                    return new MockupPlayResult(false,
                        "This list is not cached. Connect to load it before playing.", item);

                MockupDiscoveryItem remote = FindDiscoveryItem(item.RemoteItemId);
                if (remote == null)
                    return new MockupPlayResult(false,
                        "The community list data is unavailable. Connect and try again.", item);

                if (!HasSameGameplayContent(item, remote))
                    return new MockupPlayResult(false,
                        "This community item has different words under the same ID. The local copy and records were kept.",
                        item);

                SyncCommunityMetadata(item, remote);
                item.HasOfflineCache = true;
            }

            return new MockupPlayResult(true,
                item.Ownership == MockupOwnership.CommunityDownloaded
                    ? "List from Community Downloads is ready. This is a mock play action; no game scene was launched."
                    : "List from My Lists is ready. This is a mock play action; no game scene was launched.",
                item);
        }

        public MockupActionResult TryUpdateDownloadedItem(string mockId)
        {
            MockupWordList item = RequireList(mockId);
            if (item.Ownership != MockupOwnership.CommunityDownloaded)
                throw new InvalidOperationException("Only a community download can be updated.");
            if (!isOnline)
                return new MockupActionResult(false, "You need to be online to check for community list updates.", item);

            MockupDiscoveryItem remote = RequireDiscoveryItem(item.RemoteItemId);
            if (!HasSameGameplayContent(item, remote))
                return new MockupActionResult(false,
                    "The community item changed its words without changing its ID. The local words and records were kept; content changes require a new community ID.",
                    item, remote);

            SyncCommunityMetadata(item, remote);
            item.HasOfflineCache = true;
            remote.IsDownloaded = true;
            return new MockupActionResult(true,
                "Community metadata was checked. The word content and personal records were unchanged.", item, remote);
        }

        /// <summary>Deletes a local list/cache only. It never changes a remote publication.</summary>
        public MockupActionResult TryDeleteList(string mockId)
        {
            MockupWordList item = FindById(mockId);
            if (item == null)
                return new MockupActionResult(false, "This local list no longer exists.");
            bool publicationIsActive = item.IsPubliclyVisible
                || item.PublicationStatus == MockupPublicationStatus.Published
                || item.PublicationStatus == MockupPublicationStatus.UpdateNeeded;
            if (item.Ownership == MockupOwnership.Owned && publicationIsActive)
                return new MockupActionResult(false,
                    "Make this published list private before deleting its local copy.", item);

            libraryEntries.Remove(item);
            if (item.Ownership == MockupOwnership.CommunityDownloaded)
            {
                MockupDiscoveryItem remote = FindDiscoveryItem(item.RemoteItemId);
                if (remote != null)
                    remote.IsDownloaded = libraryEntries.Any(candidate =>
                        candidate.Ownership == MockupOwnership.CommunityDownloaded
                        && candidate.RemoteItemId == item.RemoteItemId
                        && candidate.HasOfflineCache);
            }

            return new MockupActionResult(true, "Local list removed. No remote item was changed.", item);
        }

        public MockupWordList CreateList(string title, MockupLanguage language)
        {
            DateTime now = DateTime.UtcNow;
            var created = new MockupWordList
            {
                Id = NextMockId(),
                Title = string.IsNullOrWhiteSpace(title) ? "New List" : title.Trim(),
                Description = "",
                Tags = "",
                Language = language,
                Words = new List<string>(),
                WordCount = 0,
                AddedAt = now,
                ModifiedAt = now,
                ContentModifiedAt = now,
                Ownership = MockupOwnership.Owned,
                HasOfflineCache = true,
                DownloadStatus = MockupDownloadStatus.None,
                PublicationStatus = MockupPublicationStatus.NotPublished,
                ContentRevision = 1
            };
            libraryEntries.Add(created);
            return created;
        }

        public MockupWordList CreateList(string title, MockupLanguage language, IEnumerable<string> words)
        {
            List<string> normalizedWords = NormalizeWords(words);
            RequireAtLeastOneWord(normalizedWords);

            MockupWordList created = CreateList(title, language);
            created.Words = normalizedWords;
            created.WordCount = normalizedWords.Count;
            return created;
        }

        public bool CompleteRun(string mockId, int stars, float bestTimeSeconds)
        {
            MockupWordList item = RequireList(mockId);
            if (stars < 0 || stars > 5)
                throw new ArgumentOutOfRangeException(nameof(stars), "Mock star ratings must be from 0 to 5.");
            if (float.IsNaN(bestTimeSeconds) || float.IsInfinity(bestTimeSeconds) || bestTimeSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(bestTimeSeconds), "Mock run time must be a finite non-negative number.");

            DateTime now = DateTime.UtcNow;
            item.PlayCount++;
            item.LastPlayedAt = now;
            item.PersonalStars = item.PersonalStars.HasValue
                ? Math.Max(item.PersonalStars.Value, stars)
                : stars;
            item.BestTimeSeconds = item.BestTimeSeconds.HasValue
                ? Math.Min(item.BestTimeSeconds.Value, bestTimeSeconds)
                : bestTimeSeconds;

            completedRuns++;
            RefreshAchievementProgress();
            return true;
        }

        public MockupWordList ImportList(string title, MockupLanguage language, int wordCount)
        {
            MockupWordList imported = CreateList(title, language);
            imported.Words = SampleWords(language, wordCount);
            imported.WordCount = imported.Words.Count;
            return imported;
        }

        public MockupWordList ImportList(string title, MockupLanguage language, IEnumerable<string> words)
        {
            return CreateList(title, language, words);
        }

        /// <summary>
        /// Creates a local editable copy of a read-only community download. Remote
        /// author and download/publication bindings are intentionally not copied.
        /// </summary>
        public MockupWordList EditCommunityCopy(string mockId)
        {
            MockupWordList source = RequireList(mockId);
            if (source.Ownership != MockupOwnership.CommunityDownloaded)
                throw new InvalidOperationException("Only a community download can be copied with this action.");

            DateTime now = DateTime.UtcNow;
            var copy = new MockupWordList
            {
                Id = NextMockId(),
                Title = source.Title + " Copy",
                Description = source.Description,
                Tags = source.Tags,
                Language = source.Language,
                Words = new List<string>(source.Words ?? new List<string>()),
                WordCount = source.WordCount,
                AddedAt = now,
                ModifiedAt = now,
                ContentModifiedAt = now,
                Ownership = MockupOwnership.Owned,
                HasOfflineCache = true,
                DownloadStatus = MockupDownloadStatus.None,
                PublicationStatus = MockupPublicationStatus.NotPublished,
                ContentRevision = 1
            };

            libraryEntries.Add(copy);
            return copy;
        }

        public MockupEditResult SaveEdit(string mockId, MockupWordListEdit edit)
        {
            if (edit == null)
                throw new ArgumentNullException(nameof(edit));

            MockupWordList item = RequireList(mockId);
            if (item.IsReadOnly)
                throw new InvalidOperationException("Community downloads are read-only. Create a copy first.");

            List<string> nextWords = ResolveEditedWords(item, edit);
            bool contentChanged = item.Language != edit.Language
                || !WordsEqual(item.Words, nextWords);
            if (contentChanged)
                RequireAtLeastOneWord(nextWords);

            string nextTitle = string.IsNullOrWhiteSpace(edit.Title) ? item.Title : edit.Title.Trim();
            string nextDescription = edit.Description ?? item.Description;
            string nextTags = edit.Tags ?? item.Tags;
            bool metadataChanged = nextTitle != item.Title
                || nextDescription != item.Description
                || nextTags != item.Tags;

            if (contentChanged && !string.IsNullOrEmpty(item.RemoteItemId))
                throw new InvalidOperationException(
                    "Published gameplay content is fixed. Create a new content version instead of editing this item.");

            item.Title = nextTitle;
            item.Description = nextDescription;
            item.Tags = nextTags;
            if (metadataChanged || contentChanged)
                item.ModifiedAt = DateTime.UtcNow;

            if (contentChanged)
            {
                item.ArchiveCurrentRecord();
                item.Language = edit.Language;
                item.Words = nextWords;
                item.WordCount = nextWords.Count;
                item.ContentRevision++;
                item.ContentModifiedAt = item.ModifiedAt;
                item.PersonalStars = null;
                item.BestTimeSeconds = null;
            }

            // Metadata updates can require a publication refresh but never affect records.
            if ((metadataChanged || contentChanged) && !string.IsNullOrEmpty(item.RemoteItemId))
                item.PublicationStatus = MockupPublicationStatus.UpdateNeeded;

            return new MockupEditResult(item, contentChanged);
        }

        public MockupEditResult SavePublishedContentAsNewList(string mockId, MockupWordListEdit edit)
        {
            if (edit == null)
                throw new ArgumentNullException(nameof(edit));

            MockupWordList source = RequireList(mockId);
            if (source.Ownership != MockupOwnership.Owned || string.IsNullOrEmpty(source.RemoteItemId))
                throw new InvalidOperationException("Only a previously published owned list can start a new content version.");

            List<string> nextWords = ResolveEditedWords(source, edit);
            bool contentChanged = source.Language != edit.Language
                || !WordsEqual(source.Words, nextWords);
            if (!contentChanged)
                throw new InvalidOperationException("A new published version needs a gameplay-content change.");
            RequireAtLeastOneWord(nextWords);

            DateTime now = DateTime.UtcNow;
            var version = new MockupWordList
            {
                Id = NextMockId(),
                Title = string.IsNullOrWhiteSpace(edit.Title) ? source.Title : edit.Title.Trim(),
                Description = edit.Description ?? source.Description,
                Tags = edit.Tags ?? source.Tags,
                Language = edit.Language,
                Words = nextWords,
                WordCount = nextWords.Count,
                AddedAt = now,
                ModifiedAt = now,
                ContentModifiedAt = now,
                Ownership = MockupOwnership.Owned,
                HasOfflineCache = true,
                DownloadStatus = MockupDownloadStatus.None,
                PublicationStatus = MockupPublicationStatus.NotPublished,
                ContentRevision = 1
            };

            libraryEntries.Add(version);
            return new MockupEditResult(version, true, true);
        }

        /// <summary>Moves the same owned item into My Published; it never duplicates it.</summary>
        public MockupWordList Publish(string mockId)
        {
            RequireOnline("Publish a community list");
            MockupWordList item = RequireList(mockId);
            if (item.Ownership != MockupOwnership.Owned)
                throw new InvalidOperationException("Only an owned list can be published.");

            if (item.PublicationStatus == MockupPublicationStatus.NotPublished)
                item.RemoteItemId = "mock-published-" + (nextPublishedNumber++).ToString("D2");

            item.PublicationStatus = MockupPublicationStatus.Published;
            item.IsPubliclyVisible = true;
            return item;
        }

        public MockupActionResult TryPublish(string mockId)
        {
            MockupWordList item = RequireList(mockId);
            if (!isOnline)
                return new MockupActionResult(false, "You need to be online to publish a list.", item);
            return new MockupActionResult(true, "List published publicly in the mock store.", Publish(mockId));
        }

        /// <summary>Updates the publication binding on the same item and keeps it in My Published.</summary>
        public MockupWordList UpdatePublication(string mockId)
        {
            RequireOnline("Update a published list");
            MockupWordList item = RequireList(mockId);
            if (item.Ownership != MockupOwnership.Owned
                || item.PublicationStatus == MockupPublicationStatus.NotPublished)
                throw new InvalidOperationException("Only an already published owned list can be updated.");

            item.PublicationStatus = item.IsPubliclyVisible
                ? MockupPublicationStatus.Published
                : MockupPublicationStatus.Unpublished;
            return item;
        }

        public MockupActionResult TryUpdatePublication(string mockId)
        {
            MockupWordList item = RequireList(mockId);
            if (!isOnline)
                return new MockupActionResult(false, "You need to be online to update a published list.", item);
            return new MockupActionResult(true, "Published metadata update completed in the mock store.",
                UpdatePublication(mockId));
        }

        public MockupWordList Unpublish(string mockId)
        {
            RequireOnline("Make a published list private");
            MockupWordList item = RequireList(mockId);
            if (item.Ownership != MockupOwnership.Owned || string.IsNullOrEmpty(item.RemoteItemId))
                throw new InvalidOperationException("Only a previously published owned list can be made private.");

            item.IsPubliclyVisible = false;
            item.PublicationStatus = MockupPublicationStatus.Unpublished;
            return item;
        }

        public MockupActionResult TryUnpublish(string mockId)
        {
            MockupWordList item = RequireList(mockId);
            if (!isOnline)
                return new MockupActionResult(false, "You need to be online to make a published list private.", item);
            return new MockupActionResult(true, "List made private in the mock store.", Unpublish(mockId));
        }

        private void SeedLibraryFixtures()
        {
            libraryEntries.Add(CreateFixture(
                "mock-list-everyday-english", "Everyday English", "Short daily words.",
                MockupLanguage.English, 12, MockupOwnership.Owned, "2026-10-20", "2026-10-21",
                "2026-10-31", 3, 58f, true));
            libraryEntries.Add(CreateFixture(
                "mock-list-shop-signs", "Shop Signs", "Common signs and labels.",
                MockupLanguage.English, 18, MockupOwnership.Owned, "2026-10-28", "2026-10-29",
                null, null, null, false));
            libraryEntries.Add(CreateFixture(
                "mock-list-hsk-starter", "HSK Starter", "A few beginner Chinese words.",
                MockupLanguage.Chinese, 10, MockupOwnership.Owned, "2026-10-29", "2026-10-30",
                "2026-10-30", 2, 130f, true));
            libraryEntries.Add(CreateFixture(
                "mock-list-travel-copy", "Travel Phrases", "A personal editable copy.",
                MockupLanguage.Mixed, 16, MockupOwnership.Owned, "2026-10-30", "2026-10-31",
                null, null, null, false));
            libraryEntries.Add(CreateFixture(
                "mock-list-long-mixed", "城市通勤 / City Commute: Long Mixed Practice Collection",
                "A longer mixed-language list for buses, trains, transfers and directions. "
                    + "包含英文站名、中文问路短句、换乘提示和常见出行词语。",
                MockupLanguage.Mixed, 72, MockupOwnership.Owned, "2026-09-18", "2026-10-30",
                "2026-10-30", 5, 44f, true));
            libraryEntries.Add(CreateFixture(
                "mock-list-empty", "Blank Canvas", "An empty list ready for new words.",
                MockupLanguage.English, 0, MockupOwnership.Owned, "2026-10-31", "2026-10-31",
                null, null, null, false));

            libraryEntries.Add(CreateFixture(
                "mock-list-night-market", "Night Market Words", "Signs from a night market.",
                MockupLanguage.Chinese, 20, MockupOwnership.CommunityDownloaded, "2026-10-31", "2026-10-29",
                null, null, null, true, "mock-ugc-night-market", "MapleWords",
                MockupDownloadStatus.RecentlyAdded));
            libraryEntries.Add(CreateFixture(
                "mock-list-cafe-abroad", "Café Abroad", "Mixed phrases for ordering food.",
                MockupLanguage.Mixed, 16, MockupOwnership.CommunityDownloaded, "2026-10-24", "2026-10-30",
                "2026-10-31", 3, 72f, false, "mock-ugc-cafe-abroad", "WanderWords",
                MockupDownloadStatus.None));
            libraryEntries.Add(CreateFixture(
                "mock-list-park-trails", "Park Trails", "Trail signs and directions.",
                MockupLanguage.English, 9, MockupOwnership.CommunityDownloaded, "2026-10-21", "2026-10-20",
                "2026-10-27", 1, 151f, true, "mock-ugc-park-trails", "TrailType",
                MockupDownloadStatus.None));

            libraryEntries.Add(CreateFixture(
                "mock-list-punctuation", "Punctuation Practice", "Short punctuation drills.",
                MockupLanguage.English, 11, MockupOwnership.Owned, "2026-10-20", "2026-10-20",
                "2026-10-22", 2, 108f, false, "mock-published-punctuation", null, MockupDownloadStatus.None,
                MockupPublicationStatus.Published));
            libraryEntries.Add(CreateFixture(
                "mock-list-kitchen-mix", "Kitchen Mix", "Kitchen vocabulary in two languages.",
                MockupLanguage.Mixed, 18, MockupOwnership.Owned, "2026-10-25", "2026-10-31",
                "2026-10-31", 3, 65f, true, "mock-published-kitchen", null,
                MockupDownloadStatus.None, MockupPublicationStatus.UpdateNeeded));
            libraryEntries.Add(CreateFixture(
                "mock-list-mini-hsk", "Mini HSK", "A compact Chinese practice list.",
                MockupLanguage.Chinese, 8, MockupOwnership.Owned, "2026-10-30", "2026-10-30",
                null, null, null, false, "mock-published-hsk", null,
                MockupDownloadStatus.None, MockupPublicationStatus.Published));
        }

        private void SeedDiscoveryFixtures()
        {
            discoveryItems.Add(CreateDiscovery(
                "mock-ugc-night-market", "Night Market Words", "Signs from a night market.",
                MockupLanguage.Chinese, 20, "MapleWords", MockupDiscoveryStatus.New, 24, true));
            discoveryItems.Add(CreateDiscovery(
                "mock-ugc-cafe-abroad", "Café Abroad", "Mixed phrases for ordering food.",
                MockupLanguage.Mixed, 16, "WanderWords", MockupDiscoveryStatus.New, 17, true));
            discoveryItems.Add(CreateDiscovery(
                "mock-ugc-weekend-garden", "Weekend Garden", "Garden tools and plants.",
                MockupLanguage.English, 10, "GreenType", MockupDiscoveryStatus.New, 12, false));
            discoveryItems.Add(CreateDiscovery(
                "mock-ugc-airport-mix", "Airport Mix", "Airport phrases in English and Chinese.",
                MockupLanguage.Mixed, 13, "GateWords", MockupDiscoveryStatus.Updated, 31, false));
        }

        private void SeedAchievementFixtures()
        {
            achievements.Add(new MockupAchievement(
                "COMPLETE_FIRST_WORD",
                "Complete Your First Word",
                "Finish a word phase.",
                MockupAchievementStatus.Unlocked,
                "1 / 1"));
            achievements.Add(new MockupAchievement(
                "COMPLETE_TWO_WORDS",
                "Complete Two Words",
                "Finish two word phases.",
                MockupAchievementStatus.InProgress,
                "1 / 2"));
            achievements.Add(new MockupAchievement(
                "COMPLETE_WORD_WITH_D",
                "Complete a Word with D",
                "Finish a word whose typing target contains D.",
                MockupAchievementStatus.Locked,
                "Locked"));
        }

        private MockupWordList CreateFixture(
            string id,
            string title,
            string description,
            MockupLanguage language,
            int wordCount,
            MockupOwnership ownership,
            string addedDate,
            string modifiedDate,
            string lastPlayedDate,
            int? stars,
            float? bestTime,
            bool favorite,
            string remoteItemId = null,
            string remoteAuthor = null,
            MockupDownloadStatus downloadStatus = MockupDownloadStatus.None,
            MockupPublicationStatus publicationStatus = MockupPublicationStatus.NotPublished)
        {
            DateTime added = FixtureDate(addedDate, 9);
            DateTime modified = FixtureDate(modifiedDate, 12);
            return new MockupWordList
            {
                Id = id,
                Title = title,
                Description = description,
                Language = language,
                Words = SampleWords(language, wordCount),
                WordCount = wordCount,
                AddedAt = added,
                ModifiedAt = modified,
                ContentModifiedAt = modified,
                LastPlayedAt = lastPlayedDate == null ? (DateTime?)null : FixtureDate(lastPlayedDate, 15),
                PersonalStars = stars,
                BestTimeSeconds = bestTime,
                Ownership = ownership,
                HasOfflineCache = true,
                RemoteItemId = remoteItemId,
                RemoteAuthor = remoteAuthor,
                IsFavorite = favorite,
                DownloadStatus = downloadStatus,
                PublicationStatus = publicationStatus,
                IsPubliclyVisible = publicationStatus == MockupPublicationStatus.Published
                    || publicationStatus == MockupPublicationStatus.UpdateNeeded,
                PlayCount = lastPlayedDate == null ? 0 : 1,
                ContentRevision = 1
            };
        }

        private static MockupDiscoveryItem CreateDiscovery(
            string remoteItemId,
            string title,
            string description,
            MockupLanguage language,
            int wordCount,
            string remoteAuthor,
            MockupDiscoveryStatus status,
            int likeCount,
            bool isDownloaded)
        {
            return new MockupDiscoveryItem
            {
                RemoteItemId = remoteItemId,
                Title = title,
                Description = description,
                Language = language,
                Words = SampleWords(language, wordCount),
                WordCount = Math.Max(0, wordCount),
                RemoteAuthor = remoteAuthor,
                Status = status,
                LikeCount = likeCount,
                IsLiked = false,
                IsDownloaded = isDownloaded,
                IsHiddenByCurrentUser = false,
                IsReportedByCurrentUser = false
            };
        }

        private static bool HasSameGameplayContent(MockupWordList item, MockupDiscoveryItem remote)
        {
            return item.Language == remote.Language && WordsEqual(item.Words, remote.Words);
        }

        private static void SyncCommunityMetadata(MockupWordList item, MockupDiscoveryItem remote)
        {
            DateTime now = DateTime.UtcNow;
            item.Title = remote.Title;
            item.Description = remote.Description;
            item.RemoteAuthor = remote.RemoteAuthor;
            item.ModifiedAt = now;
        }

        private void RefreshAchievementProgress()
        {
            MockupAchievement first = achievements.FirstOrDefault(item => item.Id == "COMPLETE_FIRST_WORD");
            if (first != null)
            {
                first.Status = MockupAchievementStatus.Unlocked;
                first.ProgressText = "1 / 1";
            }

            MockupAchievement two = achievements.FirstOrDefault(item => item.Id == "COMPLETE_TWO_WORDS");
            if (two != null)
            {
                int progress = Math.Min(2, completedRuns);
                two.Status = progress >= 2 ? MockupAchievementStatus.Unlocked : MockupAchievementStatus.InProgress;
                two.ProgressText = progress.ToString(CultureInfo.InvariantCulture) + " / 2";
            }
        }

        private void RequireOnline(string action)
        {
            if (!isOnline)
                throw new InvalidOperationException("Cannot " + action + " while offline.");
        }

        private IEnumerable<MockupWordList> Sort(
            IEnumerable<MockupWordList> source,
            MockupSortMode sortMode,
            MockupSortDirection direction)
        {
            bool ascending = direction == MockupSortDirection.Ascending;
            List<MockupWordList> ordered;

            switch (sortMode)
            {
                case MockupSortMode.Name:
                    ordered = source.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
                    break;
                case MockupSortMode.Added:
                    ordered = source.OrderBy(item => item.AddedAt).ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
                    break;
                case MockupSortMode.Modified:
                    ordered = source.OrderBy(item => item.ModifiedAt).ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
                    break;
                case MockupSortMode.WordCount:
                    ordered = source.OrderBy(item => item.WordCount).ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
                    break;
                case MockupSortMode.PersonalStars:
                    ordered = source.Where(item => item.PersonalStars.HasValue)
                        .OrderBy(item => item.PersonalStars.Value)
                        .ThenBy(item => item.Id, StringComparer.Ordinal)
                        .Concat(source.Where(item => !item.PersonalStars.HasValue)
                            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(item => item.Id, StringComparer.Ordinal))
                        .ToList();
                    break;
                default:
                    ordered = source.Where(item => item.LastPlayedAt.HasValue)
                        .OrderBy(item => item.LastPlayedAt.Value)
                        .ThenBy(item => item.Id, StringComparer.Ordinal)
                        .Concat(source.Where(item => !item.LastPlayedAt.HasValue)
                            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(item => item.Id, StringComparer.Ordinal))
                        .ToList();
                    break;
            }

            if (!ascending)
                ordered.Reverse();

            // Null scores and unplayed entries stay at the end in either direction.
            if (sortMode == MockupSortMode.PersonalStars || sortMode == MockupSortMode.RecentlyPlayed)
            {
                IEnumerable<MockupWordList> scored = sortMode == MockupSortMode.PersonalStars
                    ? ordered.Where(item => item.PersonalStars.HasValue)
                    : ordered.Where(item => item.LastPlayedAt.HasValue);
                IEnumerable<MockupWordList> empty = sortMode == MockupSortMode.PersonalStars
                    ? ordered.Where(item => !item.PersonalStars.HasValue)
                    : ordered.Where(item => !item.LastPlayedAt.HasValue);
                ordered = scored.Concat(empty).ToList();
            }

            return ordered;
        }

        private MockupWordList RequireList(string mockId)
        {
            MockupWordList item = FindById(mockId);
            if (item == null)
                throw new ArgumentException("Unknown mock list ID: " + mockId, nameof(mockId));
            return item;
        }

        private MockupDiscoveryItem RequireDiscoveryItem(string remoteItemId)
        {
            MockupDiscoveryItem item = FindDiscoveryItem(remoteItemId);
            if (item == null)
                throw new ArgumentException("Unknown mock discovery ID: " + remoteItemId, nameof(remoteItemId));
            return item;
        }

        private string NextMockId()
        {
            return "mock-list-created-" + (nextMockListNumber++).ToString("D2");
        }

        private static bool Contains(string value, string term)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<string> NormalizeWords(IEnumerable<string> words)
        {
            if (words == null)
                return new List<string>();

            return words.Select(word => word == null ? "" : word.Trim())
                .Where(word => word.Length > 0)
                .ToList();
        }

        private static void RequireAtLeastOneWord(IReadOnlyCollection<string> words)
        {
            if (words == null || words.Count == 0)
                throw new ArgumentException("Enter at least one non-empty word.", nameof(words));
        }

        private static List<string> ResolveEditedWords(MockupWordList item, MockupWordListEdit edit)
        {
            List<string> currentWords = item.Words ?? new List<string>();
            if (edit.Words != null)
            {
                List<string> normalized = NormalizeWords(edit.Words);
                if (!WordsEqual(currentWords, normalized))
                {
                    RequireAtLeastOneWord(normalized);
                    return normalized;
                }

                if (Math.Max(0, edit.WordCount) == currentWords.Count)
                    return new List<string>(currentWords);
            }

            // Compatibility path for older count-only callers: preserve unchanged
            // content, or materialize an explicit sample sequence for a count change.
            int requestedCount = Math.Max(0, edit.WordCount);
            if (requestedCount == currentWords.Count)
                return new List<string>(currentWords);

            List<string> samples = SampleWords(edit.Language, requestedCount);
            RequireAtLeastOneWord(samples);
            return samples;
        }

        private static bool WordsEqual(IEnumerable<string> left, IEnumerable<string> right)
        {
            return (left ?? Enumerable.Empty<string>()).SequenceEqual(
                right ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        }

        private static List<string> SampleWords(MockupLanguage language, int requestedCount)
        {
            string[] samples;
            switch (language)
            {
                case MockupLanguage.Chinese:
                    samples = ChineseSampleWords;
                    break;
                case MockupLanguage.Mixed:
                    samples = MixedSampleWords;
                    break;
                default:
                    samples = EnglishSampleWords;
                    break;
            }

            int count = Math.Max(0, requestedCount);
            var result = new List<string>(count);
            for (int index = 0; index < count; index++)
            {
                string word = samples[index % samples.Length];
                int cycle = index / samples.Length;
                if (cycle > 0)
                    word += " (sample " + (cycle + 1).ToString(CultureInfo.InvariantCulture) + ")";
                result.Add(word);
            }

            return result;
        }

        private static readonly string[] EnglishSampleWords =
        {
            "apple", "garden", "seed", "soil", "water", "leaf", "flower", "window",
            "morning", "ticket", "station", "platform", "transfer", "market", "receipt",
            "borrow", "return", "quiet", "bright", "neighbour", "weather", "umbrella",
            "shelf", "basket", "corner", "choose", "fresh", "closed", "entrance", "exit",
            "left", "right"
        };

        private static readonly string[] ChineseSampleWords =
        {
            "苹果", "花园", "种子", "土壤", "浇水", "叶子", "花朵", "窗户", "早上", "车票",
            "车站", "站台", "换乘", "市场", "收据", "借用", "归还", "安静", "明亮", "邻居",
            "天气", "雨伞", "架子", "篮子", "街角", "选择", "新鲜", "关门", "入口", "出口",
            "左边", "右边"
        };

        private static readonly string[] MixedSampleWords =
        {
            "apple — 苹果", "garden — 花园", "seed — 种子", "soil — 土壤", "water — 浇水",
            "leaf — 叶子", "flower — 花朵", "window — 窗户", "morning — 早上", "ticket — 车票",
            "station — 车站", "platform — 站台", "transfer — 换乘", "market — 市场",
            "receipt — 收据", "borrow — 借用", "return — 归还", "quiet — 安静", "bright — 明亮",
            "neighbour — 邻居", "weather — 天气", "umbrella — 雨伞", "shelf — 架子",
            "basket — 篮子", "corner — 街角", "choose — 选择", "fresh — 新鲜", "closed — 关门",
            "entrance — 入口", "exit — 出口", "left — 左边", "right — 右边",
            "street — 街道", "crosswalk — 斑马线", "bridge — 桥", "river — 河", "bus — 公交车",
            "train — 火车", "subway — 地铁", "schedule — 时刻表", "delay — 延误", "direction — 方向",
            "map — 地图", "nearby — 附近", "straight — 直走", "turn — 转弯", "next station — 下一站",
            "arrival — 到达", "departure — 出发", "luggage — 行李", "passport — 护照", "gate — 登机口",
            "boarding pass — 登机牌", "information desk — 问询台", "restroom — 洗手间", "snack — 小吃",
            "spicy — 辣", "sweet — 甜", "vegetarian — 素食", "menu — 菜单", "order — 点餐",
            "bill — 账单", "cash — 现金", "bank card — 银行卡", "change — 找零", "queue — 排队",
            "available — 有空", "sold out — 售罄", "repair — 修理", "lost and found — 失物招领",
            "emergency — 紧急情况", "pharmacy — 药店", "hospital — 医院", "medicine — 药",
            "headache — 头痛", "appointment — 预约", "address — 地址", "phone number — 电话号码",
            "internet — 网络", "password — 密码", "battery — 电池", "charger — 充电器", "key — 钥匙",
            "elevator — 电梯", "stairs — 楼梯", "floor — 楼层", "apartment — 公寓", "help — 帮助"
        };

        private static DateTime FixtureDate(string isoDate, int hour)
        {
            DateTime date = DateTime.ParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return DateTime.SpecifyKind(date.AddHours(hour), DateTimeKind.Utc);
        }
    }
}
