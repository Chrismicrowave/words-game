// Assets/-Scripts/UI/UIController.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using SFB;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIController : MonoBehaviour
{
    [Header("Text Displays")]
    [SerializeField] private TextMeshProUGUI targetTextUI;
    [SerializeField] private TextMeshProUGUI matchedTextUI;
    [SerializeField] private TextMeshProUGUI statusTextUI;

    [Header("Cursor Blink")]
    [SerializeField] private float cursorBlinkInterval = 0.5f;
    private float blinkTimer;
    private bool showCursor = true;

    [Header("Phase List UI")]
    [SerializeField] private TMP_InputField phaseInputField;
    [SerializeField] private Color inputFieldActiveColor = Color.white;
    [SerializeField] private Color inputFieldInactiveColor = new Color(0.75f, 0.75f, 0.75f, 1f);
    [SerializeField] private GameObject phaseInputFocusBorder;
    [SerializeField] private Transform phaseListContent;
    [SerializeField] private GameObject phaseButtonPrefab;
    [SerializeField] private Color phaseSelectedColor = Color.yellow;
    [SerializeField] private Color phaseUnselectedColor = Color.white;

    [Header("Timer UI")]
    [SerializeField] private TextMeshProUGUI phaseTimeUI;
    [SerializeField] private TextMeshProUGUI totalTimeUI;

    [Header("Delete Animation")]
    [SerializeField] private float deleteDelayBetweenLetters = 0.05f;
    [SerializeField] private AudioManager audioKeys;

    [Header("Action Prompt")]
    [SerializeField] private bool showActionPrompt = true;

    [Header("Tabs")]
    [SerializeField] private Button myListTabBtn;
    [SerializeField] private Button dailyTabBtn;

    [Header("Panel Buttons")]
    [SerializeField] private GameObject myListPanelBtns;
    [SerializeField] private GameObject dailyPanelBtns;

    [Header("Daily Picker")]
    [SerializeField] private DailyPickerPanelController dailyPickerPanel;

    [Header("Panel Toggles")]
    [SerializeField] private MenuAnimOnOff wordsPanelAnim;
    [SerializeField] private MenuAnimOnOff timerPanelAnim;

    [Header("Info / Rules")]
    [SerializeField] private GameObject instructionPanel;
    private bool _infoPanelOn;

    [Header("Import / Export")]
    [SerializeField] private Button importBtn;
    [SerializeField] private Button exportBtn;

    [Header("Config")]
    [SerializeField] private GameConfig config;

    private const string ActiveTabPrefKey = "ActiveTab"; // "mylist" or "daily"
    private const string WordsPanelPrefKey = "WordsPanelOn";
    private const string TimerPanelPrefKey = "TimerPanelOn";
    private const string InfoPanelPrefKey  = "InfoPanelOn";

    private IWordListProvider myListProvider;

    private WordEngine wordEngine;
    private int selectedPhaseIndex = -1;
    private Coroutine deleteAnimCoroutine;
    private Color matchedTextOriginalColor;
    private Color phaseInputTextOriginalColor;

    void OnEnable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStepProcessed += HandleStepProcessed;
            GameStateManager.Instance.OnPhaseCompleted += HandlePhaseCompleted;
            GameStateManager.Instance.OnPhaseFailed += HandlePhaseFailed;
            GameStateManager.Instance.OnPhaseRestarted += HandleRestart;
            GameStateManager.Instance.OnGameReset += HandleGameReset;
            GameStateManager.Instance.OnAllPhasesCompleted += HandleAllComplete;
        }
        if (PhaseManager.Instance != null)
        {
            PhaseManager.Instance.OnWordListChanged += RefreshPhaseList;
        }
        if (TimerSystem.Instance != null)
        {
            TimerSystem.Instance.OnTimerUpdated += UpdateTimerDisplay;
        }
    }

    void OnDisable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStepProcessed -= HandleStepProcessed;
            GameStateManager.Instance.OnPhaseCompleted -= HandlePhaseCompleted;
            GameStateManager.Instance.OnPhaseFailed -= HandlePhaseFailed;
            GameStateManager.Instance.OnPhaseRestarted -= HandleRestart;
            GameStateManager.Instance.OnGameReset -= HandleGameReset;
            GameStateManager.Instance.OnAllPhasesCompleted -= HandleAllComplete;
        }
        if (PhaseManager.Instance != null)
        {
            PhaseManager.Instance.OnWordListChanged -= RefreshPhaseList;
        }
        if (TimerSystem.Instance != null)
        {
            TimerSystem.Instance.OnTimerUpdated -= UpdateTimerDisplay;
        }
    }

    void Start()
    {
        OnDisable();
        OnEnable();

        if (config != null)
        {
            bool showImportExport = !config.isDemo;
            if (importBtn != null) importBtn.gameObject.SetActive(showImportExport);
            if (exportBtn != null) exportBtn.gameObject.SetActive(showImportExport);
        }

        if (PhaseManager.Instance != null)
            myListProvider = PhaseManager.Instance.ActiveProvider;

        // Load saved tab preference, default to mylist
        string savedTab = PlayerPrefs.GetString(ActiveTabPrefKey, "mylist");
        if (savedTab == "daily")
        {
            OnDailyTabClicked();
        }
        else
        {
            OnMyListTabClicked();
        }

        // Restore Words panel state (startOn default = false)
        if (wordsPanelAnim != null)
        {
            bool wordsOn = PlayerPrefs.GetInt(WordsPanelPrefKey, 0) == 1;
            if (wordsOn) wordsPanelAnim.On(); else wordsPanelAnim.Off();
        }

        // Restore Timer panel state (startOn default = false)
        if (timerPanelAnim != null)
        {
            bool timerOn = PlayerPrefs.GetInt(TimerPanelPrefKey, 0) == 1;
            if (timerOn) timerPanelAnim.On(); else timerPanelAnim.Off();
        }

        // Restore Info panel state (starts active in scene, default = 1)
        _infoPanelOn = PlayerPrefs.GetInt(InfoPanelPrefKey, 1) == 1;
        if (instructionPanel != null)
            instructionPanel.SetActive(_infoPanelOn);
    }

    public void Initialize(WordEngine engine)
    {
        wordEngine = engine;
        matchedTextOriginalColor = matchedTextUI.color;
        if (phaseInputField != null)
            phaseInputTextOriginalColor = phaseInputField.textComponent.color;
        UpdateTextDisplay();
        RefreshPhaseList();
    }

    void Update()
    {
        bool phaseFieldFocused = phaseInputField != null
            && EventSystem.current != null
            && EventSystem.current.currentSelectedGameObject == phaseInputField.gameObject;

        // Input field visual feedback
        if (phaseInputField != null)
            phaseInputField.GetComponent<Image>().color = phaseFieldFocused
                ? inputFieldActiveColor
                : inputFieldInactiveColor;

        // Orange focus border on the add-phase field
        if (phaseInputFocusBorder != null)
            phaseInputFocusBorder.SetActive(phaseFieldFocused);

        // Gameplay text: dim and freeze cursor when add-phase field has focus
        if (phaseFieldFocused)
        {
            matchedTextUI.color = new Color(0.45f, 0.45f, 0.45f, 1f);
            phaseInputField.textComponent.color = Color.white;
            if (showCursor)
            {
                showCursor = false;
                UpdateTextDisplay();
            }
        }
        else
        {
            matchedTextUI.color = matchedTextOriginalColor;
            phaseInputField.textComponent.color = phaseInputTextOriginalColor;
            blinkTimer += Time.deltaTime;
            if (blinkTimer >= cursorBlinkInterval)
            {
                blinkTimer = 0f;
                showCursor = !showCursor;
                UpdateTextDisplay();
            }
        }
    }

    public void UpdateTextDisplay()
    {
        if (wordEngine == null) return;

        targetTextUI.text = wordEngine.TargetText;
        matchedTextUI.text = wordEngine.GetDisplayText(showCursor);

        // Action prompt
        if (showActionPrompt && GameStateManager.Instance.CurrentState == GameState.Playing)
        {
            string prompt = wordEngine.GetActionPrompt();
            if (!string.IsNullOrEmpty(prompt))
                statusTextUI.text = prompt;
        }
    }

    private void HandleStepProcessed(StepResult result, Step step)
    {
        UpdateTextDisplay();

        if (result == StepResult.Failed)
        {
            statusTextUI.text = wordEngine.LastFailureMessage
                + ". Press Backspace to start again";
        }
    }

    private void HandlePhaseCompleted()
    {
        statusTextUI.text = "Phase complete! Hit Return to continue...";
    }

    private void HandlePhaseFailed()
    {
        statusTextUI.text = wordEngine.LastFailureMessage
            + ". Press Backspace to start again";
    }

    private void HandleRestart()
    {
        if (deleteAnimCoroutine != null)
            StopCoroutine(deleteAnimCoroutine);
        deleteAnimCoroutine = StartCoroutine(DeleteTextAnim());
    }

    private void HandleGameReset()
    {
        HandleRestart();
    }

    private void HandleAllComplete()
    {
        targetTextUI.text = "";
        matchedTextUI.text = "";
        statusTextUI.text = "Congratulations! You completed all phases!";
    }

    // --- Timer Display ---

    private void UpdateTimerDisplay(float phaseDuration, float total)
    {
        TimeSpan phaseTime = TimeSpan.FromSeconds(phaseDuration);
        phaseTimeUI.text = FormatTime(phaseTime);

        TimeSpan totalTime = TimeSpan.FromSeconds(total);
        totalTimeUI.text = FormatTime(totalTime);
    }

    private string FormatTime(TimeSpan t)
    {
        return $"{t.Hours:D2}\"{t.Minutes:D2}\'{t.Seconds:D2}.{t.Milliseconds / 10:D2}";
    }

    // --- Phase List UI ---

    public void RefreshPhaseList()
    {
        if (phaseListContent == null || phaseButtonPrefab == null) return;

        foreach (Transform child in phaseListContent)
            Destroy(child.gameObject);

        var words = PhaseManager.Instance.Words;
        for (int i = 0; i < words.Count; i++)
        {
            int index = i;
            GameObject btnObj = Instantiate(phaseButtonPrefab, phaseListContent);
            btnObj.GetComponentInChildren<TextMeshProUGUI>().text = $"{index + 1}. {words[i]}";

            Button btn = btnObj.GetComponent<Button>();
            btn.onClick.AddListener(() =>
            {
                selectedPhaseIndex = index;
                HighlightSelectedButton(btnObj);
            });
        }

        Canvas.ForceUpdateCanvases();
    }

    private void HighlightSelectedButton(GameObject selected)
    {
        foreach (Transform child in phaseListContent)
        {
            Image img = child.GetComponent<Image>();
            if (img != null)
                img.color = (child.gameObject == selected) ? phaseSelectedColor : phaseUnselectedColor;
        }
    }

    // Button callbacks (wire in Inspector)
    public void OnAddPhaseClicked()
    {
        string text = phaseInputField.text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        PhaseManager.Instance.AddPhase(text, 0);
        phaseInputField.text = "";
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void OnDeletePhaseClicked()
    {
        if (selectedPhaseIndex < 0) return;
        PhaseManager.Instance.RemovePhase(selectedPhaseIndex);
        selectedPhaseIndex = -1;
    }

    public void OnSwapPhaseClicked()
    {
        if (selectedPhaseIndex < 0) return;
        PhaseManager.Instance.JumpToPhase(selectedPhaseIndex);
        GameStateManager.Instance.RaisePhaseRestarted();
        GameStateManager.Instance.TransitionTo(GameState.Playing);
    }

    public void OnMovePhaseUpClicked()
    {
        if (selectedPhaseIndex <= 0) return;
        PhaseManager.Instance.MovePhase(selectedPhaseIndex, selectedPhaseIndex - 1);
        selectedPhaseIndex--;
    }

    public void OnMovePhaseDownClicked()
    {
        if (selectedPhaseIndex < 0 || selectedPhaseIndex >= PhaseManager.Instance.TotalPhases - 1) return;
        PhaseManager.Instance.MovePhase(selectedPhaseIndex, selectedPhaseIndex + 1);
        selectedPhaseIndex++;
    }

    [Header("Tab Colors")]
    [SerializeField] private Color tabActiveColor   = new Color(1f, 0.5f, 0f, 1f);
    [SerializeField] private Color tabInactiveColor = Color.white;

    private void SetTabColors(bool myListActive)
    {
        if (myListTabBtn != null) myListTabBtn.GetComponent<Image>().color = myListActive ? tabActiveColor : tabInactiveColor;
        if (dailyTabBtn  != null) dailyTabBtn .GetComponent<Image>().color = myListActive ? tabInactiveColor : tabActiveColor;
    }

    public void OnMyListTabClicked()
    {
        PlayerPrefs.SetString(ActiveTabPrefKey, "mylist");
        if (myListProvider != null)
            PhaseManager.Instance.LoadWordList(myListProvider);
        if (myListPanelBtns != null) myListPanelBtns.SetActive(true);
        if (dailyPanelBtns != null) dailyPanelBtns.SetActive(false);
        SetTabColors(myListActive: true);
    }

    public void OnFetchDailyClicked()
    {
        if (dailyPickerPanel == null) return;
        dailyPickerPanel.OnListSelected = (provider) =>
        {
            PhaseManager.Instance.LoadWordList(provider);
            PlayerPrefs.SetString(ActiveTabPrefKey, "daily");
            if (myListPanelBtns != null) myListPanelBtns.SetActive(false);
            if (dailyPanelBtns != null) dailyPanelBtns.SetActive(true);
            SetTabColors(myListActive: false);
        };
        dailyPickerPanel.gameObject.SetActive(true);
    }

    public void OnDailyTabClicked()
    {
        SetTabColors(myListActive: false);
        PlayerPrefs.SetString(ActiveTabPrefKey, "daily");
        string date = System.DateTime.Now.ToString("yyyy-MM-dd");
        string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "DailyLists"));
        string path = System.IO.Path.Combine(dir, date + ".json");

        if (!System.IO.File.Exists(path))
        {
            // Create sample for today if missing
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            string json = "{\"name\":\"Daily - " + date + "\",\"words\":[\"hello\",\"world\",\"unity\"],\"date\":\"" + date + "\"}";
            System.IO.File.WriteAllText(path, json);
        }

        var provider = new DailyWordListProvider(path);
        PhaseManager.Instance.LoadWordList(provider);
        if (myListPanelBtns != null) myListPanelBtns.SetActive(false);
        if (dailyPanelBtns != null) dailyPanelBtns.SetActive(true);
    }

    public void OnToggleWordsClicked()
    {
        if (wordsPanelAnim == null) return;
        wordsPanelAnim.Toggle();
        bool isOn = wordsPanelAnim.GetComponent<Animator>().GetBool("ON");
        PlayerPrefs.SetInt(WordsPanelPrefKey, isOn ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void OnToggleTimerClicked()
    {
        if (timerPanelAnim == null) return;
        timerPanelAnim.Toggle();
        bool isOn = timerPanelAnim.GetComponent<Animator>().GetBool("ON");
        PlayerPrefs.SetInt(TimerPanelPrefKey, isOn ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void OnTogglePromptsClicked()
    {
        if (instructionPanel == null) return;
        _infoPanelOn = !_infoPanelOn;
        instructionPanel.SetActive(_infoPanelOn);
        PlayerPrefs.SetInt(InfoPanelPrefKey, _infoPanelOn ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void OnImportClicked()
    {
        var ext = new[] { new ExtensionFilter("Text Files", "txt") };
        StandaloneFileBrowser.OpenFilePanelAsync("Import Word List", "", ext, false, paths =>
        {
            if (paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
            var provider = TxtWordListImporter.ImportFromTxt(paths[0]);
            PhaseManager.Instance.LoadWordList(provider);
        });
    }

    public void OnExportClicked()
    {
        var provider = PhaseManager.Instance.ActiveProvider;
        string defaultName = provider.DisplayName.Replace(" ", "_");
        var ext = new[] { new ExtensionFilter("Text Files", "txt") };
        StandaloneFileBrowser.SaveFilePanelAsync("Export Word List", "", defaultName, ext, path =>
        {
            if (string.IsNullOrEmpty(path)) return;
            TxtWordListImporter.ExportToTxt(provider, path);
        });
    }

    // --- Delete Text Animation ---

    private IEnumerator DeleteTextAnim()
    {
        string currentDisplayText = matchedTextUI.text;
        string visibleText = Regex.Replace(currentDisplayText, "<.*?>", string.Empty);

        if (string.IsNullOrEmpty(visibleText))
        {
            UpdateTextDisplay();
            yield break;
        }

        audioKeys.PlaySound(audioKeys.released);

        char[] textChars = visibleText.ToCharArray();

        for (int i = textChars.Length - 1; i >= 0; i--)
        {
            if (textChars[i] == '_' || textChars[i] == ' ')
                continue;

            textChars[i] = '_';

            string newText = RebuildWithOriginalFormatting(currentDisplayText, new string(textChars));
            matchedTextUI.text = newText;
            matchedTextUI.ForceMeshUpdate();

            if (i > 0)
                audioKeys.PlaySound(audioKeys.released);

            yield return new WaitForSeconds(deleteDelayBetweenLetters);
        }

        UpdateTextDisplay();
    }

    private string RebuildWithOriginalFormatting(string originalText, string newContent)
    {
        var tags = new List<(int pos, string tag)>();
        var matches = Regex.Matches(originalText, "<.*?>");
        foreach (Match match in matches)
            tags.Add((match.Index, match.Value));

        StringBuilder result = new StringBuilder();
        int contentIndex = 0;

        for (int i = 0; i < originalText.Length; i++)
        {
            var currentTag = tags.Find(t => t.pos == i);
            if (currentTag.tag != null)
            {
                result.Append(currentTag.tag);
                i += currentTag.tag.Length - 1;
            }
            else if (contentIndex < newContent.Length)
            {
                char c = newContent[contentIndex++];
                result.Append(c == '_' && originalText[i] == ' ' ? ' ' : c);
            }
        }

        return result.ToString();
    }
}
