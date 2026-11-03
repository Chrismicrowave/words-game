// Assets/-Scripts/GameCoordinator.cs
using System.Collections.Generic;
using UnityEngine;

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
    private bool wordAchievementProfileReady;
    private bool wordAchievementStorageWarningLogged;
    private bool wordAchievementStatsCallbackSubscribed;
    private string boundWordAchievementUserId;
    private float nextWordAchievementProfileRetryTime;
    private float nextWordAchievementInitRetryTime;
    private float nextWordAchievementPersistenceRetryTime;

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

        try
        {
            wordAchievementPlatform = WordAchievementPlatformRegistry.CreateOrLocalOnly();
            if (wordAchievementPlatform == null)
                wordAchievementPlatform = WordAchievementPlatformRegistry.CreateLocalOnly();
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
        Services.Get<InputHandler>().OnKeyAction += HandleKeyAction;
        Services.Get<InputHandler>().OnBackspacePressed += HandleBackspace;
        Services.Get<InputHandler>().OnEnterPressed += HandleEnter;

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
    }

    void OnDestroy()
    {
        if (wordAchievementProgress != null && wordAchievementProgress.HasPendingPersistence
            && !wordAchievementProgress.RetryPersistence())
        {
            LogWordAchievementStorageWarning("Pending progress could not be saved during shutdown. "
                + wordAchievementProgress.LastPersistenceError);
        }

        if (Services.Get<InputHandler>() != null)
        {
            Services.Get<InputHandler>().OnKeyAction -= HandleKeyAction;
            Services.Get<InputHandler>().OnBackspacePressed -= HandleBackspace;
            Services.Get<InputHandler>().OnEnterPressed -= HandleEnter;
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
            wordAchievementPlatform.Shutdown();
        }
    }

    void Update()
    {
        TryInitializeWordAchievementPlatform();

        if (wordAchievementPlatform != null && wordAchievementPlatform.IsInitialized)
        {
            try
            {
                wordAchievementPlatform.RunCallbacks();
            }
            catch (System.Exception exception)
            {
                LogWordAchievementStorageWarning("Steam achievement callback processing failed; gameplay continues. "
                    + exception.Message);
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
                // Always play the word-complete sound via the PhaseComplete event,
                // even for the last word. Then go straight to AllComplete if done.
                Services.Get<GameStateManager>().TransitionTo(GameState.PhaseComplete);
                if (!Services.Get<PhaseManager>().HasMorePhases)
                {
                    leaderboardService.SubmitScore(
                        Services.Get<PhaseManager>().ActiveProvider?.DisplayName ?? "Unknown",
                        Services.Get<TimerSystem>().TotalElapsedTime,
                        Services.Get<PhaseManager>().TotalPhases
                    );
                    Debug.Log($"[TimeDebug] TransitionTo AllComplete");
                    Services.Get<GameStateManager>().TransitionTo(GameState.AllComplete);
                }
                break;
            case StepResult.Failed:
                Services.Get<TimerSystem>().PauseTimer();
                Services.Get<GameStateManager>().TransitionTo(GameState.PhaseFailed);
                break;
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

        // Save time record for LevelWordListProvider lists (challenge + custom)
        var provider = Services.Get<PhaseManager>().ActiveProvider;
        Debug.Log($"[TimeDebug] HandleAllPhasesCompleted, provider type={provider?.GetType()?.Name}, mode={Services.Get<PhaseManager>().CurrentLevelMode}");
        if (provider is LevelWordListProvider lvlProvider)
        {
            string key = lvlProvider.GetListKey();
            float time = Services.Get<TimerSystem>().TotalElapsedTime;
            int phases = Services.Get<PhaseManager>().TotalPhases;
            int errors = Services.Get<PhaseManager>().TotalErrors;
            Debug.Log($"[TimeDebug] Saving: key={key} time={time:F2}s phases={phases} errors={errors}");
            ListTimeManager.SaveTime(key, time, phases, errors);
        }
        else
        {
            Debug.LogWarning($"[TimeDebug] NOT a LevelWordListProvider, cannot save time");
        }

        if (Services.Get<PhaseManager>().CurrentLevelMode == LevelMode.Challenge)
        {
            int stars = ChallengeProgression.CalculateStars(Services.Get<PhaseManager>().TotalErrors);
            string trackPrefix;
            int challengeIndex = FindChallengeIndex(provider, out trackPrefix);
            if (challengeIndex >= 0)
            {
                ChallengeProgression.SaveStarRating(challengeIndex, stars, trackPrefix);
                ChallengeProgression.UnlockNext(
                    challengeIndex, trackPrefix, GetEffectiveChallengeCap(Services.Get<PhaseManager>()));
            }

            string levelName = provider?.DisplayName ?? "Level";
            if (levelCompletePanel != null)
                levelCompletePanel.Show(levelName, stars, Services.Get<TimerSystem>().TotalElapsedTime, challengeIndex, Services.Get<PhaseManager>().TotalErrors);
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
        if (phaseManager == null) return false;

        string trackPrefix = GetPreferredChallengeTrackPrefix(phaseManager.AllowChineseAndMixed);
        int completedChallenges = ChallengeProgression.GetUnlockedCountForTrack(trackPrefix) - 1;
        if (phaseManager.ActiveProfile != null)
            return phaseManager.ActiveProfile.CanOpenCustomLists(completedChallenges);

        LevelPanelController panel = LevelPanelController.Instance;
        if (panel == null)
            panel = FindFirstObjectByType<LevelPanelController>(FindObjectsInactive.Include);
        return panel != null
            && (panel.LegacyUnlockCustomTab || completedChallenges >= panel.LegacyCustomUnlockThreshold);
    }

    private void HandleBackspace()
    {
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
        uiController.RebuildMixedDisplays(wordEngine.CurrentMixedData);
        uiController.UpdateTextDisplay();
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
        Services.Get<TimerSystem>().ResetAll();
        LoadCurrentPhase();   // refresh display target to match the new word at CurrentPhaseIndex
        Services.Get<GameStateManager>().TransitionTo(GameState.Playing);
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
