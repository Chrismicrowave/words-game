using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DailyListPanelController : MonoBehaviour
{
    [SerializeField] private Transform listContent;
    [SerializeField] private GameObject listItemPrefab; // reuse phaseButtonPrefab or a simple button

    private List<DailyWordListProvider> providers = new List<DailyWordListProvider>();

    void OnEnable()
    {
        RefreshList();
    }

    public void RefreshList()
    {
        foreach (Transform child in listContent)
            Destroy(child.gameObject);

        providers.Clear();

        string dir = Path.Combine(Application.persistentDataPath, "DailyLists");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            // Create a sample daily list for testing
            CreateSampleDailyList(dir);
        }

        var files = Directory.GetFiles(dir, "*.json");
        foreach (var file in files)
        {
            var provider = new DailyWordListProvider(file);
            providers.Add(provider);

            var item = Instantiate(listItemPrefab, listContent);
            var btn = item.GetComponent<Button>();
            var label = item.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = provider.DisplayName;

            var captured = provider;
            if (btn != null)
                btn.onClick.AddListener(() => OnDailyListSelected(captured));
        }
    }

    private void OnDailyListSelected(DailyWordListProvider provider)
    {
        PhaseManager.Instance.LoadWordList(provider);
    }

    private void CreateSampleDailyList(string dir)
    {
        string json = "{\"name\":\"Daily - Sample\",\"words\":[\"hello\",\"world\",\"unity\"],\"date\":\"2026-04-06\"}";
        File.WriteAllText(Path.Combine(dir, "2026-04-06.json"), json);
    }
}
