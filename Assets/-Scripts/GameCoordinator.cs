// Assets/-Scripts/GameCoordinator.cs
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

    void Start()
    {
        Cursor.SetCursor(customCursor, cursorHotspot, CursorMode.Auto);

        wordEngine = new WordEngine();
        leaderboardService = new NullLeaderboardService();

        uiController = FindAnyObjectByType<UIController>();

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

        // Fallback: first challenge, or default word list
        if (target == null)
        {
            var challenges = LevelWordListProvider.ScanDirectory(
                LevelWordListProvider.GetChallengeDirectory());
            if (challenges.Count > 0)
                target = challenges[0];
            else if (defaultWordList != null)
                target = defaultWordList;
        }

        if (target != null)
        {
            Services.Get<PhaseManager>().LoadWordList(target);
            Services.Get<PhaseManager>().CurrentLevelMode = restoreMode;
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
        LoadCurrentPhase();
        Services.Get<GameStateManager>().TransitionTo(GameState.Playing);
    }

    void OnDestroy()
    {
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

        Step step = wordEngine.Steps[stepIndex];
        Services.Get<GameStateManager>().RaiseStepProcessed(result, step);

        switch (result)
        {
            case StepResult.Correct:
                break;
            case StepResult.PhaseComplete:
                Services.Get<TimerSystem>().StopAndAccumulate();
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
            int challengeIndex = FindChallengeIndex(provider);
            if (challengeIndex >= 0)
            {
                ChallengeProgression.SaveStarRating(challengeIndex, stars);
                ChallengeProgression.UnlockNext(challengeIndex);
            }

            string levelName = provider?.DisplayName ?? "Level";
            if (levelCompletePanel != null)
                levelCompletePanel.Show(levelName, stars, Services.Get<TimerSystem>().TotalElapsedTime);
        }
    }

    private int FindChallengeIndex(IWordListProvider provider)
    {
        if (provider == null || !(provider is LevelWordListProvider lvlProvider)) return -1;
        var challengeDir = LevelWordListProvider.GetChallengeDirectory();
        var challenges = LevelWordListProvider.ScanDirectory(challengeDir);
        for (int i = 0; i < challenges.Count; i++)
        {
            if (challenges[i].FilePath == lvlProvider.FilePath)
                return i;
        }
        return -1;
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
