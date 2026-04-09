using TMPro;
using UnityEngine;

public class CircularScrollingText : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI textUI;
    [SerializeField] private string baseText = "12345";
    [SerializeField] private float scrollSpeed = 5f;
    [SerializeField] private bool scrollLeft = true;

    private float timer;
    private string currentText;

    void Start()
    {
        currentText = baseText;
        textUI.text = currentText;
    }

    void Update()
    {
        timer += Time.deltaTime;

        // Control scroll speed based on characters per second
        if (timer >= 1f / scrollSpeed)
        {
            timer = 0f;
            currentText = ScrollText(currentText, scrollLeft);
            textUI.text = currentText;
        }
    }

    string ScrollText(string text, bool left)
    {
        if (text.Length <= 1) return text;

        return left
            ? text.Substring(1) + text[0]           // e.g., "12345" �� "23451"
            : text[^1] + text.Substring(0, text.Length - 1); // e.g., "12345" �� "51234"
    }
}
