using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;

public class RebuildEnglishCellPrefab
{
    [MenuItem("Tools/Rebuild EnglishCell Prefab")]
    public static void Execute()
    {
        string prefabPath = "Assets/-Prefabs/UI/EnglishCell.prefab";

        // Copy TargetCell prefab as starting point, then clear its children
        string sourcePath = "Assets/-Prefabs/UI/TargetCell.prefab";
        if (!AssetDatabase.CopyAsset(sourcePath, prefabPath))
        {
            // Prefab may already exist — open it directly
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                Debug.LogError("[RebuildEnglishCellPrefab] Could not copy TargetCell prefab to " + prefabPath);
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

            // Remove any existing components that came from TargetCell
            var existingCell = root.GetComponent<TargetCell>();
            if (existingCell != null) GameObject.DestroyImmediate(existingCell);

            // Root rect: default 100x70; layout handles actual width at runtime
            var rootRT = root.GetComponent<RectTransform>();
            rootRT.sizeDelta = new Vector2(100f, 70f);

            // Remove any existing layout group
            var hlg = root.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) GameObject.DestroyImmediate(hlg);
            var vlg = root.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) GameObject.DestroyImmediate(vlg);

            // Root LayoutElement: width set at runtime by BuildMixedCells
            var le = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
            le.minHeight = 70f;
            le.preferredHeight = 70f;

            // ── Label ────────────────────────────────────────────────────────────
            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(root.transform, false);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;
            var tmp = labelGO.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = 32f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.text = "";

            // ── Wire EnglishCell fields ──────────────────────────────────────────
            var cell = root.GetComponent<EnglishCell>() ?? root.AddComponent<EnglishCell>();
            var so = new SerializedObject(cell);
            so.FindProperty("label").objectReferenceValue = tmp;
            so.ApplyModifiedProperties();

            Debug.Log("[RebuildEnglishCellPrefab] Done — " + prefabPath);
        }
    }
}
