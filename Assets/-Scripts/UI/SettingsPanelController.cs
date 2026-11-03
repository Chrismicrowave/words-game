using TMPro;
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

    [Header("Username")]
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private Button usernameOkBtn;
    [SerializeField] private Button usernameBackBtn;
    [SerializeField] private Color usernameDefaultColor = Color.white;
    [SerializeField] private Color usernameEditColor = Color.black;

    private TextMeshProUGUI usernameText;
    private string usernameBeforeEdit;
    private bool usernameEditing;

    void Awake()
    {
        if (usernameInput != null)
        {
            usernameInput.characterLimit = 16;
            usernameText = usernameInput.textComponent as TextMeshProUGUI;
            usernameInput.onSelect.AddListener(_ => EnterUsernameEdit());
            usernameInput.onDeselect.AddListener(_ => { /* ignore — OK/Back handle exit */ });
        }
        if (usernameOkBtn != null)
            usernameOkBtn.onClick.AddListener(OnUsernameOk);
        if (usernameBackBtn != null)
            usernameBackBtn.onClick.AddListener(OnUsernameBack);
    }

    void OnEnable()
    {
        if (Services.Get<InputHandler>() != null) Services.Get<InputHandler>().SetGameplayBlocked(true);
        ShowAudio(); // default to Audio tab
    }

    public void Close()
    {
        if (Services.Get<InputHandler>() != null) Services.Get<InputHandler>().SetGameplayBlocked(false);
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
        RefreshUsername();
    }

    public void ShowCustom()
    {
        SetActive(customPanel, gameplayPanel, audioPanel, displayPanel);
        SetTabColors(customTabBtn, gameplayTabBtn, audioTabBtn, displayTabBtn);
    }

    public void ResetToDefaults()
    {
        Services.Get<SettingsManager>().ResetToDefaults();
    }

    // ── Username editing ───────────────────────────────────────────────────────────

    private void RefreshUsername()
    {
        usernameEditing = false;
        if (usernameInput != null)
        {
            usernameInput.text = Services.Get<SettingsManager>()?.PlayerName ?? "Unnamed User #1234";
            usernameInput.interactable = true; // still clickable to start editing
            if (usernameText != null)
                usernameText.color = usernameDefaultColor;
        }
        if (usernameOkBtn != null) usernameOkBtn.gameObject.SetActive(false);
        if (usernameBackBtn != null) usernameBackBtn.gameObject.SetActive(false);
    }

    private void EnterUsernameEdit()
    {
        if (usernameEditing) return;
        usernameEditing = true;
        usernameBeforeEdit = usernameInput?.text ?? "";

        if (usernameText != null)
            usernameText.color = usernameEditColor;
        if (usernameOkBtn != null) usernameOkBtn.gameObject.SetActive(true);
        if (usernameBackBtn != null) usernameBackBtn.gameObject.SetActive(true);
    }

    private void ExitUsernameEdit(bool save)
    {
        if (!usernameEditing) return;
        usernameEditing = false;

        if (save)
            SaveUsername();
        else if (usernameInput != null)
            usernameInput.text = usernameBeforeEdit; // revert

        // Return to default display
        if (usernameText != null)
            usernameText.color = usernameDefaultColor;
        if (usernameOkBtn != null) usernameOkBtn.gameObject.SetActive(false);
        if (usernameBackBtn != null) usernameBackBtn.gameObject.SetActive(false);
    }

    private void SaveUsername()
    {
        if (Services.Get<SettingsManager>() == null) return;
        string name = usernameInput?.text?.Trim();
        if (string.IsNullOrEmpty(name))
            name = "Unnamed User #1234";
        if (name.Length > 16)
            name = name.Substring(0, 16);
        Services.Get<SettingsManager>().PlayerName = name;
    }

    private void OnUsernameOk()
    {
        ExitUsernameEdit(true);
    }

    private void OnUsernameBack()
    {
        ExitUsernameEdit(false);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

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
