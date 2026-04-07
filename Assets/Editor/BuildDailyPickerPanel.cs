using UnityEngine;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

/// <summary>
/// Creates the DailyPickerPanel overlay, adds "Fetch Daily" button to DailyPanelBtns,
/// and wires UIController.dailyPickerPanel. Run via Tools > Build Daily Picker Panel.
/// Safe: does NOT modify any existing scroll view layout settings.
/// </summary>
public class BuildDailyPickerPanel
{
    [MenuItem("Tools/Build Daily Picker Panel")]
    public static void Execute()
    {
        var canvas = GameObject.Find("--- UI ---");
        if (canvas == null) { Debug.LogError("Canvas '--- UI ---' not found"); return; }

        var wordListUI = canvas.transform.Find("Menus/Scroll View");
        if (wordListUI == null) { Debug.LogError("Scroll View not found"); return; }

        var uiController = Object.FindFirstObjectByType<UIController>();
        if (uiController == null) { Debug.LogError("UIController not found"); return; }

        var phaseBtnPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/-Prefabs/PhaseBtnInScrollView.prefab");
        if (phaseBtnPrefab == null) { Debug.LogError("PhaseBtnInScrollView prefab not found"); return; }

        var noNav = new Navigation { mode = Navigation.Mode.None };

        // ── Remove existing DailyPickerPanel if re-running ────────────────────
        var existing = canvas.transform.Find("DailyPickerPanel");
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        // ── Fullscreen dim overlay ────────────────────────────────────────────
        var overlay = new GameObject("DailyPickerPanel");
        overlay.transform.SetParent(canvas.transform, false);
        var overlayRT = overlay.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;
        var overlayImg = overlay.AddComponent<Image>();
        overlayImg.color = new Color(0f, 0f, 0f, 0.7f);

        // ── Card ──────────────────────────────────────────────────────────────
        var card = new GameObject("Card");
        card.transform.SetParent(overlay.transform, false);
        var cardRT = card.AddComponent<RectTransform>();
        cardRT.anchorMin = new Vector2(0.5f, 0.5f);
        cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.pivot = new Vector2(0.5f, 0.5f);
        cardRT.sizeDelta = new Vector2(420f, 460f);
        cardRT.anchoredPosition = Vector2.zero;
        var cardImg = card.AddComponent<Image>();
        cardImg.color = new Color(0.12f, 0.12f, 0.12f, 1f);

        // ── Title ─────────────────────────────────────────────────────────────
        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(card.transform, false);
        var titleRT = titleGO.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0f, 1f);
        titleRT.anchorMax = new Vector2(1f, 1f);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -10f);
        titleRT.sizeDelta = new Vector2(0f, 36f);
        var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
        titleTMP.text = "Pick Daily List";
        titleTMP.fontSize = 22;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.color = Color.white;
        titleTMP.raycastTarget = false;

        // ── Search field ──────────────────────────────────────────────────────
        var searchGO = new GameObject("SearchField");
        searchGO.transform.SetParent(card.transform, false);
        var searchRT = searchGO.AddComponent<RectTransform>();
        searchRT.anchorMin = new Vector2(0f, 1f);
        searchRT.anchorMax = new Vector2(1f, 1f);
        searchRT.pivot = new Vector2(0.5f, 1f);
        searchRT.anchoredPosition = new Vector2(0f, -54f);
        searchRT.sizeDelta = new Vector2(-20f, 30f);
        searchGO.AddComponent<Image>().color = new Color(0.22f, 0.22f, 0.22f, 1f);
        var searchInput = searchGO.AddComponent<TMP_InputField>();

        var taGO = new GameObject("Text Area");
        taGO.transform.SetParent(searchGO.transform, false);
        var taRT = taGO.AddComponent<RectTransform>();
        taRT.anchorMin = Vector2.zero;
        taRT.anchorMax = Vector2.one;
        taRT.offsetMin = new Vector2(8f, 0f);
        taRT.offsetMax = new Vector2(-8f, 0f);
        taGO.AddComponent<RectMask2D>();

        var phGO = new GameObject("Placeholder");
        phGO.transform.SetParent(taGO.transform, false);
        var phRT = phGO.AddComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
        phRT.offsetMin = Vector2.zero; phRT.offsetMax = Vector2.zero;
        var phTMP = phGO.AddComponent<TextMeshProUGUI>();
        phTMP.text = "Search by word...";
        phTMP.fontSize = 16;
        phTMP.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        phTMP.fontStyle = FontStyles.Italic;
        phTMP.alignment = TextAlignmentOptions.MidlineLeft;
        phTMP.raycastTarget = false;

        var itGO = new GameObject("Text");
        itGO.transform.SetParent(taGO.transform, false);
        var itRT = itGO.AddComponent<RectTransform>();
        itRT.anchorMin = Vector2.zero; itRT.anchorMax = Vector2.one;
        itRT.offsetMin = Vector2.zero; itRT.offsetMax = Vector2.zero;
        var itTMP = itGO.AddComponent<TextMeshProUGUI>();
        itTMP.fontSize = 16;
        itTMP.color = Color.white;
        itTMP.alignment = TextAlignmentOptions.MidlineLeft;
        itTMP.raycastTarget = false;

        searchInput.textViewport = taRT;
        searchInput.textComponent = itTMP;
        searchInput.placeholder = phTMP;
        searchInput.caretColor = Color.white;
        searchInput.selectionColor = new Color(0.3f, 0.6f, 1f, 0.5f);

        // ── Scroll view ───────────────────────────────────────────────────────
        var scrollGO = new GameObject("ScrollView");
        scrollGO.transform.SetParent(card.transform, false);
        var scrollRT = scrollGO.AddComponent<RectTransform>();
        scrollRT.anchorMin = new Vector2(0f, 0f);
        scrollRT.anchorMax = new Vector2(1f, 1f);
        scrollRT.offsetMin = new Vector2(10f, 44f);
        scrollRT.offsetMax = new Vector2(-10f, -92f);
        scrollGO.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 1f);
        var scrollRect = scrollGO.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;

        var vpGO = new GameObject("Viewport");
        vpGO.transform.SetParent(scrollGO.transform, false);
        var vpRT = vpGO.AddComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = Vector2.zero;
        vpGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
        var maskComp = vpGO.AddComponent<Mask>();
        maskComp.showMaskGraphic = false;

        var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(vpGO.transform, false);
        var contentRT = contentGO.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = Vector2.zero;
        // NOTE: ContentSizeFitter + childForceExpandHeight=false is ONLY on this new Content,
        // not touching any existing scroll view layouts.
        var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 2f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        var csf = contentGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content = contentRT;
        scrollRect.viewport = vpRT;
        scrollRect.movementType = ScrollRect.MovementType.Elastic;
        scrollRect.elasticity = 0.1f;
        scrollRect.scrollSensitivity = 20f;

        // ── Close button ──────────────────────────────────────────────────────
        var closeBtnGO = new GameObject("CloseBtn");
        closeBtnGO.transform.SetParent(card.transform, false);
        var closeBtnRT = closeBtnGO.AddComponent<RectTransform>();
        closeBtnRT.anchorMin = new Vector2(0f, 0f);
        closeBtnRT.anchorMax = new Vector2(1f, 0f);
        closeBtnRT.pivot = new Vector2(0.5f, 0f);
        closeBtnRT.anchoredPosition = new Vector2(0f, 8f);
        closeBtnRT.sizeDelta = new Vector2(-20f, 30f);
        closeBtnGO.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f, 1f);
        var closeBtnComp = closeBtnGO.AddComponent<Button>();
        closeBtnComp.navigation = noNav;
        var closeLabelGO = new GameObject("Text");
        closeLabelGO.transform.SetParent(closeBtnGO.transform, false);
        var clRT = closeLabelGO.AddComponent<RectTransform>();
        clRT.anchorMin = Vector2.zero; clRT.anchorMax = Vector2.one;
        clRT.offsetMin = Vector2.zero; clRT.offsetMax = Vector2.zero;
        var clTMP = closeLabelGO.AddComponent<TextMeshProUGUI>();
        clTMP.text = "Close";
        clTMP.fontSize = 16;
        clTMP.alignment = TextAlignmentOptions.Center;
        clTMP.color = Color.white;
        clTMP.raycastTarget = false;

        // ── DailyPickerPanelController ────────────────────────────────────────
        var controller = overlay.AddComponent<DailyPickerPanelController>();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(DailyPickerPanelController).GetField("listContent",    flags)?.SetValue(controller, contentGO.transform);
        typeof(DailyPickerPanelController).GetField("listItemPrefab", flags)?.SetValue(controller, phaseBtnPrefab);
        typeof(DailyPickerPanelController).GetField("searchField",    flags)?.SetValue(controller, searchInput);

        // Wire Close button now that controller exists
        UnityEventTools.AddVoidPersistentListener(closeBtnComp.onClick, controller.Close);

        // ── Wire UIController.dailyPickerPanel ────────────────────────────────
        typeof(UIController).GetField("dailyPickerPanel", flags)?.SetValue(uiController, controller);
        EditorUtility.SetDirty(uiController);

        // ── FetchDailyBtn in DailyPanelBtns ──────────────────────────────────
        var dailyBtns = wordListUI.Find("DailyPanelBtns");
        if (dailyBtns != null)
        {
            var dRT = dailyBtns.GetComponent<RectTransform>();
            dRT.sizeDelta = new Vector2(200f, 30f);

            var hlg = dailyBtns.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null)
            {
                hlg = dailyBtns.gameObject.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 4f;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = true;
                hlg.childControlWidth = false;
                hlg.childControlHeight = false;
                hlg.childAlignment = TextAnchor.MiddleLeft;
            }

            var swapBtn = dailyBtns.Find("SwapBtn (1)");
            if (swapBtn != null)
            {
                var le = swapBtn.GetComponent<LayoutElement>() ?? swapBtn.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 95f;
                le.preferredHeight = 30f;
            }

            var oldFetch = dailyBtns.Find("FetchDailyBtn");
            if (oldFetch != null) Object.DestroyImmediate(oldFetch.gameObject);

            var fetchGO = new GameObject("FetchDailyBtn");
            fetchGO.transform.SetParent(dailyBtns, false);
            var fetchRT = fetchGO.AddComponent<RectTransform>();
            fetchRT.sizeDelta = new Vector2(95f, 30f);
            fetchGO.AddComponent<Image>().color = Color.white;
            var fetchBtn = fetchGO.AddComponent<Button>();
            fetchBtn.navigation = noNav;
            UnityEventTools.AddVoidPersistentListener(fetchBtn.onClick, uiController.OnFetchDailyClicked);

            var fetchLE = fetchGO.AddComponent<LayoutElement>();
            fetchLE.preferredWidth = 95f;
            fetchLE.preferredHeight = 30f;

            var fetchLabelGO = new GameObject("Text (TMP)");
            fetchLabelGO.transform.SetParent(fetchGO.transform, false);
            var flRT = fetchLabelGO.AddComponent<RectTransform>();
            flRT.anchorMin = Vector2.zero; flRT.anchorMax = Vector2.one;
            flRT.offsetMin = Vector2.zero; flRT.offsetMax = Vector2.zero;
            var flTMP = fetchLabelGO.AddComponent<TextMeshProUGUI>();
            flTMP.text = "fetch";
            flTMP.fontSize = 18;
            flTMP.alignment = TextAlignmentOptions.Center;
            flTMP.color = Color.black;
            flTMP.raycastTarget = false;

            EditorUtility.SetDirty(dailyBtns.gameObject);
            Debug.Log("FetchDailyBtn created and wired");
        }

        // ── Inactive by default ───────────────────────────────────────────────
        overlay.SetActive(false);

        // ── Save scene ────────────────────────────────────────────────────────
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

        Debug.Log("=== BuildDailyPickerPanel COMPLETE ===");
        Debug.Log("  DailyPickerPanel: " + (overlay != null));
        Debug.Log("  UIController.dailyPickerPanel: " + controller);
        Debug.Log("  listContent: " + contentGO.transform);
        Debug.Log("  listItemPrefab: " + phaseBtnPrefab);
        Debug.Log("  searchField: " + searchInput);
        Debug.Log("  FetchDailyBtn wired: " + (dailyBtns != null));
    }
}
