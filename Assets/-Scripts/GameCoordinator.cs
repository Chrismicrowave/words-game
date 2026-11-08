// Assets/-Scripts/GameCoordinator.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Words.Community;

public enum GameRunSourceKind
{
    Challenge,
    Custom,
    Community
}

public sealed class GameRunCompletionSnapshot
{
    public string LocalListId { get; }
    public IWordListProvider Provider { get; }
    public LanguageMode LanguageMode { get; }
    public LevelMode Mode { get; }
    public int PhaseCount { get; }
    public int Errors { get; }
    public float TotalTimeSeconds { get; }
    public int Stars { get; }
    public bool IsChallenge { get; }
    public string ChallengeTrackPrefix { get; }
    public int ChallengeIndex { get; }
    public bool RecordPersisted { get; }
    public string RecordError { get; }
    public bool HasAchievementProgressSnapshot { get; }
    public WordAchievementProgressSnapshot AchievementProgressSnapshot { get; }
    public GameRunSourceKind SourceKind => Provider is CommunityWordListProvider
        ? GameRunSourceKind.Community
        : IsChallenge ? GameRunSourceKind.Challenge : GameRunSourceKind.Custom;
    public CommunityItemKey CommunityKey => communityRunIdentity?.Key;
    public string CommunityContentHash => communityRunIdentity?.Version?.ContentHash ?? string.Empty;
    public ulong CommunityAccountId => communityRunIdentity != null ? communityRunIdentity.AccountId : 0;

    private readonly CommunityGameplayRunIdentity communityRunIdentity;

    public GameRunCompletionSnapshot(string localListId, IWordListProvider provider,
        LanguageMode languageMode, LevelMode mode, int phaseCount, int errors,
        float totalTimeSeconds, int stars, bool isChallenge, string challengeTrackPrefix,
        int challengeIndex, bool recordPersisted, string recordError, bool hasAchievementProgressSnapshot,
        WordAchievementProgressSnapshot achievementProgressSnapshot)
        : this(localListId, provider, languageMode, mode, phaseCount, errors, totalTimeSeconds,
            stars, isChallenge, challengeTrackPrefix, challengeIndex, recordPersisted, recordError,
            hasAchievementProgressSnapshot, achievementProgressSnapshot, null)
    {
    }

    public GameRunCompletionSnapshot(string localListId, IWordListProvider provider,
        LanguageMode languageMode, LevelMode mode, int phaseCount, int errors,
        float totalTimeSeconds, int stars, bool isChallenge, string challengeTrackPrefix,
        int challengeIndex, bool recordPersisted, string recordError, bool hasAchievementProgressSnapshot,
        WordAchievementProgressSnapshot achievementProgressSnapshot,
        CommunityGameplayRunIdentity communityRunIdentity)
    {
        LocalListId = localListId;
        Provider = provider;
        LanguageMode = languageMode;
        Mode = mode;
        PhaseCount = phaseCount;
        Errors = errors;
        TotalTimeSeconds = totalTimeSeconds;
        Stars = stars;
        IsChallenge = isChallenge;
        ChallengeTrackPrefix = challengeTrackPrefix;
        ChallengeIndex = challengeIndex;
        RecordPersisted = recordPersisted;
        RecordError = recordError ?? string.Empty;
        HasAchievementProgressSnapshot = hasAchievementProgressSnapshot;
        AchievementProgressSnapshot = achievementProgressSnapshot;
        this.communityRunIdentity = communityRunIdentity;
    }
}

public class GameCoordinator : MonoBehaviour
{
    [Header("Systems")]
    [SerializeField] private KeyboardVisualController keyboardVisual;

    [Header("Settings")]
    [SerializeField] private Texture2D customCursor;
    [SerializeField] private Vector2 cursorHotspot = Vector2.zero;

    [Header("Config")]
    [SerializeField] private GameConfig config;


    [Header("Word List")]
    [SerializeField] private FixedWordListProvider defaultWordList;

    [Header("Complete Panel")]
    [SerializeField] private LevelCompletePanelController levelCompletePanel;

    private WordEngine wordEngine;
    private ILeaderboardService leaderboardService;
    private UIController uiController;
    private WordAchievementProgress wordAchievementProgress;
    private IWordAchievementPlatform wordAchievementPlatform;
    private SteamClientSessionLease steamClientSessionLease;
    private ISteamClientSession steamClientSession;
    private bool wordAchievementProfileReady;
    private bool wordAchievementStorageWarningLogged;
    private bool wordAchievementStatsCallbackSubscribed;
    private string boundWordAchievementUserId;
    private float nextWordAchievementProfileRetryTime;
    private float nextWordAchievementInitRetryTime;
    private float nextWordAchievementPersistenceRetryTime;
    private bool hubVisible;
    private bool hubTimerWasRunning;
    private Func<bool> debugKeyDownCallback;
    private CommunityGameplayRunIdentity activeCommunityRunIdentity;

    public event Action<GameRunCompletionSnapshot> RunCompleted;
    public bool IsHubVisible => hubVisible;
    public bool HasAchievementProgressSnapshot => wordAchievementProfileReady && wordAchievementProgress != null;
    public WordAchievementProgressSnapshot AchievementProgressSnapshot =>
        HasAchievementProgressSnapshot ? wordAchievementProgress.Snapshot : default;

    void Start()
    {
        Cursor.SetCursor(customCursor, cursorHotspot, CursorMode.Auto);

        wordEngine = new WordEngine();
        leaderboardService = new NullLeaderboardService();

        uiController = FindAnyObjectByType<UIController>();
        var phaseManager = Services.Get<PhaseManager>();
        if (phaseManager == null)
        {
            Debug.LogError("[GameCoordinator] PhaseManager is unavailable; gameplay was not started.");
            return;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        TryAttachSteamDiagnostics(phaseManager.ActiveProfile);
#endif

        try
        {
            steamClientSessionLease = SteamClientSessionRegistry.AcquireRuntime();
            steamClientSession = steamClientSessionLease.Session;
        }
        catch (System.Exception exception)
        {
            LogWordAchievementStorageWarning("Steam client session is already owned or unavailable; continuing locally. "
                + exception.Message);
        }

        try
        {
            wordAchievementPlatform = steamClientSession != null
                ? WordAchievementPlatformRegistry.CreateOrLocalOnly(steamClientSession)
                : WordAchievementPlatformRegistry.CreateLocalOnly();
        }
        catch (System.Exception exception)
        {
            wordAchievementPlatform = WordAchievementPlatformRegistry.CreateLocalOnly();
            LogWordAchievementStorageWarning("Steam achievement integration is unavailable; continuing locally. "
                + exception.Message);
        }
        TryInitializeWordAchievementPlatform();

        try
        {
            string achievementProfileDirectory = System.IO.Path.Combine(
                Application.persistentDataPath, "WordAchievementProfiles");
            wordAchievementProgress = new WordAchievementProgress(achievementProfileDirectory);
            string initialSteamId = GetCurrentWordAchievementUserId();
            if (TryActivateWordAchievementProfile(initialSteamId))
            {
                boundWordAchievementUserId = initialSteamId;
                SubmitWordAchievementProgress();
            }
        }
        catch (System.Exception exception)
        {
            wordAchievementProgress = null;
            wordAchievementProfileReady = false;
            LogWordAchievementStorageWarning("Local progress initialization failed. " + exception.Message);
        }

        // Restore last played list — challenge or custom
        string savedPath = PlayerPrefs.GetString("LevelPanel_LastPath", "");
        IWordListProvider target = null;
        LevelMode restoreMode = LevelMode.Challenge;

        if (!string.IsNullOrEmpty(savedPath))
        {
            // Try challenge directory first
            var challenges = LevelWordListProvider.ScanDirectory(
                LevelWordListProvider.GetChallengeDirectory());
            target = challenges.Find(c => c.FilePath == savedPath);

            // Try custom directory if not found
            if (target == null)
            {
                var customs = LevelWordListProvider.ScanDirectory(
                    LevelWordListProvider.GetCustomDirectory(), true);
                target = customs.Find(c => c.FilePath == savedPath);
                if (target != null)
                    restoreMode = LevelMode.Custom;
            }
        }

        // A saved Chinese path may remain in PlayerPrefs while Chinese gameplay is disabled.
        // Keep the saved path and progress intact, but do not activate that provider.
        if (target != null && !phaseManager.CanLoadWordList(target))
        {
            target = null;
            restoreMode = LevelMode.Challenge;
        }

        if (target != null && restoreMode == LevelMode.Custom && !CanOpenCustomLists(phaseManager))
        {
            Debug.LogWarning("[GameCoordinator] The saved custom list is still locked by the active profile; restoring an available challenge instead.");
            target = null;
            restoreMode = LevelMode.Challenge;
        }

        if (restoreMode == LevelMode.Challenge
            && target is LevelWordListProvider savedChallenge
            && ChallengeTrackResolver.GetTrackPrefixForChallengeProvider(savedChallenge) != null
            && !CanEnterChallenge(savedChallenge, phaseManager))
        {
            Debug.LogWarning("[GameCoordinator] The saved challenge is locked by the active progression policy; restoring an available challenge instead.");
            target = null;
            restoreMode = LevelMode.Challenge;
        }

        // Fallback: first challenge, or default word list
        if (target == null)
        {
            string preferredTrack = GetPreferredChallengeTrackPrefix(phaseManager.AllowChineseAndMixed);
            var challenges = ChallengeTrackResolver.FilterByTrack(
                LevelWordListProvider.ScanDirectory(LevelWordListProvider.GetChallengeDirectory()),
                preferredTrack);
            if (!phaseManager.AllowChineseAndMixed)
                challenges.RemoveAll(challenge => !phaseManager.CanLoadWordList(challenge));

            for (int i = 0; i < challenges.Count; i++)
            {
                if (CanEnterChallenge(challenges[i], phaseManager))
                {
                    target = challenges[i];
                    break;
                }
            }

            if (target == null && defaultWordList != null && phaseManager.CanLoadWordList(defaultWordList))
                target = defaultWordList;
        }

        bool loadedWordList = false;
        if (target != null)
        {
            loadedWordList = phaseManager.LoadWordList(target);
            if (loadedWordList)
                phaseManager.CurrentLevelMode = restoreMode;
        }

        // Subscribe to input events
        InputHandler inputHandler = Services.Get<InputHandler>();
        if (inputHandler != null)
        {
            inputHandler.OnKeyAction += HandleKeyAction;
            inputHandler.OnBackspacePressed += HandleBackspace;
            inputHandler.OnEnterPressed += HandleEnter;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            debugKeyDownCallback = TryConsumeDebugAnyKeyDown;
            inputHandler.RegisterDebugKeyDownConsumer(debugKeyDownCallback);
#endif
        }

        // Subscribe to phase changes
        Services.Get<PhaseManager>().OnPhaseWordChanged += HandlePhaseWordChanged;
        Services.Get<PhaseManager>().OnWordListChanged  += HandleWordListChanged;

        // Track failures for star rating
        Services.Get<GameStateManager>().OnPhaseFailed += HandlePhaseFailed;

        // Track challenge completion for star saving + panel
        Services.Get<GameStateManager>().OnAllPhasesCompleted += HandleAllPhasesCompleted;

        // Start the first phase
        uiController.Initialize(wordEngine);
        if (!loadedWordList)
        {
            Debug.LogWarning("[GameCoordinator] No allowed word list is available; remaining idle instead of starting an empty game.");
            Services.Get<GameStateManager>().TransitionTo(GameState.Idle);
            return;
        }

        LoadCurrentPhase();
        Services.Get<GameStateManager>().TransitionTo(GameState.Playing);
        MarkActiveLocalListPlayed(phaseManager);
    }

    void OnDestroy()
    {
        if (hubVisible)
        {
            hubVisible = false;
            if (Services.Get<InputHandler>() != null)
                Services.Get<InputHandler>().SetHubGameplayBlocked(false);
            if (Services.Get<TimerSystem>() != null)
                Services.Get<TimerSystem>().ResumeFromOverlay(false);
        }

        if (wordAchievementProgress != null && wordAchievementProgress.HasPendingPersistence
            && !wordAchievementProgress.RetryPersistence())
        {
            LogWordAchievementStorageWarning("Pending progress could not be saved during shutdown. "
                + wordAchievementProgress.LastPersistenceError);
        }

        if (Services.Get<InputHandler>() != null)
        {
            InputHandler inputHandler = Services.Get<InputHandler>();
            inputHandler.OnKeyAction -= HandleKeyAction;
            inputHandler.OnBackspacePressed -= HandleBackspace;
            inputHandler.OnEnterPressed -= HandleEnter;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            inputHandler.UnregisterDebugKeyDownConsumer(debugKeyDownCallback);
            debugKeyDownCallback = null;
#endif
        }
        if (Services.Get<PhaseManager>() != null)
        {
            Services.Get<PhaseManager>().OnPhaseWordChanged -= HandlePhaseWordChanged;
            Services.Get<PhaseManager>().OnWordListChanged  -= HandleWordListChanged;
        }
        if (Services.Get<GameStateManager>() != null)
        {
            Services.Get<GameStateManager>().OnPhaseFailed -= HandlePhaseFailed;
            Services.Get<GameStateManager>().OnAllPhasesCompleted -= HandleAllPhasesCompleted;
        }

        if (wordAchievementPlatform != null)
        {
            if (wordAchievementStatsCallbackSubscribed)
                wordAchievementPlatform.StatsStored -= HandleWordAchievementStatsStored;
            try
            {
                wordAchievementPlatform.Shutdown();
            }
            catch (System.Exception exception)
            {
                LogWordAchievementStorageWarning("Steam achievement callback cleanup failed. " + exception.Message);
            }
        }

        if (steamClientSessionLease != null)
        {
            SteamClientSessionLease lease = steamClientSessionLease;
            steamClientSessionLease = null;
            steamClientSession = null;
            try
            {
                lease.Dispose();
            }
            catch (System.Exception exception)
            {
                LogWordAchievementStorageWarning("Steam client session shutdown failed. " + exception.Message);
            }
        }
    }

    void Update()
    {
        TryInitializeWordAchievementPlatform();

        if (steamClientSession != null && steamClientSession.IsInitialized)
        {
            try
            {
                steamClientSession.RunCallbacks();
            }
            catch (System.Exception exception)
            {
                LogWordAchievementStorageWarning("Steam callback processing failed; gameplay continues. "
                    + exception.Message);
            }

            if (!steamClientSession.IsInitialized)
            {
                if (wordAchievementPlatform != null)
                {
                    if (wordAchievementStatsCallbackSubscribed)
                    {
                        wordAchievementPlatform.StatsStored -= HandleWordAchievementStatsStored;
                        wordAchievementStatsCallbackSubscribed = false;
                    }
                    try
                    {
                        wordAchievementPlatform.Shutdown();
                    }
                    catch (System.Exception exception)
                    {
                        LogWordAchievementStorageWarning("Steam achievement callback cleanup failed. "
                            + exception.Message);
                    }
                }

                LogWordAchievementStorageWarning("Steam disconnected. Local achievement progress remains available. "
                    + steamClientSession.LastError);
                nextWordAchievementInitRetryTime = Time.unscaledTime + 10f;
            }
            else if (wordAchievementPlatform != null && wordAchievementPlatform.IsInitialized)
            {
                try
                {
                    // Processes pending snapshots after the session has pumped SDK callbacks.
                    wordAchievementPlatform.RunCallbacks();
                }
                catch (System.Exception exception)
                {
                    LogWordAchievementStorageWarning("Steam achievement callback processing failed; gameplay continues. "
                        + exception.Message);
                }
            }
        }

        if (wordAchievementProfileReady && wordAchievementProgress != null
            && wordAchievementProgress.HasPendingPersistence
            && Time.unscaledTime >= nextWordAchievementPersistenceRetryTime)
        {
            if (wordAchievementProgress.RetryPersistence())
            {
                wordAchievementStorageWarningLogged = false;
                nextWordAchievementPersistenceRetryTime = 0f;
                SubmitWordAchievementProgress();
            }
            else
            {
                LogWordAchievementStorageWarning("Progress is still pending local persistence. "
                    + wordAchievementProgress.LastPersistenceError);
                nextWordAchievementPersistenceRetryTime = Time.unscaledTime + 5f;
            }
        }

        string currentSteamId = GetCurrentWordAchievementUserId();
        bool identityChanged = !string.IsNullOrEmpty(currentSteamId)
            && !string.Equals(currentSteamId, boundWordAchievementUserId, System.StringComparison.Ordinal);
        bool activationNeeded = identityChanged || !wordAchievementProfileReady;
        if (!activationNeeded || Time.unscaledTime < nextWordAchievementProfileRetryTime)
            return;

        string targetSteamId = identityChanged ? currentSteamId : boundWordAchievementUserId;
        if (TryActivateWordAchievementProfile(targetSteamId))
        {
            boundWordAchievementUserId = targetSteamId;
            nextWordAchievementProfileRetryTime = 0f;
            SubmitWordAchievementProgress();
        }
        else
        {
            nextWordAchievementProfileRetryTime = Time.unscaledTime + 5f;
        }
    }

    private void HandleKeyAction(KeyCode key, bool isPressed)
    {
        if (hubVisible) return;
        var state = Services.Get<GameStateManager>().CurrentState;

        if (state != GameState.Playing)
            return;

        // Start timer on first key action
        if (!Services.Get<TimerSystem>().IsRunning)
            Services.Get<TimerSystem>().StartTimer();

        StepResult result = wordEngine.ProcessInput(key, isPressed);

        // Get the step that was just processed for feedback
        int stepIndex = result == StepResult.Failed
            ? wordEngine.CurrentStep
            : wordEngine.CurrentStep - 1;

        Step step = (stepIndex >= 0 && stepIndex < wordEngine.Steps.Count)
            ? wordEngine.Steps[stepIndex]
            : default;
        Services.Get<GameStateManager>().RaiseStepProcessed(result, step);

        switch (result)
        {
            case StepResult.Correct:
                break;
            case StepResult.PhaseComplete:
                CompleteCurrentWord();
                break;
            case StepResult.Failed:
                Services.Get<TimerSystem>().PauseTimer();
                Services.Get<GameStateManager>().TransitionTo(GameState.PhaseFailed);
                break;
        }
    }

    private bool TryConsumeDebugAnyKeyDown()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        GameConfigProfile profile = Services.Get<PhaseManager>()?.ActiveProfile;
        GameStateManager gameState = Services.Get<GameStateManager>();
        if (profile == null || !profile.debugAnyKeyCompletesWord
            || hubVisible || gameState == null || gameState.CurrentState != GameState.Playing
            || wordEngine == null || !wordEngine.TryCompleteCurrentWordForDebug())
            return false;

        Step completedStep = wordEngine.Steps.Count > 0
            ? wordEngine.Steps[wordEngine.Steps.Count - 1]
            : default;
        if (wordEngine.Steps.Count > 0)
            gameState.RaiseStepProcessed(StepResult.PhaseComplete, completedStep);
        CompleteCurrentWord();

        // Debug completion consumes this keydown and advances immediately, but leaves
        // the final word in AllComplete instead of starting another list.
        if (gameState.CurrentState == GameState.PhaseComplete)
            HandleEnter();
        return true;
#else
        return false;
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void TryAttachSteamDiagnostics(GameConfigProfile profile)
    {
        if (profile == null || profile.buildPurpose != BuildPurpose.LocalTesting || !profile.enableSteamDiagnostics)
            return;

        try
        {
            Type diagnosticsType = Type.GetType("SteamDiagnosticsOverlay, SteamAchievementIntegration", false);
            MethodInfo attachDiagnostics = diagnosticsType?.GetMethod("Attach", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(GameObject), typeof(GameConfigProfile) }, null);
            if (attachDiagnostics == null)
            {
                Debug.LogWarning("[GameCoordinator] Steam diagnostics are enabled, but the optional Steam diagnostics assembly is unavailable.");
                return;
            }

            attachDiagnostics.Invoke(null, new object[] { gameObject, profile });
        }
        catch (Exception exception)
        {
            Exception rootCause = exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
            Debug.LogWarning("[GameCoordinator] Optional Steam diagnostics could not attach: " + rootCause.GetType().Name);
        }
    }
#endif

    private void CompleteCurrentWord()
    {
        Services.Get<TimerSystem>().StopAndAccumulate();
        if (wordAchievementProgress != null && wordAchievementProfileReady)
        {
            WordAchievementProgressRecordResult progressResult =
                wordAchievementProgress.RecordCompletedWord(wordEngine.TargetText);
            if (!progressResult.PersistenceSucceeded)
            {
                LogWordAchievementStorageWarning("Progress is pending local persistence and will be retried. "
                    + progressResult.StorageError);
                nextWordAchievementPersistenceRetryTime = Time.unscaledTime + 5f;
            }
            else
            {
                nextWordAchievementPersistenceRetryTime = 0f;
                SubmitWordAchievementProgress();
            }
        }

        Services.Get<GameStateManager>().TransitionTo(GameState.PhaseComplete);
        if (!Services.Get<PhaseManager>().HasMorePhases)
        {
            leaderboardService.SubmitScore(
                Services.Get<PhaseManager>().ActiveProvider?.DisplayName ?? "Unknown",
                Services.Get<TimerSystem>().TotalElapsedTime,
                Services.Get<PhaseManager>().TotalPhases
            );
            Debug.Log("[TimeDebug] TransitionTo AllComplete");
            Services.Get<GameStateManager>().TransitionTo(GameState.AllComplete);
        }
    }

    private void HandlePhaseFailed()
    {
        Services.Get<PhaseManager>()?.RecordFailure();
    }

    private void HandleAllPhasesCompleted()
    {
        if (Services.Get<PhaseManager>() == null) return;

        // Stop camera/keyboard shake effects from the last phase
        var cameraShake = Services.Get<CameraShakeAndZoom>();
        var keyboardShake = Services.Get<KeyboardShake>();
        if (cameraShake != null)
            cameraShake.ResetFOV();
        if (keyboardShake != null)
            keyboardShake.SetShaking(false);

        // Save legacy and versioned records for the canonical file provider.
        var provider = Services.Get<PhaseManager>().ActiveProvider;
        var phaseManager = Services.Get<PhaseManager>();
        float time = Services.Get<TimerSystem>().TotalElapsedTime;
        int phases = phaseManager.TotalPhases;
        int errors = phaseManager.TotalErrors;
        int stars = ChallengeProgression.CalculateStars(errors);
        string localListId = null;
        bool recordPersisted = false;
        string recordError = null;
        string trackPrefix = null;
        int challengeIndex = -1;
        bool isChallenge = provider is LevelWordListProvider
            && phaseManager.CurrentLevelMode == LevelMode.Challenge;
        Debug.Log($"[TimeDebug] HandleAllPhasesCompleted, provider type={provider?.GetType()?.Name}, mode={Services.Get<PhaseManager>().CurrentLevelMode}");
        CommunityGameplayRunIdentity completedCommunityRun = null;
        if (provider is CommunityWordListProvider communityProvider)
        {
            if (activeCommunityRunIdentity != null
                && ReferenceEquals(activeCommunityRunIdentity.Provider, communityProvider)
                && activeCommunityRunIdentity.Key.Equals(communityProvider.Key)
                && activeCommunityRunIdentity.Version.Equals(communityProvider.Version))
            {
                completedCommunityRun = activeCommunityRunIdentity;
                try
                {
                    recordPersisted = CommunityServiceRegistry.GetShared().TryRecordRun(
                        completedCommunityRun.Key,
                        completedCommunityRun.Version.ContentHash,
                        completedCommunityRun.AccountId,
                        time, errors, stars,
                        out _, out CommunityError communityRecordError);
                    if (!recordPersisted)
                        recordError = communityRecordError != null
                            ? communityRecordError.Message
                            : "The Community record could not be saved for the run-start account.";
                }
                catch (Exception exception)
                {
                    recordError = exception.Message;
                    recordPersisted = false;
                }
            }
            else
            {
                recordError = "The Community run source or run-start account could not be verified; no record was saved.";
            }

            if (!recordPersisted)
                Debug.LogWarning("[GameCoordinator] " + recordError);
        }
        else if (provider is LevelWordListProvider lvlProvider && File.Exists(lvlProvider.FilePath))
        {
            localListId = lvlProvider.GetListKey();
            try
            {
                recordPersisted = WordListLibraryService.Shared.TryRecordRun(
                    lvlProvider, time, phases, errors, stars, out recordError);
            }
            catch (Exception exception)
            {
                recordError = exception.Message;
                recordPersisted = false;
            }
            if (!recordPersisted)
                Debug.LogWarning("[GameCoordinator] The local run record was not saved: " + (recordError ?? "Unknown persistence error."));
            else if (string.IsNullOrEmpty(lvlProvider.IdentityPersistenceError))
            {
                try { ListTimeManager.SaveTime(localListId, time, phases, errors); }
                catch (Exception exception)
                {
                    Debug.LogWarning("[GameCoordinator] The compatibility time record was not saved: " + exception.Message);
                }
            }
        }
        else
        {
            recordError = "The active word list is unavailable; no local run record was saved.";
            Debug.LogWarning("[GameCoordinator] " + recordError);
        }

        if (isChallenge)
        {
            challengeIndex = FindChallengeIndex(provider, out trackPrefix);
            if (challengeIndex >= 0)
            {
                ChallengeProgression.SaveStarRating(challengeIndex, stars, trackPrefix);
                ChallengeProgression.UnlockNext(
                    challengeIndex, trackPrefix, GetEffectiveChallengeCap(Services.Get<PhaseManager>()));
            }

            string levelName = provider?.DisplayName ?? "Level";
            if (levelCompletePanel != null)
                levelCompletePanel.Show(levelName, stars, time, challengeIndex, errors);
        }

        var snapshot = new GameRunCompletionSnapshot(
            localListId, provider, phaseManager.CurrentLanguageMode, phaseManager.CurrentLevelMode,
            phases, errors, time, stars, isChallenge, trackPrefix, challengeIndex,
            recordPersisted, recordError, HasAchievementProgressSnapshot, AchievementProgressSnapshot,
            completedCommunityRun);
        RaiseRunCompleted(snapshot);
        activeCommunityRunIdentity = null;
    }

    private void RaiseRunCompleted(GameRunCompletionSnapshot snapshot)
    {
        Action<GameRunCompletionSnapshot> listeners = RunCompleted;
        if (listeners == null) return;
        foreach (Delegate listener in listeners.GetInvocationList())
        {
            try { ((Action<GameRunCompletionSnapshot>)listener)(snapshot); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }

    private int FindChallengeIndex(IWordListProvider provider, out string trackPrefix)
    {
        trackPrefix = null;
        if (provider == null || !(provider is LevelWordListProvider lvlProvider)) return -1;

        trackPrefix = ChallengeTrackResolver.GetTrackPrefixForChallengeProvider(lvlProvider);
        if (trackPrefix == null) return -1;

        var challenges = ChallengeTrackResolver.FilterByTrack(
            LevelWordListProvider.ScanDirectory(LevelWordListProvider.GetChallengeDirectory()),
            trackPrefix);
        return ChallengeTrackResolver.FindIndex(lvlProvider, challenges);
    }

    private bool CanEnterChallenge(LevelWordListProvider provider, PhaseManager phaseManager)
    {
        if (provider == null || phaseManager == null) return false;

        string trackPrefix = ChallengeTrackResolver.GetTrackPrefixForChallengeProvider(provider);
        if (trackPrefix == null) return false;

        var trackChallenges = ChallengeTrackResolver.FilterByTrack(
            LevelWordListProvider.ScanDirectory(LevelWordListProvider.GetChallengeDirectory()),
            trackPrefix);
        int challengeIndex = ChallengeTrackResolver.FindIndex(provider, trackChallenges);
        if (challengeIndex < 0) return false;

        LevelPanelController panel = LevelPanelController.Instance;
        if (panel == null)
            panel = FindFirstObjectByType<LevelPanelController>(FindObjectsInactive.Include);

        int legacyCap = panel != null ? panel.LegacyChallengeCap : ChallengeProgression.MaxUnlockableLevel;
        bool legacyUnlockAll = panel != null && panel.LegacyUnlockAllChallenges;
        int unlockedCount = ChallengeProgression.GetUnlockedCountForTrack(trackPrefix);
        return ChallengeAccessPolicy.CanEnterChallenge(
            phaseManager.ActiveProfile, challengeIndex, unlockedCount, legacyCap, legacyUnlockAll);
    }

    private static int GetEffectiveChallengeCap(PhaseManager phaseManager)
    {
        if (phaseManager != null && phaseManager.ActiveProfile != null)
            return phaseManager.ActiveProfile.EffectiveChallengeCap;

        LevelPanelController panel = LevelPanelController.Instance;
        if (panel == null)
            panel = FindFirstObjectByType<LevelPanelController>(FindObjectsInactive.Include);
        return panel != null ? panel.LegacyChallengeCap : ChallengeProgression.MaxUnlockableLevel;
    }

    private static string GetPreferredChallengeTrackPrefix(bool allowChineseAndMixed)
    {
        try
        {
            var locale = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale;
            return allowChineseAndMixed && locale != null && locale.Identifier.Code == "zh-Hans"
                ? ChallengeTrackResolver.ChineseTrackPrefix
                : ChallengeTrackResolver.EnglishTrackPrefix;
        }
        catch
        {
            return ChallengeTrackResolver.EnglishTrackPrefix;
        }
    }

    private bool CanOpenCustomLists(PhaseManager phaseManager)
    {
        return phaseManager != null && phaseManager.IsCustomUnlockedForProfile;
    }

    private void HandleBackspace()
    {
        if (hubVisible) return;
        var state = Services.Get<GameStateManager>().CurrentState;
        if (state == GameState.Playing || state == GameState.PhaseFailed)
        {
            wordEngine.Reset();
            LoadCurrentPhase();
            Services.Get<TimerSystem>().ResetPhaseTimer();

            Services.Get<GameStateManager>().RaisePhaseRestarted();
            Services.Get<GameStateManager>().TransitionTo(GameState.Playing);

            keyboardVisual.FlashKey(KeyCode.Backspace, Color.yellow);
        }
    }

    private void HandleEnter()
    {
        if (hubVisible) return;
        if (Services.Get<GameStateManager>().CurrentState != GameState.PhaseComplete)
            return;

        if (Services.Get<PhaseManager>().AdvancePhase())
        {
            LoadCurrentPhase();
            Services.Get<GameStateManager>().TransitionTo(GameState.Playing);
            keyboardVisual.FlashKey(KeyCode.Return, Color.yellow);
        }
        else
        {
            // All phases done — submit score
            leaderboardService.SubmitScore(
                Services.Get<PhaseManager>().ActiveProvider?.DisplayName ?? "Unknown",
                Services.Get<TimerSystem>().TotalElapsedTime,
                Services.Get<PhaseManager>().TotalPhases
            );
            Services.Get<GameStateManager>().TransitionTo(GameState.AllComplete);
        }
    }

    // Loads the current phase into the WordEngine, handling Chinese and English modes.
    private void LoadCurrentPhase()
    {
        int index = Services.Get<PhaseManager>().CurrentPhaseIndex;
        var lang  = Services.Get<PhaseManager>().CurrentLanguageMode;

        MixedPhaseParser.MixedPhaseResult parsed;

        if (lang == LanguageMode.Chinese)
        {
            var cw = Services.Get<PhaseManager>().GetChineseWord(index);
            parsed = cw != null
                ? MixedPhaseParser.FromChinese(cw)
                : MixedPhaseParser.FromEnglish(Services.Get<PhaseManager>().CurrentWord);
        }
        else if (lang == LanguageMode.Mixed)
        {
            var mw = Services.Get<PhaseManager>().GetMixedWord(index);
            if (mw != null)
            {
                if (MixedPhaseParser.IsPurelyEnglish(mw))
                {
                    // Rebuild original text (preserves commas, spaces, punctuation) and use
                    // FromEnglish so typeTarget keeps those chars for the plain-text display path.
                    var sb = new System.Text.StringBuilder();
                    foreach (var seg in mw.segments)
                        if (seg.type == "english") sb.Append(seg.text);
                    parsed = MixedPhaseParser.FromEnglish(sb.ToString());
                }
                else
                {
                    parsed = MixedPhaseParser.Parse(mw);
                }
            }
            else
            {
                parsed = MixedPhaseParser.FromEnglish(Services.Get<PhaseManager>().CurrentWord);
            }
        }
        else
        {
            parsed = MixedPhaseParser.FromEnglish(Services.Get<PhaseManager>().CurrentWord);
        }

        wordEngine.LoadMixedWord(parsed);
        if (uiController != null)
        {
            uiController.RebuildMixedDisplays(wordEngine.CurrentMixedData);
            uiController.UpdateTextDisplay();
        }
        wordAchievementProgress?.BeginPhaseAttempt();
    }

    private bool TryActivateWordAchievementProfile(string steamId)
    {
        if (wordAchievementProgress == null)
        {
            wordAchievementProfileReady = false;
            return false;
        }

        try
        {
            wordAchievementProgress.ActivateProfile(steamId);
            wordAchievementProfileReady = true;
            wordAchievementStorageWarningLogged = false;
            return true;
        }
        catch (System.Exception exception)
        {
            // Do not keep recording against the previous account if profile activation fails.
            wordAchievementProfileReady = false;
            LogWordAchievementStorageWarning("Local achievement profile activation failed; achievement tracking is paused. "
                + exception.Message);
            return false;
        }
    }

    private void TryInitializeWordAchievementPlatform()
    {
        if (wordAchievementPlatform == null || wordAchievementPlatform.IsInitialized
            || Time.unscaledTime < nextWordAchievementInitRetryTime)
            return;

        if (steamClientSession != null && !steamClientSession.IsInitialized)
        {
            bool sessionInitialized;
            try
            {
                sessionInitialized = steamClientSession.TryInitialize();
            }
            catch (System.Exception exception)
            {
                sessionInitialized = false;
                LogWordAchievementStorageWarning("Steam client session initialization failed; local achievements remain available. "
                    + exception.Message);
            }

            if (!sessionInitialized)
            {
                if (!string.IsNullOrEmpty(steamClientSession.LastError))
                    LogWordAchievementStorageWarning("Steam client session is unavailable; local achievements remain available. "
                        + steamClientSession.LastError);
                nextWordAchievementInitRetryTime = Time.unscaledTime + 10f;
                return;
            }
        }

        if (WordAchievementPlatformRegistry.TryInitialize(
                wordAchievementPlatform, LogWordAchievementStorageWarning))
        {
            if (!wordAchievementStatsCallbackSubscribed)
            {
                wordAchievementPlatform.StatsStored += HandleWordAchievementStatsStored;
                wordAchievementStatsCallbackSubscribed = true;
            }

            nextWordAchievementInitRetryTime = 0f;
        }
        else
        {
            nextWordAchievementInitRetryTime = Time.unscaledTime + 10f;
        }
    }

    private string GetCurrentWordAchievementUserId()
    {
        return wordAchievementPlatform != null && wordAchievementPlatform.IsInitialized
            ? wordAchievementPlatform.CurrentUserId
            : null;
    }

    private void SubmitWordAchievementProgress()
    {
        if (!wordAchievementProfileReady || wordAchievementProgress == null
            || wordAchievementPlatform == null || !wordAchievementPlatform.IsInitialized
            || string.IsNullOrEmpty(boundWordAchievementUserId)
            || !string.Equals(wordAchievementProgress.ActiveProfileId, boundWordAchievementUserId, System.StringComparison.Ordinal))
            return;

        if (!wordAchievementPlatform.SubmitProgress(boundWordAchievementUserId, wordAchievementProgress.Snapshot))
        {
            LogWordAchievementStorageWarning("Steam could not queue the saved achievement snapshot; it remains in the local profile.");
        }
    }

    private void HandleWordAchievementStatsStored(string steamId, bool succeeded)
    {
        if (!string.Equals(steamId, boundWordAchievementUserId, System.StringComparison.Ordinal)
            || wordAchievementProgress == null
            || !string.Equals(steamId, wordAchievementProgress.ActiveProfileId, System.StringComparison.Ordinal))
            return;

        if (succeeded && !wordAchievementProgress.HasPendingPersistence)
        {
            wordAchievementStorageWarningLogged = false;
            return;
        }

        if (!succeeded)
            LogWordAchievementStorageWarning("Steam did not confirm the achievement update. Local progress remains saved and will be retried.");
    }

    private void LogWordAchievementStorageWarning(string message)
    {
        if (wordAchievementStorageWarningLogged)
            return;

        wordAchievementStorageWarningLogged = true;
        Debug.LogWarning("[Achievements] " + message);
    }

    private void HandlePhaseWordChanged(string word)
    {
        LoadCurrentPhase();
    }

    private void HandleWordListChanged()
    {
        PhaseManager phaseManager = Services.Get<PhaseManager>();
        IWordListProvider provider = phaseManager?.ActiveProvider;
        if (provider is CommunityWordListProvider communityProvider)
        {
            if (!CommunityGameplayRunContext.TryConsume(
                communityProvider, out activeCommunityRunIdentity))
                activeCommunityRunIdentity = null;
        }
        else
        {
            activeCommunityRunIdentity = null;
            CommunityGameplayRunContext.ClearPending();
        }

        Services.Get<TimerSystem>()?.ResetAll();
        LoadCurrentPhase();   // refresh display target to match the new word at CurrentPhaseIndex
        Services.Get<GameStateManager>()?.TransitionTo(GameState.Playing);
        MarkActiveLocalListPlayed(phaseManager);
    }

    private static void MarkActiveLocalListPlayed(PhaseManager phaseManager)
    {
        LevelWordListProvider provider = phaseManager?.ActiveProvider as LevelWordListProvider;
        if (provider == null
            || ChallengeTrackResolver.GetTrackPrefixForChallengeProvider(provider) == null)
            return;

        if (!WordListLibraryService.Shared.MarkPlayed(provider, out string error))
            Debug.LogWarning("[GameCoordinator] Challenge gameplay started, but last-played metadata was not saved: "
                + error);
    }

    /// <summary>Blocks gameplay keys and suspends only an already-running phase timer.</summary>
    public void SetHubVisible(bool visible)
    {
        if (hubVisible == visible) return;
        hubVisible = visible;
        InputHandler input = Services.Get<InputHandler>();
        if (input != null) input.SetHubGameplayBlocked(visible);

        TimerSystem timer = Services.Get<TimerSystem>();
        if (timer == null) return;
        if (visible)
            hubTimerWasRunning = timer.SuspendForOverlay();
        else
        {
            timer.ResumeFromOverlay(hubTimerWasRunning);
            hubTimerWasRunning = false;
        }
    }

    // Called from UI button
    public void ResetGame()
    {
        Services.Get<PhaseManager>().ResetToBeginning();
        LoadCurrentPhase();
        Services.Get<TimerSystem>().ResetAll();
        Services.Get<GameStateManager>().RaiseGameReset();
        Services.Get<GameStateManager>().TransitionTo(GameState.Playing);
    }

    public void CloseGame()
    {
        Application.Quit();
    }
}
