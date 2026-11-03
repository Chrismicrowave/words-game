using System.Collections.Generic;
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

        var phaseManager = Services.Get<PhaseManager>();
        if (phaseManager == null) return;

        var currentProvider = phaseManager.ActiveProvider as LevelWordListProvider;
        string trackPrefix = ChallengeTrackResolver.GetTrackPrefixForChallengeProvider(currentProvider);
        if (trackPrefix == null)
        {
            Services.Get<GameStateManager>().TransitionTo(GameState.Idle);
            return;
        }

        var challenges = ChallengeTrackResolver.FilterByTrack(
            LevelWordListProvider.ScanDirectory(LevelWordListProvider.GetChallengeDirectory()),
            trackPrefix);
        if (!phaseManager.AllowChineseAndMixed)
            challenges.RemoveAll(challenge => !phaseManager.CanLoadWordList(challenge));

        int currentIdx = FindChallengeIndex(phaseManager.ActiveProvider, challenges);
        int nextIdx = currentIdx + 1;

        int unlockedCount = ChallengeProgression.GetUnlockedCountForTrack(trackPrefix);
        LevelPanelController panel = LevelPanelController.Instance;
        if (panel == null)
            panel = FindFirstObjectByType<LevelPanelController>(FindObjectsInactive.Include);
        int legacyCap = panel != null ? panel.LegacyChallengeCap : ChallengeProgression.MaxUnlockableLevel;
        bool legacyUnlockAll = panel != null && panel.LegacyUnlockAllChallenges;

        if (nextIdx < challenges.Count && ChallengeAccessPolicy.CanEnterChallenge(
            phaseManager.ActiveProfile, nextIdx, unlockedCount, legacyCap, legacyUnlockAll))
        {
            LevelMode previousMode = phaseManager.CurrentLevelMode;
            phaseManager.CurrentLevelMode = LevelMode.Challenge;
            if (!phaseManager.LoadWordList(challenges[nextIdx]))
            {
                phaseManager.CurrentLevelMode = previousMode;
                Debug.LogWarning("[LevelCompletePanelController] The next challenge is not allowed; returning to idle.");
                Services.Get<GameStateManager>().TransitionTo(GameState.Idle);
                return;
            }

            // Persist so game restores this level on next launch
            PlayerPrefs.SetString("LevelPanel_LastPath", challenges[nextIdx].FilePath);
            PlayerPrefs.Save();
        }
        else
        {
            Services.Get<GameStateManager>().TransitionTo(GameState.Idle);
        }
    }

    private int FindChallengeIndex(IWordListProvider provider, List<LevelWordListProvider> challenges)
    {
        if (provider == null || !(provider is LevelWordListProvider lvlProvider)) return -1;
        return ChallengeTrackResolver.FindIndex(lvlProvider, challenges);
    }
}
