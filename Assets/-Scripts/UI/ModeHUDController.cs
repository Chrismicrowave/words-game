using UnityEngine;

/// <summary>
/// Shows the appropriate child HUD (Challenge / Custom) based on
/// PhaseManager.CurrentLevelMode. Both children exist in the scene but only
/// one is active at a time.
///
/// Subscribes in Start when PhaseManager.Awake has already run, ensuring
/// Services.Get<PhaseManager>() is available — OnEnable runs too early.
/// </summary>
public class ModeHUDController : MonoBehaviour
{
    [SerializeField] private GameObject challengeHUD; // "Challenge" child
    [SerializeField] private GameObject customHUD;    // "Custom" child (also used for Trending)

    void Start()
    {
        if (Services.Get<PhaseManager>() != null)
            Services.Get<PhaseManager>().OnWordListChanged += Refresh;
        Refresh();
    }

    void OnDestroy()
    {
        if (Services.Get<PhaseManager>() != null)
            Services.Get<PhaseManager>().OnWordListChanged -= Refresh;
    }

    private void Refresh()
    {
        LevelMode mode = Services.Get<PhaseManager>() != null
            ? Services.Get<PhaseManager>().CurrentLevelMode
            : LevelMode.Challenge;

        if (challengeHUD != null)
            challengeHUD.SetActive(mode == LevelMode.Challenge);
        if (customHUD != null)
            customHUD.SetActive(mode == LevelMode.Custom);
    }
}
