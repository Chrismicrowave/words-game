using System.Collections.Generic;
using System.IO;
using SFB;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum LevelTab
{
    Challenges,
    Custom,
    Community
}

public class LevelPanelController : MonoBehaviour
{
    [Header("Master Config")]
    [SerializeField] private GameConfig config;

    [Header("Demo Mode")]
    [Tooltip("Enable demo restrictions: hides custom-list CRUD, caps lists and words per list.")]
    public bool isDemo = false;
    [Tooltip("Max number of custom word lists in demo mode.")]
    public int maxCustomLists = 1;
    [Tooltip("Max number of words/phases per list in demo mode.")]
    public int maxWordsPerList = 3;
    [Tooltip("Max challenge levels visible and unlockable in demo mode. 0 = no cap.")]
    public int maxChallengeLevels = 5;

    [Header("Debug")]
    [SerializeField] private bool unlockAllChallenges;  // bypass all challenge level locks
    [SerializeField] private int customUnlockLevels = 5;  // challenges to clear before Custom tab unlocks
    [SerializeField] private int communityUnlockLevels = 10;  // challenges to clear before Community tab unlocks
    [SerializeField] private bool unlockCustomTab;  // bypass Custom tab lock
    [SerializeField] private bool unlockCommunityTab;  // bypass Community tab lock

    public static LevelPanelController Instance { get; private set; }

    private GameConfigProfile ActiveProfile
    {
        get => config != null ? config.activeProfile : null;
    }

    public int LegacyChallengeCap => isDemo && !unlockAllChallenges ? Mathf.Max(0, maxChallengeLevels) : 0;
    public bool LegacyUnlockAllChallenges => unlockAllChallenges;
    public int LegacyCustomUnlockThreshold => Mathf.Max(0, customUnlockLevels);
    public bool LegacyUnlockCustomTab => unlockCustomTab;

    public bool IsDemo => ActiveProfile != null ? ActiveProfile.isDemo : isDemo;
    public int EffectiveMaxWordsPerList => ActiveProfile != null ? ActiveProfile.maxWordsPerList : maxWordsPerList;
    public bool CanAddWords => ActiveProfile == null || ActiveProfile.IsFeatureAvailable(ActiveProfile.unlockAddWords);
    public bool CanEditLists => ActiveProfile == null || ActiveProfile.IsFeatureAvailable(ActiveProfile.unlockEditLists);
    public bool CanImportLists => ActiveProfile == null || ActiveProfile.IsFeatureAvailable(ActiveProfile.unlockImportLists);
    public bool CanExportLists => ActiveProfile == null || ActiveProfile.IsFeatureAvailable(ActiveProfile.unlockExportLists);

    public bool ShouldShowImportButton => ActiveProfile == null ? !isDemo : CanImportLists;
    public bool ShouldShowExportButton => ActiveProfile == null ? !isDemo : CanExportLists;

    [Header("Tab Buttons")]
    [SerializeField] private Button challengesTabBtn;
    [SerializeField] private Button customTabBtn;
    [SerializeField] private Button communityTabBtn;
    [SerializeField] private Color tabActiveColor = new Color(1f, 0.5f, 0f, 1f);
    [SerializeField] private Color tabInactiveColor = Color.white;
    [SerializeField] private Color tabActiveTextColor = Color.white;
    [SerializeField] private Color tabInactiveTextColor = new Color(0.92f, 0.92f, 0.92f);

    [Header("Content Areas")]
    [SerializeField] private Transform levelGridContent;  // The "Content" child inside LevelGrid scroll
    [SerializeField] private Transform wordListContent;   // The "Content" child inside WordList scroll

    [Header("Prefabs")]
    [SerializeField] private GameObject levelButtonPrefab;   // For grid tiles
    [SerializeField] private GameObject wordLabelPrefab;     // For word preview items

    [Header("Button Row")]
    [SerializeField] private Button cancelBtn;
    [SerializeField] private Button okBtn;
    [SerializeField] private Button importBtn;
    [SerializeField] private Button exportBtn;
    [SerializeField] private Button createListBtn;
    [SerializeField] private Button deleteListBtn;
    [SerializeField] private Button duplicateListBtn;

    [Header("Font")]
    [SerializeField] private TMP_FontAsset chineseFontAsset;

    [Header("Placeholders")]
    [SerializeField] private GameObject communityPlaceholder;  // "Coming Soon" shown in Community tab
    [SerializeField] private GameObject customLockedPlaceholder; // "Clear X challenges" shown when Custom tab locked

    [Header("Scroll")]
    [SerializeField] private ScrollRect levelScrollRect;  // LevelGrid ScrollRect — reset to top on refresh

    [Header("Demo Limit Prompt")]
    [SerializeField] private GameObject demoCustomLimitPrompt;
    [SerializeField] private float demoPromptDuration = 2f;

    

    private const string LastLevelPathPrefKey = "LevelPanel_LastPath";
    private const string LastLevelTabPrefKey  = "LevelPanel_LastTab";

    private LevelTab currentTab = LevelTab.Challenges;
    private LevelWordListProvider selectedProvider;

    private List<LevelWordListProvider> challengeProviders = new List<LevelWordListProvider>();
    private List<LevelWordListProvider> challengeCnProviders = new List<LevelWordListProvider>();
    private List<LevelWordListProvider> customProviders = new List<LevelWordListProvider>();

    private Image challengesTabImage;
    private Image customTabImage;
    private Image communityTabImage;
    private TextMeshProUGUI challengesTabText;
    private TextMeshProUGUI customTabText;
    private TextMeshProUGUI communityTabText;

    void Awake()
    {
        Instance = this;
        challengesTabImage = challengesTabBtn?.GetComponent<Image>();
        customTabImage = customTabBtn?.GetComponent<Image>();
        communityTabImage = communityTabBtn?.GetComponent<Image>();

        challengesTabText = challengesTabBtn?.transform.Find("TabNameTMP")?.GetComponent<TextMeshProUGUI>();
        customTabText = customTabBtn?.transform.Find("TabNameTMP")?.GetComponent<TextMeshProUGUI>();
        communityTabText = communityTabBtn?.transform.Find("TabNameTMP")?.GetComponent<TextMeshProUGUI>();

        if (cancelBtn != null) cancelBtn.onClick.AddListener(OnCancel);
        if (okBtn != null) okBtn.onClick.AddListener(OnOK);
        if (importBtn != null) importBtn.onClick.AddListener(OnImport);
        if (exportBtn != null) exportBtn.onClick.AddListener(OnExport);
        if (createListBtn != null) createListBtn.onClick.AddListener(OnCreateList);
        if (deleteListBtn != null) deleteListBtn.onClick.AddListener(OnDeleteList);
        if (duplicateListBtn != null) duplicateListBtn.onClick.AddListener(OnDuplicateList);

        if (challengesTabBtn != null) challengesTabBtn.onClick.AddListener(() => SwitchTab(LevelTab.Challenges));
        if (customTabBtn != null) customTabBtn.onClick.AddListener(() => SwitchTab(LevelTab.Custom));
        if (communityTabBtn != null) communityTabBtn.onClick.AddListener(() => SwitchTab(LevelTab.Community));
    }

    void OnEnable()
    {
        if (Services.Get<InputHandler>() != null) Services.Get<InputHandler>().SetGameplayBlocked(true);
        RefreshAll();

        // Restore last selected tab from PlayerPrefs
        LevelTab restoreTab = LevelTab.Challenges;
        string savedTab = PlayerPrefs.GetString(LastLevelTabPrefKey, "Challenges");
        if (savedTab == "Custom") restoreTab = LevelTab.Custom;
        else if (savedTab == "Community") restoreTab = LevelTab.Community;
        SwitchTab(restoreTab);
    }

    void OnDisable()
    {
        if (Services.Get<InputHandler>() != null) Services.Get<InputHandler>().SetGameplayBlocked(false);
    }

    private void RefreshAll()
    {
        LevelWordListProvider.SeedCustomDirectoryIfEmpty();
        var allChallenges = LevelWordListProvider.ScanDirectory(
            LevelWordListProvider.GetChallengeDirectory());

        // Separate English and Chinese challenge files by naming convention
        challengeProviders = new List<LevelWordListProvider>();
        challengeCnProviders = new List<LevelWordListProvider>();
        foreach (var p in allChallenges)
        {
            string fileName = System.IO.Path.GetFileName(p.FilePath);
            if (fileName.Contains("_cn"))
                challengeCnProviders.Add(p);
            else
                challengeProviders.Add(p);
        }

        customProviders = LevelWordListProvider.ScanDirectory(
            LevelWordListProvider.GetCustomDirectory(), true);

        var phaseManager = Services.Get<PhaseManager>();
        if (phaseManager != null && !phaseManager.AllowChineseAndMixed)
        {
            challengeProviders.RemoveAll(provider => !phaseManager.CanLoadWordList(provider));
            challengeCnProviders.RemoveAll(provider => !phaseManager.CanLoadWordList(provider));
            customProviders.RemoveAll(provider => !phaseManager.CanLoadWordList(provider));
        }

    }

    /// <summary>Returns true when the current Unity locale is Chinese (zh-Hans).</summary>
    private static bool IsChineseLocale()
    {
        try
        {
            var locale = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale;
            return locale != null && locale.Identifier.Code == "zh-Hans";
        }
        catch { return false; }
    }

    private void UpdateOkButtonState()
    {
        if (okBtn != null)
            okBtn.interactable = selectedProvider != null
                && (currentTab != LevelTab.Challenges
                    || IsChallengeUnlocked(GetCurrentChallengeProviders(), selectedProvider));
    }

    private void SwitchTab(LevelTab tab)
    {
        currentTab = tab;
        selectedProvider = null;
        UpdateOkButtonState();
        UpdateTabColors();
        UpdateButtonVisibility();
        PopulateGrid();

        // Show/hide placeholder for Community
        if (communityPlaceholder != null)
            communityPlaceholder.SetActive(tab == LevelTab.Community);
    }

    private void UpdateTabColors()
    {
        bool isChallenges = currentTab == LevelTab.Challenges;
        bool isCustom = currentTab == LevelTab.Custom;
        bool isCommunity = currentTab == LevelTab.Community;

        if (challengesTabImage != null)
            challengesTabImage.color = isChallenges ? tabActiveColor : tabInactiveColor;
        if (customTabImage != null)
            customTabImage.color = isCustom ? tabActiveColor : tabInactiveColor;
        if (communityTabImage != null)
            communityTabImage.color = isCommunity ? tabActiveColor : tabInactiveColor;

        if (challengesTabText != null)
            challengesTabText.color = isChallenges ? tabActiveTextColor : tabInactiveTextColor;
        if (customTabText != null)
            customTabText.color = isCustom ? tabActiveTextColor : tabInactiveTextColor;
        if (communityTabText != null)
            communityTabText.color = isCommunity ? tabActiveTextColor : tabInactiveTextColor;
    }

    private string PreferredProgressTrackPrefix
    {
        get
        {
            var phaseManager = Services.Get<PhaseManager>();
            bool allowChineseAndMixed = phaseManager == null || phaseManager.AllowChineseAndMixed;
            return allowChineseAndMixed && IsChineseLocale()
                ? ChallengeTrackResolver.ChineseTrackPrefix
                : ChallengeTrackResolver.EnglishTrackPrefix;
        }
    }

    private bool IsCustomUnlocked
    {
        get
        {
            int completedChallenges = ChallengeProgression.GetUnlockedCountForTrack(PreferredProgressTrackPrefix) - 1;
            return ActiveProfile != null
                ? ActiveProfile.CanOpenCustomLists(completedChallenges)
                : unlockCustomTab || completedChallenges >= customUnlockLevels;
        }
    }

    private bool CanAccessCustomListActions(string actionName)
    {
        if (currentTab == LevelTab.Custom && IsCustomUnlocked)
            return true;

        Debug.LogWarning($"[LevelPanelController] {actionName} requires the unlocked Custom tab.");
        return false;
    }

    private bool DemoLimitsEnabled => ActiveProfile != null
        ? ActiveProfile.isDemo && !ActiveProfile.unlockAllFeatures
        : isDemo;

    private int EffectiveMaxCustomLists => ActiveProfile != null ? ActiveProfile.maxCustomLists : maxCustomLists;

    private bool IsCustomListLimitReached()
    {
        if (!DemoLimitsEnabled) return false;
        if (ActiveProfile != null && EffectiveMaxCustomLists <= 0) return false;
        return customProviders.Count >= EffectiveMaxCustomLists;
    }

    public bool IsWordLimitReached(int totalPhases)
    {
        if (!DemoLimitsEnabled) return false;
        if (ActiveProfile != null && EffectiveMaxWordsPerList <= 0) return false;
        return totalPhases >= EffectiveMaxWordsPerList;
    }

    private bool CanCreateLists => ActiveProfile == null || ActiveProfile.IsFeatureAvailable(ActiveProfile.unlockCreateLists);
    private bool CanDeleteLists => ActiveProfile == null || ActiveProfile.IsFeatureAvailable(ActiveProfile.unlockDeleteLists);
    private bool CanDuplicateLists => ActiveProfile == null || ActiveProfile.IsFeatureAvailable(ActiveProfile.unlockDuplicateLists);

    private bool ShouldShowFeature(bool available)
    {
        return ActiveProfile == null ? !isDemo : available;
    }

    private void UpdateButtonVisibility()
    {
        bool isCustom = currentTab == LevelTab.Custom;
        bool unlocked = IsCustomUnlocked;
        bool showCustomActions = isCustom && unlocked;
        if (importBtn != null) importBtn.gameObject.SetActive(showCustomActions && ShouldShowFeature(CanImportLists));
        if (exportBtn != null) exportBtn.gameObject.SetActive(showCustomActions && ShouldShowFeature(CanExportLists));
        if (createListBtn != null) createListBtn.gameObject.SetActive(showCustomActions && ShouldShowFeature(CanCreateLists));
        if (deleteListBtn != null) deleteListBtn.gameObject.SetActive(showCustomActions && ShouldShowFeature(CanDeleteLists));
        if (duplicateListBtn != null) duplicateListBtn.gameObject.SetActive(showCustomActions && ShouldShowFeature(CanDuplicateLists));

        if (customLockedPlaceholder != null)
            customLockedPlaceholder.SetActive(isCustom && !unlocked);
    }

    private void PopulateGrid()
    {
        // Clear existing buttons
        if (levelGridContent == null) return;
        foreach (Transform child in levelGridContent)
            Destroy(child.gameObject);

        // Clear word preview too
        ClearWordPreview();

        // Ensure OK/Cancel buttons are visible by default
        if (okBtn != null) okBtn.gameObject.SetActive(true);
        if (cancelBtn != null) cancelBtn.gameObject.SetActive(true);

        // If Custom tab is locked, skip grid (placeholder handles the visual)
        if (currentTab == LevelTab.Custom && !IsCustomUnlocked)
        {
            if (okBtn != null) okBtn.gameObject.SetActive(false);
            return;
        }

        // Pick challenge set based on locale
        var phaseManager = Services.Get<PhaseManager>();
        bool allowChineseAndMixed = phaseManager == null || phaseManager.AllowChineseAndMixed;
        bool isChinese = allowChineseAndMixed && IsChineseLocale();
        List<LevelWordListProvider> providers;
        switch (currentTab)
        {
            case LevelTab.Challenges:
                providers = isChinese ? challengeCnProviders : challengeProviders;
                break;
            case LevelTab.Custom:
                providers = customProviders;
                break;
            case LevelTab.Community:
            default:
                providers = new List<LevelWordListProvider>();
                break;
        }

        if (providers.Count == 0)
        {
            // Show empty state
            if (okBtn != null) okBtn.gameObject.SetActive(currentTab == LevelTab.Community ? false : true);
            return;
        }

        foreach (var provider in providers)
        {
            var btnObj = Instantiate(levelButtonPrefab, levelGridContent);
            var tmp = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
            {
                string displayName = provider.DisplayName;
                if (currentTab == LevelTab.Challenges && !string.IsNullOrEmpty(provider.DisplayNameZh))
                {
                    try
                    {
                        var locale = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale;
                        if (locale != null && locale.Identifier.Code == "zh-Hans")
                            displayName = provider.DisplayNameZh;
                    }
                    catch
                    {
                        // LocalizationSettings.SelectedLocale can throw NRE when
                        // the system hasn't fully initialized — ignore and use English.
                    }
                }
                tmp.text = displayName;

                if (chineseFontAsset != null && PinyinLookup.ContainsChinese(displayName))
                    tmp.font = chineseFontAsset;
            }

            var btn = btnObj.GetComponent<Button>();
            if (btn != null)
            {
                var captured = provider;
                btn.onClick.AddListener(() => OnLevelClicked(captured, btnObj));
            }

            // Apply locked/unlocked state for Challenge tab
            if (currentTab == LevelTab.Challenges)
            {
                bool unlocked = IsChallengeUnlocked(providers, provider);
                SetChallengeLockState(btnObj, btn, unlocked);
                SetStarDisplay(btnObj, providers, provider);

                // Show unlock description on special levels
                int levelNum = providers.IndexOf(provider) + 1;
                SetUnlockDisplay(btnObj, levelNum);
            }

            // Show level number (01, 02, etc.) for all tabs
            int cellIndex = providers.IndexOf(provider) + 1;
            foreach (Transform child in btnObj.transform)
            {
                if (child.name == "LevelNumTMP")
                {
                    var numTmp = child.GetComponent<TMPro.TextMeshProUGUI>();
                    if (numTmp != null) numTmp.text = cellIndex.ToString("D2");
                    break;
                }
            }

            // Show best time if a record exists (for both challenge and custom)
            SetTimeDisplay(btnObj, provider);
        }

        // Auto-select: restore saved selection or pick first unlocked item
        TryRestoreSelection(providers);
        UpdateOkButtonState();

        // Scroll back to top so first row is visible
        if (levelScrollRect != null)
            levelScrollRect.normalizedPosition = new Vector2(0, 1);
    }

    private bool IsChallengeUnlocked(List<LevelWordListProvider> providers, LevelWordListProvider provider)
    {
        int idx = providers.IndexOf(provider);
        if (idx < 0) return false;

        string trackPrefix = ChallengeTrackResolver.GetTrackPrefixForChallengeProvider(provider);
        if (trackPrefix == null) return false;

        int unlockedCount = ChallengeProgression.GetUnlockedCountForTrack(trackPrefix);
        return ChallengeAccessPolicy.CanEnterChallenge(
            ActiveProfile, idx, unlockedCount, LegacyChallengeCap, LegacyUnlockAllChallenges);
    }

    private List<LevelWordListProvider> GetCurrentChallengeProviders()
    {
        var phaseManager = Services.Get<PhaseManager>();
        bool allowChineseAndMixed = phaseManager == null || phaseManager.AllowChineseAndMixed;
        return allowChineseAndMixed && IsChineseLocale()
            ? challengeCnProviders
            : challengeProviders;
    }

    private void SetChallengeLockState(GameObject btnObj, Button btn, bool unlocked)
    {
        // Find children by name
        Transform nameTf = null;
        Transform lockedTf = null;
        foreach (Transform child in btnObj.transform)
        {
            if (child.name == "LevelNameTMP") nameTf = child;
            else if (child.name == "Locked") lockedTf = child;
        }

        if (nameTf != null) nameTf.gameObject.SetActive(unlocked);
        if (lockedTf != null) lockedTf.gameObject.SetActive(!unlocked);
        if (btn != null) btn.interactable = unlocked;
    }

    private void SetStarDisplay(GameObject btnObj, List<LevelWordListProvider> providers, LevelWordListProvider provider)
    {
        int idx = providers.IndexOf(provider);
        if (idx < 0) return;

        string trackPrefix = ChallengeTrackResolver.GetTrackPrefixForChallengeProvider(provider);
        if (trackPrefix == null) return;

        int stars = ChallengeProgression.GetStarRating(idx, trackPrefix);
        string starText = ChallengeProgression.GetStarDisplay(stars);

        // Find Stars child
        foreach (Transform child in btnObj.transform)
        {
            if (child.name == "Stars")
            {
                var tmp = child.GetComponent<TMPro.TextMeshProUGUI>();
                if (tmp != null) tmp.text = starText;
                break;
            }
        }
    }

    private void SetUnlockDisplay(GameObject btnObj, int levelNum)
    {
        int customUnlockThreshold = ActiveProfile != null
            ? ActiveProfile.customUnlockLevels
            : customUnlockLevels;
        bool customAlwaysUnlocked = ActiveProfile != null
            && (ActiveProfile.unlockAllFeatures || ActiveProfile.unlockCustomLists);
        bool isCustomUnlock = !customAlwaysUnlocked && levelNum == customUnlockThreshold;
        bool isCommunityUnlock = levelNum == communityUnlockLevels;

        foreach (Transform child in btnObj.transform)
        {
            if (child.name == "Unlocks")
            {
                if (isCustomUnlock || isCommunityUnlock)
                {
                    if (isCommunityUnlock)
                    {
                        // Level 10+ shows community unlock — switch from default key
                        var localizeText = child.GetComponent<LocalizeText>();
                        if (localizeText != null)
                            localizeText.localizedString.SetReference("UI", "UI.Level.UnlockCommunity");
                    }
                    // Level 5+ or default: LocalizeText on prefab already resolves "UI.Level.UnlockCustom"
                    child.gameObject.SetActive(true);
                }
                else
                {
                    child.gameObject.SetActive(false);
                }
                break;
            }
        }
    }

    private void SetTimeDisplay(GameObject btnObj, LevelWordListProvider provider)
    {
        string listKey = provider.GetListKey();
        var time = ListTimeManager.GetTime(listKey);

        foreach (Transform child in btnObj.transform)
        {
            if (child.name == "Time")
            {
                var tmp = child.GetComponent<TMPro.TextMeshProUGUI>();
                if (tmp != null)
                {
                    if (time.HasValue)
                    {
                        tmp.text = "Best: " + ListTimeManager.FormatTime(time.Value.TotalTime);
                        child.gameObject.SetActive(true);
                    }
                    else
                    {
                        child.gameObject.SetActive(false);
                    }
                }
                break;
            }
        }
    }

    private void TryRestoreSelection(List<LevelWordListProvider> providers)
    {
        if (providers.Count == 0) return;

        string savedPath = PlayerPrefs.GetString(LastLevelPathPrefKey, "");

        // Try to match saved path
        if (!string.IsNullOrEmpty(savedPath))
        {
            for (int i = 0; i < providers.Count; i++)
            {
                if (providers[i].FilePath == savedPath && i < levelGridContent.childCount)
                {
                    if (currentTab == LevelTab.Challenges && !IsChallengeUnlocked(providers, providers[i]))
                        break;

                    var btnObj = levelGridContent.GetChild(i).gameObject;
                    OnLevelClicked(providers[i], btnObj);
                    return;
                }
            }
        }

        // Fallback: select the first unlocked item
        for (int i = 0; i < levelGridContent.childCount; i++)
        {
            bool isLocked = currentTab == LevelTab.Challenges
                && !IsChallengeUnlocked(providers, providers[i]);
            if (!isLocked)
            {
                var fallbackBtn = levelGridContent.GetChild(i).gameObject;
                OnLevelClicked(providers[i], fallbackBtn);
                return;
            }
        }
    }

    private void OnLevelClicked(LevelWordListProvider provider, GameObject btnObj)
    {
        if (currentTab == LevelTab.Challenges
            && !IsChallengeUnlocked(GetCurrentChallengeProviders(), provider))
        {
            Debug.LogWarning("[LevelPanelController] The selected challenge is still locked.");
            return;
        }

        // Deselect previous
        if (levelGridContent != null)
        {
            foreach (Transform child in levelGridContent)
            {
                var img = child.GetComponent<Image>();
                if (img != null) img.color = tabInactiveColor;
            }
        }

        // Highlight selected
        var selectedImg = btnObj.GetComponent<Image>();
        if (selectedImg != null) selectedImg.color = tabActiveColor;

        selectedProvider = provider;
        UpdateOkButtonState();
        ShowWordPreview(provider);
    }

    private void ShowWordPreview(LevelWordListProvider provider)
    {
        ClearWordPreview();
        if (wordListContent == null || provider == null) return;

        var words = provider.GetWords();
        bool useChineseFont = chineseFontAsset != null &&
            provider.LanguageMode == LanguageMode.Mixed;

        foreach (var word in words)
        {
            if (DisplayWidthUtil.IsOverLimit(word, provider.LanguageMode))
                continue;

            var labelObj = Instantiate(wordLabelPrefab, wordListContent);
            var tmp = labelObj.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = word;
                if (useChineseFont) tmp.font = chineseFontAsset;
            }
        }
    }

    private void ClearWordPreview()
    {
        if (wordListContent == null) return;
        foreach (Transform child in wordListContent)
            Destroy(child.gameObject);
    }

    // --- Button Handlers ---

    public void OnCancel()
    {
        gameObject.SetActive(false);
    }

    public void OnOK()
    {
        if (selectedProvider == null) return;

        if (currentTab == LevelTab.Custom && !IsCustomUnlocked)
        {
            Debug.LogWarning("[LevelPanelController] Custom is locked; the selection was not loaded.");
            return;
        }
        if (currentTab == LevelTab.Challenges
            && !IsChallengeUnlocked(GetCurrentChallengeProviders(), selectedProvider))
        {
            Debug.LogWarning("[LevelPanelController] The selected challenge is still locked; the selection was not loaded.");
            return;
        }

        var phaseManager = Services.Get<PhaseManager>();
        if (phaseManager == null || !phaseManager.CanLoadWordList(selectedProvider))
        {
            Debug.LogWarning("[LevelPanelController] The selected word list could not be loaded; selection was not saved.");
            return;
        }

        LevelMode previousMode = phaseManager.CurrentLevelMode;
        phaseManager.CurrentLevelMode = currentTab == LevelTab.Challenges
            ? LevelMode.Challenge
            : LevelMode.Custom;
        if (!phaseManager.LoadWordList(selectedProvider))
        {
            phaseManager.CurrentLevelMode = previousMode;
            Debug.LogWarning("[LevelPanelController] The selected word list could not be loaded; selection was not saved.");
            return;
        }

        // Persist selection across sessions
        PlayerPrefs.SetString(LastLevelPathPrefKey, selectedProvider.FilePath);
        PlayerPrefs.SetString(LastLevelTabPrefKey, currentTab.ToString());
        PlayerPrefs.Save();

        // Restart timer and transition to playing
        Services.Get<TimerSystem>().ResetAll();
        Services.Get<GameStateManager>().RaiseGameReset();
        Services.Get<GameStateManager>().TransitionTo(GameState.Playing);
        gameObject.SetActive(false);
    }

    public void OnImport()
    {
        if (!CanAccessCustomListActions("Importing")) return;
        if (ActiveProfile != null && !CanImportLists)
        {
            Debug.LogWarning("[LevelPanelController] Importing custom lists is disabled by the active profile.");
            return;
        }
        if (IsCustomListLimitReached())
        {
            ShowDemoLimitPrompt($"Max {EffectiveMaxCustomLists} custom list in demo");
            return;
        }

        var ext = new[] { new ExtensionFilter("Text Files", "txt") };
        StandaloneFileBrowser.OpenFilePanelAsync("Import Word List", "", ext, false, paths =>
        {
            if (paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
            if (!CanAccessCustomListActions("Importing") || !CanImportLists) return;
            if (IsCustomListLimitReached())
            {
                ShowDemoLimitPrompt($"Max {EffectiveMaxCustomLists} custom list in demo");
                return;
            }

            var phaseManager = Services.Get<PhaseManager>();
            if (phaseManager == null)
            {
                Debug.LogWarning("[LevelPanelController] PhaseManager is unavailable; the custom list was not copied.");
                return;
            }
            if (!phaseManager.CanLoadWordList(new LevelWordListProvider(paths[0])))
            {
                Debug.LogWarning("[LevelPanelController] Chinese content is disabled; the custom list was not copied.");
                return;
            }

            // Copy to custom levels directory
            string fileName = Path.GetFileName(paths[0]);
            string destDir = LevelWordListProvider.GetCustomDirectory();
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);
            string destPath = Path.Combine(destDir, fileName);

            // Avoid overwriting: append number if exists
            if (File.Exists(destPath))
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string extOnly = Path.GetExtension(fileName);
                int n = 1;
                do
                {
                    destPath = Path.Combine(destDir, $"{nameWithoutExt}_{n}{extOnly}");
                    n++;
                } while (File.Exists(destPath));
            }

            File.Copy(paths[0], destPath);

            // Refresh
            RefreshAll();
            SwitchTab(LevelTab.Custom);
        });
    }

    public void OnExport()
    {
        if (!CanAccessCustomListActions("Exporting")) return;
        if (ActiveProfile != null && !CanExportLists)
        {
            Debug.LogWarning("[LevelPanelController] Exporting custom lists is disabled by the active profile.");
            return;
        }
        if (selectedProvider == null) return;

        var providerToExport = selectedProvider;
        string defaultName = providerToExport.DisplayName.Replace(" ", "_");
        var words = providerToExport.GetWords();
        var ext = new[] { new ExtensionFilter("Text Files", "txt") };
        StandaloneFileBrowser.SaveFilePanelAsync("Export Word List", "", defaultName, ext, path =>
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!CanAccessCustomListActions("Exporting") || !CanExportLists || selectedProvider != providerToExport)
                return;
            File.WriteAllLines(path, words);
        });
    }

    public void OnCreateList()
    {
        if (!CanAccessCustomListActions("Creating a list")) return;
        if (ActiveProfile != null && !CanCreateLists)
        {
            Debug.LogWarning("[LevelPanelController] Creating custom lists is disabled by the active profile.");
            return;
        }
        if (IsCustomListLimitReached())
        {
            ShowDemoLimitPrompt($"Max {EffectiveMaxCustomLists} custom list in demo");
            return;
        }

        var provider = LevelWordListProvider.CreateNewCustom();
        RefreshAll();
        SwitchTab(LevelTab.Custom);

        // Auto-select the new list
        int idx = customProviders.IndexOf(provider);
        if (idx >= 0 && idx < levelGridContent.childCount)
        {
            var newBtn = levelGridContent.GetChild(idx).gameObject;
            OnLevelClicked(provider, newBtn);
        }
    }

    public void OnDeleteList()
    {
        if (!CanAccessCustomListActions("Deleting a list")) return;
        if (ActiveProfile != null && !CanDeleteLists)
        {
            Debug.LogWarning("[LevelPanelController] Deleting custom lists is disabled by the active profile.");
            return;
        }
        if (selectedProvider == null || currentTab != LevelTab.Custom) return;

        // Delete time record before deleting the file
        ListTimeManager.DeleteTime(selectedProvider.GetListKey());
        selectedProvider.DeleteFile();
        selectedProvider = null;
        RefreshAll();
        SwitchTab(LevelTab.Custom);
    }

    public void OnDuplicateList()
    {
        if (!CanAccessCustomListActions("Duplicating a list")) return;
        if (ActiveProfile != null && !CanDuplicateLists)
        {
            Debug.LogWarning("[LevelPanelController] Duplicating custom lists is disabled by the active profile.");
            return;
        }
        if (selectedProvider == null || currentTab != LevelTab.Custom) return;

        if (IsCustomListLimitReached())
        {
            ShowDemoLimitPrompt($"Max {EffectiveMaxCustomLists} custom list in demo");
            return;
        }

        var copy = selectedProvider.Duplicate();
        if (copy == null) return;

        RefreshAll();
        SwitchTab(LevelTab.Custom);

        // Auto-select the duplicated list
        int idx = customProviders.IndexOf(copy);
        if (idx >= 0 && idx < levelGridContent.childCount)
        {
            var dupBtn = levelGridContent.GetChild(idx).gameObject;
            OnLevelClicked(copy, dupBtn);
        }
    }

    private void ShowDemoLimitPrompt(string message)
    {
        if (demoCustomLimitPrompt == null) return;
        var tmp = demoCustomLimitPrompt.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        if (tmp != null) tmp.text = message;
        demoCustomLimitPrompt.SetActive(true);
        if (this.isActiveAndEnabled)
            StartCoroutine(HideDemoLimitPrompt());
    }

    private System.Collections.IEnumerator HideDemoLimitPrompt()
    {
        yield return new WaitForSeconds(demoPromptDuration);
        if (demoCustomLimitPrompt != null)
            demoCustomLimitPrompt.SetActive(false);
    }
}
