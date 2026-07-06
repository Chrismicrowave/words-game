using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Flashes the FailBG overlay with shifting random colours when the player
/// fails a phase. Exposes interval (rate of colour shift) and displayTime
/// (total flash duration).
/// </summary>
public class FailFlashController : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] private float interval = 0.15f;      // time between colour shifts
    [SerializeField] private float displayTime = 1.5f;     // total flash duration

    [Header("Colour")]
    [SerializeField] private float saturation = 0.8f;
    [SerializeField] private float brightness = 0.7f;
    [SerializeField] private float alpha = 0.5f;

    private Image image;
    private float flashTimer;
    private float elapsed;
    private bool isFlashing;

    void Awake()
    {
        image = GetComponent<Image>();
        // Start visible but transparent so OnEnable events fire
        var c = image.color;
        c.a = 0f;
        image.color = c;
    }

    void OnEnable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnPhaseFailed += OnPhaseFailed;
            GameStateManager.Instance.OnPhaseRestarted += OnPhaseRestarted;
            GameStateManager.Instance.OnGameReset += OnPhaseRestarted;
        }
    }

    void OnDisable()
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
        isFlashing = true;
        flashTimer = 0f;
        elapsed = 0f;
        ShiftColour();
    }

    private void OnPhaseRestarted()
    {
        StopFlashing();
    }

    private void StopFlashing()
    {
        isFlashing = false;
        if (image != null)
        {
            var c = image.color;
            c.a = 0f;
            image.color = c;
        }
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
