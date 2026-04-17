#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Replaces the broken LanguageDropdown (missing Template) with one built via
/// DefaultControls.CreateDropdown(), which includes the full Template hierarchy.
/// Also re-wires options + OnValueChanged + DisplaySettingsController.languageDropdown.
/// Run via Tools > Words > Rebuild Language Dropdown.
/// </summary>
public class RebuildLanguageDropdown
{
    const string LanguageRowPath  = "--- UI ---/Menus/SettingsPanel/Card/ContentArea/-DisplayPanel/LanguageRow";
    const string DisplayPanelPath = "--- UI ---/Menus/SettingsPanel/Card/ContentArea/-DisplayPanel";

    [MenuItem("Tools/Words/Rebuild Language Dropdown")]
    public static void Execute()
    {
        var rowGO = FindInScene(LanguageRowPath);
        if (rowGO == null) { Debug.LogError("[RebuildLD] LanguageRow not found."); return; }

        // ── Remove old LanguageDropdown child ──────────────────────────────────
        Transform oldT = rowGO.transform.Find("LanguageDropdown");
        if (oldT != null)
        {
            Object.DestroyImmediate(oldT.gameObject);
            Debug.Log("[RebuildLD] Removed old LanguageDropdown.");
        }

        // ── Create new dropdown via DefaultControls (includes full Template) ───
        var resources = new DefaultControls.Resources();
        var dropdownGO = DefaultControls.CreateDropdown(resources);
        dropdownGO.name = "LanguageDropdown";
        dropdownGO.transform.SetParent(rowGO.transform, false);

        // ── Set options ───────────────────────────────────────────────────────
        var dropdown = dropdownGO.GetComponent<Dropdown>();
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string> { "English", "中文（简体）" });

        // ── Wire OnValueChanged → DisplaySettingsController ───────────────────
        var displayPanelGO = FindInScene(DisplayPanelPath);
        if (displayPanelGO == null) { Debug.LogError("[RebuildLD] -DisplayPanel not found."); return; }
        var dsc = displayPanelGO.GetComponent<DisplaySettingsController>();
        if (dsc == null) { Debug.LogError("[RebuildLD] DisplaySettingsController not found."); return; }

        dropdown.onValueChanged.RemoveAllListeners();
        UnityEventTools.AddPersistentListener(
            dropdown.onValueChanged,
            new UnityAction<int>(dsc.OnLanguageChanged));

        // ── Assign languageDropdown field on DisplaySettingsController ─────────
        var so = new SerializedObject(dsc);
        var prop = so.FindProperty("languageDropdown");
        if (prop != null)
        {
            prop.objectReferenceValue = dropdown;
            so.ApplyModifiedProperties();
            Debug.Log("[RebuildLD] languageDropdown wired on DisplaySettingsController.");
        }
        else Debug.LogWarning("[RebuildLD] 'languageDropdown' field not found on DisplaySettingsController.");

        EditorUtility.SetDirty(rowGO);
        EditorUtility.SetDirty(displayPanelGO);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[RebuildLD] DONE.");
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
