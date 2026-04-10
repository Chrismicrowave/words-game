using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;

public class RebuildCharacterCellPrefab
{
    public static void Execute()
    {
        string prefabPath = "Assets/-Prefabs/UI/CharacterCell.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) { Debug.LogError("[RebuildCharacterCellPrefab] Prefab not found at " + prefabPath); return; }

        // Load Noto Sans SC for the character label
        var chineseFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/-Fonts/Noto_Sans_SC/NotoSansSC-VariableFont_wght SDF.asset");
        if (chineseFont == null)
            Debug.LogWarning("[RebuildCharacterCellPrefab] Chinese font not found — charLabel will use default font.");

        using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
        {
            var root = scope.prefabContentsRoot;

            // Remove existing children
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                GameObject.DestroyImmediate(root.transform.GetChild(i).gameObject);

            // Root: 60x70, VerticalLayoutGroup (letter on top, char below)
            var rootRT = root.GetComponent<RectTransform>();
            rootRT.sizeDelta = new Vector2(60f, 70f);

            // Replace HorizontalLayoutGroup with VerticalLayoutGroup
            var hlg = root.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) GameObject.DestroyImmediate(hlg);
            var vlg = root.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, 0, 0, 0);
            vlg.spacing = 2f;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // ── LetterLabel (pinyin in progress) ─────────────────────────────
            var letterGO = new GameObject("LetterLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            letterGO.transform.SetParent(root.transform, false);
            letterGO.GetComponent<RectTransform>().sizeDelta = new Vector2(60f, 30f);
            var letterTMP = letterGO.GetComponent<TextMeshProUGUI>();
            letterTMP.fontSize = 18f;
            letterTMP.color = Color.white;
            letterTMP.alignment = TextAlignmentOptions.Center;
            letterTMP.text = "";
            var leLetter = letterGO.AddComponent<LayoutElement>();
            leLetter.minHeight = 30f;
            leLetter.preferredHeight = 30f;

            // ── CharLabel (Chinese character, revealed when complete) ─────────
            var charGO = new GameObject("CharLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            charGO.transform.SetParent(root.transform, false);
            charGO.GetComponent<RectTransform>().sizeDelta = new Vector2(60f, 38f);
            var charTMP = charGO.GetComponent<TextMeshProUGUI>();
            charTMP.fontSize = 32f;
            charTMP.color = Color.white;
            charTMP.alignment = TextAlignmentOptions.Center;
            charTMP.text = "";
            if (chineseFont != null) charTMP.font = chineseFont;
            var leChar = charGO.AddComponent<LayoutElement>();
            leChar.minHeight = 38f;
            leChar.preferredHeight = 38f;

            // ── Wire CharacterCell fields ─────────────────────────────────────
            var cell = root.GetComponent<CharacterCell>();
            var so = new SerializedObject(cell);
            so.FindProperty("letterLabel").objectReferenceValue = letterTMP;
            so.FindProperty("charLabel").objectReferenceValue   = charTMP;
            so.ApplyModifiedProperties();

            Debug.Log("[RebuildCharacterCellPrefab] Done.");
        }
    }
}
