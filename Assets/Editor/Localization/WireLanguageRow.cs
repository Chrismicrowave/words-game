#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization;
using UnityEngine.SceneManagement;

/// <summary>
/// Finishes wiring the LanguageRow in the Display settings panel:
/// - Upgrades Label from legacy Text to TextMeshProUGUI + LocalizeText
/// - Configures LanguageDropdown options (English / 中文（简体）)
/// - Wires dropdown OnValueChanged to DisplaySettingsController.OnLanguageChanged
/// - Assigns languageDropdown reference on DisplaySettingsController
/// Run via Tools > Words > Wire Language Row.
/// </summary>
public class WireLanguageRow
{
    const string LabelPath    = "--- UI ---/Menus/SettingsPanel/Card/ContentArea/-DisplayPanel/LanguageRow/Label";
    const string DropdownPath = "--- UI ---/Menus/SettingsPanel/Card/ContentArea/-DisplayPanel/LanguageRow/LanguageDropdown";
    const string DisplayPanelPath = "--- UI ---/Menus/SettingsPanel/Card/ContentArea/-DisplayPanel";

    [MenuItem("Tools/Words/Wire Language Row")]
    public static void Execute()
    {
        // ── Label: replace legacy Text with TMP + LocalizeText ────────────────
        var labelGO = FindInScene(LabelPath);
        if (labelGO == null) { Debug.LogError("[WireLanguageRow] Label not found."); return; }

        // Remove legacy Text
        var legacyText = labelGO.GetComponent<UnityEngine.UI.Text>();
        if (legacyText != null) Object.DestroyImmediate(legacyText);

        // Add TMP if needed
        var tmp = labelGO.GetComponent<TextMeshProUGUI>();
        if (tmp == null) tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = "Language"; // fallback visible in Editor

        // Add LocalizeText
        var old = labelGO.GetComponent<LocalizeText>();
        if (old != null) Object.DestroyImmediate(old);
        var lt = labelGO.AddComponent<LocalizeText>();
        lt.localizedString = new LocalizedString("UI", "UI.Settings.Display.Language");
        EditorUtility.SetDirty(labelGO);

        // ── Dropdown: set options + wire OnValueChanged ───────────────────────
        var dropdownGO = FindInScene(DropdownPath);
        if (dropdownGO == null) { Debug.LogError("[WireLanguageRow] LanguageDropdown not found."); return; }

        var dropdown = dropdownGO.GetComponent<UnityEngine.UI.Dropdown>();
        if (dropdown == null) { Debug.LogError("[WireLanguageRow] Dropdown not found on LanguageDropdown."); return; }

        dropdown.ClearOptions();
        dropdown.AddOptions(new System.Collections.Generic.List<string> { "English", "中文（简体）" });

        // Find DisplaySettingsController
        var displayPanelGO = FindInScene(DisplayPanelPath);
        if (displayPanelGO == null) { Debug.LogError("[WireLanguageRow] -DisplayPanel not found."); return; }
        var dsc = displayPanelGO.GetComponent<DisplaySettingsController>();
        if (dsc == null) { Debug.LogError("[WireLanguageRow] DisplaySettingsController not found."); return; }

        // Wire OnValueChanged → OnLanguageChanged
        dropdown.onValueChanged.RemoveAllListeners();
        UnityEventTools.AddPersistentListener(
            dropdown.onValueChanged,
            new UnityAction<int>(dsc.OnLanguageChanged));

        // Assign languageDropdown field on DisplaySettingsController via SerializedObject
        var so = new SerializedObject(dsc);
        var prop = so.FindProperty("languageDropdown");
        if (prop != null)
        {
            prop.objectReferenceValue = dropdown;
            so.ApplyModifiedProperties();
            Debug.Log("[WireLanguageRow] languageDropdown wired on DisplaySettingsController.");
        }
        else Debug.LogWarning("[WireLanguageRow] languageDropdown field not found on DisplaySettingsController.");

        EditorUtility.SetDirty(dropdownGO);
        EditorUtility.SetDirty(displayPanelGO);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[WireLanguageRow] DONE.");
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
