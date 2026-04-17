#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds DropdownTMPBridge to LanguageDropdown and wires captionTMP → Label.
/// Leaves all other Dropdown/Label settings untouched.
/// Run via Tools > Words > Wire Dropdown TMP Bridge.
/// </summary>
public class WireDropdownTMPBridge
{
    const string DropdownPath = "--- UI ---/Menus/SettingsPanel/Card/ContentArea/-DisplayPanel/LanguageRow/LanguageDropdown";
    const string LabelPath    = "--- UI ---/Menus/SettingsPanel/Card/ContentArea/-DisplayPanel/LanguageRow/LanguageDropdown/Label";

    [MenuItem("Tools/Words/Wire Dropdown TMP Bridge")]
    public static void Execute()
    {
        var dropdownGO = FindInScene(DropdownPath);
        if (dropdownGO == null) { Debug.LogError("[WireDB] LanguageDropdown not found."); return; }

        var labelGO = FindInScene(LabelPath);
        if (labelGO == null) { Debug.LogError("[WireDB] Label not found."); return; }

        var tmp = labelGO.GetComponent<TMPro.TextMeshProUGUI>();
        if (tmp == null) { Debug.LogError("[WireDB] No TextMeshProUGUI on Label."); return; }

        // Add or reuse DropdownTMPBridge
        var bridge = dropdownGO.GetComponent<DropdownTMPBridge>();
        if (bridge == null) bridge = dropdownGO.AddComponent<DropdownTMPBridge>();

        var so = new SerializedObject(bridge);
        var prop = so.FindProperty("captionTMP");
        prop.objectReferenceValue = tmp;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(dropdownGO);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[WireDB] DONE — DropdownTMPBridge wired to Label TMP.");
    }

    static GameObject FindInScene(string path)
    {
        var parts = path.Split('/');
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;
            if (parts.Length == 1) return root;
            Transform t = root.transform;
            for (int i = 1; i < parts.Length; i++)
            {
                Transform found = null;
                for (int c = 0; c < t.childCount; c++)
                    if (t.GetChild(c).name == parts[i]) { found = t.GetChild(c); break; }
                if (found == null) return null;
                t = found;
            }
            return t.gameObject;
        }
        return null;
    }
}
#endif
