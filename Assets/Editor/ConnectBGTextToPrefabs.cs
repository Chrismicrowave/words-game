using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;

public class ConnectBGTextToPrefabs
{
    public static void Execute()
    {
        string prefabA = "Assets/-Prefabs/AnimatedBackgroundText.prefab";
        string prefabB = "Assets/-Prefabs/AnimatedBackgroundTextB.prefab";

        var goA = AssetDatabase.LoadAssetAtPath<GameObject>(prefabA);
        var goB = AssetDatabase.LoadAssetAtPath<GameObject>(prefabB);

        if (goA == null) { Debug.LogError("Not found: " + prefabA); return; }
        if (goB == null) { Debug.LogError("Not found: " + prefabB); return; }

        int count = 0;
        count += ConnectChildren("--- UI ---/BG/A", goA);
        count += ConnectChildren("--- UI ---/BG/B", goB);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ConnectBGTextToPrefabs] Connected {count} GOs to prefabs.");
    }

    static int ConnectChildren(string parentPath, GameObject prefabAsset)
    {
        GameObject parent = GameObject.Find(parentPath);
        if (parent == null) { Debug.LogError("Parent not found: " + parentPath); return 0; }

        // Snapshot children first (avoid modifying collection during iteration)
        var children = new List<Transform>();
        foreach (Transform child in parent.transform)
            children.Add(child);

        int count = 0;
        foreach (var child in children)
        {
            // Skip if already a prefab instance of the correct prefab
            if (PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
            {
                GameObject src = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                if (src != null && AssetDatabase.GetAssetPath(src) == AssetDatabase.GetAssetPath(prefabAsset))
                    continue; // already connected
            }

            // Capture overrides
            var rt = child.GetComponent<RectTransform>();
            Vector2 anchorMin      = rt.anchorMin;
            Vector2 anchorMax      = rt.anchorMax;
            Vector2 pivot          = rt.pivot;
            Vector2 anchoredPos    = rt.anchoredPosition;
            Vector2 sizeDelta      = rt.sizeDelta;
            Vector3 localScale     = rt.localScale;
            Quaternion localRot    = rt.localRotation;

            var tmp = child.GetComponent<TextMeshProUGUI>();
            string text            = tmp != null ? tmp.text : "";

            var anim = child.GetComponent<Animator>();
            RuntimeAnimatorController animCtrl = anim != null ? anim.runtimeAnimatorController : null;

            string goName          = child.name;
            int siblingIndex       = child.GetSiblingIndex();

            Undo.DestroyObjectImmediate(child.gameObject);

            // Instantiate prefab as child of same parent
            GameObject newGo = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, parent.transform);
            newGo.name = goName;
            newGo.transform.SetSiblingIndex(siblingIndex);

            var newRt = newGo.GetComponent<RectTransform>();
            newRt.anchorMin      = anchorMin;
            newRt.anchorMax      = anchorMax;
            newRt.pivot          = pivot;
            newRt.anchoredPosition = anchoredPos;
            newRt.sizeDelta      = sizeDelta;
            newRt.localScale     = localScale;
            newRt.localRotation  = localRot;

            var newTmp = newGo.GetComponent<TextMeshProUGUI>();
            if (newTmp != null) newTmp.text = text;

            var newAnim = newGo.GetComponent<Animator>();
            if (newAnim != null && animCtrl != null)
                newAnim.runtimeAnimatorController = animCtrl;

            count++;
        }
        return count;
    }
}
