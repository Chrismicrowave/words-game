using System;
using System.Collections.Concurrent;
using SFB;
using UnityEngine;
using UnityEngine.UI;
using Words.Community;
using Words.UiMockups.WordListHub;

/// <summary>
/// Connects the authored Word List Hub to production gameplay and the local Library.
/// The Hub controller remains UI-only; this bridge owns the service and game callbacks.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class WordListHubGameBridge : MonoBehaviour
{
    [SerializeField] private WordListHubMockupController hubController;
    [SerializeField] private LevelPanelController levelPanelController;
    [SerializeField] private GameCoordinator gameCoordinator;
    [SerializeField] private LevelCompletePanelController levelCompletePanel;
    [SerializeField] private AudioSource menuClickAudioSource;

    private WordListHubProductionBackend backend;
    private WordListHubCommunityFacade communityFacade;
    private IDisposable publicationResolverLease;
    private Action<string> openExternalUrl;
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private volatile bool isShuttingDown;
    private float nextCommunityPollTime;

    public WordListHubMockupController HubController => hubController;
    public AudioSource MenuClickAudioSource => menuClickAudioSource;

    private void Awake()
    {
        // The bridge is an active sibling of the production Hub host, so it can
        // normalize an editor-active Canvas before the first frame is rendered.
        // The controller defers production initialization until Start injects the backend.
        if (hubController != null)
            hubController.SetHubVisible(false);
    }

    private void Start()
    {
        if (hubController == null || levelPanelController == null || gameCoordinator == null
            || menuClickAudioSource == null)
        {
            Debug.LogError("[WordListHubGameBridge] Required production scene references are missing.", this);
            enabled = false;
            return;
        }
        if (!hubController.IsProductionMode)
        {
            Debug.LogError("[WordListHubGameBridge] The production Hub controller is not in Production Mode.", this);
            enabled = false;
            return;
        }

        publicationResolverLease = CommunityServiceRegistry.RegisterPublicationRequestResolver(
            ResolveCommunityPublicationRequest);
        openExternalUrl = Application.OpenURL;
        communityFacade = new WordListHubCommunityFacade(
            CommunityServiceRegistry.GetShared(), QueueMainThread);
        backend = new WordListHubProductionBackend(
            WordListLibraryService.Shared,
            levelPanelController,
            () => gameCoordinator.HasAchievementProgressSnapshot
                ? gameCoordinator.AchievementProgressSnapshot
                : default,
            communityFacade);

        hubController.PlayRequested += HandlePlayRequested;
        hubController.CommunityPlayRequested += HandleCommunityPlayRequested;
        hubController.SteamCommunityPageRequested += HandleSteamCommunityPageRequested;
        hubController.HubVisibilityChanged += HandleHubVisibilityChanged;
        hubController.PageChanged += HandleHubPageChanged;
        hubController.ImportRequested += HandleImportRequested;
        hubController.ExportRequested += HandleExportRequested;
        gameCoordinator.RunCompleted += HandleRunCompleted;

        // Keep the scene-authored canvas inactive at startup. Its OnEnable/Awake path
        // initializes on the first user request, without creating preview fixtures.
        hubController.SetButtonClickAudioSource(menuClickAudioSource);
        hubController.InitializeProductionBackend(backend);
    }

    private void OnDestroy()
    {
        isShuttingDown = true;
        while (mainThreadActions.TryDequeue(out _)) { }
        publicationResolverLease?.Dispose();
        publicationResolverLease = null;
        communityFacade?.Dispose();
        if (hubController != null)
        {
            hubController.PlayRequested -= HandlePlayRequested;
            hubController.CommunityPlayRequested -= HandleCommunityPlayRequested;
            hubController.SteamCommunityPageRequested -= HandleSteamCommunityPageRequested;
            hubController.HubVisibilityChanged -= HandleHubVisibilityChanged;
            hubController.PageChanged -= HandleHubPageChanged;
            hubController.ImportRequested -= HandleImportRequested;
            hubController.ExportRequested -= HandleExportRequested;
        }
        if (gameCoordinator != null)
        {
            gameCoordinator.RunCompleted -= HandleRunCompleted;
            if (gameCoordinator.IsHubVisible)
                gameCoordinator.SetHubVisible(false);
        }
    }

    private void OnApplicationQuit()
    {
        isShuttingDown = true;
        while (mainThreadActions.TryDequeue(out _)) { }
    }

    private void Update()
    {
        if (isShuttingDown) return;
        if (communityFacade != null && Time.unscaledTime >= nextCommunityPollTime)
        {
            nextCommunityPollTime = Time.unscaledTime + 1f;
            communityFacade.PollConnection();
        }
        while (mainThreadActions.TryDequeue(out Action action))
        {
            if (isShuttingDown) return;
            try { action?.Invoke(); }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                if (hubController != null)
                    hubController.ShowExternalMessage("The file operation could not be completed.", true);
            }
        }
    }

    private void HandleImportRequested()
    {
        if (backend == null || levelPanelController == null) return;
        if (!levelPanelController.CanImportListFromHub(out string gateError))
        {
            hubController.ShowExternalMessage(gateError, true);
            return;
        }

        var filters = new[] { new ExtensionFilter("Text Files", "txt") };
        try
        {
            StandaloneFileBrowser.OpenFilePanelAsync("Review word list import", "", filters, false, paths =>
            {
                if (paths == null || paths.Length == 0 || string.IsNullOrWhiteSpace(paths[0])) return;
                string selectedPath = paths[0];
                QueueMainThread(() => PrepareImport(selectedPath));
            });
        }
        catch (Exception exception)
        {
            hubController.ShowExternalMessage("The file picker could not be opened: " + exception.Message, true);
        }
    }

    private void PrepareImport(string sourcePath)
    {
        if (isShuttingDown || backend == null || hubController == null) return;
        if (!backend.TryPrepareImport(sourcePath, out string token, out MockupWordListEdit draft,
            out string error))
        {
            hubController.ShowExternalMessage(error, true);
            return;
        }
        hubController.BeginImportReview(token, draft);
    }

    private void HandleExportRequested(string localListId)
    {
        if (backend == null || levelPanelController == null) return;
        if (!levelPanelController.CanExportListFromHub(out string gateError))
        {
            hubController.ShowExternalMessage(gateError, true);
            return;
        }

        MockupWordList item = backend.FindById(localListId);
        if (item == null)
        {
            hubController.ShowExternalMessage("The selected list is no longer available.", true);
            return;
        }
        string defaultName = string.IsNullOrWhiteSpace(item.Title) ? "wordlist" : item.Title.Replace(" ", "_");
        var filters = new[] { new ExtensionFilter("Text Files", "txt") };
        try
        {
            StandaloneFileBrowser.SaveFilePanelAsync("Export entries", "", defaultName, filters, path =>
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                string destinationPath = path;
                QueueMainThread(() => ExportEntries(localListId, destinationPath));
            });
        }
        catch (Exception exception)
        {
            hubController.ShowExternalMessage("The file picker could not be opened: " + exception.Message, true);
        }
    }

    private void ExportEntries(string localListId, string destinationPath)
    {
        if (isShuttingDown || backend == null || hubController == null) return;
        if (!backend.TryExportList(localListId, destinationPath, out string error))
        {
            hubController.ShowExternalMessage(error, true);
            return;
        }
        hubController.ShowExternalMessage(
            "Exported the word-list entries. Library metadata is not included.");
    }

    private void QueueMainThread(Action action)
    {
        if (action != null && !isShuttingDown)
            mainThreadActions.Enqueue(action);
    }

    public void ShowChallenges()
    {
        if (hubController == null) return;
        hubController.ShowHub(WordListHubPage.Challenges);
    }

    public void AttachMenuClickSound(Button button)
    {
        if (button == null || menuClickAudioSource == null) return;
        WordListHubButtonSoundRelay.Attach(button, menuClickAudioSource);
    }

    private void HandleHubVisibilityChanged(bool visible)
    {
        if (visible)
            communityFacade?.PollConnection(force: true);
        if (gameCoordinator != null)
            gameCoordinator.SetHubVisible(visible);
    }

    private void HandleHubPageChanged(WordListHubPage page)
    {
        if (page == WordListHubPage.Community && hubController != null && hubController.IsHubVisible)
            communityFacade?.PollConnection(force: true);
    }

    private CommunityPublishRequest ResolveCommunityPublicationRequest(string localListId)
    {
        if (isShuttingDown || string.IsNullOrWhiteSpace(localListId)) return null;
        WordListLibraryItem item = WordListLibraryService.Shared.FindById(localListId);
        if (item == null || !string.Equals(item.LocalListId, localListId, StringComparison.Ordinal)
            || !item.IsEditable || item.IsChallenge || item.Content == null)
            return null;

        return new CommunityPublishRequest(item.LocalListId, item.Title, item.Description,
            Array.Empty<string>(), item.Content.Clone());
    }

    private void HandleSteamCommunityPageRequested(string pageUri)
    {
        if (isShuttingDown || !TryValidateSteamItemUri(pageUri, out string validatedUri))
        {
            hubController?.ShowExternalMessage(
                "Steam did not provide a safe Workshop item page. No page was opened.", true);
            return;
        }

        try
        {
            openExternalUrl?.Invoke(validatedUri);
        }
        catch (Exception exception)
        {
            hubController?.ShowExternalMessage("The Steam Workshop page could not be opened: "
                + exception.Message, true);
        }
    }

    private static bool TryValidateSteamItemUri(string value, out string validatedUri)
    {
        validatedUri = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "steamcommunity.com", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.AbsolutePath.TrimEnd('/'), "/sharedfiles/filedetails",
                StringComparison.OrdinalIgnoreCase))
            return false;
        validatedUri = uri.AbsoluteUri;
        return true;
    }

    private void HandlePlayRequested(string localListId)
    {
        MockupWordList item = backend != null ? backend.FindById(localListId) : null;
        LevelWordListProvider provider = backend != null ? backend.FindProvider(localListId) : null;
        if (item == null || provider == null)
        {
            hubController.ShowExternalMessage("The selected list is no longer available.", true);
            return;
        }

        LevelTab tab = item.IsChallenge ? LevelTab.Challenges : LevelTab.Custom;
        if (item.IsChallenge && !item.IsChallengeUnlocked)
        {
            hubController.ShowExternalMessage("This challenge is still locked.", true);
            return;
        }

        if (!levelPanelController.TryStartProvider(provider, tab, out string error))
        {
            hubController.ShowExternalMessage(error, true);
            return;
        }

        if (!item.IsChallenge
            && !WordListLibraryService.Shared.MarkPlayed(provider, out string recordError))
            Debug.LogWarning("[WordListHubGameBridge] The run started, but last-played metadata was not saved: "
                + recordError, this);

        // Production Play has no mock-result callback. The active GameCoordinator
        // records the completed run and this bridge reopens the correct Hub page.
        hubController.HideHub();
    }

    private void HandleCommunityPlayRequested(string remoteItemId, string contentHash)
    {
        if (isShuttingDown || backend == null || communityFacade == null
            || hubController == null || levelPanelController == null)
            return;

        if (!communityFacade.TryGetCachedProvider(remoteItemId, contentHash,
            out Words.Community.CommunityWordListProvider provider, out string cacheError))
        {
            hubController.ShowExternalMessage(cacheError, true);
            return;
        }

        ulong accountId = communityFacade.EffectiveLocalAccountId;
        if (!levelPanelController.TryStartCommunityProvider(provider, accountId, out string error))
        {
            hubController.ShowExternalMessage(error, true);
            return;
        }

        // Community identity is remote/app/hash scoped; it never becomes a canonical Library ID.
        hubController.HideHub();
    }

    private void HandleRunCompleted(GameRunCompletionSnapshot snapshot)
    {
        if (snapshot == null || hubController == null) return;
        if (snapshot.SourceKind == GameRunSourceKind.Community)
        {
            string remoteItemId = snapshot.CommunityKey != null
                ? snapshot.CommunityKey.RemoteItemId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
            hubController.ShowCommunityCompletedRun(remoteItemId,
                snapshot.CommunityContentHash,
                snapshot.RecordPersisted,
                snapshot.Stars,
                snapshot.TotalTimeSeconds,
                snapshot.RecordError);
            return;
        }
        if (snapshot.IsChallenge)
        {
            // The existing completion panel retains its Enter-to-next behavior while
            // the Hub is closed. Its explicit return button opens Challenges.
            hubController.RememberCompletedChallenge(
                snapshot.LocalListId, snapshot.RecordPersisted, snapshot.RecordError);
            if (!snapshot.RecordPersisted)
            {
                if (levelCompletePanel == null)
                    levelCompletePanel = FindFirstObjectByType<LevelCompletePanelController>(FindObjectsInactive.Include);
                if (levelCompletePanel != null)
                    levelCompletePanel.ShowRecordPersistenceError(snapshot.RecordError);
            }
            return;
        }

        // Custom-list completion remains on the existing game result panel.
        // Showing the Hub here would interrupt the normal next-word/return flow.
    }
}
