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
        GameStateManager.Instance.OnPhaseFailed += Show;
        GameStateManager.Instance.OnPhaseRestarted += Hide;
        GameStateManager.Instance.OnGameReset += Hide;
        GameStateManager.Instance.OnPhaseStarted += Hide;
    }

    void OnDestroy()
    {
        if (GameStateManager.Instance == null) return;
        GameStateManager.Instance.OnPhaseFailed -= Show;
        GameStateManager.Instance.OnPhaseRestarted -= Hide;
        GameStateManager.Instance.OnGameReset -= Hide;
        GameStateManager.Instance.OnPhaseStarted -= Hide;
    }

    private void Show() { if (failBG) failBG.SetActive(true); }
    private void Hide() { if (failBG) failBG.SetActive(false); }
}
