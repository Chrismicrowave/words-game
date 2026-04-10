using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;

public class BuildChinesePinyinPopup
{
    public static void Execute()
    {
        // Find the Menus parent
        var menusGo = GameObject.Find("--- UI ---/Menus");
        if (menusGo == null) { Debug.LogError("[BuildChinesePinyinPopup] Could not find '--- UI ---/Menus'"); return; }

        // Remove existing if any
        var existing = menusGo.transform.Find("ChinesePinyinPopup");
        if (existing != null) GameObject.DestroyImmediate(existing.gameObject);

        // ── Root panel (full-screen, starts inactive) ──────────────────────
        var root = new GameObject("ChinesePinyinPopup", typeof(RectTransform));
        root.transform.SetParent(menusGo.transform, false);
        var rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        // ── Dim overlay ─────────────────────────────────────────────────────
        var dim = new GameObject("Dim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dim.transform.SetParent(root.transform, false);
        var dimRT = dim.GetComponent<RectTransform>();
        dimRT.anchorMin = Vector2.zero;
        dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = Vector2.zero;
        dimRT.offsetMax = Vector2.zero;
        dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        // ── Card ─────────────────────────────────────────────────────────────
        var card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup));
        card.transform.SetParent(root.transform, false);
        var cardRT = card.GetComponent<RectTransform>();
        cardRT.anchorMin = new Vector2(0.5f, 0.5f);
        cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.pivot = new Vector2(0.5f, 0.5f);
        cardRT.sizeDelta = new Vector2(640f, 380f);
        cardRT.anchoredPosition = Vector2.zero;
        card.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 1f);
        var vlg = card.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 16, 16);
        vlg.spacing = 10f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // ── Title ─────────────────────────────────────────────────────────────
        var titleGo = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        titleGo.transform.SetParent(card.transform, false);
        titleGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 30f);
        var titleTmp = titleGo.GetComponent<TextMeshProUGUI>();
        titleTmp.text = "Add Chinese Phase";
        titleTmp.fontSize = 20f;
        titleTmp.color = Color.white;
        titleTmp.fontStyle = FontStyles.Bold;
        var le0 = titleGo.AddComponent<LayoutElement>();
        le0.minHeight = 30f;
        le0.preferredHeight = 30f;

        // ── Preview Row (HorizontalLayoutGroup for TargetCell prefabs) ────────
        var previewGo = new GameObject("PreviewRow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        previewGo.transform.SetParent(card.transform, false);
        previewGo.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 1f);
        var prevRT = previewGo.GetComponent<RectTransform>();
        prevRT.sizeDelta = new Vector2(0f, 70f);
        var lePreview = previewGo.AddComponent<LayoutElement>();
        lePreview.minHeight = 70f;
        lePreview.preferredHeight = 70f;
        var hlg = previewGo.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(8, 8, 6, 6);
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        var csf = previewGo.GetComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        // ── Char Row ──────────────────────────────────────────────────────────
        var charRow = MakeRow("CharRow", card.transform, 36f);
        MakeLabel("CharLabel", charRow.transform, "Characters:", 110f);
        var charField = MakeTMPInputField("CharField", charRow.transform, 480f, 36f);
        charField.readOnly = true;
        charField.textComponent.color = new Color(0.85f, 0.85f, 0.85f, 1f);

        // ── Pinyin Row ────────────────────────────────────────────────────────
        var pinyinRow = MakeRow("PinyinRow", card.transform, 36f);
        MakeLabel("PinyinLabel", pinyinRow.transform, "Pinyin:", 110f);
        var pinyinField = MakeTMPInputField("PinyinField", pinyinRow.transform, 480f, 36f);
        pinyinField.textComponent.color = Color.white;

        // Wire OnPinyinFieldChanged callback
        pinyinField.onValueChanged.AddListener(pinyinField.GetComponent<TMP_InputField>().onValueChanged.GetPersistentEventCount() > 0
            ? null
            : (UnityEngine.Events.UnityAction<string>)null);
        // (The actual wiring to ChinesePinyinPopup.OnPinyinFieldChanged is done via AddPersistentListener below)

        // ── Error Label ───────────────────────────────────────────────────────
        var errorGo = new GameObject("ErrorLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        errorGo.transform.SetParent(card.transform, false);
        errorGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 22f);
        var errorTmp = errorGo.GetComponent<TextMeshProUGUI>();
        errorTmp.text = "";
        errorTmp.fontSize = 13f;
        errorTmp.color = new Color(1f, 0.4f, 0.4f, 1f);
        var leErr = errorGo.AddComponent<LayoutElement>();
        leErr.minHeight = 22f;
        leErr.preferredHeight = 22f;

        // ── Button Row ────────────────────────────────────────────────────────
        var btnRow = MakeRow("ButtonRow", card.transform, 40f);
        btnRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleRight;
        MakeBtn("CancelBtn", btnRow.transform, "Cancel", new Color(0.3f, 0.3f, 0.3f, 1f), 120f, 36f);
        MakeBtn("OKBtn", btnRow.transform, "OK", new Color(0.15f, 0.55f, 0.25f, 1f), 120f, 36f);

        // ── Attach ChinesePinyinPopup component ───────────────────────────────
        var popup = root.AddComponent<ChinesePinyinPopup>();

        // Use SerializedObject to assign serialized fields
        var so = new SerializedObject(popup);
        so.FindProperty("targetCellPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/-Prefabs/UI/TargetCell.prefab");
        so.FindProperty("previewContainer").objectReferenceValue = previewGo.transform;
        so.FindProperty("charField").objectReferenceValue = charField;
        so.FindProperty("pinyinField").objectReferenceValue = pinyinField;
        so.FindProperty("errorLabel").objectReferenceValue = errorGo.GetComponent<TextMeshProUGUI>();
        so.FindProperty("okBtn").objectReferenceValue =
            card.transform.Find("ButtonRow/OKBtn").GetComponent<Button>();
        so.FindProperty("cancelBtn").objectReferenceValue =
            card.transform.Find("ButtonRow/CancelBtn").GetComponent<Button>();
        so.ApplyModifiedProperties();

        // ── Wire PinyinField.onValueChanged → popup.OnPinyinFieldChanged ──────
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            pinyinField.onValueChanged,
            popup.OnPinyinFieldChanged);

        // ── Deactivate on start ───────────────────────────────────────────────
        root.SetActive(false);

        // ── Save scene ────────────────────────────────────────────────────────
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[BuildChinesePinyinPopup] Done. Popup created at '--- UI ---/Menus/ChinesePinyinPopup'.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static GameObject MakeRow(string name, Transform parent, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
        var hlg = go.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        return go;
    }

    static void MakeLabel(string name, Transform parent, string text, float width)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(width, 30f);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 14f;
        tmp.color = new Color(0.8f, 0.8f, 0.8f, 1f);
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;
    }

    static TMP_InputField MakeTMPInputField(string name, Transform parent, float width, float height)
    {
        // Use DefaultControls to get a properly structured TMP InputField
        var resources = new DefaultControls.Resources();
        var go = DefaultControls.CreateInputField(resources);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);

        // Replace legacy InputField with TMP_InputField via the TMP helper
        // Actually DefaultControls gives us a legacy InputField — replace text with TMP
        // Easier: just build minimal structure manually using TMP_InputField prefab approach
        GameObject.DestroyImmediate(go);

        var root2 = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root2.transform.SetParent(parent, false);
        root2.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);
        root2.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);

        var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(root2.transform, false);
        var taRT = textArea.GetComponent<RectTransform>();
        taRT.anchorMin = Vector2.zero;
        taRT.anchorMax = Vector2.one;
        taRT.offsetMin = new Vector2(8f, 4f);
        taRT.offsetMax = new Vector2(-8f, -4f);

        var placeholder = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        placeholder.transform.SetParent(textArea.transform, false);
        var phRT = placeholder.GetComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = Vector2.zero;
        phRT.offsetMax = Vector2.zero;
        var phTmp = placeholder.GetComponent<TextMeshProUGUI>();
        phTmp.text = "";
        phTmp.fontSize = 14f;
        phTmp.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        phTmp.fontStyle = FontStyles.Italic;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(textArea.transform, false);
        var tRT = textGo.GetComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero;
        tRT.anchorMax = Vector2.one;
        tRT.offsetMin = Vector2.zero;
        tRT.offsetMax = Vector2.zero;
        var textTmp = textGo.GetComponent<TextMeshProUGUI>();
        textTmp.fontSize = 15f;
        textTmp.color = Color.white;

        var inputField = root2.AddComponent<TMP_InputField>();
        inputField.textViewport = taRT;
        inputField.textComponent = textTmp;
        inputField.placeholder = phTmp;

        var le = root2.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;

        return inputField;
    }

    static void MakeBtn(string name, Transform parent, string label, Color color, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);
        go.GetComponent<Image>().color = color;
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;
        le.minHeight = height;

        var txtGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        txtGo.transform.SetParent(go.transform, false);
        var txtRT = txtGo.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = Vector2.zero;
        txtRT.offsetMax = Vector2.zero;
        var tmp = txtGo.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 14f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
    }
}
