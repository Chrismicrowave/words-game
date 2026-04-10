using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Stage 8 wiring for v0.5 Chinese language support.
/// Run via: WireChineseDisplays.Execute()
/// </summary>
public class WireChineseDisplays
{
    public static void Execute()
    {
        string fontPath = "Assets/-Fonts/Noto_Sans_SC/NotoSansSC-VariableFont_wght SDF.asset";
        TMP_FontAsset chineseFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        if (chineseFont == null) { Debug.LogError("[WireChineseDisplays] Noto Sans SC font not found at: " + fontPath); return; }

        // ── 1. Finish wiring CharacterCell GO ─────────────────────────────────
        GameObject charCellGO = GameObject.Find("CharacterCell");
        if (charCellGO == null) { Debug.LogError("[WireChineseDisplays] CharacterCell GO not found in scene."); return; }

        var charCellComp = charCellGO.GetComponent<CharacterCell>();
        var letterLabel  = charCellGO.transform.Find("LetterLabel")?.GetComponent<TextMeshProUGUI>();
        var charLabel    = charCellGO.transform.Find("CharLabel")?.GetComponent<TextMeshProUGUI>();

        if (charLabel != null)  charLabel.font  = chineseFont;
        if (letterLabel != null) letterLabel.fontSize = 36;
        if (charLabel != null)  charLabel.fontSize  = 36;

        if (charCellComp != null)
        {
            var so = new SerializedObject(charCellComp);
            so.FindProperty("letterLabel").objectReferenceValue = letterLabel;
            so.FindProperty("charLabel").objectReferenceValue   = charLabel;
            so.ApplyModifiedProperties();
        }

        // ── 2. Create CharacterCell prefab ─────────────────────────────────────
        string charCellPrefabPath = "Assets/-Prefabs/UI/CharacterCell.prefab";
        PrefabUtility.SaveAsPrefabAssetAndConnect(charCellGO, charCellPrefabPath, InteractionMode.AutomatedAction);
        Debug.Log("[WireChineseDisplays] CharacterCell prefab saved: " + charCellPrefabPath);

        // ── 3. Create TargetCell GO + prefab ──────────────────────────────────
        GameObject targetCellGO = new GameObject("TargetCell");
        targetCellGO.AddComponent<RectTransform>();
        var vlg = targetCellGO.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.spacing = 2;

        var targetCellComp = targetCellGO.AddComponent<TargetCell>();

        // PinyinLabel (small, top)
        GameObject pinyinLabelGO = new GameObject("PinyinLabel");
        pinyinLabelGO.transform.SetParent(targetCellGO.transform, false);
        var pinyinTMP = pinyinLabelGO.AddComponent<TextMeshProUGUI>();
        pinyinTMP.text = "ni";
        pinyinTMP.fontSize = 18;
        pinyinTMP.alignment = TextAlignmentOptions.Center;
        pinyinLabelGO.AddComponent<RectTransform>();

        // CharLabel (large, bottom — Noto Sans SC)
        GameObject tcCharLabelGO = new GameObject("CharLabel");
        tcCharLabelGO.transform.SetParent(targetCellGO.transform, false);
        var tcCharTMP = tcCharLabelGO.AddComponent<TextMeshProUGUI>();
        tcCharTMP.text = "你";
        tcCharTMP.fontSize = 36;
        tcCharTMP.alignment = TextAlignmentOptions.Center;
        tcCharTMP.font = chineseFont;
        tcCharLabelGO.AddComponent<RectTransform>();

        // Wire TargetCell fields
        var tcSO = new SerializedObject(targetCellComp);
        tcSO.FindProperty("pinyinLabel").objectReferenceValue = pinyinTMP;
        tcSO.FindProperty("charLabel").objectReferenceValue   = tcCharTMP;
        tcSO.ApplyModifiedProperties();

        string targetCellPrefabPath = "Assets/-Prefabs/UI/TargetCell.prefab";
        PrefabUtility.SaveAsPrefabAssetAndConnect(targetCellGO, targetCellPrefabPath, InteractionMode.AutomatedAction);
        Debug.Log("[WireChineseDisplays] TargetCell prefab saved: " + targetCellPrefabPath);

        // ── 4. Load prefab assets ──────────────────────────────────────────────
        GameObject charCellPrefabAsset   = AssetDatabase.LoadAssetAtPath<GameObject>(charCellPrefabPath);
        GameObject targetCellPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(targetCellPrefabPath);

        // ── 5. Create ChineseMatchedDisplay GO under GameplayText ─────────────
        GameObject gameplayText = GameObject.Find("--- UI ---/GameplayText");
        if (gameplayText == null) { Debug.LogError("[WireChineseDisplays] GameplayText GO not found."); return; }

        GameObject cmdGO = new GameObject("ChineseMatchedDisplay");
        cmdGO.transform.SetParent(gameplayText.transform, false);
        cmdGO.AddComponent<RectTransform>();
        var cmdHLG = cmdGO.AddComponent<HorizontalLayoutGroup>();
        cmdHLG.spacing = 4;
        cmdHLG.childAlignment = TextAnchor.MiddleCenter;
        cmdHLG.childControlWidth = false;
        cmdHLG.childControlHeight = false;
        var cmdComp = cmdGO.AddComponent<ChineseMatchedDisplay>();

        // cellContainer = a child Content GO
        GameObject cmdContainer = new GameObject("Content");
        cmdContainer.transform.SetParent(cmdGO.transform, false);
        cmdContainer.AddComponent<RectTransform>();
        var cmdContHLG = cmdContainer.AddComponent<HorizontalLayoutGroup>();
        cmdContHLG.spacing = 4;
        cmdContHLG.childAlignment = TextAnchor.MiddleCenter;

        var cmdSO = new SerializedObject(cmdComp);
        cmdSO.FindProperty("characterCellPrefab").objectReferenceValue = charCellPrefabAsset;
        cmdSO.FindProperty("cellContainer").objectReferenceValue       = cmdContainer.transform;
        cmdSO.ApplyModifiedProperties();

        cmdGO.SetActive(false); // hidden by default
        Debug.Log("[WireChineseDisplays] ChineseMatchedDisplay GO created under GameplayText.");

        // ── 6. Create ChineseTargetDisplay GO under GameplayText ──────────────
        GameObject ctdGO = new GameObject("ChineseTargetDisplay");
        ctdGO.transform.SetParent(gameplayText.transform, false);
        ctdGO.AddComponent<RectTransform>();
        var ctdHLG = ctdGO.AddComponent<HorizontalLayoutGroup>();
        ctdHLG.spacing = 4;
        ctdHLG.childAlignment = TextAnchor.MiddleCenter;
        var ctdComp = ctdGO.AddComponent<ChineseTargetDisplay>();

        // cellContainer = a child Content GO
        GameObject ctdContainer = new GameObject("Content");
        ctdContainer.transform.SetParent(ctdGO.transform, false);
        ctdContainer.AddComponent<RectTransform>();
        var ctdContHLG = ctdContainer.AddComponent<HorizontalLayoutGroup>();
        ctdContHLG.spacing = 4;
        ctdContHLG.childAlignment = TextAnchor.MiddleCenter;

        var ctdSO = new SerializedObject(ctdComp);
        ctdSO.FindProperty("targetCellPrefab").objectReferenceValue = targetCellPrefabAsset;
        ctdSO.FindProperty("cellContainer").objectReferenceValue    = ctdContainer.transform;
        ctdSO.ApplyModifiedProperties();

        ctdGO.SetActive(false); // hidden by default
        Debug.Log("[WireChineseDisplays] ChineseTargetDisplay GO created under GameplayText.");

        // ── 7. Wire UIController ──────────────────────────────────────────────
        UIController uiController = Object.FindFirstObjectByType<UIController>(FindObjectsInactive.Include);
        if (uiController != null)
        {
            var uiSO = new SerializedObject(uiController);
            uiSO.FindProperty("chineseMatchedDisplay").objectReferenceValue = cmdComp;
            uiSO.FindProperty("chineseTargetDisplay").objectReferenceValue  = ctdComp;
            uiSO.ApplyModifiedProperties();
            Debug.Log("[WireChineseDisplays] UIController wired.");
        }
        else Debug.LogWarning("[WireChineseDisplays] UIController not found.");

        // ── 8. Wire PhaseListUIManager chinese font ────────────────────────────
        PhaseListUIManager phaseListUI = Object.FindFirstObjectByType<PhaseListUIManager>(FindObjectsInactive.Include);
        if (phaseListUI != null)
        {
            var plSO = new SerializedObject(phaseListUI);
            plSO.FindProperty("chineseFontAsset").objectReferenceValue = chineseFont;
            plSO.ApplyModifiedProperties();
            Debug.Log("[WireChineseDisplays] PhaseListUIManager chinese font wired.");
        }

        // ── 9. Clean up temp TargetCell GO from scene root ────────────────────
        Object.DestroyImmediate(targetCellGO);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[WireChineseDisplays] Stage 8 complete. Save the scene.");
    }
}
