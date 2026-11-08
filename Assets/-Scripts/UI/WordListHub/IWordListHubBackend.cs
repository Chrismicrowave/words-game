using System;
using System.Collections.Generic;

namespace Words.UiMockups.WordListHub
{
    /// <summary>Current access state for a gated Hub page.</summary>
    public sealed class MockupHubPageAccess
    {
        public bool IsUnlocked { get; }
        public bool IsAvailable { get; }
        public int CompletedChallenges { get; }
        public int RequiredChallenges { get; }
        public string TrackLabel { get; }

        public MockupHubPageAccess(bool isUnlocked, int completedChallenges,
            int requiredChallenges, string trackLabel, bool isAvailable = true)
        {
            IsAvailable = isAvailable;
            IsUnlocked = isAvailable && isUnlocked;
            CompletedChallenges = Math.Max(0, completedChallenges);
            RequiredChallenges = Math.Max(0, requiredChallenges);
            TrackLabel = trackLabel ?? string.Empty;
        }

        public static MockupHubPageAccess OpenPreview =>
            new MockupHubPageAccess(true, 0, 0, "Preview", true);
    }

    /// <summary>
    /// UI-facing data/actions contract for Word List Hub. Returned models are display
    /// and editing projections; implementations own persistence and source content.
    /// </summary>
    public interface IWordListHubBackend
    {
        MockupHubPageAccess GetPageAccess(WordListHubPage page);
        bool CanCreateLists { get; }
        bool CanEditLists { get; }
        bool CanDeleteLists { get; }
        bool CanDuplicateLists { get; }
        bool CanImportLists { get; }
        bool CanExportLists { get; }

        IReadOnlyList<MockupAchievement> Achievements { get; }

        IReadOnlyList<MockupWordList> QueryLibrary(MockupLibraryQuery query);
        IReadOnlyList<MockupWordList> QueryChallenges();
        IReadOnlyList<MockupDiscoveryItem> QueryDiscoveryWithFilters(MockupDiscoveryQuery query);
        MockupWordList FindById(string localListId);
        MockupWordList FindCommunityDownload(string remoteItemId);
        MockupDiscoveryItem FindDiscoveryItem(string remoteItemId);
        MockupWordList EditCommunityCopy(string localListId);
        MockupWordListEdit CreateEditDraft(string localListId);

        bool ToggleFavorite(string localListId);
        MockupActionResult TryToggleLike(string remoteItemId);
        MockupActionResult TryDownload(string remoteItemId);
        MockupActionResult TryReportAndHide(string remoteItemId);
        MockupPlayResult PlayDiscoveryItem(string remoteItemId);
        bool CompleteRun(string localListId, int stars, float bestTimeSeconds);
        MockupActionResult TryPublish(string localListId);
        MockupActionResult TryUnpublish(string localListId);
        MockupActionResult TryDeleteList(string localListId);

        bool TryCreateList(
            string title,
            MockupLanguage language,
            IEnumerable<string> displayWords,
            string description,
            string tags,
            out MockupWordList item,
            out string error);

        bool TryPrepareImport(string sourcePath, out string importToken,
            out MockupWordListEdit draft, out string error);
        bool TryCommitImport(string importToken, MockupWordListEdit draft,
            out MockupWordList item, out string error);
        bool TryExportList(string localListId, string destinationPath, out string error);

        MockupEditResult SaveEdit(string localListId, MockupWordListEdit edit);
        MockupEditResult SaveOwnedContentAsNewList(string localListId, MockupWordListEdit edit);
    }
}
