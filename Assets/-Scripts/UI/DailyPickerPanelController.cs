using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DailyPickerPanelController : MonoBehaviour
{
    [SerializeField] private Transform listContent;       // date buttons
    [SerializeField] private Transform wordContent;       // word preview buttons
    [SerializeField] private GameObject listItemPrefab;
    [SerializeField] private TMP_InputField searchField;
    [SerializeField] private Button loadBtn;
    [SerializeField] private Color dateSelectedColor   = new Color(1f, 0.5f, 0f, 1f);
    [SerializeField] private Color dateUnselectedColor = Color.white;

    public System.Action<DailyWordListProvider> OnListSelected;

    private List<DailyWordListProvider> allProviders = new List<DailyWordListProvider>();
    private DailyWordListProvider selectedProvider;
    private Image selectedDateImage;

    void OnEnable()
    {
        selectedProvider = null;
        selectedDateImage = null;
        if (loadBtn != null) loadBtn.interactable = false;

        LoadAllLists();
        if (searchField != null)
        {
            searchField.text = "";
            searchField.onValueChanged.RemoveAllListeners();
            searchField.onValueChanged.AddListener(OnSearchChanged);
        }
        RefreshDateList(allProviders);
        ClearWordPreview();
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
            RefreshDateList(allProviders);
            return;
        }

        string lower = query.ToLower().Trim();
        var filtered = allProviders
            .Where(p => p.GetWords().Any(w => w.ToLower().Contains(lower)))
            .ToList();
        RefreshDateList(filtered);
    }

    private void RefreshDateList(List<DailyWordListProvider> providers)
    {
        foreach (Transform child in listContent)
            Destroy(child.gameObject);

        foreach (var provider in providers)
        {
            var item = Instantiate(listItemPrefab, listContent);
            var label = item.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = provider.DisplayName;

            var btn = item.GetComponent<Button>();
            var img = item.GetComponent<Image>();
            var captured = provider;
            if (btn != null)
                btn.onClick.AddListener(() => OnDateClicked(captured, img));
        }
    }

    private void OnDateClicked(DailyWordListProvider provider, Image btnImage)
    {
        // Deselect previous
        if (selectedDateImage != null) selectedDateImage.color = dateUnselectedColor;

        selectedProvider = provider;
        selectedDateImage = btnImage;
        if (selectedDateImage != null) selectedDateImage.color = dateSelectedColor;
        if (loadBtn != null) loadBtn.interactable = true;

        // Populate word preview
        ClearWordPreview();
        foreach (var word in provider.GetWords())
        {
            var item = Instantiate(listItemPrefab, wordContent);
            var label = item.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = word;

            // Word items are display-only — remove button interaction
            var btn = item.GetComponent<Button>();
            if (btn != null) btn.interactable = false;
        }
    }

    private void ClearWordPreview()
    {
        if (wordContent == null) return;
        foreach (Transform child in wordContent)
            Destroy(child.gameObject);
    }

    public void OnLoadClicked()
    {
        if (selectedProvider == null) return;
        OnListSelected?.Invoke(selectedProvider);
        gameObject.SetActive(false);
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }
}
