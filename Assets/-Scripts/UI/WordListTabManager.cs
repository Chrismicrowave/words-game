using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class WordListTabManager : MonoBehaviour
{
    [Header("Tab Buttons")]
    [SerializeField] private Button myListTabBtn;
    [SerializeField] private Button dailyTabBtn;

    [Header("Panel Button Groups")]
    [SerializeField] private GameObject myListPanelBtns;
    [SerializeField] private GameObject dailyPanelBtns;

    [Header("Daily Picker")]
    [SerializeField] private DailyPickerPanelController dailyPickerPanel;

    [Header("Tab Colors")]
    [SerializeField] private Color tabActiveColor   = new Color(1f, 0.5f, 0f, 1f);
    [SerializeField] private Color tabInactiveColor = Color.white;

    private const string ActiveTabPrefKey    = "ActiveTab";
    private const string DailyListPathPrefKey = "DailyListPath";
    private const string MyListPathPrefKey    = "MyListPath";

    private IWordListProvider myListProvider;
    private IWordListProvider dailyProvider;

    IEnumerator Start()
    {
        // Init my list provider — restore last imported path if it still exists,
        // otherwise fall back to mylist.json (creating it with defaults if missing).
        string defaultPath = System.IO.Path.Combine(
            FileWordListProvider.GetWordListDirectory(), "mylist.json");
        string savedMyListPath = PlayerPrefs.GetString(MyListPathPrefKey, defaultPath);

        if (!string.IsNullOrEmpty(savedMyListPath) && System.IO.File.Exists(savedMyListPath)
            && savedMyListPath != defaultPath)
        {
            // Restore a previously imported list
            myListProvider = new FileWordListProvider(savedMyListPath);
        }
        else if (PhaseManager.Instance != null)
        {
            var fileProvider = new FileWordListProvider(defaultPath);
            if (!System.IO.File.Exists(defaultPath))
            {
                var defaultWords = PhaseManager.Instance.ActiveProvider?.GetWords()
                    ?? new System.Collections.Generic.List<string>();
                fileProvider.SetName("My List");
                fileProvider.SetWords(defaultWords);
                fileProvider.Save();
            }
            myListProvider = fileProvider;
        }

        // Restore saved daily list path
        string savedDailyPath = PlayerPrefs.GetString(DailyListPathPrefKey, "");
        if (!string.IsNullOrEmpty(savedDailyPath) && System.IO.File.Exists(savedDailyPath))
            dailyProvider = new DailyWordListProvider(savedDailyPath);

        // Yield one frame so GameCoordinator.Start() completes first — it loads
        // defaultWordList and subscribes to OnWordListChanged. Without this yield,
        // if WordListTabManager runs first, its LoadWordList call fires before the
        // subscription exists and gets overridden by GameCoordinator's defaultWordList.
        yield return null;

        // Restore tab
        string savedTab = PlayerPrefs.GetString(ActiveTabPrefKey, "daily");
        if (savedTab == "daily")
            OnDailyTabClicked();
        else
            OnMyListTabClicked();
    }

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
        if (dailyPanelBtns  != null) dailyPanelBtns .SetActive(false);
        SetTabColors(myListActive: true);
        EventSystem.current?.SetSelectedGameObject(null);
    }

    public void OnDailyTabClicked()
    {
        SetTabColors(myListActive: false);
        PlayerPrefs.SetString(ActiveTabPrefKey, "daily");
        if (dailyProvider != null)
            PhaseManager.Instance.LoadWordList(dailyProvider);
        if (myListPanelBtns != null) myListPanelBtns.SetActive(false);
        if (dailyPanelBtns  != null) dailyPanelBtns .SetActive(true);
        EventSystem.current?.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Called after import so switching tabs reloads the imported list, not the original mylist.json.
    /// </summary>
    public void SetMyListProvider(IWordListProvider provider)
    {
        myListProvider = provider;
    }

    /// <summary>
    /// Persists the imported list path to PlayerPrefs so it survives a restart.
    /// </summary>
    public void SaveMyListPath(string filePath)
    {
        PlayerPrefs.SetString(MyListPathPrefKey, filePath);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Persists the active tab name ("mylist" or "daily") to PlayerPrefs.
    /// Called from import so the tab is restored correctly on next session.
    /// </summary>
    public void SaveActiveTab(string tabName)
    {
        PlayerPrefs.SetString(ActiveTabPrefKey, tabName);
        PlayerPrefs.Save();
    }

    public void OnFetchDailyClicked()
    {
        if (dailyPickerPanel == null) return;
        dailyPickerPanel.OnListSelected = (provider) =>
        {
            dailyProvider = provider;
            PlayerPrefs.SetString(DailyListPathPrefKey, provider.FilePath);
            PlayerPrefs.SetString(ActiveTabPrefKey, "daily");
            PlayerPrefs.Save();
            PhaseManager.Instance.LoadWordList(provider);
            if (myListPanelBtns != null) myListPanelBtns.SetActive(false);
            if (dailyPanelBtns  != null) dailyPanelBtns .SetActive(true);
            SetTabColors(myListActive: false);
        };
        dailyPickerPanel.gameObject.SetActive(true);
    }
}
