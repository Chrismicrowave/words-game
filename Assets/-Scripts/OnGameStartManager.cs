using UnityEngine;

/// <summary>
/// Controls GameObject active states at game start.
/// Also bridges fail-state events to FailBG so it works even if
/// FailBG starts inactive in the scene.
/// </summary>
public class OnGameStartManager : MonoBehaviour
{
    [System.Serializable]
    public struct GameObjectToggle
    {
        public GameObject gameObject;
        public bool activeAtStart;
    }

    [Header("Initial States")]
    [SerializeField] private GameObjectToggle[] toggles;

    [Header("Fail BG")]
    [SerializeField] private GameObject failBG;           // FailBG reference (always-active bridge)
    [SerializeField] private FailFlashController failFlash; // for Show() if needed

    void Awake()
    {
        // Bridge fail events — runs because GameSystems is always active
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnPhaseFailed += OnPhaseFailed;
            GameStateManager.Instance.OnPhaseRestarted += OnPhaseRestarted;
            GameStateManager.Instance.OnGameReset += OnPhaseRestarted;
            GameStateManager.Instance.OnPhaseStarted += OnPhaseStarted;
        }
    }

    void Start()
    {
        foreach (var toggle in toggles)
        {
            if (toggle.gameObject != null)
                toggle.gameObject.SetActive(toggle.activeAtStart);
        }
    }

    void OnDestroy()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnPhaseFailed -= OnPhaseFailed;
            GameStateManager.Instance.OnPhaseRestarted -= OnPhaseRestarted;
            GameStateManager.Instance.OnGameReset -= OnPhaseRestarted;
            GameStateManager.Instance.OnPhaseStarted -= OnPhaseStarted;
        }
    }

    private void OnPhaseFailed()
    {
        if (failBG != null) failBG.SetActive(true);
    }

    private void OnPhaseRestarted()
    {
        if (failBG != null) failBG.SetActive(false);
    }

    private void OnPhaseStarted()
    {
        if (failBG != null) failBG.SetActive(false);
    }
}
