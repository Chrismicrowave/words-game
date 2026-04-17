// Assets/-Scripts/UI/UIController.cs
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

    [Header("Phase Input")]
    [SerializeField] private TMP_InputField phaseInputField;
    [SerializeField] private Color inputFieldActiveColor = Color.white;
    [SerializeField] private Color inputFieldInactiveColor = new Color(0.75f, 0.75f, 0.75f, 1f);
    [SerializeField] private GameObject phaseInputFocusBorder;

    [Header("Delete Animation")]
    [SerializeField] private float deleteDelayBetweenLetters = 0.05f;
    [SerializeField] private AudioManager audioKeys;

    [Header("Action Prompt")]
    [SerializeField] private bool showActionPrompt = true;

    [Header("Panel Toggles")]
    [SerializeField] private MenuAnimOnOff wordsPanelAnim;
    [SerializeField] private MenuAnimOnOff timerPanelAnim;

    [Header("Info / Rules")]
    [SerializeField] private GameObject instructionPanel;
    private bool _infoPanelOn;

    [Header("Settings")]
    [SerializeField] private SettingsPanelController settingsPanel;

    [Header("Import / Export")]
    [SerializeField] private Button importBtn;
    [SerializeField] private Button exportBtn;

    [Header("Config")]
    [SerializeField] private GameConfig config;

    [Header("Sub-managers")]
    [SerializeField] private PhaseListUIManager phaseListUIManager;
    [SerializeField] private WordListTabManager wordListTabManager;

    [Header("Chinese Language")]
    [SerializeField] private ChineseMatchedDisplay chineseMatchedDisplay;
    [SerializeField] private ChineseTargetDisplay chineseTargetDisplay;
    [SerializeField] private ChinesePinyinPopup chinesePinyinPopup;

    private const int    MaxWordLength    = 140;
    private const string WordsPanelPrefKey = "WordsPanelOn";
    private const string TimerPanelPrefKey = "TimerPanelOn";
    private const string InfoPanelPrefKey  = "InfoPanelOn";

    private WordEngine wordEngine;
    private Coroutine deleteAnimCoroutine;
    private Color matchedTextOriginalColor;
    private Color phaseInputTextOriginalColor;

    void OnEnable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStepProcessed     += HandleStepProcessed;
            GameStateManager.Instance.OnPhaseCompleted    += HandlePhaseCompleted;
            GameStateManager.Instance.OnPhaseFailed       += HandlePhaseFailed;
            GameStateManager.Instance.OnPhaseRestarted    += HandleRestart;
            GameStateManager.Instance.OnGameReset         += HandleGameReset;
            GameStateManager.Instance.OnAllPhasesCompleted += HandleAllComplete;
        }
    }

    void OnDisable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStepProcessed     -= HandleStepProcessed;
            GameStateManager.Instance.OnPhaseCompleted    -= HandlePhaseCompleted;
            GameStateManager.Instance.OnPhaseFailed       -= HandlePhaseFailed;
            GameStateManager.Instance.OnPhaseRestarted    -= HandleRestart;
            GameStateManager.Instance.OnGameReset         -= HandleGameReset;
            GameStateManager.Instance.OnAllPhasesCompleted -= HandleAllComplete;
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

        // Restore Words panel state
        if (wordsPanelAnim != null)
        {
            bool wordsOn = PlayerPrefs.GetInt(WordsPanelPrefKey, 0) == 1;
            if (wordsOn) wordsPanelAnim.On(); else wordsPanelAnim.Off();
        }

        // Restore Timer panel state
        if (timerPanelAnim != null)
        {
            bool timerOn = PlayerPrefs.GetInt(TimerPanelPrefKey, 0) == 1;
            if (timerOn) timerPanelAnim.On(); else timerPanelAnim.Off();
        }

        // Restore Info panel state
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
        phaseListUIManager?.RefreshPhaseList();
    }

    /// <summary>
    /// Called by GameCoordinator when a Chinese phase is loaded.
    /// Rebuilds both the matched and target cell layouts.
    /// </summary>
    public void RebuildChineseDisplays(ChinesePhaseData data)
    {
        chineseMatchedDisplay?.BuildCells(data);
        chineseTargetDisplay?.BuildCells(data);

        if (chineseTargetDisplay != null && SettingsManager.Instance != null)
            chineseTargetDisplay.SetPinyinVisible(SettingsManager.Instance.ShowPinyin);
    }

    /// <summary>
    /// Called by GameCoordinator for every phase (English, Chinese, or Mixed).
    /// </summary>
    public void RebuildMixedDisplays(MixedPhaseParser.MixedPhaseResult parsed)
    {
        // Only build cell display when there is actual Chinese content
        if (!MixedPhaseParser.IsPurelyEnglish(parsed))
            chineseMatchedDisplay?.BuildMixedCells(parsed);
        else
            chineseMatchedDisplay?.Clear();

        // Show target display for any phase that has Chinese content
        if (chineseTargetDisplay != null)
        {
            if (!MixedPhaseParser.IsPurelyEnglish(parsed))
            {
                chineseTargetDisplay.BuildMixedCells(parsed);
                if (SettingsManager.Instance != null)
                    chineseTargetDisplay.SetPinyinVisible(SettingsManager.Instance.ShowPinyin);
                chineseTargetDisplay.gameObject.SetActive(true);
                chineseTargetDisplay?.SyncFontSizesNextFrame();
            }
            else
            {
                chineseTargetDisplay.Clear();
                chineseTargetDisplay.gameObject.SetActive(false);
            }
        }
    }

    void Update()
    {
        bool phaseFieldFocused = phaseInputField != null
            && EventSystem.current != null
            && EventSystem.current.currentSelectedGameObject == phaseInputField.gameObject;

        if (phaseInputField != null)
            phaseInputField.GetComponent<Image>().color = phaseFieldFocused
                ? inputFieldActiveColor
                : inputFieldInactiveColor;

        if (phaseInputFocusBorder != null)
            phaseInputFocusBorder.SetActive(phaseFieldFocused);

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

        // Use cell-based display only when there is actual Chinese content
        bool useCellDisplay = wordEngine.IsChinesePhase ||
            (wordEngine.IsMixedPhase && !MixedPhaseParser.IsPurelyEnglish(wordEngine.CurrentMixedData));

        if (useCellDisplay)
        {
            matchedTextUI.gameObject.SetActive(false);
            if (chineseMatchedDisplay != null) chineseMatchedDisplay.gameObject.SetActive(true);
            chineseMatchedDisplay?.UpdateProgress(wordEngine.MatchedLength);

            // ChineseTargetDisplay handles target for all phases with Chinese content
            targetTextUI.gameObject.SetActive(false);
        }
        else
        {
            targetTextUI.gameObject.SetActive(true);
            matchedTextUI.gameObject.SetActive(true);
            if (chineseMatchedDisplay != null) chineseMatchedDisplay.gameObject.SetActive(false);
            if (chineseTargetDisplay != null) chineseTargetDisplay.gameObject.SetActive(false);

            targetTextUI.text = wordEngine.TargetText;
            matchedTextUI.text = wordEngine.GetDisplayText(showCursor);
        }

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
            statusTextUI.text = wordEngine.LastFailureMessage + ". Press Backspace to start again";
    }

    private void HandlePhaseCompleted()
    {
        statusTextUI.text = "Phase complete! Hit Return to continue...";
    }

    private void HandlePhaseFailed()
    {
        statusTextUI.text = wordEngine.LastFailureMessage + ". Press Backspace to start again";
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
        targetTextUI.text  = "";
        matchedTextUI.text = "";
        statusTextUI.text  = "Congratulations! You completed all phases!";
    }

    // --- Phase List Button Callbacks ---

    public void OnAddPhaseClicked()
    {
        string text = phaseInputField.text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (text.Length > MaxWordLength)
            text = text.Substring(0, MaxWordLength);

        if (PinyinLookup.ContainsChinese(text))
        {
            // Open pinyin confirmation popup for any text containing Chinese characters
            chinesePinyinPopup?.Show(text, entry =>
            {
                PhaseManager.Instance.AddMixedPhase(entry);
                PhaseManager.Instance.SaveCurrentList();
                phaseInputField.text = "";
                EventSystem.current.SetSelectedGameObject(null);
            }, () => { /* cancelled — leave input as-is */ });
        }
        else
        {
            PhaseManager.Instance.AddPhase(text, 0);
            PhaseManager.Instance.SaveCurrentList();
            phaseInputField.text = "";
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    public void OnDeletePhaseClicked()
    {
        if (phaseListUIManager == null || phaseListUIManager.SelectedPhaseIndex < 0) return;
        PhaseManager.Instance.RemovePhase(phaseListUIManager.SelectedPhaseIndex);
        PhaseManager.Instance.SaveCurrentList();
        phaseListUIManager.ClearSelection();
    }

    public void OnSwapPhaseClicked()
    {
        if (phaseListUIManager == null || phaseListUIManager.SelectedPhaseIndex < 0) return;
        PhaseManager.Instance.JumpToPhase(phaseListUIManager.SelectedPhaseIndex);
        GameStateManager.Instance.RaisePhaseRestarted();
        GameStateManager.Instance.TransitionTo(GameState.Playing);
    }

    public void OnMovePhaseUpClicked()
    {
        if (phaseListUIManager == null || phaseListUIManager.SelectedPhaseIndex <= 0) return;
        int idx = phaseListUIManager.SelectedPhaseIndex;
        PhaseManager.Instance.MovePhase(idx, idx - 1);
        // RefreshPhaseList has already run (via OnWordListChanged); re-highlight at new position
        phaseListUIManager.SetSelectedPhaseIndex(idx - 1);
    }

    public void OnMovePhaseDownClicked()
    {
        if (phaseListUIManager == null || phaseListUIManager.SelectedPhaseIndex < 0
            || phaseListUIManager.SelectedPhaseIndex >= PhaseManager.Instance.TotalPhases - 1) return;
        int idx = phaseListUIManager.SelectedPhaseIndex;
        PhaseManager.Instance.MovePhase(idx, idx + 1);
        // RefreshPhaseList has already run (via OnWordListChanged); re-highlight at new position
        phaseListUIManager.SetSelectedPhaseIndex(idx + 1);
    }

    // --- Panel Toggles ---

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

    public void OnSettingsBtnClicked()
    {
        if (settingsPanel != null) settingsPanel.gameObject.SetActive(true);
    }

    public void OnImportClicked()
    {
        var ext = new[] { new ExtensionFilter("Text Files", "txt") };
        StandaloneFileBrowser.OpenFilePanelAsync("Import Word List", "", ext, false, paths =>
        {
            if (paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
            var provider = TxtWordListImporter.ImportFromTxt(paths[0]);
            // Update tab manager state: set provider + persist path + mark active tab as mylist.
            // Call these directly rather than through OnMyListTabClicked — the StandaloneFileBrowser
            // async callback is not guaranteed to be on Unity's main thread on all platforms, so
            // we keep the Unity API surface minimal and explicit.
            wordListTabManager?.SetMyListProvider(provider);
            wordListTabManager?.SaveMyListPath(provider.FilePath);
            wordListTabManager?.SaveActiveTab("mylist");
            PhaseManager.Instance.LoadWordList(provider);
        });
    }

    public void OnExportClicked()
    {
        var provider = PhaseManager.Instance.ActiveProvider;
        string defaultName = (provider?.DisplayName ?? "wordlist").Replace(" ", "_");
        // Snapshot the display words from PhaseManager — provider.GetWords() may be empty for
        // Chinese/Mixed lists where the provider stores chineseWords/mixedWords, not words[].
        // PhaseManager.Words is always the rebuilt display list populated by LoadWordList().
        var words = new System.Collections.Generic.List<string>(PhaseManager.Instance.Words);
        var ext = new[] { new ExtensionFilter("Text Files", "txt") };
        StandaloneFileBrowser.SaveFilePanelAsync("Export Word List", "", defaultName, ext, path =>
        {
            if (string.IsNullOrEmpty(path)) return;
            System.IO.File.WriteAllLines(path, words);
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
