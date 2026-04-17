#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wires FontLocalizationManager on --- UI ---:
/// - Sets chineseFont to NotoSansSC SDF asset
/// - Populates targets with every TextMeshProUGUI that also has LocalizeText
/// Run via Tools > Words > Wire Font Localization Manager.
/// Safe to re-run.
/// </summary>
public class WireFontLocalizationManager
{
    const string FontAssetPath = "Assets/-Fonts/Noto_Sans_SC/NotoSansSC-VariableFont_wght SDF.asset";

    [MenuItem("Tools/Words/Wire Font Localization Manager")]
    public static void Execute()
    {
        // ── Find FontLocalizationManager ──────────────────────────────────────
        var uiRoot = FindInScene("--- UI ---");
        if (uiRoot == null) { Debug.LogError("[WireFLM] '--- UI ---' not found."); return; }

        var flm = uiRoot.GetComponent<FontLocalizationManager>();
        if (flm == null) { Debug.LogError("[WireFLM] FontLocalizationManager not on '--- UI ---'."); return; }

        // ── Load NotoSansSC font asset ────────────────────────────────────────
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (font == null) { Debug.LogError($"[WireFLM] Font not found at: {FontAssetPath}"); return; }

        // ── Collect all TMP labels that have LocalizeText ─────────────────────
        var targets = new List<TextMeshProUGUI>();
        foreach (var lt in Object.FindObjectsByType<LocalizeText>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var tmp = lt.GetComponent<TextMeshProUGUI>();
            if (tmp != null) targets.Add(tmp);
        }

        Debug.Log($"[WireFLM] Found {targets.Count} LocalizeText TMP targets.");

        // ── Apply via SerializedObject ────────────────────────────────────────
        var so = new SerializedObject(flm);

        var fontProp = so.FindProperty("chineseFont");
        if (fontProp != null) fontProp.objectReferenceValue = font;
        else Debug.LogWarning("[WireFLM] 'chineseFont' property not found.");

        var listProp = so.FindProperty("targets");
        if (listProp != null)
        {
            listProp.ClearArray();
            for (int i = 0; i < targets.Count; i++)
            {
                listProp.InsertArrayElementAtIndex(i);
                listProp.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
            }
        }
        else Debug.LogWarning("[WireFLM] 'targets' property not found.");

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(uiRoot);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log($"[WireFLM] DONE. chineseFont={font.name}, targets={targets.Count}.");
    }

    static GameObject FindInScene(string name)
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == name) return root;
        return null;
    }
}
#endif
