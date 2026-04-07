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
///
/// Layout:
///   [Search field — full width]
///   [Date scroll view | Word preview scroll view]
///   [Load btn] [Close btn]
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

        // ── Guard: abort if panel already exists (don't wipe Inspector changes) ─
        var existing = canvas.transform.Find("DailyPickerPanel");
        if (existing != null)
        {
            Debug.LogWarning("DailyPickerPanel already exists — aborting to preserve Inspector values. Delete it manually first if you want a full rebuild.");
            return;
        }

        // ── Fullscreen dim overlay ────────────────────────────────────────────
        var overlay = new GameObject("DailyPickerPanel");
        overlay.transform.SetParent(canvas.transform, false);
        var overlayRT = overlay.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;
        overlay.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);

        // ── Card (wider to fit two columns) ───────────────────────────────────
        var card = new GameObject("Card");
        card.transform.SetParent(overlay.transform, false);
        var cardRT = card.AddComponent<RectTransform>();
        cardRT.anchorMin = new Vector2(0.5f, 0.5f);
        cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.pivot = new Vector2(0.5f, 0.5f);
        cardRT.sizeDelta = new Vector2(700f, 480f);
        cardRT.anchoredPosition = Vector2.zero;
        card.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 1f);

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

        // ── Search field (full width) ─────────────────────────────────────────
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
        taRT.anchorMin = Vector2.zero; taRT.anchorMax = Vector2.one;
        taRT.offsetMin = new Vector2(8f, 0f); taRT.offsetMax = new Vector2(-8f, 0f);
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
        itTMP.fontSize = 16; itTMP.color = Color.white;
        itTMP.alignment = TextAlignmentOptions.MidlineLeft;
        itTMP.raycastTarget = false;

        searchInput.textViewport = taRT;
        searchInput.textComponent = itTMP;
        searchInput.placeholder = phTMP;
        searchInput.caretColor = Color.white;
        searchInput.selectionColor = new Color(0.3f, 0.6f, 1f, 0.5f);

        // ── Helper: make a scroll view with Content ───────────────────────────
        // Returns the Content transform
        System.Func<Transform, string, RectOffset, Transform> MakeScrollView = (parent, name, padding) =>
        {
            var sv = new GameObject(name);
            sv.transform.SetParent(parent, false);
            sv.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 1f);
            var sr = sv.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.elasticity = 0.1f;
            sr.scrollSensitivity = 20f;

            var vp = new GameObject("Viewport");
            vp.transform.SetParent(sv.transform, false);
            var vpRT2 = vp.AddComponent<RectTransform>();
            vpRT2.anchorMin = Vector2.zero; vpRT2.anchorMax = Vector2.one;
            vpRT2.offsetMin = Vector2.zero; vpRT2.offsetMax = Vector2.zero;
            vp.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            var m = vp.AddComponent<Mask>(); m.showMaskGraphic = false;

            var ct = new GameObject("Content");
            ct.transform.SetParent(vp.transform, false);
            var ctRT = ct.AddComponent<RectTransform>();
            ctRT.anchorMin = new Vector2(0f, 1f); ctRT.anchorMax = new Vector2(1f, 1f);
            ctRT.pivot = new Vector2(0.5f, 1f);
            ctRT.anchoredPosition = Vector2.zero; ctRT.sizeDelta = Vector2.zero;
            var vlg2 = ct.AddComponent<VerticalLayoutGroup>();
            vlg2.spacing = 2f;
            vlg2.childForceExpandWidth = true;
            vlg2.childForceExpandHeight = false;
            vlg2.childControlWidth = true;
            vlg2.childControlHeight = false;
            vlg2.padding = padding;
            var csf2 = ct.AddComponent<ContentSizeFitter>();
            csf2.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.content = ctRT;
            sr.viewport = vpRT2;
            return ct.transform;
        };

        // ── Column labels ─────────────────────────────────────────────────────
        float colY = -92f;   // top of columns (below search field)
        float colH = -48f;   // bottom of columns (above buttons)
        float colPad = 10f;

        // Left label: "Dates"
        var lblDates = new GameObject("LabelDates");
        lblDates.transform.SetParent(card.transform, false);
        var ldRT = lblDates.AddComponent<RectTransform>();
        ldRT.anchorMin = new Vector2(0f, 1f); ldRT.anchorMax = new Vector2(0.5f, 1f);
        ldRT.pivot = new Vector2(0f, 1f);
        ldRT.anchoredPosition = new Vector2(colPad, colY);
        ldRT.sizeDelta = new Vector2(-colPad * 1.5f, 20f);
        var ldTMP = lblDates.AddComponent<TextMeshProUGUI>();
        ldTMP.text = "DATES"; ldTMP.fontSize = 13; ldTMP.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        ldTMP.fontStyle = FontStyles.Bold; ldTMP.alignment = TextAlignmentOptions.Left;
        ldTMP.raycastTarget = false;

        // Right label: "Words"
        var lblWords = new GameObject("LabelWords");
        lblWords.transform.SetParent(card.transform, false);
        var lwRT = lblWords.AddComponent<RectTransform>();
        lwRT.anchorMin = new Vector2(0.5f, 1f); lwRT.anchorMax = new Vector2(1f, 1f);
        lwRT.pivot = new Vector2(0f, 1f);
        lwRT.anchoredPosition = new Vector2(colPad * 0.5f, colY);
        lwRT.sizeDelta = new Vector2(-colPad * 1.5f, 20f);
        var lwTMP = lblWords.AddComponent<TextMeshProUGUI>();
        lwTMP.text = "WORDS IN LIST"; lwTMP.fontSize = 13; lwTMP.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        lwTMP.fontStyle = FontStyles.Bold; lwTMP.alignment = TextAlignmentOptions.Left;
        lwTMP.raycastTarget = false;

        // ── Date scroll view (left half) ──────────────────────────────────────
        var dateScrollGO = new GameObject("DateScrollView");
        dateScrollGO.transform.SetParent(card.transform, false);
        var dsRT = dateScrollGO.AddComponent<RectTransform>();
        dsRT.anchorMin = new Vector2(0f, 0f); dsRT.anchorMax = new Vector2(0.5f, 1f);
        dsRT.offsetMin = new Vector2(colPad, colH);
        dsRT.offsetMax = new Vector2(-colPad * 0.5f, colY - 20f);
        dateScrollGO.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 1f);
        var dateSR = dateScrollGO.AddComponent<ScrollRect>();
        dateSR.horizontal = false;
        dateSR.movementType = ScrollRect.MovementType.Elastic;
        dateSR.elasticity = 0.1f; dateSR.scrollSensitivity = 20f;

        var dateVP = new GameObject("Viewport");
        dateVP.transform.SetParent(dateScrollGO.transform, false);
        var dvpRT = dateVP.AddComponent<RectTransform>();
        dvpRT.anchorMin = Vector2.zero; dvpRT.anchorMax = Vector2.one;
        dvpRT.offsetMin = Vector2.zero; dvpRT.offsetMax = Vector2.zero;
        // No Image on viewport — image blocks spawned content raycasts
        var dm = dateVP.AddComponent<Mask>(); dm.showMaskGraphic = false;

        var dateContent = new GameObject("Content");
        dateContent.transform.SetParent(dateVP.transform, false);
        var dcRT = dateContent.AddComponent<RectTransform>();
        dcRT.anchorMin = new Vector2(0f, 1f); dcRT.anchorMax = new Vector2(1f, 1f);
        dcRT.pivot = new Vector2(0.5f, 1f);
        dcRT.anchoredPosition = Vector2.zero; dcRT.sizeDelta = Vector2.zero;
        var dcVLG = dateContent.AddComponent<VerticalLayoutGroup>();
        dcVLG.spacing = 2f;
        dcVLG.childForceExpandWidth = true; dcVLG.childForceExpandHeight = false;
        dcVLG.childControlWidth = true; dcVLG.childControlHeight = false;
        dcVLG.padding = new RectOffset(4, 4, 4, 4);
        var dcCSF = dateContent.AddComponent<ContentSizeFitter>();
        dcCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        dateSR.content = dcRT; dateSR.viewport = dvpRT;

        // ── Word preview scroll view (right half) ─────────────────────────────
        var wordScrollGO = new GameObject("WordScrollView");
        wordScrollGO.transform.SetParent(card.transform, false);
        var wsRT = wordScrollGO.AddComponent<RectTransform>();
        wsRT.anchorMin = new Vector2(0.5f, 0f); wsRT.anchorMax = new Vector2(1f, 1f);
        wsRT.offsetMin = new Vector2(colPad * 0.5f, colH);
        wsRT.offsetMax = new Vector2(-colPad, colY - 20f);
        wordScrollGO.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 1f);
        var wordSR = wordScrollGO.AddComponent<ScrollRect>();
        wordSR.horizontal = false;
        wordSR.movementType = ScrollRect.MovementType.Elastic;
        wordSR.elasticity = 0.1f; wordSR.scrollSensitivity = 20f;

        var wordVP = new GameObject("Viewport");
        wordVP.transform.SetParent(wordScrollGO.transform, false);
        var wvpRT = wordVP.AddComponent<RectTransform>();
        wvpRT.anchorMin = Vector2.zero; wvpRT.anchorMax = Vector2.one;
        wvpRT.offsetMin = Vector2.zero; wvpRT.offsetMax = Vector2.zero;
        // No Image on viewport — image blocks spawned content raycasts
        var wm = wordVP.AddComponent<Mask>(); wm.showMaskGraphic = false;

        var wordContent = new GameObject("Content");
        wordContent.transform.SetParent(wordVP.transform, false);
        var wcRT = wordContent.AddComponent<RectTransform>();
        wcRT.anchorMin = new Vector2(0f, 1f); wcRT.anchorMax = new Vector2(1f, 1f);
        wcRT.pivot = new Vector2(0.5f, 1f);
        wcRT.anchoredPosition = Vector2.zero; wcRT.sizeDelta = Vector2.zero;
        var wcVLG = wordContent.AddComponent<VerticalLayoutGroup>();
        wcVLG.spacing = 2f;
        wcVLG.childForceExpandWidth = true; wcVLG.childForceExpandHeight = false;
        wcVLG.childControlWidth = true; wcVLG.childControlHeight = false;
        wcVLG.padding = new RectOffset(4, 4, 4, 4);
        var wcCSF = wordContent.AddComponent<ContentSizeFitter>();
        wcCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        wordSR.content = wcRT; wordSR.viewport = wvpRT;

        // ── Load button ───────────────────────────────────────────────────────
        var loadBtnGO = new GameObject("LoadBtn");
        loadBtnGO.transform.SetParent(card.transform, false);
        var loadRT = loadBtnGO.AddComponent<RectTransform>();
        loadRT.anchorMin = new Vector2(0f, 0f); loadRT.anchorMax = new Vector2(0.5f, 0f);
        loadRT.pivot = new Vector2(0.5f, 0f);
        loadRT.anchoredPosition = new Vector2(0f, 8f);
        loadRT.sizeDelta = new Vector2(-20f, 30f);
        loadBtnGO.AddComponent<Image>().color = new Color(0.2f, 0.6f, 0.2f, 1f);
        var loadBtnComp = loadBtnGO.AddComponent<Button>();
        loadBtnComp.navigation = noNav;
        loadBtnComp.interactable = false; // enabled only when a date is selected

        var loadLblGO = new GameObject("Text");
        loadLblGO.transform.SetParent(loadBtnGO.transform, false);
        var llRT = loadLblGO.AddComponent<RectTransform>();
        llRT.anchorMin = Vector2.zero; llRT.anchorMax = Vector2.one;
        llRT.offsetMin = Vector2.zero; llRT.offsetMax = Vector2.zero;
        var llTMP = loadLblGO.AddComponent<TextMeshProUGUI>();
        llTMP.text = "Load List"; llTMP.fontSize = 16;
        llTMP.alignment = TextAlignmentOptions.Center;
        llTMP.color = Color.white; llTMP.raycastTarget = false;

        // ── Close button ──────────────────────────────────────────────────────
        var closeBtnGO = new GameObject("CloseBtn");
        closeBtnGO.transform.SetParent(card.transform, false);
        var closeBtnRT = closeBtnGO.AddComponent<RectTransform>();
        closeBtnRT.anchorMin = new Vector2(0.5f, 0f); closeBtnRT.anchorMax = new Vector2(1f, 0f);
        closeBtnRT.pivot = new Vector2(0.5f, 0f);
        closeBtnRT.anchoredPosition = new Vector2(0f, 8f);
        closeBtnRT.sizeDelta = new Vector2(-20f, 30f);
        closeBtnGO.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f, 1f);
        var closeBtnComp = closeBtnGO.AddComponent<Button>();
        closeBtnComp.navigation = noNav;

        var closeLblGO = new GameObject("Text");
        closeLblGO.transform.SetParent(closeBtnGO.transform, false);
        var clRT = closeLblGO.AddComponent<RectTransform>();
        clRT.anchorMin = Vector2.zero; clRT.anchorMax = Vector2.one;
        clRT.offsetMin = Vector2.zero; clRT.offsetMax = Vector2.zero;
        var clTMP = closeLblGO.AddComponent<TextMeshProUGUI>();
        clTMP.text = "Close"; clTMP.fontSize = 16;
        clTMP.alignment = TextAlignmentOptions.Center;
        clTMP.color = Color.white; clTMP.raycastTarget = false;

        // ── DailyPickerPanelController ────────────────────────────────────────
        var controller = overlay.AddComponent<DailyPickerPanelController>();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(DailyPickerPanelController).GetField("listContent",    flags)?.SetValue(controller, dateContent.transform);
        typeof(DailyPickerPanelController).GetField("wordContent",    flags)?.SetValue(controller, wordContent.transform);
        typeof(DailyPickerPanelController).GetField("listItemPrefab", flags)?.SetValue(controller, phaseBtnPrefab);
        typeof(DailyPickerPanelController).GetField("searchField",    flags)?.SetValue(controller, searchInput);
        typeof(DailyPickerPanelController).GetField("loadBtn",        flags)?.SetValue(controller, loadBtnComp);

        UnityEventTools.AddVoidPersistentListener(closeBtnComp.onClick, controller.Close);
        UnityEventTools.AddVoidPersistentListener(loadBtnComp.onClick, controller.OnLoadClicked);

        // ── Wire UIController.dailyPickerPanel ────────────────────────────────
        typeof(UIController).GetField("dailyPickerPanel", flags)?.SetValue(uiController, controller);
        EditorUtility.SetDirty(uiController);

        // ── FetchDailyBtn in DailyPanelBtns ──────────────────────────────────
        var dailyBtns = wordListUI.Find("DailyPanelBtns");
        if (dailyBtns != null)
        {
            var dRT2 = dailyBtns.GetComponent<RectTransform>();
            dRT2.sizeDelta = new Vector2(200f, 30f);

            var hlg = dailyBtns.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null)
            {
                hlg = dailyBtns.gameObject.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 4f;
                hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
                hlg.childControlWidth = false; hlg.childControlHeight = false;
                hlg.childAlignment = TextAnchor.MiddleLeft;
            }

            var swapBtn = dailyBtns.Find("SwapBtn (1)");
            if (swapBtn != null)
            {
                var le = swapBtn.GetComponent<LayoutElement>() ?? swapBtn.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 95f; le.preferredHeight = 30f;
            }

            var oldFetch = dailyBtns.Find("FetchDailyBtn");
            if (oldFetch != null) Object.DestroyImmediate(oldFetch.gameObject);

            var fetchGO = new GameObject("FetchDailyBtn");
            fetchGO.transform.SetParent(dailyBtns, false);
            var fetchRT2 = fetchGO.AddComponent<RectTransform>();
            fetchRT2.sizeDelta = new Vector2(95f, 30f);
            fetchGO.AddComponent<Image>().color = Color.white;
            var fetchBtn = fetchGO.AddComponent<Button>();
            fetchBtn.navigation = noNav;
            UnityEventTools.AddVoidPersistentListener(fetchBtn.onClick, uiController.OnFetchDailyClicked);
            var fetchLE = fetchGO.AddComponent<LayoutElement>();
            fetchLE.preferredWidth = 95f; fetchLE.preferredHeight = 30f;

            var fetchLblGO = new GameObject("Text (TMP)");
            fetchLblGO.transform.SetParent(fetchGO.transform, false);
            var flRT2 = fetchLblGO.AddComponent<RectTransform>();
            flRT2.anchorMin = Vector2.zero; flRT2.anchorMax = Vector2.one;
            flRT2.offsetMin = Vector2.zero; flRT2.offsetMax = Vector2.zero;
            var flTMP = fetchLblGO.AddComponent<TextMeshProUGUI>();
            flTMP.text = "fetch"; flTMP.fontSize = 18;
            flTMP.alignment = TextAlignmentOptions.Center;
            flTMP.color = Color.black; flTMP.raycastTarget = false;

            EditorUtility.SetDirty(dailyBtns.gameObject);
        }

        // ── Inactive by default ───────────────────────────────────────────────
        overlay.SetActive(false);

        // ── Save scene ────────────────────────────────────────────────────────
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

        Debug.Log("=== BuildDailyPickerPanel COMPLETE ===");
        Debug.Log("  dateContent: " + dateContent.transform);
        Debug.Log("  wordContent: " + wordContent.transform);
        Debug.Log("  loadBtn: " + loadBtnComp);
        Debug.Log("  UIController.dailyPickerPanel: " + controller);
        Debug.Log("  FetchDailyBtn: " + (dailyBtns != null));
    }
}
