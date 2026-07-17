using TMPro;
using UnityEngine;

/// <summary>
/// Shows when a challenge level is fully cleared.
/// Activated by LevelPanelController when AllPhasesCompleted fires.
/// Displays "Level 01 - Wildlife\ncleared!" + star rating + retries.
/// Only Enter is accepted — loads the next challenge.
/// </summary>
public class LevelCompletePanelController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI levelResultTMP; // "Level 01 - Wildlife\ncleared!"
    [SerializeField] private TextMeshProUGUI starsTMP;       // "★★★"
    [SerializeField] private TextMeshProUGUI timeSpentTMP;   // "Time: 12.34s"
    [SerializeField] private TextMeshProUGUI retriesTMP;     // "retries: 3"

    private bool panelActive;

    void OnEnable()
    {
        if (Services.Get<InputHandler>() != null)
            Services.Get<InputHandler>().OnEnterPressed += HandleEnter;
    }

    void OnDisable()
    {
        if (Services.Get<InputHandler>() != null)
            Services.Get<InputHandler>().OnEnterPressed -= HandleEnter;
    }

    /// <summary>Called by GameCoordinator when a challenge is completed.</summary>
    public void Show(string levelName, int stars, float totalTime, int challengeIndex, int totalErrors)
    {
        if (levelResultTMP != null)
            levelResultTMP.text = $"Level {challengeIndex + 1:D2} - {levelName}\ncleared!";
        if (starsTMP != null)
            starsTMP.text = ChallengeProgression.GetStarDisplay(stars);
        if (timeSpentTMP != null)
            timeSpentTMP.text = $"Time: {totalTime:F2}s";
        if (retriesTMP != null)
            retriesTMP.text = $"retries: {totalErrors}";

        gameObject.SetActive(true);
        panelActive = true;

        if (Services.Get<InputHandler>() != null)
            Services.Get<InputHandler>().SetGameplayBlocked(true);
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

        if (Services.Get<InputHandler>() != null)
            Services.Get<InputHandler>().SetGameplayBlocked(false);

        if (Services.Get<PhaseManager>() == null) return;

        var challengeDir = LevelWordListProvider.GetChallengeDirectory();
        var challenges = LevelWordListProvider.ScanDirectory(challengeDir);
        int currentIdx = FindChallengeIndex(Services.Get<PhaseManager>().ActiveProvider);
        int nextIdx = currentIdx + 1;

        // Cap next level at demo max if set
        int maxDemo = ChallengeProgression.MaxUnlockableLevel;

        if (nextIdx < challenges.Count && (maxDemo <= 0 || nextIdx < maxDemo))
        {
            Services.Get<PhaseManager>().CurrentLevelMode = LevelMode.Challenge;
            Services.Get<PhaseManager>().LoadWordList(challenges[nextIdx]);
            // Persist so game restores this level on next launch
            PlayerPrefs.SetString("LevelPanel_LastPath", challenges[nextIdx].FilePath);
            PlayerPrefs.Save();
        }
        else
        {
            Services.Get<GameStateManager>().TransitionTo(GameState.Idle);
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
