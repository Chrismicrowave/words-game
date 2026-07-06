using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Flashes shifting random colours while its GO is active.
/// Activated/deactivated by FailBGBridge. No event subscriptions.
/// Works regardless of Editor active state.
/// </summary>
public class FailFlashController : MonoBehaviour
{
    [SerializeField] private float interval = 0.5f;
    [SerializeField] private float saturation = 1.0f;
    [SerializeField] private float brightness = 1.0f;
    [SerializeField] private float alpha = 0.7f;

    private Image image;
    private float flashTimer;

    void Awake() { image = GetComponent<Image>(); }

    void OnEnable() { flashTimer = 0f; ShiftColour(); }

    void Update()
    {
        flashTimer += Time.deltaTime;
        if (flashTimer >= interval) { flashTimer = 0f; ShiftColour(); }
    }

    private void ShiftColour()
    {
        var c = Color.HSVToRGB(Random.value, saturation, brightness);
        c.a = alpha;
        image.color = c;
    }
}
