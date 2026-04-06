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

        // Start the first phase
        wordEngine.LoadWord(PhaseManager.Instance.CurrentWord);
        GameStateManager.Instance.TransitionTo(GameState.Playing);
        uiController.Initialize(wordEngine);
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
            wordEngine.LoadWord(PhaseManager.Instance.CurrentWord);
            TimerSystem.Instance.ResetPhaseTimer();

            GameStateManager.Instance.RaisePhaseRestarted();
            GameStateManager.Instance.TransitionTo(GameState.Playing);

            keyboardVisual.FlashKey(KeyCode.Backspace, Color.yellow);
            uiController.UpdateTextDisplay();
        }
    }

    private void HandleEnter()
    {
        if (GameStateManager.Instance.CurrentState != GameState.PhaseComplete)
            return;

        if (PhaseManager.Instance.AdvancePhase())
        {
            wordEngine.LoadWord(PhaseManager.Instance.CurrentWord);
            GameStateManager.Instance.TransitionTo(GameState.Playing);
            keyboardVisual.FlashKey(KeyCode.Return, Color.yellow);
            uiController.UpdateTextDisplay();
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

    private void HandlePhaseWordChanged(string word)
    {
        wordEngine.LoadWord(word);
        uiController.UpdateTextDisplay();
    }

    // Called from UI button
    public void ResetGame()
    {
        PhaseManager.Instance.ResetToBeginning();
        wordEngine.LoadWord(PhaseManager.Instance.CurrentWord);
        TimerSystem.Instance.ResetAll();
        GameStateManager.Instance.RaiseGameReset();
        GameStateManager.Instance.TransitionTo(GameState.Playing);
        uiController.UpdateTextDisplay();
    }

    public void CloseGame()
    {
        Application.Quit();
    }
}
