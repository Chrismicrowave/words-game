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
    Trendy
}

public class LevelPanelController : MonoBehaviour
{
    [Header("Tab Buttons")]
    [SerializeField] private Button challengesTabBtn;
    [SerializeField] private Button customTabBtn;
    [SerializeField] private Button trendyTabBtn;
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

    [Header("Trendy Placeholder")]
    [SerializeField] private GameObject trendyPlaceholder;  // "Coming Soon" shown in Trendy tab

    private LevelTab currentTab = LevelTab.Challenges;
    private LevelWordListProvider selectedProvider;

    private List<LevelWordListProvider> challengeProviders = new List<LevelWordListProvider>();
    private List<LevelWordListProvider> customProviders = new List<LevelWordListProvider>();

    private Image challengesTabImage;
    private Image customTabImage;
    private Image trendyTabImage;

    void Awake()
    {
        challengesTabImage = challengesTabBtn?.GetComponent<Image>();
        customTabImage = customTabBtn?.GetComponent<Image>();
        trendyTabImage = trendyTabBtn?.GetComponent<Image>();

        if (cancelBtn != null) cancelBtn.onClick.AddListener(OnCancel);
        if (okBtn != null) okBtn.onClick.AddListener(OnOK);
        if (importBtn != null) importBtn.onClick.AddListener(OnImport);
        if (exportBtn != null) exportBtn.onClick.AddListener(OnExport);
        if (createListBtn != null) createListBtn.onClick.AddListener(OnCreateList);
        if (deleteListBtn != null) deleteListBtn.onClick.AddListener(OnDeleteList);

        if (challengesTabBtn != null) challengesTabBtn.onClick.AddListener(() => SwitchTab(LevelTab.Challenges));
        if (customTabBtn != null) customTabBtn.onClick.AddListener(() => SwitchTab(LevelTab.Custom));
        if (trendyTabBtn != null) trendyTabBtn.onClick.AddListener(() => SwitchTab(LevelTab.Trendy));
    }

    void OnEnable()
    {
        if (InputHandler.Instance != null) InputHandler.Instance.SetGameplayBlocked(true);
        RefreshAll();
        SwitchTab(LevelTab.Challenges);
    }

    void OnDisable()
    {
        if (InputHandler.Instance != null) InputHandler.Instance.SetGameplayBlocked(false);
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

        // Show/hide placeholder for Trendy
        if (trendyPlaceholder != null)
            trendyPlaceholder.SetActive(tab == LevelTab.Trendy);
    }

    private void LoadChallengeManifest()
    {
        challengeNameMap = new Dictionary<string, string>();
        string path = Path.Combine(Application.streamingAssetsPath, "Levels", "levels.json");
        if (!File.Exists(path)) return;

        string json = File.ReadAllText(path);
        var wrapper = new LevelManifest();
        JsonUtility.FromJsonOverwrite("{\"entries\":" + json + "}", wrapper);
        if (wrapper.entries == null) return;
        foreach (var entry in wrapper.entries)
            challengeNameMap[entry.file] = entry.name;
    }

    private void UpdateTabColors()
    {
        if (challengesTabImage != null)
            challengesTabImage.color = currentTab == LevelTab.Challenges ? tabActiveColor : tabInactiveColor;
        if (customTabImage != null)
            customTabImage.color = currentTab == LevelTab.Custom ? tabActiveColor : tabInactiveColor;
        if (trendyTabImage != null)
            trendyTabImage.color = currentTab == LevelTab.Trendy ? tabActiveColor : tabInactiveColor;
    }

    private void UpdateButtonVisibility()
    {
        bool isCustom = currentTab == LevelTab.Custom;
        if (importBtn != null) importBtn.gameObject.SetActive(isCustom);
        if (exportBtn != null) exportBtn.gameObject.SetActive(isCustom);
        if (createListBtn != null) createListBtn.gameObject.SetActive(isCustom);
        if (deleteListBtn != null) deleteListBtn.gameObject.SetActive(isCustom);
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
            case LevelTab.Trendy:
            default:
                providers = new List<LevelWordListProvider>();
                break;
        }

        if (providers.Count == 0)
        {
            // Show empty state
            okBtn?.gameObject.SetActive(currentTab == LevelTab.Trendy ? false : true);
            return;
        }

        foreach (var provider in providers)
        {
            var btnObj = Instantiate(levelButtonPrefab, levelGridContent);
            var tmp = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
            {
                string displayName = provider.DisplayName;
                if (currentTab == LevelTab.Challenges && challengeNameMap != null)
                {
                    string fileName = Path.GetFileName(provider.FilePath);
                    if (challengeNameMap.TryGetValue(fileName, out var mapped))
                        displayName = mapped;
                }
                // Localize via string table — falls back to English name if no
                // locale-specific entry exists (e.g. new challenge not yet translated)
                string localized = LocalizationService.Get("UI", displayName);
                tmp.text = localized != displayName ? localized : displayName;

                if (chineseFontAsset != null && provider.LanguageMode == LanguageMode.Mixed)
                    tmp.font = chineseFontAsset;
            }

            var btn = btnObj.GetComponent<Button>();
            if (btn != null)
            {
                var captured = provider;
                btn.onClick.AddListener(() => OnLevelClicked(captured, btnObj));
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

        PhaseManager.Instance.LoadWordList(selectedProvider);
        // Restart timer and transition to playing
        TimerSystem.Instance.ResetAll();
        GameStateManager.Instance.RaiseGameReset();
        GameStateManager.Instance.TransitionTo(GameState.Playing);
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
