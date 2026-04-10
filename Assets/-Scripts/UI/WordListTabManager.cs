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

    private IWordListProvider myListProvider;
    private IWordListProvider dailyProvider;

    void Start()
    {
        // Init my list provider
        if (PhaseManager.Instance != null)
        {
            string myListPath = System.IO.Path.Combine(
                FileWordListProvider.GetWordListDirectory(), "mylist.json");
            var fileProvider = new FileWordListProvider(myListPath);
            if (!System.IO.File.Exists(myListPath))
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
