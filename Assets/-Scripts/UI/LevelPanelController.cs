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
    [Header("Tab Buttons")]
    [SerializeField] private Button challengesTabBtn;
    [SerializeField] private Button customTabBtn;
    [SerializeField] private Button communityTabBtn;
    [SerializeField] private Color tabActiveColor = new Color(1f, 0.5f, 0f, 1f);
    [SerializeField] private Color tabInactiveColor = Color.white;

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

    [Header("Font")]
    [SerializeField] private TMP_FontAsset chineseFontAsset;

    [Header("Trending Placeholder")]
    [SerializeField] private GameObject communityPlaceholder;  // "Coming Soon" shown in Trending tab

    [Header("Scroll")]
    [SerializeField] private ScrollRect levelScrollRect;  // LevelGrid ScrollRect — reset to top on refresh

    [Header("Debug")]
    [SerializeField] private bool unlockAllChallenges;  // bypass all challenge level locks
    [SerializeField] private int customUnlockLevels = 5;  // challenges to clear before Custom tab unlocks
    [SerializeField] private bool unlockCustomTab;  // bypass Custom tab lock

    private const string LastLevelPathPrefKey = "LevelPanel_LastPath";
    private const string LastLevelTabPrefKey  = "LevelPanel_LastTab";

    private LevelTab currentTab = LevelTab.Challenges;
    private LevelWordListProvider selectedProvider;

    private List<LevelWordListProvider> challengeProviders = new List<LevelWordListProvider>();
    private List<LevelWordListProvider> customProviders = new List<LevelWordListProvider>();

    private Image challengesTabImage;
    private Image customTabImage;
    private Image communityTabImage;

    void Awake()
    {
        challengesTabImage = challengesTabBtn?.GetComponent<Image>();
        customTabImage = customTabBtn?.GetComponent<Image>();
        communityTabImage = communityTabBtn?.GetComponent<Image>();

        if (cancelBtn != null) cancelBtn.onClick.AddListener(OnCancel);
        if (okBtn != null) okBtn.onClick.AddListener(OnOK);
        if (importBtn != null) importBtn.onClick.AddListener(OnImport);
        if (exportBtn != null) exportBtn.onClick.AddListener(OnExport);
        if (createListBtn != null) createListBtn.onClick.AddListener(OnCreateList);
        if (deleteListBtn != null) deleteListBtn.onClick.AddListener(OnDeleteList);

        if (challengesTabBtn != null) challengesTabBtn.onClick.AddListener(() => SwitchTab(LevelTab.Challenges));
        if (customTabBtn != null) customTabBtn.onClick.AddListener(() => SwitchTab(LevelTab.Custom));
        if (communityTabBtn != null) communityTabBtn.onClick.AddListener(() => SwitchTab(LevelTab.Trending));
    }

    void OnEnable()
    {
        if (Services.Get<InputHandler>() != null) Services.Get<InputHandler>().SetGameplayBlocked(true);
        RefreshAll();

        // Restore last selected tab from PlayerPrefs
        LevelTab restoreTab = LevelTab.Challenges;
        string savedTab = PlayerPrefs.GetString(LastLevelTabPrefKey, "Challenges");
        if (savedTab == "Custom") restoreTab = LevelTab.Custom;
        else if (savedTab == "Trending") restoreTab = LevelTab.Trending;
        SwitchTab(restoreTab);
    }

    void OnDisable()
    {
        if (Services.Get<InputHandler>() != null) Services.Get<InputHandler>().SetGameplayBlocked(false);
    }

    private void RefreshAll()
    {
        challengeProviders = LevelWordListProvider.ScanDirectory(
            LevelWordListProvider.GetChallengeDirectory());
        customProviders = LevelWordListProvider.ScanDirectory(
            LevelWordListProvider.GetCustomDirectory(), true);
    }

    private void SwitchTab(LevelTab tab)
    {
        currentTab = tab;
        selectedProvider = null;
        UpdateTabColors();
        UpdateButtonVisibility();
        PopulateGrid();

        // Show/hide placeholder for Trending
        if (communityPlaceholder != null)
            communityPlaceholder.SetActive(tab == LevelTab.Trending);
    }

    private void UpdateTabColors()
    {
        if (challengesTabImage != null)
            challengesTabImage.color = currentTab == LevelTab.Challenges ? tabActiveColor : tabInactiveColor;
        if (customTabImage != null)
            customTabImage.color = currentTab == LevelTab.Custom ? tabActiveColor : tabInactiveColor;
        if (communityTabImage != null)
            communityTabImage.color = currentTab == LevelTab.Trending ? tabActiveColor : tabInactiveColor;
    }

    private void UpdateButtonVisibility()
    {
        bool isCustom = currentTab == LevelTab.Custom;
        if (importBtn != null) importBtn.gameObject.SetActive(isCustom);
        if (exportBtn != null) exportBtn.gameObject.SetActive(isCustom);
        if (createListBtn != null) createListBtn.gameObject.SetActive(isCustom);
        if (deleteListBtn != null) deleteListBtn.gameObject.SetActive(isCustom);

        // Lock Custom tab until enough challenges are cleared
        bool customUnlocked = unlockCustomTab ||
            (ChallengeProgression.UnlockedCount - 1) >= customUnlockLevels;
        if (customTabBtn != null)
            customTabBtn.interactable = customUnlocked;
    }

    private void PopulateGrid()
    {
        // Clear existing buttons
        if (levelGridContent == null) return;
        foreach (Transform child in levelGridContent)
            Destroy(child.gameObject);

        // Clear word preview too
        ClearWordPreview();

        List<LevelWordListProvider> providers;
        switch (currentTab)
        {
            case LevelTab.Challenges:
                providers = challengeProviders;
                break;
            case LevelTab.Custom:
                providers = customProviders;
                break;
            case LevelTab.Trending:
            default:
                providers = new List<LevelWordListProvider>();
                break;
        }

        if (providers.Count == 0)
        {
            // Show empty state
            okBtn?.gameObject.SetActive(currentTab == LevelTab.Trending ? false : true);
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

                if (chineseFontAsset != null && provider.LanguageMode == LanguageMode.Mixed)
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
                bool unlocked = unlockAllChallenges || IsChallengeUnlocked(providers, provider);
                SetChallengeLockState(btnObj, btn, unlocked);
                SetStarDisplay(btnObj, providers, provider);

                // Show level number (1-20) on challenge buttons
                int levelNum = providers.IndexOf(provider) + 1;
                foreach (Transform child in btnObj.transform)
                {
                    if (child.name == "LevelNumTMP")
                    {
                        var numTmp = child.GetComponent<TMPro.TextMeshProUGUI>();
                        if (numTmp != null) numTmp.text = levelNum.ToString();
                        break;
                    }
                }
            }
        }

        // Auto-select: restore saved selection or pick first unlocked item
        TryRestoreSelection(providers);

        // Scroll back to top so first row is visible
        if (levelScrollRect != null)
            levelScrollRect.normalizedPosition = new Vector2(0, 1);
    }

    private bool IsChallengeUnlocked(List<LevelWordListProvider> providers, LevelWordListProvider provider)
    {
        int idx = providers.IndexOf(provider);
        return idx < 0 || ChallengeProgression.IsUnlocked(idx);
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

        int stars = ChallengeProgression.GetStarRating(idx);
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
                    var btnObj = levelGridContent.GetChild(i).gameObject;
                    OnLevelClicked(providers[i], btnObj);
                    return;
                }
            }
        }

        // Fallback: select the first unlocked item
        for (int i = 0; i < levelGridContent.childCount; i++)
        {
            bool isLocked = currentTab == LevelTab.Challenges && !unlockAllChallenges
                && !ChallengeProgression.IsUnlocked(i);
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

        // Persist selection across sessions
        PlayerPrefs.SetString(LastLevelPathPrefKey, selectedProvider.FilePath);
        PlayerPrefs.SetString(LastLevelTabPrefKey, currentTab.ToString());
        PlayerPrefs.Save();

        // Track which mode the player selected
        Services.Get<PhaseManager>().CurrentLevelMode = currentTab == LevelTab.Challenges
            ? LevelMode.Challenge
            : LevelMode.Custom;

        Services.Get<PhaseManager>().LoadWordList(selectedProvider);
        // Restart timer and transition to playing
        Services.Get<TimerSystem>().ResetAll();
        Services.Get<GameStateManager>().RaiseGameReset();
        Services.Get<GameStateManager>().TransitionTo(GameState.Playing);
        gameObject.SetActive(false);
    }

    public void OnImport()
    {
        var ext = new[] { new ExtensionFilter("Text Files", "txt") };
        StandaloneFileBrowser.OpenFilePanelAsync("Import Word List", "", ext, false, paths =>
        {
            if (paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;

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
        if (selectedProvider == null) return;

        string defaultName = selectedProvider.DisplayName.Replace(" ", "_");
        var words = selectedProvider.GetWords();
        var ext = new[] { new ExtensionFilter("Text Files", "txt") };
        StandaloneFileBrowser.SaveFilePanelAsync("Export Word List", "", defaultName, ext, path =>
        {
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllLines(path, words);
        });
    }

    public void OnCreateList()
    {
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
        if (selectedProvider == null || currentTab != LevelTab.Custom) return;

        selectedProvider.DeleteFile();
        selectedProvider = null;
        RefreshAll();
        SwitchTab(LevelTab.Custom);
    }
}
