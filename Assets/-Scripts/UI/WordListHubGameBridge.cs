using System;
using System.Collections.Concurrent;
using SFB;
using UnityEngine;
using UnityEngine.UI;
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
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private volatile bool isShuttingDown;

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

        backend = new WordListHubProductionBackend(
            WordListLibraryService.Shared,
            levelPanelController,
            () => gameCoordinator.HasAchievementProgressSnapshot
                ? gameCoordinator.AchievementProgressSnapshot
                : default);

        hubController.PlayRequested += HandlePlayRequested;
        hubController.HubVisibilityChanged += HandleHubVisibilityChanged;
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
        if (hubController != null)
        {
            hubController.PlayRequested -= HandlePlayRequested;
            hubController.HubVisibilityChanged -= HandleHubVisibilityChanged;
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
        if (gameCoordinator != null)
            gameCoordinator.SetHubVisible(visible);
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

        if (!WordListLibraryService.Shared.MarkPlayed(provider, out string recordError))
            Debug.LogWarning("[WordListHubGameBridge] The run started, but last-played metadata was not saved: "
                + recordError, this);

        // Production Play has no mock-result callback. The active GameCoordinator
        // records the completed run and this bridge reopens the correct Hub page.
        hubController.HideHub();
    }

    private void HandleRunCompleted(GameRunCompletionSnapshot snapshot)
    {
        if (snapshot == null || hubController == null) return;
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

        hubController.ShowCompletedRun(
            snapshot.LocalListId,
            snapshot.IsChallenge,
            snapshot.RecordPersisted,
            snapshot.Stars,
            snapshot.TotalTimeSeconds,
            snapshot.RecordError);
    }
}
