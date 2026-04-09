using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PhaseListUIManager : MonoBehaviour
{
    [SerializeField] private Transform phaseListContent;
    [SerializeField] private GameObject phaseButtonPrefab;
    [SerializeField] private Color phaseSelectedColor = Color.yellow;
    [SerializeField] private Color phaseUnselectedColor = Color.white;
    [SerializeField] private TMP_FontAsset chineseFontAsset;  // Noto Sans SC, assigned in Inspector

    private int selectedPhaseIndex = -1;
    public int SelectedPhaseIndex => selectedPhaseIndex;

    void OnEnable()
    {
        if (PhaseManager.Instance != null)
            PhaseManager.Instance.OnWordListChanged += RefreshPhaseList;
    }

    void OnDisable()
    {
        if (PhaseManager.Instance != null)
            PhaseManager.Instance.OnWordListChanged -= RefreshPhaseList;
    }

    public void RefreshPhaseList()
    {
        if (phaseListContent == null || phaseButtonPrefab == null) return;

        foreach (Transform child in phaseListContent)
            Destroy(child.gameObject);

        bool isChinese = PhaseManager.Instance.CurrentLanguageMode == LanguageMode.Chinese;
        var words = PhaseManager.Instance.Words;
        for (int i = 0; i < words.Count; i++)
        {
            int index = i;
            GameObject btnObj = Instantiate(phaseButtonPrefab, phaseListContent);
            var tmp = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            // Chinese lists show characters only (display already set by PhaseManager); English shows numbered
            tmp.text = isChinese ? words[i] : $"{index + 1}. {words[i]}";
            if (isChinese && chineseFontAsset != null)
                tmp.font = chineseFontAsset;

            Button btn = btnObj.GetComponent<Button>();
            btn.onClick.AddListener(() =>
            {
                selectedPhaseIndex = index;
                HighlightSelectedButton(btnObj);
            });
        }

        Canvas.ForceUpdateCanvases();
    }

    public void ClearSelection()
    {
        selectedPhaseIndex = -1;
    }

    private void HighlightSelectedButton(GameObject selected)
    {
        foreach (Transform child in phaseListContent)
        {
            Image img = child.GetComponent<Image>();
            if (img != null)
                img.color = (child.gameObject == selected) ? phaseSelectedColor : phaseUnselectedColor;
        }
    }
}
