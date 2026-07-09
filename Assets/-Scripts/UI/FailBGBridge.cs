using UnityEngine;

/// <summary>
/// Always-active bridge on GameSystems. Listens to fail/restart events
/// and toggles FailBG directly. No Awake dependency on FailBG itself.
/// </summary>
public class FailBGBridge : MonoBehaviour
{
    [SerializeField] private GameObject failBG;

    void Awake()
    {
        Services.Get<GameStateManager>().OnPhaseFailed += Show;
        Services.Get<GameStateManager>().OnPhaseRestarted += Hide;
        Services.Get<GameStateManager>().OnGameReset += Hide;
        Services.Get<GameStateManager>().OnPhaseStarted += Hide;
    }

    void OnDestroy()
    {
        if (Services.Get<GameStateManager>() == null) return;
        Services.Get<GameStateManager>().OnPhaseFailed -= Show;
        Services.Get<GameStateManager>().OnPhaseRestarted -= Hide;
        Services.Get<GameStateManager>().OnGameReset -= Hide;
        Services.Get<GameStateManager>().OnPhaseStarted -= Hide;
    }

    private void Show() { if (failBG) failBG.SetActive(true); }
    private void Hide() { if (failBG) failBG.SetActive(false); }
}
