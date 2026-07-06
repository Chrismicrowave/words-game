using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Flashes the FailBG overlay with shifting random colours when the player
/// fails a phase. Stays visible (last colour) until player restarts.
/// Subscribes in Awake so subscriptions survive if OnGameStartManager
/// deactivates this GO in Start.
/// </summary>
public class FailFlashController : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] private float interval = 0.5f;      // time between colour shifts
    [SerializeField] private float displayTime = 0.5f;     // total flash duration

    [Header("Colour")]
    [SerializeField] private float saturation = 1.0f;
    [SerializeField] private float brightness = 1.0f;
    [SerializeField] private float alpha = 0.7f;

    private Image image;
    private float flashTimer;
    private float elapsed;
    private bool isFlashing;

    void Awake()
    {
        image = GetComponent<Image>();
        // Subscribe here — survives if manager deactivates GO in Start
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnPhaseFailed += OnPhaseFailed;
            GameStateManager.Instance.OnPhaseRestarted += OnPhaseRestarted;
            GameStateManager.Instance.OnGameReset += OnPhaseRestarted;
        }
        // Start transparent — OnGameStartManager may deactivate in Start
        var c = image.color;
        c.a = 0f;
        image.color = c;
    }

    void OnDestroy()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnPhaseFailed -= OnPhaseFailed;
            GameStateManager.Instance.OnPhaseRestarted -= OnPhaseRestarted;
            GameStateManager.Instance.OnGameReset -= OnPhaseRestarted;
        }
    }

    void Update()
    {
        if (!isFlashing) return;

        elapsed += Time.deltaTime;

        if (elapsed >= displayTime)
        {
            StopFlashing();
            return;
        }

        flashTimer += Time.deltaTime;
        if (flashTimer >= interval)
        {
            flashTimer = 0f;
            ShiftColour();
        }
    }

    private void OnPhaseFailed()
    {
        gameObject.SetActive(true);
        isFlashing = true;
        flashTimer = 0f;
        elapsed = 0f;
        ShiftColour();
    }

    private void OnPhaseRestarted()
    {
        isFlashing = false;
        gameObject.SetActive(false);
    }

    private void StopFlashing()
    {
        isFlashing = false;
        // Keep last colour visible until player restarts
    }

    private void ShiftColour()
    {
        if (image == null) return;
        float hue = Random.value;
        var colour = Color.HSVToRGB(hue, saturation, brightness);
        colour.a = alpha;
        image.color = colour;
    }
}
