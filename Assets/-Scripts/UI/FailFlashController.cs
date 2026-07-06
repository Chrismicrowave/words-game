using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Flashes the FailBG overlay with shifting random colours while the player
/// is in the failed state (waiting for Backspace). OFF all other times.
/// Subscribes in Awake so subscriptions survive OnGameStartManager deactivation.
/// </summary>
public class FailFlashController : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] private float interval = 0.5f;      // time between colour shifts

    [Header("Colour")]
    [SerializeField] private float saturation = 1.0f;
    [SerializeField] private float brightness = 1.0f;
    [SerializeField] private float alpha = 0.7f;

    private Image image;
    private float flashTimer;

    void Awake()
    {
        image = GetComponent<Image>();
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnPhaseFailed += OnPhaseFailed;
            GameStateManager.Instance.OnPhaseRestarted += OnPhaseRestarted;
            GameStateManager.Instance.OnGameReset += OnPhaseRestarted;
            GameStateManager.Instance.OnPhaseStarted += OnPhaseStarted;
        }
        // Start invisible — OnGameStartManager may also deactivate
        var c = image.color;
        c.a = 0f;
        image.color = c;
        gameObject.SetActive(false);
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

    void Update()
    {
        if (!gameObject.activeSelf) return;

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
        flashTimer = 0f;
        ShiftColour();
    }

    private void OnPhaseRestarted()
    {
        gameObject.SetActive(false);
    }

    private void OnPhaseStarted()
    {
        gameObject.SetActive(false);
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
