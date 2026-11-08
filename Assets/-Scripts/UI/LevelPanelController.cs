using System.Collections.Generic;
using System.IO;
using System;
using SFB;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Words.Community;

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

    [Header("Feature Access and Limits")]
    [Tooltip("Maximum number of custom word lists. 0 means unlimited.")]
    [Min(0)]
    public int maxCustomLists = 1;
    [Tooltip("Maximum number of words/phases per list. 0 means unlimited.")]
    [Min(0)]
    public int maxWordsPerList = 3;
    [Tooltip("Maximum challenge levels available. 0 means unlimited.")]
    [Min(0)]
    public int maxChallengeLevels = 5;

    [Header("Progression")]
    [SerializeField] private int customUnlockLevels = 5;  // challenges to clear before Custom tab unlocks
    [SerializeField] private int communityUnlockLevels = 10;  // challenges to clear before Community tab unlocks

    public static LevelPanelController Instance { get; private set; }

    private GameConfigProfile ActiveProfile
    {
        get => config != null ? config.activeProfile : null;
    }

    public int LegacyChallengeCap => Mathf.Max(0, maxChallengeLevels);
    public bool LegacyUnlockAllChallenges => false;
    public int LegacyCustomUnlockThreshold => Mathf.Max(0, customUnlockLevels);
    public bool LegacyUnlockCustomTab => false;
    public int LegacyCommunityUnlockThreshold => Mathf.Max(0, communityUnlockLevels);
    public bool LegacyUnlockCommunityTab => false;

    public int EffectiveMaxWordsPerList => ActiveProfile != null ? ActiveProfile.maxWordsPerList : maxWordsPerList;
    public bool CanAddWords => IsCustomUnlocked;
    public bool CanEditLists => IsCustomUnlocked;
    public bool CanImportLists => IsCustomUnlocked;
    public bool CanExportLists => IsCustomUnlocked;

    public bool CanCreateListsFromHub => IsCustomUnlocked;
    public bool CanEditListsFromHub => IsCustomUnlocked;
    public bool CanDeleteListsFromHub => IsCustomUnlocked;
    public bool CanDuplicateListsFromHub => IsCustomUnlocked;
    public bool CanImportListsFromHub => IsCustomUnlocked;
    public bool CanExportListsFromHub => IsCustomUnlocked;
    public bool CanOpenLibraryFromHub => IsCustomUnlocked;
    public bool CanOpenCommunityFromHub => IsCommunityEnabledFromHub && IsCommunityUnlocked;
    public bool HasAccessProfileFromHub => ActiveProfile != null;
    public bool IsCommunityEnabledFromHub => ActiveProfile != null && ActiveProfile.IsCommunityAvailable;
    public bool IsLibraryUnlockBypassedFromHub => ActiveProfile != null
        && ActiveProfile.unlockAllFeatures;
    public bool IsCommunityUnlockBypassedFromHub => ActiveProfile != null
        && ActiveProfile.unlockAllFeatures;
    public int CompletedChallengesForHub
    {
        get
        {
            var phaseManager = Services.Get<PhaseManager>();
            return phaseManager != null
                ? phaseManager.CompletedChallengesForProfile
                : ChallengeProgression.GetCompletedChallengesForTrack(HubProgressTrackPrefix);
        }
    }
    public int CustomUnlockRequirementForHub => ActiveProfile != null
        ? Mathf.Max(0, ActiveProfile.customUnlockLevels)
        : LegacyCustomUnlockThreshold;
    public int CommunityUnlockRequirementForHub => ActiveProfile != null
        ? Mathf.Max(0, ActiveProfile.communityUnlockLevels)
        : LegacyCommunityUnlockThreshold;

    public bool ShouldShowImportButton => CanImportLists;
    public bool ShouldShowExportButton => CanExportLists;

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

    [Header("Custom List Limit Prompt")]
    [FormerlySerializedAs("demoCustomLimitPrompt")]
    [SerializeField] private GameObject limitPrompt;
    [FormerlySerializedAs("demoPromptDuration")]
    [SerializeField] private float limitPromptDuration = 2f;

    

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
        if (tab == LevelTab.Community && !IsCommunityUnlocked)
        {
            Debug.LogWarning("[LevelPanelController] Community is still locked by the active profile.");
            return;
        }

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

    public string HubProgressTrackPrefix
    {
        get
        {
            var phaseManager = Services.Get<PhaseManager>();
            if (phaseManager != null)
                return phaseManager.PreferredProgressTrackPrefix;

            bool allowChineseAndMixed = ActiveProfile == null || ActiveProfile.allowChineseAndMixed;
            string localeCode = IsChineseLocale() ? "zh-Hans" : "en";
            return ChallengeTrackResolver.ResolvePreferredTrackPrefix(allowChineseAndMixed, localeCode);
        }
    }

    private bool IsCustomUnlocked
    {
        get => ActiveProfile != null
            && ActiveProfile.CanOpenCustomLists(CompletedChallengesForHub);
    }

    private bool IsCommunityUnlocked
    {
        get => ActiveProfile != null
            && ActiveProfile.CanOpenCommunityLists(CompletedChallengesForHub);
    }

    private bool CanAccessCustomListActions(string actionName)
    {
        if (currentTab == LevelTab.Custom && IsCustomUnlocked)
            return true;

        Debug.LogWarning($"[LevelPanelController] {actionName} requires the unlocked Custom tab.");
        return false;
    }

    private int EffectiveMaxCustomLists => ActiveProfile != null ? ActiveProfile.maxCustomLists : maxCustomLists;

    private bool IsCustomListLimitReached()
    {
        return EffectiveMaxCustomLists > 0 && customProviders.Count >= EffectiveMaxCustomLists;
    }

    public bool IsWordLimitReached(int totalPhases)
    {
        return EffectiveMaxWordsPerList > 0 && totalPhases >= EffectiveMaxWordsPerList;
    }

    public bool CanCreateListFromHub(int wordCount, out string error)
    {
        RefreshProviderCacheForStart();
        if (!IsCustomUnlocked)
        {
            error = "Custom lists are still locked for the active profile.";
            return false;
        }
        if (IsCustomListLimitReached())
        {
            error = "The active profile has reached its custom list limit.";
            return false;
        }
        if (EffectiveMaxWordsPerList > 0 && wordCount > EffectiveMaxWordsPerList)
        {
            error = "This list exceeds the active profile's word limit.";
            return false;
        }
        error = null;
        return true;
    }

    public bool CanEditListFromHub(
        int wordCount,
        bool changesContent,
        out string error,
        int existingWordCount = 0)
    {
        if (!IsCustomUnlocked)
        {
            error = "Custom lists are still locked for the active profile.";
            return false;
        }
        if (changesContent && !CanAddWords)
        {
            error = "The active profile does not allow changes to custom lists.";
            return false;
        }
        if (changesContent && EffectiveMaxWordsPerList > 0
            && wordCount > EffectiveMaxWordsPerList
            && wordCount > Mathf.Max(0, existingWordCount))
        {
            error = "This edit exceeds the active profile's word limit.";
            return false;
        }
        error = null;
        return true;
    }

    public bool CanImportListFromHub(out string error)
    {
        RefreshProviderCacheForStart();
        if (!IsCustomUnlocked)
        {
            error = "Importing custom lists is disabled by the active profile.";
            return false;
        }
        if (IsCustomListLimitReached())
        {
            error = "The active profile has reached its custom list limit.";
            return false;
        }
        error = null;
        return true;
    }

    public bool CanExportListFromHub(out string error)
    {
        if (!IsCustomUnlocked)
        {
            error = "Exporting custom lists is disabled by the active profile.";
            return false;
        }
        error = null;
        return true;
    }

    private void UpdateButtonVisibility()
    {
        bool isCustom = currentTab == LevelTab.Custom;
        bool unlocked = IsCustomUnlocked;
        bool showCustomActions = isCustom && unlocked;
        if (importBtn != null) importBtn.gameObject.SetActive(showCustomActions);
        if (exportBtn != null) exportBtn.gameObject.SetActive(showCustomActions);
        if (createListBtn != null) createListBtn.gameObject.SetActive(showCustomActions);
        if (deleteListBtn != null) deleteListBtn.gameObject.SetActive(showCustomActions);
        if (duplicateListBtn != null) duplicateListBtn.gameObject.SetActive(showCustomActions);

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
        int idx = FindProviderIndex(providers, provider);
        if (idx < 0) return false;

        string trackPrefix = ChallengeTrackResolver.GetTrackPrefixForChallengeProvider(provider);
        if (trackPrefix == null) return false;

        int unlockedCount = ChallengeProgression.GetUnlockedCountForTrack(trackPrefix);
        return ChallengeAccessPolicy.CanEnterChallenge(
            ActiveProfile, idx, unlockedCount, LegacyChallengeCap, LegacyUnlockAllChallenges);
    }

    private static int FindProviderIndex(List<LevelWordListProvider> providers, LevelWordListProvider provider)
    {
        if (providers == null || provider == null) return -1;

        for (int i = 0; i < providers.Count; i++)
        {
            LevelWordListProvider candidate = providers[i];
            if (candidate == provider) return i;
            if (candidate == null) continue;

            string candidateKey = candidate.GetListKey();
            string providerKey = provider.GetListKey();
            if (!string.IsNullOrEmpty(candidateKey)
                && string.Equals(candidateKey, providerKey, StringComparison.Ordinal))
                return i;

            if (!string.IsNullOrEmpty(candidate.FilePath) && !string.IsNullOrEmpty(provider.FilePath))
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(candidate.FilePath), Path.GetFullPath(provider.FilePath),
                        StringComparison.OrdinalIgnoreCase))
                        return i;
                }
                catch (Exception)
                {
                    // Fall through to the remaining identity check.
                }
            }
        }

        return -1;
    }

    private void RefreshProviderCacheForStart()
    {
        // This path is also called on the inactive legacy controller by the production
        // Hub. It must inspect provider files without enabling the old panel or seeding
        // default custom lists as OnEnable/RefreshAll does.
        List<LevelWordListProvider> allChallenges = LevelWordListProvider.ScanDirectory(
            LevelWordListProvider.GetChallengeDirectory());
        challengeProviders = new List<LevelWordListProvider>();
        challengeCnProviders = new List<LevelWordListProvider>();
        foreach (LevelWordListProvider provider in allChallenges)
        {
            string fileName = Path.GetFileName(provider.FilePath);
            if (fileName.IndexOf("_cn", StringComparison.OrdinalIgnoreCase) >= 0)
                challengeCnProviders.Add(provider);
            else
                challengeProviders.Add(provider);
        }

        customProviders = LevelWordListProvider.ScanDirectory(
            LevelWordListProvider.GetCustomDirectory(), true);
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
        bool customAlwaysUnlocked = ActiveProfile != null
            && ActiveProfile.CanOpenCustomLists(completedChallenges: 0);
        bool communityAlwaysUnlocked = ActiveProfile != null
            && ActiveProfile.CanOpenCommunityLists(completedChallenges: 0);
        bool isCustomUnlock = ActiveProfile != null && !customAlwaysUnlocked && CustomUnlockRequirementForHub > 0
            && levelNum == CustomUnlockRequirementForHub;
        bool isCommunityUnlock = IsCommunityEnabledFromHub && !communityAlwaysUnlocked && CommunityUnlockRequirementForHub > 0
            && levelNum == CommunityUnlockRequirementForHub;

        foreach (Transform child in btnObj.transform)
        {
            if (child.name == "Unlocks")
            {
                if (isCustomUnlock || isCommunityUnlock)
                {
                    if (isCommunityUnlock)
                    {
                        // Use the Community milestone text instead of the prefab's Custom default.
                        var localizeText = child.GetComponent<LocalizeText>();
                        if (localizeText != null)
                            localizeText.localizedString.SetReference("UI", "UI.Level.UnlockCommunity");
                    }
                    // Otherwise LocalizeText on the prefab resolves "UI.Level.UnlockCustom".
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

        if (!TryStartProvider(selectedProvider, currentTab, out string error))
            Debug.LogWarning("[LevelPanelController] " + error);
    }

    /// <summary>
    /// Starts a provider selected by the production Hub while reusing the same
    /// eligibility, preferences, reset, and state-transition path as the legacy OK button.
    /// Safe to call while this legacy panel is inactive; it does not display the panel.
    /// </summary>
    public bool TryStartProvider(LevelWordListProvider provider, LevelTab tab, out string error)
    {
        return TryStartProviderCore(provider, tab, 0, out error);
    }

    /// <summary>Starts only the exact immutable cache provider after Community profile/cache validation.</summary>
    public bool TryStartCommunityProvider(CommunityWordListProvider provider,
        ulong runStartAccountId, out string error)
    {
        return TryStartProviderCore(provider, LevelTab.Community, runStartAccountId, out error);
    }

    private bool TryStartProviderCore(IWordListProvider provider, LevelTab tab,
        ulong runStartAccountId, out string error)
    {
        error = string.Empty;
        if (provider == null)
        {
            error = "No word list was selected.";
            return false;
        }

        bool isCommunityProvider = provider is CommunityWordListProvider;
        if (tab == LevelTab.Community)
        {
            if (!isCommunityProvider)
            {
                error = "A validated Community download is required before playing this item.";
                return false;
            }
            if (!CanOpenCommunityFromHub)
            {
                error = "Community lists are still locked or disabled for the active profile.";
                return false;
            }

            var communityProvider = (CommunityWordListProvider)provider;
            ICommunityService communityService = CommunityServiceRegistry.GetShared();
            CommunityCacheEntry entry = null;
            IReadOnlyList<CommunityCacheEntry> cachedEntries = communityService.GetCachedEntries();
            if (cachedEntries != null)
            {
                foreach (CommunityCacheEntry candidate in cachedEntries)
                {
                    if (candidate != null && candidate.Key != null && candidate.Version != null
                        && candidate.Key.Equals(communityProvider.Key)
                        && candidate.Version.Equals(communityProvider.Version))
                    {
                        entry = candidate;
                        break;
                    }
                }
            }
            if (entry == null)
            {
                error = "This Community cache version is no longer available; download it again before playing.";
                return false;
            }
            if (!TryResolveValidatedCommunityProvider(communityProvider, entry,
                out CommunityWordListProvider resolvedProvider, out error))
            {
                return false;
            }
            // Cache reads reconstruct an immutable provider from the validated on-disk payload.
            // Start that trusted instance; the UI's provider is only an app/key/version selector.
            provider = resolvedProvider;
        }
        else
        {
            if (!(provider is LevelWordListProvider localProvider))
            {
                error = "Only downloaded Community items can start from the Community page.";
                return false;
            }

            RefreshProviderCacheForStart();

            if (!localProvider.IsValid || string.IsNullOrWhiteSpace(localProvider.FilePath)
                || !File.Exists(localProvider.FilePath))
            {
                error = "The selected word list is invalid or its file is missing.";
                return false;
            }

            if (tab == LevelTab.Custom)
            {
                if (!IsCustomUnlocked)
                {
                    error = "Custom lists are still locked for the active profile.";
                    return false;
                }
                if (FindProviderIndex(customProviders, localProvider) < 0)
                {
                    error = "The selected provider is not an available custom list.";
                    return false;
                }
            }
            else if (tab == LevelTab.Challenges)
            {
                List<LevelWordListProvider> currentChallenges = GetCurrentChallengeProviders();
                if (FindProviderIndex(currentChallenges, localProvider) < 0)
                {
                    error = "The selected challenge is not available for the current language settings.";
                    return false;
                }
                if (!IsChallengeUnlocked(currentChallenges, localProvider))
                {
                    error = "The selected challenge is locked by the active profile or progression.";
                    return false;
                }
            }
        }

        var phaseManager = Services.Get<PhaseManager>();
        if (phaseManager == null || !phaseManager.CanLoadWordList(provider))
        {
            error = "The selected word list cannot be loaded with the current language settings.";
            return false;
        }

        TimerSystem timerSystem = Services.Get<TimerSystem>();
        GameStateManager gameStateManager = Services.Get<GameStateManager>();
        if (timerSystem == null || gameStateManager == null)
        {
            error = "Game services are unavailable; the selected list was not started.";
            return false;
        }

        LevelMode previousMode = phaseManager.CurrentLevelMode;
        phaseManager.CurrentLevelMode = tab == LevelTab.Challenges
            ? LevelMode.Challenge
            : LevelMode.Custom;
        if (tab == LevelTab.Community)
            CommunityGameplayRunContext.Prepare((CommunityWordListProvider)provider, runStartAccountId);
        bool loaded;
        try
        {
            loaded = phaseManager.LoadWordList(provider);
        }
        catch
        {
            if (tab == LevelTab.Community)
                CommunityGameplayRunContext.ClearPending();
            phaseManager.CurrentLevelMode = previousMode;
            throw;
        }
        if (!loaded)
        {
            if (tab == LevelTab.Community)
                CommunityGameplayRunContext.ClearPending();
            phaseManager.CurrentLevelMode = previousMode;
            error = "The selected word list could not be loaded.";
            return false;
        }

        // Community items are account-scoped remote identities, not a local file path.
        // Keep the existing local selection preference untouched for Community runs.
        if (tab != LevelTab.Community)
        {
            PlayerPrefs.SetString(LastLevelPathPrefKey, ((LevelWordListProvider)provider).FilePath);
            PlayerPrefs.SetString(LastLevelTabPrefKey, tab.ToString());
            PlayerPrefs.Save();
        }

        // Restart timer and transition to playing
        timerSystem.ResetAll();
        gameStateManager.RaiseGameReset();
        gameStateManager.TransitionTo(GameState.Playing);
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
        return true;
    }

    private static bool TryResolveValidatedCommunityProvider(
        CommunityWordListProvider requestedProvider,
        CommunityCacheEntry entry,
        out CommunityWordListProvider resolvedProvider,
        out string error)
    {
        resolvedProvider = null;
        error = string.Empty;
        if (requestedProvider == null || requestedProvider.Key == null
            || requestedProvider.Version == null || entry == null
            || entry.Key == null || entry.Version == null)
        {
            error = "The selected Community cache identity is invalid.";
            return false;
        }

        if (!entry.Key.Equals(requestedProvider.Key)
            || !entry.Version.Equals(requestedProvider.Version))
        {
            error = "This exact Community content version is no longer cached; it was not replaced.";
            return false;
        }

        CommunityWordListProvider cachedProvider = entry.Provider;
        if (cachedProvider == null || cachedProvider.Key == null || cachedProvider.Version == null
            || !cachedProvider.Key.Equals(entry.Key)
            || !cachedProvider.Version.Equals(entry.Version))
        {
            error = "The validated Community cache entry does not match its content identity.";
            return false;
        }

        resolvedProvider = cachedProvider;
        return true;
    }

    public void OnImport()
    {
        if (!CanAccessCustomListActions("Importing")) return;
        if (!CanImportLists)
        {
            Debug.LogWarning("[LevelPanelController] Importing custom lists is not enabled by the active profile.");
            return;
        }
        if (IsCustomListLimitReached())
        {
            ShowLimitPrompt($"Maximum of {EffectiveMaxCustomLists} custom lists reached.");
            return;
        }

        var ext = new[] { new ExtensionFilter("Text Files", "txt") };
        StandaloneFileBrowser.OpenFilePanelAsync("Import Word List", "", ext, false, paths =>
        {
            if (paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
            if (!CanAccessCustomListActions("Importing") || !CanImportLists) return;
            if (IsCustomListLimitReached())
            {
                ShowLimitPrompt($"Maximum of {EffectiveMaxCustomLists} custom lists reached.");
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
        if (!CanExportLists)
        {
            Debug.LogWarning("[LevelPanelController] Exporting custom lists is not enabled by the active profile.");
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
        if (!IsCustomUnlocked)
        {
            Debug.LogWarning("[LevelPanelController] Creating custom lists is not enabled by the active profile.");
            return;
        }
        if (IsCustomListLimitReached())
        {
            ShowLimitPrompt($"Maximum of {EffectiveMaxCustomLists} custom lists reached.");
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
        if (!IsCustomUnlocked)
        {
            Debug.LogWarning("[LevelPanelController] Deleting custom lists is not enabled by the active profile.");
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
        if (!IsCustomUnlocked)
        {
            Debug.LogWarning("[LevelPanelController] Duplicating custom lists is not enabled by the active profile.");
            return;
        }
        if (selectedProvider == null || currentTab != LevelTab.Custom) return;

        if (IsCustomListLimitReached())
        {
            ShowLimitPrompt($"Maximum of {EffectiveMaxCustomLists} custom lists reached.");
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

    private void ShowLimitPrompt(string message)
    {
        if (limitPrompt == null) return;
        var tmp = limitPrompt.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        if (tmp != null) tmp.text = message;
        limitPrompt.SetActive(true);
        if (this.isActiveAndEnabled)
            StartCoroutine(HideLimitPrompt());
    }

    private System.Collections.IEnumerator HideLimitPrompt()
    {
        yield return new WaitForSeconds(limitPromptDuration);
        if (limitPrompt != null)
            limitPrompt.SetActive(false);
    }
}
