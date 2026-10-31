using System;
using System.Collections.Generic;

namespace Words.UiMockups.WordListHub
{
    public enum MockupLanguage
    {
        English,
        Chinese,
        Mixed
    }

    public enum MockupLibraryGroup
    {
        MyLists,
        CommunityDownloads,
        MyPublished
    }

    public enum MockupLibraryView
    {
        All,
        Favorites,
        MyLists,
        CommunityDownloads,
        MyPublished
    }

    public enum MockupLanguageFilter
    {
        All,
        English,
        Chinese,
        Mixed
    }

    public enum MockupPlayedFilter
    {
        All,
        Played,
        Unplayed
    }

    public enum MockupSortMode
    {
        RecentlyPlayed,
        Name,
        Added,
        Modified,
        WordCount,
        PersonalStars
    }

    public enum MockupSortDirection
    {
        Ascending,
        Descending
    }

    public enum MockupOwnership
    {
        Owned,
        CommunityDownloaded
    }

    public enum MockupDownloadStatus
    {
        None,
        RecentlyAdded
    }

    public enum MockupPublicationStatus
    {
        NotPublished,
        Published
    }

    public enum MockupDiscoveryStatus
    {
        New,
        Updated
    }

    public enum MockupReportStatus
    {
        None,
        PendingReview,
        Cleared,
        RemovedFromCommunity
    }

    public enum MockupAchievementStatus
    {
        Locked,
        InProgress,
        Unlocked
    }

    /// <summary>
    /// A disposable, in-memory list card for the WordListHub mockup. Its short ID is
    /// deliberately unrelated to production wordlist UUIDs.
    /// </summary>
    public sealed class MockupWordList
    {
        private readonly List<MockupPreviousRecordSummary> previousContentRecords =
            new List<MockupPreviousRecordSummary>();

        public string Id { get; internal set; }
        public string Title { get; internal set; }
        public string Description { get; internal set; }
        public string Tags { get; internal set; }
        public MockupLanguage Language { get; internal set; }
        public List<string> Words { get; internal set; }
        public int WordCount { get; internal set; }
        public bool IsFavorite { get; internal set; }
        public DateTime AddedAt { get; internal set; }
        public DateTime ModifiedAt { get; internal set; }
        public DateTime ContentModifiedAt { get; internal set; }
        public DateTime? LastPlayedAt { get; internal set; }
        public int? PersonalStars { get; internal set; }
        public float? BestTimeSeconds { get; internal set; }
        public int ContentRevision { get; internal set; }
        public int PlayCount { get; internal set; }
        public MockupOwnership Ownership { get; internal set; }
        public bool HasOfflineCache { get; internal set; }
        public bool IsPubliclyVisible { get; internal set; }
        public string RemoteItemId { get; internal set; }
        public string RemoteAuthor { get; internal set; }
        public MockupDownloadStatus DownloadStatus { get; internal set; }
        public MockupPublicationStatus PublicationStatus { get; internal set; }

        public MockupLibraryGroup Group
        {
            get
            {
                if (Ownership == MockupOwnership.CommunityDownloaded)
                    return MockupLibraryGroup.CommunityDownloads;
                return PublicationStatus == MockupPublicationStatus.NotPublished
                    ? MockupLibraryGroup.MyLists
                    : MockupLibraryGroup.MyPublished;
            }
        }

        public bool IsReadOnly => Ownership == MockupOwnership.CommunityDownloaded;
        public bool HasBeenPlayed => LastPlayedAt.HasValue;
        public IReadOnlyList<MockupPreviousRecordSummary> PreviousContentRecords =>
            previousContentRecords.AsReadOnly();

        internal void ArchiveCurrentRecord()
        {
            if (!PersonalStars.HasValue && !BestTimeSeconds.HasValue)
                return;

            previousContentRecords.Add(new MockupPreviousRecordSummary(
                ContentRevision,
                PersonalStars,
                BestTimeSeconds));

            // Keep the mock bounded if a reviewer edits the sample repeatedly.
            if (previousContentRecords.Count > 5)
                previousContentRecords.RemoveAt(0);
        }
    }

    public sealed class MockupPreviousRecordSummary
    {
        public int ContentRevision { get; }
        public int? PersonalStars { get; }
        public float? BestTimeSeconds { get; }

        internal MockupPreviousRecordSummary(int contentRevision, int? personalStars, float? bestTimeSeconds)
        {
            ContentRevision = contentRevision;
            PersonalStars = personalStars;
            BestTimeSeconds = bestTimeSeconds;
        }
    }

    public sealed class MockupDiscoveryItem
    {
        public string RemoteItemId { get; internal set; }
        public string Title { get; internal set; }
        public string Description { get; internal set; }
        public MockupLanguage Language { get; internal set; }
        public List<string> Words { get; internal set; }
        public int WordCount { get; internal set; }
        public string RemoteAuthor { get; internal set; }
        public MockupDiscoveryStatus Status { get; internal set; }
        public int LikeCount { get; internal set; }
        public bool IsLiked { get; internal set; }
        public bool IsDownloaded { get; internal set; }
        public bool IsHiddenByCurrentUser { get; internal set; }
        public bool IsReportedByCurrentUser { get; internal set; }
        public int MockReportCount { get; internal set; }
        public MockupReportStatus ReportStatus { get; internal set; }
        public bool IsRemovedFromCommunity { get; internal set; }
    }

    public sealed class MockupAchievement
    {
        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public MockupAchievementStatus Status { get; internal set; }
        public string ProgressText { get; internal set; }

        internal MockupAchievement(
            string id,
            string title,
            string description,
            MockupAchievementStatus status,
            string progressText)
        {
            Id = id;
            Title = title;
            Description = description;
            Status = status;
            ProgressText = progressText;
        }
    }

    public sealed class MockupLibraryQuery
    {
        public MockupLibraryView View { get; set; } = MockupLibraryView.All;
        public string SearchText { get; set; } = "";
        public MockupLanguageFilter LanguageFilter { get; set; } = MockupLanguageFilter.All;
        public MockupPlayedFilter PlayedFilter { get; set; } = MockupPlayedFilter.All;
        public MockupSortMode SortMode { get; set; } = MockupSortMode.RecentlyPlayed;
        public MockupSortDirection SortDirection { get; set; } = MockupSortDirection.Descending;
    }

    public sealed class MockupWordListEdit
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public List<string> Words { get; set; }
        public MockupLanguage Language { get; set; }
        public int WordCount { get; set; }

        /// <summary>
        /// Retained for older mock UI callers. Content changes are derived from the
        /// ordinal Words sequence and language instead of this flag.
        /// </summary>
        public bool GameplayContentChanged { get; set; }
        public string Tags { get; set; }
    }

    public sealed class MockupEditResult
    {
        public MockupWordList Item { get; }
        public bool ContentChanged { get; }
        public bool ShowRecordWarning { get; }
        public bool CreatedNewList { get; }

        internal MockupEditResult(MockupWordList item, bool contentChanged, bool createdNewList = false)
        {
            Item = item;
            ContentChanged = contentChanged;
            ShowRecordWarning = contentChanged;
            CreatedNewList = createdNewList;
        }
    }

    public class MockupActionResult
    {
        public bool Succeeded { get; }
        public string Message { get; }
        public MockupWordList List { get; }
        public MockupDiscoveryItem DiscoveryItem { get; }

        internal MockupActionResult(
            bool succeeded,
            string message,
            MockupWordList list = null,
            MockupDiscoveryItem discoveryItem = null)
        {
            Succeeded = succeeded;
            Message = message;
            List = list;
            DiscoveryItem = discoveryItem;
        }
    }

    public sealed class MockupPlayResult : MockupActionResult
    {
        public bool DownloadedNow { get; }

        internal MockupPlayResult(
            bool succeeded,
            string message,
            MockupWordList list = null,
            bool downloadedNow = false)
            : base(succeeded, message, list)
        {
            DownloadedNow = downloadedNow;
        }
    }
}
