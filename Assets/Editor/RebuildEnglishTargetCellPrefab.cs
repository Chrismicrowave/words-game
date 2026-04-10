using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;

public class RebuildEnglishTargetCellPrefab
{
    [MenuItem("Tools/Rebuild EnglishTargetCell Prefab")]
    public static void Execute()
    {
        string prefabPath = "Assets/-Prefabs/UI/EnglishTargetCell.prefab";

        string sourcePath = "Assets/-Prefabs/UI/TargetCell.prefab";
        if (!AssetDatabase.CopyAsset(sourcePath, prefabPath))
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                Debug.LogError("[RebuildEnglishTargetCellPrefab] Could not copy TargetCell prefab to " + prefabPath);
                return;
            }
        }
        AssetDatabase.Refresh();

        using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
        {
            var root = scope.prefabContentsRoot;

            // Remove all existing children
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                GameObject.DestroyImmediate(root.transform.GetChild(i).gameObject);

            // Remove TargetCell component
            var existingCell = root.GetComponent<TargetCell>();
            if (existingCell != null) GameObject.DestroyImmediate(existingCell);

            // Root rect: same as TargetCell (120x200); no LayoutElement — parent layout controls sizing
            var rootRT = root.GetComponent<RectTransform>();
            rootRT.sizeDelta = new Vector2(120f, 200f);

            // Remove any layout groups and LayoutElement
            var hlg = root.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) GameObject.DestroyImmediate(hlg);
            var vlg = root.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) GameObject.DestroyImmediate(vlg);
            var le = root.GetComponent<LayoutElement>();
            if (le != null) GameObject.DestroyImmediate(le);

            // ── Label (full-rect, no auto-sizing — font size synced live to Chinese cells) ────
            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(root.transform, false);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;
            var tmp = labelGO.GetComponent<TextMeshProUGUI>();
            tmp.enableAutoSizing = false;
            tmp.fontSize = 36f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.text = "";

            // ── Wire EnglishCell fields ──────────────────────────────────────────
            var cell = root.GetComponent<EnglishCell>() ?? root.AddComponent<EnglishCell>();
            var so = new SerializedObject(cell);
            so.FindProperty("label").objectReferenceValue = tmp;
            so.ApplyModifiedProperties();

            Debug.Log("[RebuildEnglishTargetCellPrefab] Done — " + prefabPath);
        }
    }
}
