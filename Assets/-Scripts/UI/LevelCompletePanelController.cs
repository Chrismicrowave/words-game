using TMPro;
using UnityEngine;

/// <summary>
/// Shows when a challenge level is fully cleared.
/// Activated by LevelPanelController when AllPhasesCompleted fires.
/// Displays "Level Name Cleared!" + star rating.
/// Only Enter is accepted — loads the next challenge.
/// </summary>
public class LevelCompletePanelController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI levelResultTMP; // "First Steps Cleared!"
    [SerializeField] private TextMeshProUGUI starsTMP;       // "★★★"

    private bool panelActive;

    void OnEnable()
    {
        if (InputHandler.Instance != null)
            InputHandler.Instance.OnEnterPressed += HandleEnter;
    }

    void OnDisable()
    {
        if (InputHandler.Instance != null)
            InputHandler.Instance.OnEnterPressed -= HandleEnter;
    }

    /// <summary>Called by GameCoordinator when a challenge is completed.</summary>
    public void Show(string levelName, int stars)
    {
        if (levelResultTMP != null)
            levelResultTMP.text = $"{levelName} Cleared!";
        if (starsTMP != null)
            starsTMP.text = ChallengeProgression.GetStarDisplay(stars);

        gameObject.SetActive(true);
        panelActive = true;

        if (InputHandler.Instance != null)
            InputHandler.Instance.SetGameplayBlocked(true);
    }

    private void HandleEnter()
    {
        if (!panelActive) return;
        LoadNextChallenge();
    }

    private void LoadNextChallenge()
    {
        panelActive = false;
        gameObject.SetActive(false);

        if (InputHandler.Instance != null)
            InputHandler.Instance.SetGameplayBlocked(false);

        if (Services.Get<PhaseManager>() == null) return;

        var challengeDir = LevelWordListProvider.GetChallengeDirectory();
        var challenges = LevelWordListProvider.ScanDirectory(challengeDir);
        int currentIdx = FindChallengeIndex(Services.Get<PhaseManager>().ActiveProvider);
        int nextIdx = currentIdx + 1;

        if (nextIdx < challenges.Count)
        {
            Services.Get<PhaseManager>().CurrentLevelMode = LevelMode.Challenge;
            Services.Get<PhaseManager>().LoadWordList(challenges[nextIdx]);
        }
        else
        {
            GameStateManager.Instance.TransitionTo(GameState.Idle);
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
}
