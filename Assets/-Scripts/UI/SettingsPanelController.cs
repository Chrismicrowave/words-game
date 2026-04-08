using UnityEngine;
using UnityEngine.UI;

public class SettingsPanelController : MonoBehaviour
{
    [SerializeField] private GameObject audioPanel;
    [SerializeField] private GameObject displayPanel;
    [SerializeField] private GameObject gameplayPanel;
    [SerializeField] private GameObject customPanel;

    [SerializeField] private Image audioTabBtn;
    [SerializeField] private Image displayTabBtn;
    [SerializeField] private Image gameplayTabBtn;
    [SerializeField] private Image customTabBtn;

    [SerializeField] private Color tabActiveColor   = new Color(1f, 0.5f, 0f, 1f);
    [SerializeField] private Color tabInactiveColor = Color.white;

    void OnEnable()
    {
        if (InputHandler.Instance != null) InputHandler.Instance.SetGameplayBlocked(true);
        ShowAudio(); // default to Audio tab
    }

    public void Close()
    {
        if (InputHandler.Instance != null) InputHandler.Instance.SetGameplayBlocked(false);
        gameObject.SetActive(false);
    }

    public void ShowAudio()
    {
        SetActive(audioPanel, gameplayPanel, displayPanel, customPanel);
        SetTabColors(audioTabBtn, gameplayTabBtn, displayTabBtn, customTabBtn);
    }

    public void ShowDisplay()
    {
        SetActive(displayPanel, gameplayPanel, audioPanel, customPanel);
        SetTabColors(displayTabBtn, gameplayTabBtn, audioTabBtn,customTabBtn);
    }

    public void ShowGameplay()
    {
        SetActive(gameplayPanel, audioPanel, displayPanel, customPanel);
        SetTabColors(gameplayTabBtn, audioTabBtn, displayTabBtn, customTabBtn);
    }

    public void ShowCustom()
    {
        SetActive(customPanel, gameplayPanel, audioPanel, displayPanel);
        SetTabColors(customTabBtn, gameplayTabBtn, audioTabBtn, displayTabBtn);
    }

    public void ResetToDefaults()
    {
        SettingsManager.Instance.ResetToDefaults();
        // Sub-panels refresh themselves via SettingsManager in v0.4+
    }

    private void SetActive(GameObject active, params GameObject[] inactive)
    {
        if (active != null) active.SetActive(true);
        foreach (var go in inactive)
            if (go != null) go.SetActive(false);
    }

    private void SetTabColors(Image active, params Image[] inactive)
    {
        if (active != null) active.color = tabActiveColor;
        foreach (var img in inactive)
            if (img != null) img.color = tabInactiveColor;
    }
}
