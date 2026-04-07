using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DailyPickerPanelController : MonoBehaviour
{
    [SerializeField] private Transform listContent;
    [SerializeField] private GameObject listItemPrefab;
    [SerializeField] private TMP_InputField searchField;

    public System.Action<DailyWordListProvider> OnListSelected;

    private List<DailyWordListProvider> allProviders = new List<DailyWordListProvider>();

    void OnEnable()
    {
        LoadAllLists();
        if (searchField != null)
        {
            searchField.onValueChanged.RemoveAllListeners();
            searchField.onValueChanged.AddListener(OnSearchChanged);
        }
        RefreshDisplay(allProviders);
    }

    private void LoadAllLists()
    {
        allProviders.Clear();
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "DailyLists"));
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir, "*.json").OrderByDescending(f => f))
            allProviders.Add(new DailyWordListProvider(file));
    }

    private void OnSearchChanged(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            RefreshDisplay(allProviders);
            return;
        }

        string lower = query.ToLower().Trim();
        var filtered = allProviders
            .Where(p => p.GetWords().Any(w => w.ToLower().Contains(lower)))
            .ToList();
        RefreshDisplay(filtered);
    }

    private void RefreshDisplay(List<DailyWordListProvider> providers)
    {
        foreach (Transform child in listContent)
            Destroy(child.gameObject);

        foreach (var provider in providers)
        {
            var item = Instantiate(listItemPrefab, listContent);
            var label = item.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = provider.DisplayName;

            var btn = item.GetComponent<Button>();
            var captured = provider;
            if (btn != null)
                btn.onClick.AddListener(() =>
                {
                    OnListSelected?.Invoke(captured);
                    gameObject.SetActive(false);
                });
        }
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }
}
