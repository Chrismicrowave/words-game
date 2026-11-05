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
/// Community actions are delegated to a private fixture store and never enter the real Library.
/// </summary>
public sealed class WordListHubProductionBackend : IWordListHubBackend
{
    private sealed class PendingImport
    {
        public string path;
        public string contentHash;
        public WordListLibraryContent content;
    }

    private readonly WordListLibraryService library;
    private readonly LevelPanelController levelPanel;
    private readonly Func<WordAchievementProgressSnapshot> achievementSnapshot;
    private readonly MockupWordListStore communityFixtures = MockupWordListStore.CreateCommunityPreviewFixtures();
    private readonly Dictionary<string, PendingImport> pendingImports =
        new Dictionary<string, PendingImport>(StringComparer.Ordinal);

    public bool CanCreateLists => IsLibraryOpen && levelPanel != null && levelPanel.CanCreateListsFromHub;
    public bool CanEditLists => IsLibraryOpen && levelPanel != null && levelPanel.CanEditListsFromHub;
    public bool CanDeleteLists => IsLibraryOpen && levelPanel != null && levelPanel.CanDeleteListsFromHub;
    public bool CanDuplicateLists => IsLibraryOpen && levelPanel != null && levelPanel.CanDuplicateListsFromHub;
    public bool CanImportLists => IsLibraryOpen && levelPanel != null && levelPanel.CanImportListsFromHub;
    public bool CanExportLists => IsLibraryOpen && levelPanel != null && levelPanel.CanExportListsFromHub;

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
    {
        this.library = library ?? WordListLibraryService.Shared;
        this.levelPanel = levelPanel;
        this.achievementSnapshot = achievementSnapshot;
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
        IEnumerable<MockupWordList> results = library.GetAll()
            .Select(item => Project(item, false, true));

        switch (query.View)
        {
            case MockupLibraryView.Favorites:
                results = results.Where(item => item.IsFavorite);
                break;
            case MockupLibraryView.MyLists:
                break;
            case MockupLibraryView.CommunityDownloads:
            case MockupLibraryView.MyPublished:
                // The production local Library does not expose fixture downloads or Workshop.
                return Array.Empty<MockupWordList>();
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
        int completedForQueriedTrack = Math.Max(0, unlockedCount - 1);
        GameConfigProfile profile = Services.Get<PhaseManager>() != null
            ? Services.Get<PhaseManager>().ActiveProfile
            : null;
        var results = new List<MockupWordList>(ordered.Count);
        int customUnlockRequirement = levelPanel != null
            ? levelPanel.CustomUnlockRequirementForHub : 5;
        int communityUnlockRequirement = levelPanel != null
            ? levelPanel.CommunityUnlockRequirementForHub : 10;
        bool customMilestoneVisible = IsUnlockMilestoneVisible(
            customUnlockRequirement, completedForQueriedTrack,
            levelPanel != null && levelPanel.IsLibraryUnlockBypassedFromHub);
        bool communityMilestoneVisible = IsUnlockMilestoneVisible(
            communityUnlockRequirement, completedForQueriedTrack,
            levelPanel != null && levelPanel.IsCommunityEnabledFromHub
                && levelPanel.IsCommunityUnlockBypassedFromHub);

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
            string rewardLabel = GetUnlockRewardLabel(index + 1,
                customUnlockRequirement, customMilestoneVisible,
                communityUnlockRequirement, communityMilestoneVisible);
            results.Add(Project(item, true, unlocked, trackStars, rewardLabel));
        }
        return results.AsReadOnly();
    }

    public IReadOnlyList<MockupDiscoveryItem> QueryDiscoveryWithFilters(MockupDiscoveryQuery query) =>
        IsCommunityOpen
            ? communityFixtures.QueryDiscoveryWithFilters(query)
            : Array.Empty<MockupDiscoveryItem>();

    public MockupWordList FindById(string localListId)
    {
        if (string.IsNullOrEmpty(localListId)) return null;
        MockupWordList challenge = FindChallengeById(localListId);
        if (challenge != null) return challenge;

        if (!IsLibraryOpen) return null;
        WordListLibraryItem item = library.FindById(localListId);
        return item != null ? Project(item, false, true) : null;
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
        IsCommunityOpen ? communityFixtures.FindCommunityDownload(remoteItemId) : null;

    public MockupDiscoveryItem FindDiscoveryItem(string remoteItemId) =>
        IsCommunityOpen ? communityFixtures.FindDiscoveryItem(remoteItemId) : null;

    public MockupWordList EditCommunityCopy(string localListId)
    {
        // This mock action creates a new owned list, so it needs both pages and
        // the active profile's existing create/duplicate capabilities.
        if (!IsCommunityOpen || !IsLibraryOpen || !CanCreateLists || !CanDuplicateLists)
            return null;
        return communityFixtures.EditCommunityCopy(localListId);
    }

    public MockupWordListEdit CreateEditDraft(string localListId)
    {
        if (!IsLibraryOpen) return null;
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
        IsCommunityOpen ? communityFixtures.TryToggleLike(remoteItemId)
            : new MockupActionResult(false, LockedPageMessage(WordListHubPage.Community));

    public MockupActionResult TryDownload(string remoteItemId)
    {
        if (!IsCommunityOpen)
            return new MockupActionResult(false, LockedPageMessage(WordListHubPage.Community));
        if (!IsLibraryOpen)
            return new MockupActionResult(false, LockedPageMessage(WordListHubPage.Library));
        return communityFixtures.TryDownload(remoteItemId);
    }

    public MockupActionResult TryReportAndHide(string remoteItemId) =>
        IsCommunityOpen ? communityFixtures.TryReportAndHide(remoteItemId)
            : new MockupActionResult(false, LockedPageMessage(WordListHubPage.Community));

    public MockupPlayResult PlayDiscoveryItem(string remoteItemId) =>
        IsCommunityOpen ? communityFixtures.PlayDiscoveryItem(remoteItemId)
            : MockupWordListProjection.CreateBlockedPlayResult(
                LockedPageMessage(WordListHubPage.Community));

    public bool CompleteRun(string localListId, int stars, float bestTimeSeconds)
    {
        // Only the isolated Community sample flow calls this legacy mock action.
        return IsCommunityOpen && communityFixtures.CompleteRun(localListId, stars, bestTimeSeconds);
    }

    public MockupActionResult TryPublish(string localListId)
    {
        if (!IsCommunityOpen)
            return new MockupActionResult(false, LockedPageMessage(WordListHubPage.Community));
        if (!IsLibraryOpen)
            return new MockupActionResult(false, LockedPageMessage(WordListHubPage.Library));
        return new MockupActionResult(false, "Publishing is not connected in this production build.");
    }

    public MockupActionResult TryUnpublish(string localListId)
    {
        if (!IsCommunityOpen)
            return new MockupActionResult(false, LockedPageMessage(WordListHubPage.Community));
        if (!IsLibraryOpen)
            return new MockupActionResult(false, LockedPageMessage(WordListHubPage.Library));
        return new MockupActionResult(false, "Publishing is not connected in this production build.");
    }

    public MockupActionResult TryDeleteList(string localListId)
    {
        if (!IsLibraryOpen)
            return new MockupActionResult(false, LockedPageMessage(WordListHubPage.Library));
        WordListLibraryItem item = library.FindById(localListId);
        if (item == null)
            return new MockupActionResult(false, "The selected list could not be found.");
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
        List<string> rows = displayWords == null ? new List<string>() : displayWords.ToList();
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
        if (levelPanel != null && !levelPanel.CanCreateListFromHub(
            draft.Words == null ? 0 : draft.Words.Count, out error))
            return false;
        if (!ValidatePinyinForContentChange(draft.Words, out error))
            return false;
        if (!WordListLibraryContentCodec.TryParseAutoEditorLines(
            draft.Words, pending.content.LanguageMode, pending.content,
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
        if (edit == null) return MockupEditResult.Failure("The edit form is unavailable.");
        WordListLibraryItem current = library.FindById(localListId);
        if (current == null || !current.IsEditable || current.IsChallenge)
            return MockupEditResult.Failure("This list is read-only or no longer available.");

        bool contentChanged = IsGameplayContentChanged(current, edit);
        if (levelPanel != null)
        {
            WordListLibraryContent existingContent = current.Content
                ?? (current.Provider != null ? WordListLibraryContent.FromProvider(current.Provider) : null);
            int existingWordCount = existingContent != null && existingContent.Words != null
                ? existingContent.Words.Count : 0;
            if (!levelPanel.CanEditListsFromHub)
                return MockupEditResult.Failure("Editing custom lists is disabled by the active profile.");
            if (!levelPanel.CanEditListFromHub(
                edit.Words == null ? 0 : edit.Words.Count, contentChanged,
                out string policyError, existingWordCount))
                return MockupEditResult.Failure(policyError);
        }
        WordListLibraryContent contentOrNull = null;
        if (contentChanged)
        {
            if (!ValidatePinyinForContentChange(edit.Words, out string pinyinError))
                return MockupEditResult.Failure(pinyinError);
            WordListLibraryContent originalContent = current.Content
                ?? (current.Provider != null ? WordListLibraryContent.FromProvider(current.Provider) : null);
            LanguageMode fallback = originalContent != null ? originalContent.LanguageMode : current.LanguageMode;
            if (!WordListLibraryContentCodec.TryParseAutoEditorLines(
                edit.Words, fallback, originalContent,
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

        bool contentChanged = IsGameplayContentChanged(source, edit);
        WordListLibraryContent sourceContent = source.Content ?? WordListLibraryContent.FromProvider(source.Provider);
        if (levelPanel != null && (!levelPanel.CanCreateListFromHub(
            edit.Words == null ? 0 : edit.Words.Count, out string policyError)
            || !levelPanel.CanDuplicateListsFromHub))
            return MockupEditResult.Failure(policyError ?? "Copying lists is disabled by the active profile.");
        WordListLibraryItem created;
        string error;
        if (contentChanged && !ValidatePinyinForContentChange(edit.Words, out string pinyinError))
            return MockupEditResult.Failure(pinyinError);
        if (!WordListLibraryContentCodec.TryParseAutoEditorLines(
            edit.Words, sourceContent.LanguageMode, sourceContent,
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

    private static bool IsUnlockMilestoneVisible(int requiredChallenges,
        int completedForQueriedTrack, bool isOverridden)
    {
        if (requiredChallenges <= 0) return false;
        // A bypass suppresses only a milestone still ahead on this exact track.
        // Reaching the threshold on a different language track cannot hide it.
        return !isOverridden || completedForQueriedTrack >= requiredChallenges;
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
        int? challengeStars = null, string unlockRewardLabel = null)
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
            detectedLanguage: ToMockupLanguage(detectedMode));
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

    private static bool IsGameplayContentChanged(WordListLibraryItem item, MockupWordListEdit edit)
    {
        if (item == null || edit == null) return false;
        return !WordsEqual(item.Content == null ? null : ToEditorLines(item.Content), edit.Words);
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
