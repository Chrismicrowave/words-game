using UnityEngine;

/// <summary>
/// Shows the appropriate child HUD (Challenge / Custom) based on
/// PhaseManager.CurrentLevelMode. Both children exist in the scene but only
/// one is active at a time.
///
/// Subscribes in Start when PhaseManager.Awake has already run, ensuring
/// PhaseManager.Instance is available — OnEnable runs too early.
/// </summary>
public class ModeHUDController : MonoBehaviour
{
    [SerializeField] private GameObject challengeHUD; // "Challenge" child
    [SerializeField] private GameObject customHUD;    // "Custom" child (also used for Trending)

    void Start()
    {
        if (PhaseManager.Instance != null)
            PhaseManager.Instance.OnWordListChanged += Refresh;
        Refresh();
    }

    void OnDestroy()
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
