using UnityEngine;

/// <summary>
/// Shows the appropriate child HUD (Challenge / Custom) based on
/// PhaseManager.CurrentLevelMode. Both children exist in the scene but only
/// one is active at a time.
/// </summary>
public class ModeHUDController : MonoBehaviour
{
    [SerializeField] private GameObject challengeHUD; // "Challenge" child
    [SerializeField] private GameObject customHUD;    // "Custom" child (also used for Trending)

    void OnEnable()
    {
        if (PhaseManager.Instance != null)
            PhaseManager.Instance.OnWordListChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        if (PhaseManager.Instance != null)
            PhaseManager.Instance.OnWordListChanged -= Refresh;
    }

    private void Refresh()
    {
        LevelMode mode = PhaseManager.Instance != null
            ? PhaseManager.Instance.CurrentLevelMode
            : LevelMode.Challenge;

        if (challengeHUD != null)
            challengeHUD.SetActive(mode == LevelMode.Challenge);
        if (customHUD != null)
            customHUD.SetActive(mode == LevelMode.Custom);
    }
}
