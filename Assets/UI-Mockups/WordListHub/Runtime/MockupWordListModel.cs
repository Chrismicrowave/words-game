using System;
using System.Collections.Generic;
using System.Text;

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

    public enum MockupDiscoveryDownloadFilter
    {
        All,
        Downloaded,
        NotDownloaded
    }

    public enum MockupDiscoverySortMode
    {
        Default,
        Name,
        Likes,
        WordCount
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
        private MockupLanguage? detectedLanguageOverride;

        public string Id { get; internal set; }
        public string Title { get; internal set; }
        public string Description { get; internal set; }
        public string Tags { get; internal set; }
        public MockupLanguage Language { get; internal set; }
        public MockupLanguage DetectedLanguage
        {
            get => detectedLanguageOverride ?? Language;
            internal set => detectedLanguageOverride = value;
        }
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
        public bool IsChallenge { get; internal set; }
        public bool IsChallengeUnlocked { get; internal set; } = true;
        public string UnlockRewardLabel { get; internal set; }

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

        public bool IsReadOnly => Ownership == MockupOwnership.CommunityDownloaded || IsChallenge;
        public bool HasPublishedBinding => Ownership == MockupOwnership.Owned
            && !string.IsNullOrEmpty(RemoteItemId);
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

        internal void AddPreviousRecord(int contentRevision, int? personalStars, float? bestTimeSeconds)
        {
            previousContentRecords.Add(new MockupPreviousRecordSummary(
                contentRevision, personalStars, bestTimeSeconds));
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

    public sealed class MockupDiscoveryQuery
    {
        public string SearchText { get; set; } = "";
        public MockupLanguageFilter LanguageFilter { get; set; } = MockupLanguageFilter.All;
        public MockupDiscoveryDownloadFilter DownloadFilter { get; set; } = MockupDiscoveryDownloadFilter.All;
        public MockupDiscoverySortMode SortMode { get; set; } = MockupDiscoverySortMode.Default;
        public MockupSortDirection SortDirection { get; set; } = MockupSortDirection.Ascending;
    }

    /// <summary>
    /// Ordered, local draft state for the edit-list word picker. Empty entries are
    /// kept while editing so Add and Delete remain explicit user actions.
    /// </summary>
    public sealed class MockupWordListEditSession
    {
        private readonly List<string> words = new List<string>();

        public IReadOnlyList<string> Words => words;
        public int SelectedIndex { get; private set; } = -1;
        public bool HasSelection => SelectedIndex >= 0 && SelectedIndex < words.Count;
        public string SelectedWord => HasSelection ? words[SelectedIndex] : string.Empty;
        public bool CanMoveUp => HasSelection && SelectedIndex > 0;
        public bool CanMoveDown => HasSelection && SelectedIndex < words.Count - 1;

        public MockupWordListEditSession(IEnumerable<string> initialWords)
        {
            if (initialWords != null)
            {
                foreach (string word in initialWords)
                    words.Add(word ?? string.Empty);
            }

            SelectedIndex = words.Count == 0 ? -1 : 0;
        }

        public List<string> ToList()
        {
            return new List<string>(words);
        }

        public void Select(int index)
        {
            SelectedIndex = words.Count == 0
                ? -1
                : Math.Max(0, Math.Min(index, words.Count - 1));
        }

        public void SetSelectedWord(string value)
        {
            if (HasSelection)
                words[SelectedIndex] = value ?? string.Empty;
        }

        public void AddAtFront()
        {
            words.Insert(0, string.Empty);
            SelectedIndex = 0;
        }

        public bool MoveSelected(int direction)
        {
            if (!HasSelection || direction == 0)
                return false;

            int targetIndex = SelectedIndex + Math.Sign(direction);
            if (targetIndex < 0 || targetIndex >= words.Count)
                return false;

            string movedWord = words[SelectedIndex];
            words[SelectedIndex] = words[targetIndex];
            words[targetIndex] = movedWord;
            SelectedIndex = targetIndex;
            return true;
        }

        public bool DeleteSelected()
        {
            if (!HasSelection)
                return false;

            int deletedIndex = SelectedIndex;
            words.RemoveAt(deletedIndex);
            SelectedIndex = words.Count == 0 ? -1 : Math.Min(deletedIndex, words.Count - 1);
            return true;
        }
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

    /// <summary>
    /// Parses the mockup's optional inline pronunciation annotation. Chinese syllables
    /// follow the display text in source-character order: "苹果 | ping guo".
    /// </summary>
    public static class MockupWordContent
    {
        private const char PinyinSeparator = '|';
        private const string MissingPinyinLabel = " (pinyin not set)";

        public static bool TryValidateWordText(
            string text,
            IEnumerable<string> unchangedLegacyWords,
            out string error)
        {
            if (string.IsNullOrEmpty(text))
                return TryValidateWordLines(null, unchangedLegacyWords, out error);

            string normalizedLineEndings = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return TryValidateWordLines(
                normalizedLineEndings.Split(new[] { '\n' }, StringSplitOptions.None),
                unchangedLegacyWords,
                out error);
        }

        public static bool TryValidateWordLines(
            IEnumerable<string> words,
            IEnumerable<string> unchangedLegacyWords,
            out string error)
        {
            var legacyWordCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (unchangedLegacyWords != null)
            {
                foreach (string legacyWord in unchangedLegacyWords)
                {
                    string value = legacyWord == null ? string.Empty : legacyWord.Trim();
                    if (value.Length == 0 || value.IndexOf(PinyinSeparator) >= 0
                        || CountHanCharacters(value) == 0)
                        continue;

                    int count;
                    legacyWordCounts.TryGetValue(value, out count);
                    legacyWordCounts[value] = count + 1;
                }
            }

            int lineNumber = 0;
            if (words != null)
            {
                foreach (string word in words)
                {
                    lineNumber++;
                    string value = word == null ? string.Empty : word.Trim();
                    if (string.IsNullOrWhiteSpace(value))
                        continue;
                    int hanCharacterCount = CountHanCharacters(value);
                    if (hanCharacterCount == 0)
                        continue;

                    int separatorIndex = value.IndexOf(PinyinSeparator);
                    if (separatorIndex < 0)
                    {
                        int unchangedCount;
                        if (legacyWordCounts.TryGetValue(value, out unchangedCount) && unchangedCount > 0)
                        {
                            legacyWordCounts[value] = unchangedCount - 1;
                            continue;
                        }

                        error = "Line " + lineNumber
                            + ": add one lowercase pinyin syllable per Chinese character using 'display | ni hao'.";
                        return false;
                    }

                    if (separatorIndex != value.LastIndexOf(PinyinSeparator))
                    {
                        error = "Line " + lineNumber + ": use one '|' between the display text and pinyin.";
                        return false;
                    }

                    string display = value.Substring(0, separatorIndex).Trim();
                    string pinyinText = value.Substring(separatorIndex + 1).Trim();
                    if (display.Length == 0 || pinyinText.Length == 0)
                    {
                        error = "Line " + lineNumber
                            + ": add one lowercase pinyin syllable per Chinese character using 'display | ni hao'.";
                        return false;
                    }

                    string[] syllables = SplitSyllables(pinyinText);
                    if (syllables.Length != hanCharacterCount)
                    {
                        error = "Line " + lineNumber + ": expected " + hanCharacterCount
                            + " pinyin syllable(s), one per Chinese character.";
                        return false;
                    }

                    foreach (string syllable in syllables)
                    {
                        if (!IsLowercasePinyinSyllable(syllable))
                        {
                            error = "Line " + lineNumber
                                + ": pinyin must use lowercase a-z only, with no tone marks.";
                            return false;
                        }
                    }
                }
            }

            error = null;
            return true;
        }

        public static string FormatForDetail(string word)
        {
            string value = word ?? string.Empty;
            if (CountHanCharacters(value) == 0)
                return value;

            string display;
            string[] syllables;
            if (!TryParsePinyinAnnotation(value, out display, out syllables))
            {
                return value.IndexOf(PinyinSeparator) < 0
                    ? value + MissingPinyinLabel
                    : value;
            }

            var formatted = new StringBuilder(display.Length + syllables.Length * 5);
            int syllableIndex = 0;
            bool previousWasHan = false;
            foreach (char character in display)
            {
                if (IsHanCharacter(character))
                {
                    if (previousWasHan)
                        formatted.Append(' ');
                    formatted.Append(character).Append('(').Append(syllables[syllableIndex++]).Append(')');
                    previousWasHan = true;
                }
                else
                {
                    formatted.Append(character);
                    previousWasHan = false;
                }
            }

            return formatted.ToString();
        }

        /// <summary>
        /// Returns display text and source-order pinyin for a stacked UI renderer.
        /// The stored word remains unchanged; invalid or legacy annotations stay visible
        /// as source text and do not acquire an inferred reading.
        /// </summary>
        public static void GetPresentationParts(
            string word,
            out string displayText,
            out string[] pinyinSyllables,
            out bool hasUnannotatedHan)
        {
            string value = word ?? string.Empty;
            if (TryParsePinyinAnnotation(value, out displayText, out pinyinSyllables))
            {
                hasUnannotatedHan = false;
                return;
            }

            displayText = value;
            pinyinSyllables = Array.Empty<string>();
            hasUnannotatedHan = CountHanCharacters(value) > 0
                && value.IndexOf(PinyinSeparator) < 0;
        }

        private static bool TryParsePinyinAnnotation(string value, out string display, out string[] syllables)
        {
            display = null;
            syllables = null;
            int separatorIndex = value.IndexOf(PinyinSeparator);
            if (separatorIndex < 0 || separatorIndex != value.LastIndexOf(PinyinSeparator))
                return false;

            display = value.Substring(0, separatorIndex).Trim();
            if (display.Length == 0 || CountHanCharacters(display) == 0)
                return false;

            string pinyinText = value.Substring(separatorIndex + 1).Trim();
            if (pinyinText.Length == 0)
                return false;

            syllables = SplitSyllables(pinyinText);
            if (syllables.Length != CountHanCharacters(display))
                return false;

            foreach (string syllable in syllables)
            {
                if (!IsLowercasePinyinSyllable(syllable))
                    return false;
            }

            return true;
        }

        private static string[] SplitSyllables(string pinyinText)
        {
            return pinyinText.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool IsLowercasePinyinSyllable(string syllable)
        {
            if (string.IsNullOrEmpty(syllable))
                return false;

            foreach (char character in syllable)
            {
                if (character < 'a' || character > 'z')
                    return false;
            }

            return true;
        }

        private static int CountHanCharacters(string value)
        {
            int count = 0;
            if (value == null)
                return count;

            foreach (char character in value)
            {
                if (IsHanCharacter(character))
                    count++;
            }

            return count;
        }

        internal static bool IsHanCharacter(char character)
        {
            // Match the production PinyinLookup.IsChinese range.
            return character >= '\u4e00' && character <= '\u9fff';
        }

    }

    public sealed class MockupEditResult
    {
        public MockupWordList Item { get; }
        public bool ContentChanged { get; }
        public bool ShowRecordWarning { get; }
        public bool CreatedNewList { get; }
        public bool Succeeded => Item != null;
        public string Message { get; }

        public MockupEditResult(MockupWordList item, bool contentChanged, bool createdNewList = false)
        {
            Item = item;
            ContentChanged = contentChanged;
            ShowRecordWarning = contentChanged;
            CreatedNewList = createdNewList;
        }

        private MockupEditResult(string message)
        {
            Message = message ?? string.Empty;
        }

        public static MockupEditResult Failure(string message)
        {
            return new MockupEditResult(message);
        }
    }

    public class MockupActionResult
    {
        public bool Succeeded { get; }
        public string Message { get; }
        public MockupWordList List { get; }
        public MockupDiscoveryItem DiscoveryItem { get; }

        public MockupActionResult(
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

    /// <summary>Creates UI read models from authoritative runtime data outside this asmdef.</summary>
    public static class MockupWordListProjection
    {
        public static MockupPlayResult CreateBlockedPlayResult(string message) =>
            new MockupPlayResult(false, message ?? string.Empty);

        public static MockupWordList CreateWordList(
            string id,
            string title,
            string description,
            string tags,
            MockupLanguage language,
            IEnumerable<string> words,
            bool isFavorite,
            DateTime addedAt,
            DateTime modifiedAt,
            DateTime contentModifiedAt,
            DateTime? lastPlayedAt,
            int? personalStars,
            float? bestTimeSeconds,
            int contentRevision,
            int playCount,
            IEnumerable<MockupPreviousRecordSummary> previousRecords = null,
            bool isChallenge = false,
            bool isChallengeUnlocked = true,
            string unlockRewardLabel = null,
            MockupLanguage? detectedLanguage = null)
        {
            List<string> projectedWords = words == null ? new List<string>() : new List<string>(words);
            var item = new MockupWordList
            {
                Id = id,
                Title = title ?? string.Empty,
                Description = description ?? string.Empty,
                Tags = tags ?? string.Empty,
                Language = language,
                DetectedLanguage = detectedLanguage ?? language,
                Words = projectedWords,
                WordCount = projectedWords.Count,
                IsFavorite = isFavorite,
                AddedAt = addedAt,
                ModifiedAt = modifiedAt,
                ContentModifiedAt = contentModifiedAt,
                LastPlayedAt = lastPlayedAt,
                PersonalStars = personalStars,
                BestTimeSeconds = bestTimeSeconds,
                ContentRevision = Math.Max(1, contentRevision),
                PlayCount = Math.Max(0, playCount),
                Ownership = MockupOwnership.Owned,
                HasOfflineCache = true,
                IsPubliclyVisible = false,
                RemoteItemId = null,
                RemoteAuthor = null,
                DownloadStatus = MockupDownloadStatus.None,
                PublicationStatus = MockupPublicationStatus.NotPublished,
                IsChallenge = isChallenge,
                IsChallengeUnlocked = isChallengeUnlocked,
                UnlockRewardLabel = unlockRewardLabel ?? string.Empty
            };

            if (previousRecords != null)
            {
                foreach (MockupPreviousRecordSummary record in previousRecords)
                {
                    if (record != null)
                        item.AddPreviousRecord(record.ContentRevision, record.PersonalStars, record.BestTimeSeconds);
                }
            }
            return item;
        }

        public static MockupPreviousRecordSummary CreatePreviousRecord(
            int contentRevision, int? personalStars, float? bestTimeSeconds)
        {
            return new MockupPreviousRecordSummary(contentRevision, personalStars, bestTimeSeconds);
        }

        public static MockupAchievement CreateAchievement(
            string id,
            string title,
            string description,
            MockupAchievementStatus status,
            string progressText)
        {
            return new MockupAchievement(id, title, description, status, progressText);
        }

        public static MockupDiscoveryItem CreateDiscoveryItem(
            string remoteItemId,
            string title,
            string description,
            MockupLanguage language,
            IEnumerable<string> words,
            string remoteAuthor,
            MockupDiscoveryStatus status = MockupDiscoveryStatus.New)
        {
            List<string> displayWords = words == null ? new List<string>() : new List<string>(words);
            return new MockupDiscoveryItem
            {
                RemoteItemId = remoteItemId,
                Title = title ?? string.Empty,
                Description = description ?? string.Empty,
                Language = language,
                Words = displayWords,
                WordCount = displayWords.Count,
                RemoteAuthor = remoteAuthor ?? string.Empty,
                Status = status,
                LikeCount = 0,
                IsLiked = false,
                IsDownloaded = false,
                IsHiddenByCurrentUser = false,
                IsReportedByCurrentUser = false,
                MockReportCount = 0,
                ReportStatus = MockupReportStatus.None,
                IsRemovedFromCommunity = false
            };
        }
    }
}
