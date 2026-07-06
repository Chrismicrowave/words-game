using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Self-contained fail flash. Subscribes in Awake (GO must be active
/// at scene load). On fail: activates and shifts random colours.
/// On restart/play: deactivates.
/// </summary>
public class FailFlashController : MonoBehaviour
{
    [SerializeField] private float interval = 0.5f;
    [SerializeField] private float saturation = 1.0f;
    [SerializeField] private float brightness = 1.0f;
    [SerializeField] private float alpha = 0.7f;

    private Image image;
    private float flashTimer;

    void Awake()
    {
        image = GetComponent<Image>();
        GameStateManager.Instance.OnPhaseFailed += OnPhaseFailed;
        GameStateManager.Instance.OnPhaseRestarted += Off;
        GameStateManager.Instance.OnGameReset += Off;
        GameStateManager.Instance.OnPhaseStarted += Off;
        gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (GameStateManager.Instance == null) return;
        GameStateManager.Instance.OnPhaseFailed -= OnPhaseFailed;
        GameStateManager.Instance.OnPhaseRestarted -= Off;
        GameStateManager.Instance.OnGameReset -= Off;
        GameStateManager.Instance.OnPhaseStarted -= Off;
    }

    void Update()
    {
        if (!gameObject.activeSelf) return;
        flashTimer += Time.deltaTime;
        if (flashTimer >= interval) { flashTimer = 0f; ShiftColour(); }
    }

    private void OnPhaseFailed()
    {
        gameObject.SetActive(true);
        flashTimer = 0f;
        ShiftColour();
    }

    private void Off() { gameObject.SetActive(false); }

    private void ShiftColour()
    {
        var c = Color.HSVToRGB(Random.value, saturation, brightness);
        c.a = alpha;
        image.color = c;
    }
}
