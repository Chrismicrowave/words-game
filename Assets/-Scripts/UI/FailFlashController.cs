using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Flashes the FailBG overlay with shifting random colours while active.
/// Activated/deactivated by OnGameStartManager bridge. No event
/// subscriptions needed — works regardless of Editor active state.
/// </summary>
public class FailFlashController : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] private float interval = 0.5f;

    [Header("Colour")]
    [SerializeField] private float saturation = 1.0f;
    [SerializeField] private float brightness = 1.0f;
    [SerializeField] private float alpha = 0.7f;

    private Image image;
    private float flashTimer;

    void Awake()
    {
        image = GetComponent<Image>();
    }

    void OnEnable()
    {
        flashTimer = 0f;
        ShiftColour();
    }

    void Update()
    {
        if (image == null) return;

        flashTimer += Time.deltaTime;
        if (flashTimer >= interval)
        {
            flashTimer = 0f;
            ShiftColour();
        }
    }

    private void ShiftColour()
    {
        float hue = Random.value;
        var colour = Color.HSVToRGB(hue, saturation, brightness);
        colour.a = alpha;
        image.color = colour;
    }
}
