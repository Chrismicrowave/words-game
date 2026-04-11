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

    private WordEngine wordEngine;
    private ILeaderboardService leaderboardService;
    private UIController uiController;

    void Start()
    {
        Cursor.SetCursor(customCursor, cursorHotspot, CursorMode.Auto);

        wordEngine = new WordEngine();
        leaderboardService = new NullLeaderboardService();

        uiController = FindAnyObjectByType<UIController>();

        // Load the default word list into PhaseManager
        if (defaultWordList != null)
            PhaseManager.Instance.LoadWordList(defaultWordList);

        // Subscribe to input events
        InputHandler.Instance.OnKeyAction += HandleKeyAction;
        InputHandler.Instance.OnBackspacePressed += HandleBackspace;
        InputHandler.Instance.OnEnterPressed += HandleEnter;

        // Subscribe to phase changes
        PhaseManager.Instance.OnPhaseWordChanged += HandlePhaseWordChanged;
        PhaseManager.Instance.OnWordListChanged  += HandleWordListChanged;

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
                GameStateManager.Instance.TransitionTo(GameState.PhaseComplete);
                break;
            case StepResult.Failed:
                TimerSystem.Instance.PauseTimer();
                GameStateManager.Instance.TransitionTo(GameState.PhaseFailed);
                break;
        }
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
