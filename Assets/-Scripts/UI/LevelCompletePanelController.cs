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

    /// <summary>Builds a star string with filled + empty stars, e.g. ★★☆ for 2/3.</summary>
    private static string BuildStarString(int filled)
    {
        char[] stars = { '☆', '☆', '☆' };
        for (int i = 0; i < filled && i < 3; i++)
            stars[i] = '★';
        return new string(stars);
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

        if (PhaseManager.Instance == null) return;

        var challengeDir = LevelWordListProvider.GetChallengeDirectory();
        var challenges = LevelWordListProvider.ScanDirectory(challengeDir);
        int currentIdx = FindChallengeIndex(PhaseManager.Instance.ActiveProvider);
        int nextIdx = currentIdx + 1;

        if (nextIdx < challenges.Count)
        {
            PhaseManager.Instance.CurrentLevelMode = LevelMode.Challenge;
            PhaseManager.Instance.LoadWordList(challenges[nextIdx]);
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
