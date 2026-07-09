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

        // Load the first challenge as the default word list, so the game
        // always starts in Challenge mode with level 1's words.
        var challengeDir = LevelWordListProvider.GetChallengeDirectory();
        var challenges = LevelWordListProvider.ScanDirectory(challengeDir);
        if (challenges.Count > 0)
        {
            PhaseManager.Instance.LoadWordList(challenges[0]);
            PhaseManager.Instance.CurrentLevelMode = LevelMode.Challenge;
        }
        else if (defaultWordList != null)
        {
            PhaseManager.Instance.LoadWordList(defaultWordList);
        }

        // Subscribe to input events
        InputHandler.Instance.OnKeyAction += HandleKeyAction;
        InputHandler.Instance.OnBackspacePressed += HandleBackspace;
        InputHandler.Instance.OnEnterPressed += HandleEnter;

        // Subscribe to phase changes
        PhaseManager.Instance.OnPhaseWordChanged += HandlePhaseWordChanged;
        PhaseManager.Instance.OnWordListChanged  += HandleWordListChanged;

        // Track failures for star rating
        GameStateManager.Instance.OnPhaseFailed += HandlePhaseFailed;

        // Track challenge completion for star saving + panel
        GameStateManager.Instance.OnAllPhasesCompleted += HandleAllPhasesCompleted;

        // Start the first phase
        uiController.Initialize(wordEngine);
        LoadCurrentPhase();
        GameStateManager.Instance.TransitionTo(GameState.Playing);
    }

    void OnDestroy()
    {
        if (InputHandler.Instance != null)
        {
            InputHandler.Instance.OnKeyAction -= HandleKeyAction;
            InputHandler.Instance.OnBackspacePressed -= HandleBackspace;
            InputHandler.Instance.OnEnterPressed -= HandleEnter;
        }
        if (PhaseManager.Instance != null)
        {
            PhaseManager.Instance.OnPhaseWordChanged -= HandlePhaseWordChanged;
            PhaseManager.Instance.OnWordListChanged  -= HandleWordListChanged;
        }
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnPhaseFailed -= HandlePhaseFailed;
            GameStateManager.Instance.OnAllPhasesCompleted -= HandleAllPhasesCompleted;
        }
    }

    private void HandleKeyAction(KeyCode key, bool isPressed)
    {
        var state = GameStateManager.Instance.CurrentState;

        if (state != GameState.Playing)
            return;

        // Start timer on first key action
        if (!TimerSystem.Instance.IsRunning)
            TimerSystem.Instance.StartTimer();

        StepResult result = wordEngine.ProcessInput(key, isPressed);

        // Get the step that was just processed for feedback
        int stepIndex = result == StepResult.Failed
            ? wordEngine.CurrentStep
            : wordEngine.CurrentStep - 1;

        Step step = wordEngine.Steps[stepIndex];
        GameStateManager.Instance.RaiseStepProcessed(result, step);

        switch (result)
        {
            case StepResult.Correct:
                break;
            case StepResult.PhaseComplete:
                TimerSystem.Instance.StopAndAccumulate();
                // Always play the word-complete sound via the PhaseComplete event,
                // even for the last word. Then go straight to AllComplete if done.
                GameStateManager.Instance.TransitionTo(GameState.PhaseComplete);
                if (!PhaseManager.Instance.HasMorePhases)
                {
                    leaderboardService.SubmitScore(
                        PhaseManager.Instance.ActiveProvider?.DisplayName ?? "Unknown",
                        TimerSystem.Instance.TotalElapsedTime,
                        PhaseManager.Instance.TotalPhases
                    );
                    GameStateManager.Instance.TransitionTo(GameState.AllComplete);
                }
                break;
            case StepResult.Failed:
                TimerSystem.Instance.PauseTimer();
                GameStateManager.Instance.TransitionTo(GameState.PhaseFailed);
                break;
        }
    }

    private void HandlePhaseFailed()
    {
        PhaseManager.Instance?.RecordFailure();
    }

    private void HandleAllPhasesCompleted()
    {
        if (PhaseManager.Instance == null) return;

        // Stop camera/keyboard shake effects from the last phase
        var cameraShake = Services.Get<CameraShakeAndZoom>();
        var keyboardShake = Services.Get<KeyboardShake>();
        if (cameraShake != null)
            cameraShake.ResetFOV();
        if (keyboardShake != null)
            keyboardShake.SetShaking(false);

        if (PhaseManager.Instance.CurrentLevelMode == LevelMode.Challenge)
        {
            int stars = ChallengeProgression.CalculateStars(PhaseManager.Instance.TotalErrors);
            int challengeIndex = FindChallengeIndex(PhaseManager.Instance.ActiveProvider);
            if (challengeIndex >= 0)
            {
                ChallengeProgression.SaveStarRating(challengeIndex, stars);
                ChallengeProgression.UnlockNext(challengeIndex);
            }

            string levelName = PhaseManager.Instance.ActiveProvider?.DisplayName ?? "Level";
            if (levelCompletePanel != null)
                levelCompletePanel.Show(levelName, stars);
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
        var state = GameStateManager.Instance.CurrentState;
        if (state == GameState.Playing || state == GameState.PhaseFailed)
        {
            wordEngine.Reset();
            LoadCurrentPhase();
            TimerSystem.Instance.ResetPhaseTimer();

            GameStateManager.Instance.RaisePhaseRestarted();
            GameStateManager.Instance.TransitionTo(GameState.Playing);

            keyboardVisual.FlashKey(KeyCode.Backspace, Color.yellow);
        }
    }

    private void HandleEnter()
    {
        if (GameStateManager.Instance.CurrentState != GameState.PhaseComplete)
            return;

        if (PhaseManager.Instance.AdvancePhase())
        {
            LoadCurrentPhase();
            GameStateManager.Instance.TransitionTo(GameState.Playing);
            keyboardVisual.FlashKey(KeyCode.Return, Color.yellow);
        }
        else
        {
            // All phases done — submit score
            leaderboardService.SubmitScore(
                PhaseManager.Instance.ActiveProvider?.DisplayName ?? "Unknown",
                TimerSystem.Instance.TotalElapsedTime,
                PhaseManager.Instance.TotalPhases
            );
            GameStateManager.Instance.TransitionTo(GameState.AllComplete);
        }
    }

    // Loads the current phase into the WordEngine, handling Chinese and English modes.
    private void LoadCurrentPhase()
    {
        int index = PhaseManager.Instance.CurrentPhaseIndex;
        var lang  = PhaseManager.Instance.CurrentLanguageMode;

        MixedPhaseParser.MixedPhaseResult parsed;

        if (lang == LanguageMode.Chinese)
        {
            var cw = PhaseManager.Instance.GetChineseWord(index);
            parsed = cw != null
                ? MixedPhaseParser.FromChinese(cw)
                : MixedPhaseParser.FromEnglish(PhaseManager.Instance.CurrentWord);
        }
        else if (lang == LanguageMode.Mixed)
        {
            var mw = PhaseManager.Instance.GetMixedWord(index);
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
                parsed = MixedPhaseParser.FromEnglish(PhaseManager.Instance.CurrentWord);
            }
        }
        else
        {
            parsed = MixedPhaseParser.FromEnglish(PhaseManager.Instance.CurrentWord);
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
        TimerSystem.Instance.ResetAll();
        LoadCurrentPhase();   // refresh display target to match the new word at CurrentPhaseIndex
        GameStateManager.Instance.TransitionTo(GameState.Playing);
    }

    // Called from UI button
    public void ResetGame()
    {
        PhaseManager.Instance.ResetToBeginning();
        LoadCurrentPhase();
        TimerSystem.Instance.ResetAll();
        GameStateManager.Instance.RaiseGameReset();
        GameStateManager.Instance.TransitionTo(GameState.Playing);
    }

    public void CloseGame()
    {
        Application.Quit();
    }
}
