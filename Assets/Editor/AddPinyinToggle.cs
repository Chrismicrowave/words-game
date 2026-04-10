using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Adds a "Show Pinyin" toggle row to the Display settings panel
/// and wires it to DisplaySettingsController.
/// </summary>
public class AddPinyinToggle
{
    public static void Execute()
    {
        string displayPanelPath = "--- UI ---/Menus/SettingsPanel/Card/ContentArea/-DisplayPanel";
        string screenShakeRowPath = displayPanelPath + "/ScreenShakeRow";

        GameObject displayPanel   = GameObject.Find(displayPanelPath);
        GameObject screenShakeRow = GameObject.Find(screenShakeRowPath);

        if (displayPanel == null)   { Debug.LogError("[AddPinyinToggle] DisplayPanel not found."); return; }
        if (screenShakeRow == null) { Debug.LogError("[AddPinyinToggle] ScreenShakeRow not found."); return; }

        // Duplicate ScreenShakeRow as template
        GameObject pinyinRow = Object.Instantiate(screenShakeRow, displayPanel.transform);
        pinyinRow.name = "ShowPinyinRow";

        // Set sibling index right after ScreenShakeRow
        pinyinRow.transform.SetSiblingIndex(screenShakeRow.transform.GetSiblingIndex() + 1);

        // Update label text
        var label = pinyinRow.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
        if (label != null) label.text = "Show Pinyin Hints";

        // Rename and find the toggle
        var toggleInRow = pinyinRow.GetComponentInChildren<Toggle>(true);
        if (toggleInRow != null)
        {
            toggleInRow.gameObject.name = "ShowPinyinToggle";
            // Set default to on (matches ShowPinyin default = true)
            toggleInRow.isOn = true;
        }

        // Wire DisplaySettingsController
        DisplaySettingsController dsc = Object.FindFirstObjectByType<DisplaySettingsController>(FindObjectsInactive.Include);
        if (dsc != null)
        {
            var so = new SerializedObject(dsc);
            so.FindProperty("showPinyinToggle").objectReferenceValue = toggleInRow;
            so.ApplyModifiedProperties();
            Debug.Log("[AddPinyinToggle] DisplaySettingsController.showPinyinToggle wired.");

            // Wire toggle onClick → OnShowPinyinChanged
            if (toggleInRow != null)
            {
                toggleInRow.onValueChanged.RemoveAllListeners();
                var entry = new UnityEngine.Events.UnityAction<bool>(dsc.OnShowPinyinChanged);
                toggleInRow.onValueChanged.AddListener(entry);
                // Persist via persistent listener
                UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(
                    toggleInRow.onValueChanged,
                    dsc.OnShowPinyinChanged,
                    true
                );
            }
        }
        else Debug.LogWarning("[AddPinyinToggle] DisplaySettingsController not found.");

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[AddPinyinToggle] ShowPinyinRow added to DisplayPanel.");
    }
}
