using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Words.UiMockups.WordListHub;

/// <summary>
/// Production adapter for the scene-authored Hub. The mockup models produced here are
/// UI projections only; WordListLibraryService and LevelWordListProvider remain canonical.
/// Community actions use the asynchronous service; cached downloads remain separate from the canonical Library.
/// </summary>
public sealed class WordListHubProductionBackend : IWordListHubBackend, IWordListHubCommunityBackend
{
    private const string EmptyListContentError = "Add at least one word before saving the list.";

    private sealed class PendingImport
    {
        public string path;
        public string contentHash;
        public WordListLibraryContent content;
    }

    private readonly WordListLibraryService library;
    private readonly LevelPanelController levelPanel;
    private readonly Func<WordAchievementProgressSnapshot> achievementSnapshot;
    private readonly IWordListHubCommunityAsync communityAsync;
    private readonly Dictionary<string, PendingImport> pendingImports =
        new Dictionary<string, PendingImport>(StringComparer.Ordinal);

    public bool CanCreateLists => IsLibraryOpen && levelPanel != null && levelPanel.CanCreateListsFromHub;
    public bool CanEditLists => IsLibraryOpen && levelPanel != null && levelPanel.CanEditListsFromHub;
    public bool CanDeleteLists => IsLibraryOpen && levelPanel != null && levelPanel.CanDeleteListsFromHub;
    public bool CanDuplicateLists => IsLibraryOpen && levelPanel != null && levelPanel.CanDuplicateListsFromHub;
    public bool CanImportLists => IsLibraryOpen && levelPanel != null && levelPanel.CanImportListsFromHub;
    public bool CanExportLists => IsLibraryOpen && levelPanel != null && levelPanel.CanExportListsFromHub;
    public bool CanPublishLists => IsLibraryOpen && IsCommunityOpen
        && communityAsync != null && communityAsync.IsRemoteAuthenticated;
    public IWordListHubCommunityAsync CommunityAsync => communityAsync;

    public MockupHubPageAccess GetPageAccess(WordListHubPage page)
    {
        if (page != WordListHubPage.Library && page != WordListHubPage.Community)
            return MockupHubPageAccess.OpenPreview;

        int completed = levelPanel != null ? levelPanel.CompletedChallengesForHub : 0;
        string trackPrefix = levelPanel != null ? levelPanel.HubProgressTrackPrefix : string.Empty;
        string trackLabel = string.Equals(trackPrefix, ChallengeTrackResolver.ChineseTrackPrefix,
            StringComparison.OrdinalIgnoreCase) ? "Chinese" : "English";
        bool libraryPage = page == WordListHubPage.Library;
        if (levelPanel == null)
            return new MockupHubPageAccess(false, completed, libraryPage ? 5 : 10, "English", false);

        bool hasAccessProfile = levelPanel.HasAccessProfileFromHub;
        bool isAvailable = hasAccessProfile
            && (libraryPage || levelPanel.IsCommunityEnabledFromHub);

        int required = libraryPage
            ? levelPanel.CustomUnlockRequirementForHub
            : levelPanel.CommunityUnlockRequirementForHub;
        bool unlocked = libraryPage
            ? levelPanel.CanOpenLibraryFromHub
            : levelPanel.CanOpenCommunityFromHub;
        return new MockupHubPageAccess(unlocked, completed, required, trackLabel, isAvailable);
    }

    private bool IsLibraryOpen => GetPageAccess(WordListHubPage.Library).IsUnlocked;
    private bool IsCommunityOpen => GetPageAccess(WordListHubPage.Community).IsUnlocked;

    private string LockedPageMessage(WordListHubPage page)
    {
        MockupHubPageAccess access = GetPageAccess(page);
        string pageName = page == WordListHubPage.Library ? "Library" : "Community";
        if (!access.IsAvailable)
            return pageName + " is unavailable for the active profile. Open Challenges for details.";
        if (access.RequiredChallenges <= 0)
            return pageName + " is locked by the active game profile. Open Challenges for details.";
        return "Complete " + access.RequiredChallenges + " "
            + (access.RequiredChallenges == 1 ? "challenge" : "challenges")
            + " to unlock " + pageName + " (" + access.CompletedChallenges + "/"
            + access.RequiredChallenges + " on " + access.TrackLabel + ").";
    }

    public WordListHubProductionBackend(
        WordListLibraryService library,
        LevelPanelController levelPanel,
        Func<WordAchievementProgressSnapshot> achievementSnapshot)
        : this(library, levelPanel, achievementSnapshot, null)
    {
    }

    public WordListHubProductionBackend(
        WordListLibraryService library,
        LevelPanelController levelPanel,
        Func<WordAchievementProgressSnapshot> achievementSnapshot,
        IWordListHubCommunityAsync communityAsync)
    {
        this.library = library ?? WordListLibraryService.Shared;
        this.levelPanel = levelPanel;
        this.achievementSnapshot = achievementSnapshot;
        this.communityAsync = communityAsync;
    }

    public IReadOnlyList<MockupAchievement> Achievements
    {
        get
        {
            WordAchievementProgressSnapshot progress = achievementSnapshot != null
                ? achievementSnapshot()
                : new WordAchievementProgressSnapshot(0, false);

            return new[]
            {
                Achievement(
                    WordAchievementApiNames.CompleteFirstWord,
                    "Complete your first word",
                    "Finish one word correctly.",
                    progress.CompleteFirstWordUnlocked,
                    progress.CompletedWordCount > 0,
                    Math.Min(progress.CompletedWordCount, 1), 1),
                Achievement(
                    WordAchievementApiNames.CompleteTwoWords,
                    "Complete two words",
                    "Finish two words correctly.",
                    progress.CompleteTwoWordsUnlocked,
                    progress.CompletedWordCount > 0,
                    Math.Min(progress.CompletedWordCount, 2), 2),
                MockupWordListProjection.CreateAchievement(
                    WordAchievementApiNames.CompleteWordWithD,
                    "Complete a word with D",
                    "Finish a word whose target contains the letter D.",
                    progress.CompleteWordWithDUnlocked
                        ? MockupAchievementStatus.Unlocked : MockupAchievementStatus.Locked,
                    progress.CompleteWordWithDUnlocked ? "Unlocked" : "Not yet unlocked")
            };
        }
    }

    public IReadOnlyList<MockupWordList> QueryLibrary(MockupLibraryQuery query)
    {
        if (!IsLibraryOpen) return Array.Empty<MockupWordList>();
        query = query ?? new MockupLibraryQuery();
        List<MockupWordList> canonical = library.GetAll()
            .Select(ProjectWithPublication)
            .Where(item => item != null)
            .ToList();
        IReadOnlyList<MockupWordList> communityDownloads = communityAsync?.DownloadedItems
            ?? Array.Empty<MockupWordList>();
        IEnumerable<MockupWordList> results;

        switch (query.View)
        {
            case MockupLibraryView.Favorites:
                results = canonical.Where(item => item.IsFavorite);
                break;
            case MockupLibraryView.MyLists:
                results = canonical.Where(item => item.Group == MockupLibraryGroup.MyLists);
                break;
            case MockupLibraryView.CommunityDownloads:
                results = communityDownloads;
                break;
            case MockupLibraryView.MyPublished:
                results = canonical.Where(item => item.Group == MockupLibraryGroup.MyPublished);
                break;
            default:
                results = canonical.Concat(communityDownloads);
                break;
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            string term = query.SearchText.Trim();
            results = results.Where(item => Contains(item.Title, term)
                || Contains(item.Description, term) || Contains(item.Tags, term));
        }
        if (query.LanguageFilter != MockupLanguageFilter.All)
            results = results.Where(item => item.DetectedLanguage.ToString() == query.LanguageFilter.ToString());
        if (query.PlayedFilter == MockupPlayedFilter.Played)
            results = results.Where(item => item.HasBeenPlayed);
        else if (query.PlayedFilter == MockupPlayedFilter.Unplayed)
            results = results.Where(item => !item.HasBeenPlayed);

        return Sort(results, query.SortMode, query.SortDirection).ToList().AsReadOnly();
    }

    public IReadOnlyList<MockupWordList> QueryChallenges()
    {
        bool showChinese = IsChineseTrackAvailable();
        List<LevelWordListProvider> providers = LevelWordListProvider.ScanDirectory(
            LevelWordListProvider.GetChallengeDirectory());
        IEnumerable<LevelWordListProvider> currentTrack = providers.Where(provider =>
        {
            string name = Path.GetFileName(provider.FilePath) ?? string.Empty;
            bool chinese = name.IndexOf("_cn", StringComparison.OrdinalIgnoreCase) >= 0;
            return chinese == showChinese;
        });
        List<LevelWordListProvider> ordered = currentTrack
            .OrderBy(provider => Path.GetFileName(provider.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToList();
        string trackPrefix = showChinese
            ? ChallengeTrackResolver.ChineseTrackPrefix
            : ChallengeTrackResolver.EnglishTrackPrefix;
        int unlockedCount = ChallengeProgression.GetUnlockedCountForTrack(trackPrefix);
        GameConfigProfile profile = Services.Get<PhaseManager>() != null
            ? Services.Get<PhaseManager>().ActiveProfile
            : null;
        var results = new List<MockupWordList>(ordered.Count);
        int customUnlockRequirement = levelPanel != null
            ? levelPanel.CustomUnlockRequirementForHub : 5;
        int communityUnlockRequirement = levelPanel != null
            ? levelPanel.CommunityUnlockRequirementForHub : 10;
        bool hasAccessProfile = levelPanel != null && levelPanel.HasAccessProfileFromHub;
        bool customMilestoneVisible = IsUnlockMilestoneVisible(
            customUnlockRequirement, hasAccessProfile);
        bool communityMilestoneVisible = IsUnlockMilestoneVisible(
            communityUnlockRequirement,
            hasAccessProfile && levelPanel.IsCommunityEnabledFromHub);

        for (int index = 0; index < ordered.Count; index++)
        {
            LevelWordListProvider provider = ordered[index];
            WordListLibraryItem item = library.GetOrRegister(provider);
            if (item == null) continue;

            bool unlocked = ChallengeAccessPolicy.CanEnterChallenge(
                profile, index, unlockedCount,
                levelPanel != null ? levelPanel.LegacyChallengeCap : 0,
                levelPanel != null && levelPanel.LegacyUnlockAllChallenges);
            int trackStars = ChallengeProgression.GetStarRating(index, trackPrefix);
            bool challengeCompleted = HasChallengeCompletionEvidence(item, index, trackPrefix);
            string rewardLabel = GetUnlockRewardLabel(index + 1,
                customUnlockRequirement, customMilestoneVisible,
                communityUnlockRequirement, communityMilestoneVisible);
            results.Add(Project(item, true, unlocked, trackStars, rewardLabel, index + 1,
                challengeCompleted,
                profile != null && profile.unlockAllFeatures));
        }
        return results.AsReadOnly();
    }

    public IReadOnlyList<MockupDiscoveryItem> QueryDiscoveryWithFilters(MockupDiscoveryQuery query)
    {
        if (!IsCommunityOpen || communityAsync == null)
            return Array.Empty<MockupDiscoveryItem>();

        query = query ?? new MockupDiscoveryQuery();
        IEnumerable<MockupDiscoveryItem> items = communityAsync.Snapshot
            ?? Array.Empty<MockupDiscoveryItem>();
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            string term = query.SearchText.Trim();
            items = items.Where(item => Contains(item.Title, term)
                || Contains(item.Description, term));
        }
        if (query.LanguageFilter != MockupLanguageFilter.All)
            items = items.Where(item => item.Language.ToString() == query.LanguageFilter.ToString());
        if (query.DownloadFilter == MockupDiscoveryDownloadFilter.Downloaded)
            items = items.Where(item => item.IsDownloaded);
        else if (query.DownloadFilter == MockupDiscoveryDownloadFilter.NotDownloaded)
            items = items.Where(item => !item.IsDownloaded);

        switch (query.SortMode)
        {
            case MockupDiscoverySortMode.Name:
                items = query.SortDirection == MockupSortDirection.Descending
                    ? items.OrderByDescending(item => item.Title, StringComparer.OrdinalIgnoreCase)
                    : items.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase);
                break;
            case MockupDiscoverySortMode.Likes:
                items = query.SortDirection == MockupSortDirection.Descending
                    ? items.OrderByDescending(item => item.LikeCount)
                    : items.OrderBy(item => item.LikeCount);
                break;
            case MockupDiscoverySortMode.WordCount:
                items = query.SortDirection == MockupSortDirection.Descending
                    ? items.OrderByDescending(item => item.WordCount)
                    : items.OrderBy(item => item.WordCount);
                break;
        }
        return items.ToList().AsReadOnly();
    }

    public MockupWordList FindById(string localListId)
    {
        if (string.IsNullOrEmpty(localListId)) return null;
        MockupWordList challenge = FindChallengeById(localListId);
        if (challenge != null) return challenge;

        if (!IsLibraryOpen) return null;
        WordListLibraryItem item = library.FindById(localListId);
        if (item != null) return ProjectWithPublication(item);
        return communityAsync?.DownloadedItems?.FirstOrDefault(candidate => candidate.Id == localListId);
    }

    public LevelWordListProvider FindProvider(string localListId)
    {
        if (string.IsNullOrEmpty(localListId)) return null;
        if (!IsLibraryOpen)
        {
            MockupWordList challenge = FindChallengeById(localListId);
            return challenge != null ? FindLibraryOrChallenge(localListId)?.Provider : null;
        }
        return FindLibraryOrChallenge(localListId)?.Provider;
    }

    public MockupWordList FindCommunityDownload(string remoteItemId) =>
        communityAsync?.DownloadedItems?.FirstOrDefault(item => item.RemoteItemId == remoteItemId);

    public MockupDiscoveryItem FindDiscoveryItem(string remoteItemId) =>
        IsCommunityOpen && communityAsync != null
            ? communityAsync.Snapshot?.FirstOrDefault(item => item.RemoteItemId == remoteItemId)
            : null;

    public MockupWordList EditCommunityCopy(string localListId)
    {
        if (!IsLibraryOpen || communityAsync == null)
            return null;
        if (levelPanel == null || !levelPanel.CanDuplicateListsFromHub)
            return null;
        MockupWordList source = communityAsync.DownloadedItems?.FirstOrDefault(item => item != null
            && string.Equals(item.Id, localListId, StringComparison.Ordinal)
            && item.Ownership == MockupOwnership.CommunityDownloaded);
        if (source == null || source.WordCount <= 0)
            return null;
        if (!levelPanel.CanCreateListFromHub(source.WordCount, out _))
            return null;
        if (!communityAsync.TryGetDownloadedContentForCopy(localListId,
            out WordListLibraryContent content, out string title, out _)
            || content == null || content.Words == null || content.Words.Count != source.WordCount
            || !CanWriteContentMode(content.LanguageMode, out _))
            return null;
        if (!TryCreateCommunityCopyFromValidatedContent(library,
            string.IsNullOrWhiteSpace(title) ? source.Title : title,
            content, out WordListLibraryItem created, out _))
            return null;

        return Project(created, false, true);
    }

    private static bool TryCreateCommunityCopyFromValidatedContent(
        WordListLibraryService targetLibrary, string title, WordListLibraryContent content,
        out WordListLibraryItem created, out string error)
    {
        created = null;
        if (targetLibrary == null || content == null || content.Words == null || content.Words.Count == 0)
        {
            error = "The validated Community content is unavailable.";
            return false;
        }

        // WordListLibraryService creates a fresh canonical UUID and preserves the structured
        // Chinese/mixed payload; the source Community provider remains read-only and unchanged.
        return targetLibrary.TryCreateList(title, content, out created, out error);
    }

    public MockupWordListEdit CreateEditDraft(string localListId)
    {
        if (!IsLibraryOpen) return null;
        if (IsPublicationLocked(localListId)) return null;
        MockupWordList item = FindById(localListId);
        if (item == null) return null;

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

    public bool ToggleFavorite(string localListId)
    {
        if (!IsLibraryOpen) return false;
        WordListLibraryItem item = library.FindById(localListId);
        if (item == null)
            return false;

        bool nextFavorite = !item.IsFavorite;
        bool saved = library.TryUpdateMetadata(item.LocalListId, item.Title, item.Description,
            item.Tags, nextFavorite, out _, out string error);
        if (!saved)
            UnityEngine.Debug.LogWarning("[WordListHub] Could not update favorite: " + error);
        return saved && nextFavorite;
    }

    public MockupActionResult TryToggleLike(string remoteItemId) =>
        IsCommunityOpen ? new MockupActionResult(false, "Community actions require the asynchronous service.")
            : new MockupActionResult(false, LockedPageMessage(WordListHubPage.Community));

    public MockupActionResult TryDownload(string remoteItemId)
    {
        if (!IsCommunityOpen)
            return new MockupActionResult(false, LockedPageMessage(WordListHubPage.Community));
        return new MockupActionResult(false, "Community downloads require the asynchronous service.");
    }

    public MockupActionResult TryReportAndHide(string remoteItemId) =>
        IsCommunityOpen ? new MockupActionResult(false, "Community actions require the asynchronous service.")
            : new MockupActionResult(false, LockedPageMessage(WordListHubPage.Community));

    public MockupPlayResult PlayDiscoveryItem(string remoteItemId) =>
        MockupWordListProjection.CreateBlockedPlayResult(
            IsCommunityOpen ? "Community lists must be downloaded and validated before they can be played."
                : LockedPageMessage(WordListHubPage.Community));

    public bool CompleteRun(string localListId, int stars, float bestTimeSeconds)
    {
        return false;
    }

    public MockupActionResult TryPublish(string localListId)
    {
        if (!CanPublishLists)
            return new MockupActionResult(false, !IsLibraryOpen
                ? LockedPageMessage(WordListHubPage.Library)
                : !IsCommunityOpen ? LockedPageMessage(WordListHubPage.Community)
                : "Connect to Steam before publishing.");
        if (IsPublicationLocked(localListId))
            return new MockupActionResult(false, "This My List already has a Workshop publication binding.");
        return new MockupActionResult(false, "Publishing requires the asynchronous Community service.");
    }

    public MockupActionResult TryUnpublish(string localListId)
    {
        if (!IsPublicationLocked(localListId))
            return new MockupActionResult(false, "This My List has no active Workshop publication to unpublish.");
        if (communityAsync == null || !communityAsync.IsRemoteAuthenticated
            || !(communityAsync.Publications ?? Array.Empty<MockupCommunityPublicationInfo>())
                .Any(publication => publication != null
                && string.Equals(publication.LocalListId, localListId, StringComparison.Ordinal)))
            return new MockupActionResult(false,
                "Reconnect to the Steam account that published this item to unpublish it.");
        return new MockupActionResult(false, "Unpublish requires the asynchronous Community service.");
    }

    public MockupActionResult TryDeleteList(string localListId)
    {
        if (!IsLibraryOpen)
            return new MockupActionResult(false, LockedPageMessage(WordListHubPage.Library));
        WordListLibraryItem item = library.FindById(localListId);
        if (item == null)
            return new MockupActionResult(false, "The selected list could not be found.");
        if (IsPublicationLocked(localListId))
            return new MockupActionResult(false,
                "Refresh publication status and unpublish this Workshop item before deleting its My List.");
        if (!item.IsEditable || item.IsChallenge)
            return new MockupActionResult(false, "Official challenge lists are read-only.");
        if (!CanDeleteLists)
            return new MockupActionResult(false, "Deleting custom lists is disabled by the active profile.");

        PhaseManager phaseManager = Services.Get<PhaseManager>();
        LevelWordListProvider activeProvider = phaseManager != null
            ? phaseManager.ActiveProvider as LevelWordListProvider
            : null;
        if (SameProvider(activeProvider, item.Provider))
        {
            if (!TryStartFirstAllowedChallenge(out string fallbackError))
            {
                return new MockupActionResult(false,
                    "Choose a list first: no allowed official challenge could replace the active list. "
                    + fallbackError);
            }
        }

        if (!library.TryDeleteList(localListId, out string error))
            return new MockupActionResult(false, string.IsNullOrWhiteSpace(error)
                ? "The list could not be deleted." : error);
        return new MockupActionResult(true, "List deleted.");
    }

    public bool TryCreateList(
        string title,
        MockupLanguage language,
        IEnumerable<string> displayWords,
        string description,
        string tags,
        out MockupWordList item,
        out string error)
    {
        item = null;
        if (!IsLibraryOpen)
        {
            error = LockedPageMessage(WordListHubPage.Library);
            return false;
        }
        List<string> rows = WordListLibraryContentCodec.FilterEmptyEditorRows(displayWords);
        if (rows.Count == 0)
        {
            error = EmptyListContentError;
            return false;
        }
        if (levelPanel != null && !levelPanel.CanCreateListFromHub(rows.Count, out error))
            return false;
        if (!ValidatePinyinForContentChange(rows, out error))
            return false;
        if (!WordListLibraryContentCodec.TryParseAutoEditorLines(
            rows, LanguageMode.English, null, out _, out _, out WordListLibraryContent content, out error))
            return false;
        if (!CanWriteContentMode(content.LanguageMode, out error))
            return false;

        return CreateListFromContent(title, description, tags, content, out item, out error);
    }

    public bool TryPrepareImport(string sourcePath, out string importToken,
        out MockupWordListEdit draft, out string error)
    {
        importToken = null;
        draft = null;
        if (!IsLibraryOpen)
        {
            error = LockedPageMessage(WordListHubPage.Library);
            return false;
        }
        // The Hub exposes one import-review modal at a time. Keep abandoned
        // review snapshots bounded when the user cancels and starts another import.
        pendingImports.Clear();
        if (levelPanel != null && !levelPanel.CanImportListFromHub(out error))
            return false;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            error = "The selected import file could not be found.";
            return false;
        }

        var provider = new LevelWordListProvider(sourcePath, false);
        if (!provider.IsValid)
        {
            error = string.IsNullOrWhiteSpace(provider.LoadError)
                ? "The selected file is not a valid word list." : provider.LoadError;
            return false;
        }
        PhaseManager phaseManager = Services.Get<PhaseManager>();
        if (phaseManager != null && !phaseManager.CanLoadWordList(provider))
        {
            error = "Chinese content is disabled by the active profile; the list was not imported.";
            return false;
        }

        WordListLibraryContent content = WordListLibraryContent.FromProvider(provider);
        List<string> editorRows = ToEditorLines(content).Select(SuggestMissingPinyin).ToList();
        if (!TryDetectContentLanguage(content, content.LanguageMode,
            out LanguageMode detectedMode, out error))
            return false;
        if (!CanWriteContentMode(detectedMode, out error))
            return false;
        importToken = Guid.NewGuid().ToString("N");
        pendingImports[importToken] = new PendingImport
        {
            path = provider.FilePath,
            contentHash = WordListLibraryService.ComputeContentHash(content),
            content = content.Clone()
        };
        draft = new MockupWordListEdit
        {
            Title = provider.DisplayName,
            Description = string.Empty,
            Tags = string.Empty,
            Language = ToMockupLanguage(content.LanguageMode),
            Words = editorRows,
            WordCount = content.Words != null ? content.Words.Count : 0
        };
        error = null;
        return true;
    }

    public bool TryCommitImport(string importToken, MockupWordListEdit draft,
        out MockupWordList item, out string error)
    {
        item = null;
        if (!IsLibraryOpen)
        {
            error = LockedPageMessage(WordListHubPage.Library);
            return false;
        }
        if (draft == null || string.IsNullOrEmpty(importToken)
            || !pendingImports.TryGetValue(importToken, out PendingImport pending))
        {
            error = "The import review expired. Select the file again.";
            return false;
        }
        if (levelPanel != null && !levelPanel.CanImportListFromHub(out error))
            return false;
        if (!File.Exists(pending.path))
        {
            error = "The selected import file no longer exists.";
            return false;
        }
        var currentSource = new LevelWordListProvider(pending.path, false);
        if (!currentSource.IsValid || !string.Equals(
            WordListLibraryService.ComputeContentHash(WordListLibraryContent.FromProvider(currentSource)),
            pending.contentHash, StringComparison.Ordinal))
        {
            error = "The selected file changed during review. Select it again before importing.";
            return false;
        }
        List<string> editorRows = WordListLibraryContentCodec.FilterEmptyEditorRows(draft.Words);
        if (editorRows.Count == 0)
        {
            error = EmptyListContentError;
            return false;
        }
        if (levelPanel != null && !levelPanel.CanCreateListFromHub(editorRows.Count, out error))
            return false;
        if (!ValidatePinyinForContentChange(editorRows, out error))
            return false;
        if (!WordListLibraryContentCodec.TryParseAutoEditorLines(
            editorRows, pending.content.LanguageMode, pending.content,
            out _, out _, out WordListLibraryContent content, out error))
            return false;
        if (!CanWriteContentMode(content.LanguageMode, out error))
            return false;

        bool created = CreateListFromContent(draft.Title, draft.Description, draft.Tags,
            content, out item, out error);
        if (created)
            pendingImports.Remove(importToken);
        return created;
    }

    public bool TryExportList(string localListId, string destinationPath, out string error)
    {
        if (!IsLibraryOpen)
        {
            error = LockedPageMessage(WordListHubPage.Library);
            return false;
        }
        if (levelPanel != null && !levelPanel.CanExportListFromHub(out error))
            return false;
        WordListLibraryItem item = library.FindById(localListId);
        if (item == null || !item.IsEditable || item.IsChallenge || item.Provider == null
            || !File.Exists(item.Provider.FilePath))
        {
            error = "Select an owned custom list before exporting.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            error = "Choose a destination for the exported list.";
            return false;
        }
        try
        {
            string fullDestination = Path.GetFullPath(destinationPath);
            if (string.Equals(fullDestination, Path.GetFullPath(item.Provider.FilePath),
                StringComparison.OrdinalIgnoreCase))
            {
                error = "The export destination must differ from the Library file.";
                return false;
            }
            if (IsInsideDirectory(fullDestination, LevelWordListProvider.GetCustomDirectory())
                || IsInsideDirectory(fullDestination, LevelWordListProvider.GetChallengeDirectory()))
            {
                error = "Choose an export path outside the game's managed word-list folders.";
                return false;
            }
            File.Copy(item.Provider.FilePath, fullDestination, true);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = "The list could not be exported: " + ex.Message;
            return false;
        }
    }

    private static bool IsInsideDirectory(string fullPath, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(fullPath);
        return candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public MockupEditResult SaveEdit(string localListId, MockupWordListEdit edit)
    {
        if (!IsLibraryOpen)
            return MockupEditResult.Failure(LockedPageMessage(WordListHubPage.Library));
        if (IsPublicationLocked(localListId))
            return MockupEditResult.Failure(
                "Refresh publication status and unpublish this Workshop item before editing it.");
        if (edit == null) return MockupEditResult.Failure("The edit form is unavailable.");
        WordListLibraryItem current = library.FindById(localListId);
        if (current == null || !current.IsEditable || current.IsChallenge)
            return MockupEditResult.Failure("This list is read-only or no longer available.");

        List<string> editorRows = WordListLibraryContentCodec.FilterEmptyEditorRows(edit.Words);
        if (editorRows.Count == 0)
            return MockupEditResult.Failure(EmptyListContentError);

        bool contentChanged = IsGameplayContentChanged(current, editorRows);
        if (levelPanel != null)
        {
            WordListLibraryContent existingContent = current.Content
                ?? (current.Provider != null ? WordListLibraryContent.FromProvider(current.Provider) : null);
            int existingWordCount = existingContent != null && existingContent.Words != null
                ? existingContent.Words.Count : 0;
            if (!levelPanel.CanEditListsFromHub)
                return MockupEditResult.Failure("Editing custom lists is disabled by the active profile.");
            if (!levelPanel.CanEditListFromHub(
                editorRows.Count, contentChanged,
                out string policyError, existingWordCount))
                return MockupEditResult.Failure(policyError);
        }
        WordListLibraryContent contentOrNull = null;
        if (contentChanged)
        {
            if (!ValidatePinyinForContentChange(editorRows, out string pinyinError))
                return MockupEditResult.Failure(pinyinError);
            WordListLibraryContent originalContent = current.Content
                ?? (current.Provider != null ? WordListLibraryContent.FromProvider(current.Provider) : null);
            LanguageMode fallback = originalContent != null ? originalContent.LanguageMode : current.LanguageMode;
            if (!WordListLibraryContentCodec.TryParseAutoEditorLines(
                editorRows, fallback, originalContent,
                out _, out _, out contentOrNull, out string codecError))
                return MockupEditResult.Failure(codecError);
            if (!CanWriteContentMode(contentOrNull.LanguageMode, out string languageError))
                return MockupEditResult.Failure(languageError);
        }

        IEnumerable<string> tagsToSave = edit.Tags == null ? current.Tags : ParseTags(edit.Tags);
        if (!library.TrySaveListEdit(localListId, edit.Title, edit.Description,
            tagsToSave, current.IsFavorite, contentOrNull,
            out WordListLibraryItem updated, out string error))
            return MockupEditResult.Failure(error);

        return new MockupEditResult(Project(updated, false, true), contentChanged);
    }

    public MockupEditResult SaveOwnedContentAsNewList(string localListId, MockupWordListEdit edit)
    {
        if (!IsLibraryOpen)
            return MockupEditResult.Failure(LockedPageMessage(WordListHubPage.Library));
        if (edit == null) return MockupEditResult.Failure("The edit form is unavailable.");
        WordListLibraryItem source = FindLibraryOrChallenge(localListId);
        if (source == null || source.Provider == null)
            return MockupEditResult.Failure("The source list could not be found.");

        if (source.IsChallenge && !IsUnlockedChallengeCopySource(source.Provider))
            return MockupEditResult.Failure(
                "Only an unlocked challenge in the current language track can be copied to My Lists.");

        List<string> editorRows = WordListLibraryContentCodec.FilterEmptyEditorRows(edit.Words);
        if (editorRows.Count == 0)
            return MockupEditResult.Failure(EmptyListContentError);

        bool contentChanged = IsGameplayContentChanged(source, editorRows);
        WordListLibraryContent sourceContent = source.Content ?? WordListLibraryContent.FromProvider(source.Provider);
        if (levelPanel != null && (!levelPanel.CanCreateListFromHub(
            editorRows.Count, out string policyError)
            || !levelPanel.CanDuplicateListsFromHub))
            return MockupEditResult.Failure(policyError ?? "Copying lists is disabled by the active profile.");
        WordListLibraryItem created;
        string error;
        if (contentChanged && !ValidatePinyinForContentChange(editorRows, out string pinyinError))
            return MockupEditResult.Failure(pinyinError);
        if (!WordListLibraryContentCodec.TryParseAutoEditorLines(
            editorRows, sourceContent.LanguageMode, sourceContent,
            out _, out _, out WordListLibraryContent content, out string codecError))
            return MockupEditResult.Failure(codecError);
        if (!CanWriteContentMode(content.LanguageMode, out string languageError))
            return MockupEditResult.Failure(languageError);
        if (!library.TryCreateList(edit.Title, content, out created, out error))
            return MockupEditResult.Failure(error);

        if (!library.TryUpdateMetadata(created.LocalListId, edit.Title, edit.Description,
            ParseTags(edit.Tags), false, out created, out error))
        {
            if (!library.TryDeleteList(created.LocalListId, out string rollbackError))
                error = (error ?? "The copied list metadata could not be saved.")
                    + " The copied list file remains because rollback failed: " + rollbackError;
            return MockupEditResult.Failure(error);
        }

        return new MockupEditResult(Project(created, false, true), contentChanged, true);
    }

    private bool TryStartFirstAllowedChallenge(out string error)
    {
        error = "";
        if (levelPanel == null)
        {
            error = "The challenge start controller is unavailable.";
            return false;
        }

        List<LevelWordListProvider> providers = LevelWordListProvider.ScanDirectory(
            LevelWordListProvider.GetChallengeDirectory());
        providers = providers.OrderBy(provider => Path.GetFileName(provider.FilePath),
            StringComparer.OrdinalIgnoreCase).ToList();
        foreach (LevelWordListProvider provider in providers)
        {
            if (levelPanel.TryStartProvider(provider, LevelTab.Challenges, out string startError))
                return true;
            if (string.IsNullOrEmpty(error)) error = startError;
        }
        if (string.IsNullOrEmpty(error)) error = "No challenge files are available.";
        return false;
    }

    private bool CreateListFromContent(string title, string description, string tags,
        WordListLibraryContent content, out MockupWordList item, out string error)
    {
        item = null;
        if (content == null)
        {
            error = "List content is unavailable.";
            return false;
        }
        if (WordListLibraryContentCodec.FilterEmptyEditorRows(content.Words).Count == 0)
        {
            error = EmptyListContentError;
            return false;
        }
        if (!CanWriteContentMode(content.LanguageMode, out error))
            return false;
        if (!library.TryCreateList(title, content, out WordListLibraryItem created, out error))
            return false;
        if ((!string.IsNullOrWhiteSpace(description) || !string.IsNullOrWhiteSpace(tags))
            && !library.TryUpdateMetadata(created.LocalListId, created.Title, description,
                ParseTags(tags), created.IsFavorite, out created, out error))
        {
            if (!library.TryDeleteList(created.LocalListId, out string rollbackError))
                error = (error ?? "Metadata could not be saved.")
                    + " The list file remains because rollback failed: " + rollbackError;
            return false;
        }
        item = Project(created, false, true);
        error = null;
        return true;
    }

    private static bool CanWriteContentMode(LanguageMode mode, out string error)
    {
        PhaseManager phaseManager = Services.Get<PhaseManager>();
        if (phaseManager != null && !phaseManager.AllowChineseAndMixed
            && mode != LanguageMode.English)
        {
            error = "Chinese and Mixed list content is disabled by the active profile. Existing saved lists are unchanged.";
            return false;
        }
        error = null;
        return true;
    }

    private WordListLibraryItem FindLibraryOrChallenge(string localListId)
    {
        WordListLibraryItem item = library.FindById(localListId);
        if (item != null) return item;
        foreach (LevelWordListProvider provider in LevelWordListProvider.ScanDirectory(
            LevelWordListProvider.GetChallengeDirectory()))
        {
            WordListLibraryItem challenge = library.GetOrRegister(provider);
            if (challenge != null && string.Equals(challenge.LocalListId, localListId, StringComparison.Ordinal))
                return challenge;
        }
        return null;
    }

    private MockupWordList FindChallengeById(string localListId)
    {
        foreach (MockupWordList challenge in QueryChallenges())
            if (string.Equals(challenge.Id, localListId, StringComparison.Ordinal))
                return challenge;
        return null;
    }

    private static bool IsUnlockMilestoneVisible(int requiredChallenges, bool featureAvailable)
    {
        return requiredChallenges > 0 && featureAvailable;
    }

    private static string GetUnlockRewardLabel(int challengeNumber,
        int customRequirement, bool customMilestoneVisible,
        int communityRequirement, bool communityMilestoneVisible)
    {
        bool unlockLibrary = customMilestoneVisible && customRequirement > 0
            && challengeNumber == customRequirement;
        bool unlockCommunity = communityMilestoneVisible && communityRequirement > 0
            && challengeNumber == communityRequirement;
        if (unlockLibrary && unlockCommunity) return "Unlock Library + Community";
        if (unlockLibrary) return "Unlock Library";
        if (unlockCommunity) return "Unlock Community";
        return string.Empty;
    }

    private bool IsUnlockedChallengeCopySource(LevelWordListProvider provider)
    {
        if (provider == null || !IsProviderOnCurrentChallengeTrack(provider)) return false;
        string trackPrefix = ChallengeTrackResolver.GetTrackPrefixForChallengeProvider(provider);
        if (string.IsNullOrEmpty(trackPrefix)) return false;
        List<LevelWordListProvider> track = ChallengeTrackResolver.FilterByTrack(
            LevelWordListProvider.ScanDirectory(LevelWordListProvider.GetChallengeDirectory()),
            trackPrefix);
        int index = ChallengeTrackResolver.FindIndex(provider, track);
        if (index < 0) return false;
        int unlockedCount = ChallengeProgression.GetUnlockedCountForTrack(trackPrefix);
        GameConfigProfile profile = Services.Get<PhaseManager>() != null
            ? Services.Get<PhaseManager>().ActiveProfile : null;
        return ChallengeAccessPolicy.CanEnterChallenge(profile, index, unlockedCount,
            levelPanel != null ? levelPanel.LegacyChallengeCap : 0,
            levelPanel != null && levelPanel.LegacyUnlockAllChallenges);
    }

    private bool IsProviderOnCurrentChallengeTrack(LevelWordListProvider provider)
    {
        string providerPrefix = ChallengeTrackResolver.GetTrackPrefixForChallengeProvider(provider);
        string currentPrefix = IsChineseTrackAvailable()
            ? ChallengeTrackResolver.ChineseTrackPrefix
            : ChallengeTrackResolver.EnglishTrackPrefix;
        return string.Equals(providerPrefix, currentPrefix, StringComparison.Ordinal);
    }

    private static MockupAchievement Achievement(
        string id, string title, string description, bool unlocked,
        bool hasProgress, int progress, int target)
    {
        return MockupWordListProjection.CreateAchievement(
            id, title, description,
            unlocked ? MockupAchievementStatus.Unlocked
                : hasProgress ? MockupAchievementStatus.InProgress : MockupAchievementStatus.Locked,
            unlocked ? "Unlocked" : progress + " / " + target + " words completed");
    }

    private bool IsPublicationLocked(string localListId) =>
        !string.IsNullOrWhiteSpace(localListId)
        && communityAsync != null
        && communityAsync.IsLocalListPublicationLocked(localListId);

    private MockupWordList ProjectWithPublication(WordListLibraryItem item)
    {
        MockupWordList projected = Project(item, false, true);
        if (projected == null) return null;
        MockupCommunityPublicationInfo publication = communityAsync?.Publications?
            .FirstOrDefault(candidate => candidate != null
                && string.Equals(candidate.LocalListId, item.LocalListId, StringComparison.Ordinal));
        if (publication != null)
            return MockupWordListProjection.WithPublication(projected, publication);
        if (!IsPublicationLocked(item.LocalListId))
            return projected;

        return MockupWordListProjection.WithPublication(projected,
            new MockupCommunityPublicationInfo(item.LocalListId, item.Title, string.Empty,
                item.ContentHash, MockupCommunityPublicationState.RecoveryRequired, false,
                "This list has a Workshop binding under another Steam account. Sign in to that account to manage it."));
    }

    private bool IsChineseTrackAvailable()
    {
        PhaseManager phaseManager = Services.Get<PhaseManager>();
        if (phaseManager == null || !phaseManager.AllowChineseAndMixed) return false;
        try
        {
            var locale = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale;
            return locale != null && locale.Identifier.Code == "zh-Hans";
        }
        catch
        {
            return false;
        }
    }

    private static MockupWordList Project(
        WordListLibraryItem item, bool isChallenge, bool isChallengeUnlocked,
        int? challengeStars = null, string unlockRewardLabel = null, int challengeNumber = 0,
        bool challengeCompleted = false, bool revealChallengeDetails = false)
    {
        if (item == null) return null;
        List<string> editorLines = ToEditorLines(item.Content);
        LanguageMode detectedMode = item.LanguageMode;
        if (TryDetectContentLanguage(item.Content, item.LanguageMode, out LanguageMode parsedMode, out _))
            detectedMode = parsedMode;
        WordListRevisionRecord record = item.CurrentRecord;
        int? stars = challengeStars;
        if (!stars.HasValue && record != null && record.HasRecord)
            stars = Math.Max(0, record.highestStars);
        float? bestTime = record != null && record.best != null && record.best.hasResult
            ? (float?)record.best.totalTimeSeconds
            : null;
        List<MockupPreviousRecordSummary> previous = item.ArchivedRecords == null
            ? new List<MockupPreviousRecordSummary>()
            : item.ArchivedRecords.Select(archived => MockupWordListProjection.CreatePreviousRecord(
                archived.contentRevision,
                archived.HasRecord ? (int?)archived.highestStars : null,
                archived.best != null && archived.best.hasResult
                    ? (float?)archived.best.totalTimeSeconds : null)).ToList();

        return MockupWordListProjection.CreateWordList(
            item.LocalListId,
            item.Title,
            item.Description,
            item.Tags == null ? string.Empty : string.Join(", ", item.Tags),
            ToMockupLanguage(item.LanguageMode),
            editorLines,
            item.IsFavorite,
            ParseDate(item.AddedAtUtc),
            ParseDate(item.ModifiedAtUtc),
            ParseDate(item.ContentModifiedAtUtc),
            ParseNullableDate(item.LastPlayedAtUtc),
            stars,
            bestTime,
            item.ContentRevision,
            item.PlayCount,
            previous,
            isChallenge,
            isChallengeUnlocked,
            unlockRewardLabel,
            detectedLanguage: ToMockupLanguage(detectedMode),
            challengeNumber: challengeNumber,
            challengeCompleted: challengeCompleted,
            revealChallengeDetails: isChallenge && revealChallengeDetails);
    }

    private static bool HasChallengeCompletionEvidence(
        WordListLibraryItem item, int challengeIndex, string trackPrefix)
    {
        return item != null
            && (item.PlayCount > 0
                || (item.CurrentRecord != null && item.CurrentRecord.HasRecord)
                || ChallengeProgression.HasCompletedChallengeForTrack(challengeIndex, trackPrefix));
    }

    private static bool TryDetectContentLanguage(WordListLibraryContent content, LanguageMode fallback,
        out LanguageMode detectedMode, out string error)
    {
        detectedMode = fallback;
        if (content == null)
        {
            error = "List content is unavailable.";
            return false;
        }

        List<string> displays = content.Words != null ? content.Words : ToEditorLines(content);
        bool hasTypedPayload = content.LanguageMode == LanguageMode.Chinese
                && content.ChineseWords != null && content.ChineseWords.Any(word => word != null)
            || content.LanguageMode == LanguageMode.Mixed
                && content.MixedWords != null && content.MixedWords.Any(word => word != null);
        if (hasTypedPayload)
            return WordListLanguageDetector.TryDetectDisplayLines(displays, fallback,
                out detectedMode, out _, out error);

        // Old flat files may embed the editor's `display | pinyin` syntax in their raw rows.
        return WordListLanguageDetector.TryDetectEditorLines(displays, fallback,
            out detectedMode, out _, out error);
    }

    private static List<string> ToEditorLines(WordListLibraryContent content)
    {
        if (content == null) return new List<string>();
        if (content.LanguageMode == LanguageMode.English)
            return content.Words == null ? new List<string>() : new List<string>(content.Words);

        if (content.LanguageMode == LanguageMode.Chinese && content.ChineseWords != null)
            return content.ChineseWords.Where(word => word != null)
                .Select(word => AnnotatePinyin(word.display, word.entries))
                .ToList();

        if (content.LanguageMode == LanguageMode.Mixed && content.MixedWords != null)
            return content.MixedWords.Where(word => word != null)
                .Select(FormatMixedEntry)
                .ToList();

        return content.Words == null ? new List<string>() : new List<string>(content.Words);
    }

    private static string FormatMixedEntry(MixedWordEntry word)
    {
        var display = new StringBuilder();
        var pinyin = new List<string>();
        bool hasIncompletePinyin = false;
        if (word != null && word.segments != null)
        {
            foreach (MixedSegmentData segment in word.segments)
            {
                if (segment == null) continue;
                if (segment.type == "english")
                    display.Append(segment.text);
                else if (segment.type == "chinese" && segment.entries != null)
                {
                    foreach (ChinesePhaseEntry entry in segment.entries)
                    {
                        if (entry == null) continue;
                        display.Append(entry.character);
                        if (string.IsNullOrWhiteSpace(entry.pinyin)) hasIncompletePinyin = true;
                        pinyin.Add(entry.pinyin ?? string.Empty);
                    }
                }
            }
        }

        string text = display.ToString();
        return pinyin.Count > 0 && !hasIncompletePinyin
            ? text + " | " + string.Join(" ", pinyin)
            : text;
    }

    private static string AnnotatePinyin(string display, List<ChinesePhaseEntry> entries)
    {
        if (entries == null || entries.Count == 0 || entries.Any(entry => entry == null
            || string.IsNullOrWhiteSpace(entry.pinyin)))
            return display ?? string.Empty;
        return (display ?? string.Empty) + " | "
            + string.Join(" ", entries.Select(entry => entry.pinyin));
    }

    private static bool IsGameplayContentChanged(WordListLibraryItem item, IEnumerable<string> editorRows)
    {
        if (item == null) return false;
        List<string> currentRows = WordListLibraryContentCodec.FilterEmptyEditorRows(
            item.Content == null ? null : ToEditorLines(item.Content));
        return !WordsEqual(currentRows, editorRows);
    }

    private static bool WordsEqual(IEnumerable<string> left, IEnumerable<string> right)
    {
        return (left ?? Enumerable.Empty<string>()).Select(word => (word ?? string.Empty).Trim())
            .SequenceEqual((right ?? Enumerable.Empty<string>())
                .Select(word => (word ?? string.Empty).Trim()), StringComparer.Ordinal);
    }

    private static bool ValidatePinyinForContentChange(IEnumerable<string> lines, out string error)
    {
        List<string> words = (lines ?? Enumerable.Empty<string>()).ToList();
        for (int i = 0; i < words.Count; i++)
        {
            string line = words[i] ?? string.Empty;
            int separator = line.IndexOf('|');
            string display = separator < 0 ? line : line.Substring(0, separator);
            if (PhaseManager.ContainsHanCharacters(display) && separator < 0)
            {
                error = "Word " + (i + 1) + ": add one lowercase pinyin syllable per Chinese character. "
                    + "Correct every Chinese row before changing playable content.";
                return false;
            }
            if (separator >= 0 && line.Substring(separator + 1)
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                .Any(syllable => string.Equals(syllable, "xxx", StringComparison.Ordinal)))
            {
                error = "Word " + (i + 1) + ": the pinyin lookup could not suggest a reading. "
                    + "Replace each ‘xxx’ with the correct lowercase pinyin before saving.";
                return false;
            }
        }
        if (!MockupWordContent.TryValidateWordText(string.Join("\n", words), null, out error))
            return false;
        return true;
    }

    private static string SuggestMissingPinyin(string line)
    {
        if (string.IsNullOrEmpty(line) || line.IndexOf('|') >= 0)
            return line ?? string.Empty;
        if (!PhaseManager.ContainsHanCharacters(line))
            return line;

        var syllables = new List<string>();
        foreach (char character in line)
            if (PinyinLookup.IsChinese(character))
                syllables.Add(PinyinLookup.Get(character));
        return syllables.Count == 0
            ? line
            : line + " | " + string.Join(" ", syllables);
    }

    private static IEnumerable<string> ParseTags(string text)
    {
        return (text ?? string.Empty).Split(',').Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0);
    }

    private static MockupLanguage ToMockupLanguage(LanguageMode language)
    {
        return (MockupLanguage)Enum.Parse(typeof(MockupLanguage), language.ToString());
    }

    private static bool SameProvider(LevelWordListProvider first, LevelWordListProvider second)
    {
        if (first == null || second == null) return false;
        if (!string.IsNullOrEmpty(first.GetListKey())
            && string.Equals(first.GetListKey(), second.GetListKey(), StringComparison.Ordinal))
            return true;
        try
        {
            return string.Equals(Path.GetFullPath(first.FilePath), Path.GetFullPath(second.FilePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static DateTime ParseDate(string value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out DateTime result)
            ? result.ToUniversalTime()
            : DateTime.MinValue;
    }

    private static DateTime? ParseNullableDate(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? (DateTime?)null : ParseDate(value);
    }

    private static bool Contains(string value, string term) =>
        !string.IsNullOrEmpty(value)
        && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

    private static IEnumerable<MockupWordList> Sort(
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
                ordered = source.OrderBy(item => item.AddedAt)
                    .ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
                break;
            case MockupSortMode.Modified:
                ordered = source.OrderBy(item => item.ModifiedAt)
                    .ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
                break;
            case MockupSortMode.WordCount:
                ordered = source.OrderBy(item => item.WordCount)
                    .ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
                break;
            case MockupSortMode.PersonalStars:
                ordered = source.Where(item => item.PersonalStars.HasValue)
                    .OrderBy(item => item.PersonalStars.Value)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .Concat(source.Where(item => !item.PersonalStars.HasValue)
                        .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(item => item.Id, StringComparer.Ordinal)).ToList();
                break;
            default:
                ordered = source.Where(item => item.LastPlayedAt.HasValue)
                    .OrderBy(item => item.LastPlayedAt.Value)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .Concat(source.Where(item => !item.LastPlayedAt.HasValue)
                        .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(item => item.Id, StringComparer.Ordinal)).ToList();
                break;
        }
        if (!ascending) ordered.Reverse();
        if (sortMode == MockupSortMode.PersonalStars || sortMode == MockupSortMode.RecentlyPlayed)
        {
            IEnumerable<MockupWordList> populated = sortMode == MockupSortMode.PersonalStars
                ? ordered.Where(item => item.PersonalStars.HasValue)
                : ordered.Where(item => item.LastPlayedAt.HasValue);
            IEnumerable<MockupWordList> empty = sortMode == MockupSortMode.PersonalStars
                ? ordered.Where(item => !item.PersonalStars.HasValue)
                : ordered.Where(item => !item.LastPlayedAt.HasValue);
            ordered = populated.Concat(empty).ToList();
        }
        return ordered;
    }
}
