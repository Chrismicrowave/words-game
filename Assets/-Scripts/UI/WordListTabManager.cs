using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class WordListTabManager : MonoBehaviour
{
    [Header("Panel Button Groups")]
    [SerializeField] private GameObject myListPanelBtns;

    private const string MyListPathPrefKey = "MyListPath";

    private IWordListProvider myListProvider;
    private bool subscribed;

    void OnEnable()
    {
        if (PhaseManager.Instance != null && !subscribed)
        {
            PhaseManager.Instance.OnWordListChanged += OnWordListChanged;
            subscribed = true;
        }
        UpdateButtonVisibility();
    }

    void OnDisable()
    {
        if (PhaseManager.Instance != null && subscribed)
        {
            PhaseManager.Instance.OnWordListChanged -= OnWordListChanged;
            subscribed = false;
        }
    }

    IEnumerator Start()
    {
        // Re-subscribe in Start when PhaseManager is guaranteed available
        OnDisable();
        OnEnable();

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

        // Yield one frame so GameCoordinator.Start() completes first
        yield return null;

        if (myListProvider != null)
            PhaseManager.Instance.LoadWordList(myListProvider);
    }

    /// <summary>
    /// Called after import to update the active provider.
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

    private void OnWordListChanged()
    {
        UpdateButtonVisibility();
    }

    private void UpdateButtonVisibility()
    {
        if (myListPanelBtns == null) return;
        bool editable = PhaseManager.Instance?.ActiveProvider?.IsEditable ?? false;
        myListPanelBtns.SetActive(editable);
    }
}
