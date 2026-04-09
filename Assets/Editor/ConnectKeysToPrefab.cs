using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class ConnectKeysToPrefab
{
    public static void Execute()
    {
        string prefabPath = "Assets/-Prefabs/UI/KeyButton.prefab";
        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabAsset == null) { Debug.LogError("Prefab not found: " + prefabPath); return; }

        GameObject keyboard = GameObject.Find("--- UI ---/keyboard");
        if (keyboard == null) { Debug.LogError("keyboard GO not found"); return; }

        KeyboardVisualController kvc = Object.FindObjectOfType<KeyboardVisualController>();
        if (kvc == null) { Debug.LogError("KeyboardVisualController not found"); return; }

        // Capture existing keyCode → GO name mapping BEFORE destroying anything
        var so = new SerializedObject(kvc);
        var mappingsProp = so.FindProperty("keyMappings");
        var keyCodeToGoName = new Dictionary<int, string>(); // KeyCode int → GO name
        for (int i = 0; i < mappingsProp.arraySize; i++)
        {
            var elem     = mappingsProp.GetArrayElementAtIndex(i);
            var imgProp  = elem.FindPropertyRelative("keyImage");
            var codeProp = elem.FindPropertyRelative("keyCode");
            if (imgProp.objectReferenceValue is Image img && img != null)
                keyCodeToGoName[codeProp.intValue] = img.gameObject.name;
        }

        // Snapshot children (don't modify during iteration)
        var children = new List<Transform>();
        foreach (Transform child in keyboard.transform)
            children.Add(child);

        int replaced = 0;
        foreach (var child in children)
        {
            // Skip if already an instance of this prefab
            if (PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                if (src != null && AssetDatabase.GetAssetPath(src) == prefabPath)
                    continue;
            }

            // Capture all per-key data
            var rt = child.GetComponent<RectTransform>();
            Vector2 anchorMin   = rt.anchorMin;
            Vector2 anchorMax   = rt.anchorMax;
            Vector2 pivot       = rt.pivot;
            Vector2 anchoredPos = rt.anchoredPosition;
            Vector2 sizeDelta   = rt.sizeDelta;
            Vector3 localScale  = rt.localScale;

            // Capture label from TMP child (falls back to GO name for keys without keyText)
            var tmpChild = child.GetComponentInChildren<TextMeshProUGUI>(true);
            string label = tmpChild != null ? tmpChild.text : child.name;

            string goName       = child.name;
            int siblingIndex    = child.GetSiblingIndex();

            Undo.DestroyObjectImmediate(child.gameObject);

            // Instantiate prefab in-place
            GameObject newGo = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, keyboard.transform);
            newGo.name = goName;
            newGo.transform.SetSiblingIndex(siblingIndex);

            // Restore transform (preserves per-key size — BACKSPACE, ENTER, SHIFT etc. keep their sizes)
            var newRt = newGo.GetComponent<RectTransform>();
            newRt.anchorMin      = anchorMin;
            newRt.anchorMax      = anchorMax;
            newRt.pivot          = pivot;
            newRt.anchoredPosition = anchoredPos;
            newRt.sizeDelta      = sizeDelta;
            newRt.localScale     = localScale;

            // Set label on keyText child
            var newTmp = newGo.GetComponentInChildren<TextMeshProUGUI>(true);
            if (newTmp != null) newTmp.text = label;

            replaced++;
        }

        // Rebuild keyMappings pointing at the new Image components
        so.Update();
        mappingsProp.ClearArray();
        int rewired = 0;
        foreach (var kvp in keyCodeToGoName)
        {
            Transform keyGo = keyboard.transform.Find(kvp.Value);
            if (keyGo == null) { Debug.LogWarning($"Key GO not found after replace: {kvp.Value}"); continue; }
            Image img = keyGo.GetComponent<Image>();
            if (img == null) { Debug.LogWarning($"No Image on key: {kvp.Value}"); continue; }

            mappingsProp.InsertArrayElementAtIndex(rewired);
            var elem = mappingsProp.GetArrayElementAtIndex(rewired);
            elem.FindPropertyRelative("keyCode").intValue                  = kvp.Key;
            elem.FindPropertyRelative("keyImage").objectReferenceValue     = img;
            rewired++;
        }
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ConnectKeysToPrefab] Replaced {replaced} key GOs, rewired {rewired} keyMappings.");
    }
}
