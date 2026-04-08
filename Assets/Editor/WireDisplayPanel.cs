using UnityEngine;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using System.Reflection;

public class WireDisplayPanel
{
    public static void Execute()
    {
        const string panelPath = "--- UI ---/Menus/SettingsPanel/Card/ContentArea/DisplayPanel";

        // Find panel
        GameObject panel = GameObject.Find(panelPath);
        if (panel == null)
        {
            Debug.LogError("[WireDisplayPanel] Could not find DisplayPanel at: " + panelPath);
            return;
        }

        // Find UI children
        Transform fsToggleTr     = panel.transform.Find("FullscreenToggle");
        Transform vsToggleTr     = panel.transform.Find("VSyncToggle");
        Transform crtToggleTr    = panel.transform.Find("CRTToggle");
        Transform prevBtnTr      = panel.transform.Find("PrevResolutionBtn");
        Transform nextBtnTr      = panel.transform.Find("NextResolutionBtn");

        if (fsToggleTr == null || vsToggleTr == null || crtToggleTr == null ||
            prevBtnTr == null || nextBtnTr == null)
        {
            Debug.LogError("[WireDisplayPanel] One or more UI children not found.");
            return;
        }

        // Create resolution label (TMP)
        GameObject labelGO = new GameObject("ResolutionLabel");
        labelGO.transform.SetParent(panel.transform, false);
        TextMeshProUGUI tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = "1920 x 1080";
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 24;
        RectTransform labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.sizeDelta = new Vector2(200f, 40f);

        // Get component
        DisplaySettingsController controller = panel.GetComponent<DisplaySettingsController>();
        if (controller == null)
        {
            Debug.LogError("[WireDisplayPanel] DisplaySettingsController not found on panel.");
            return;
        }

        // Wire serialized fields via SerializedObject
        SerializedObject so = new SerializedObject(controller);
        so.FindProperty("fullscreenToggle").objectReferenceValue = fsToggleTr.GetComponent<Toggle>();
        so.FindProperty("vsyncToggle").objectReferenceValue      = vsToggleTr.GetComponent<Toggle>();
        so.FindProperty("crtToggle").objectReferenceValue        = crtToggleTr.GetComponent<Toggle>();
        so.FindProperty("resolutionLabel").objectReferenceValue  = tmp;
        so.ApplyModifiedProperties();

        Debug.Log("[WireDisplayPanel] Serialized fields wired successfully.");

        // Wire Toggle onValueChanged events
        Toggle fsTog  = fsToggleTr.GetComponent<Toggle>();
        Toggle vsTog  = vsToggleTr.GetComponent<Toggle>();
        Toggle crtTog = crtToggleTr.GetComponent<Toggle>();

        WireToggle(fsTog,  controller, "OnFullscreenChanged");
        WireToggle(vsTog,  controller, "OnVSyncChanged");
        WireToggle(crtTog, controller, "OnCRTChanged");

        // Wire Button onClick events
        Button prevBtn = prevBtnTr.GetComponent<Button>();
        Button nextBtn = nextBtnTr.GetComponent<Button>();

        WireButton(prevBtn, controller, "OnPrevResolution");
        WireButton(nextBtn, controller, "OnNextResolution");

        // Set toggle label text (the Label child of each toggle)
        SetToggleLabel(fsToggleTr,  "Fullscreen");
        SetToggleLabel(vsToggleTr,  "VSync");
        SetToggleLabel(crtToggleTr, "CRT Filter");

        // Set button label text
        SetButtonLabel(prevBtnTr, "<");
        SetButtonLabel(nextBtnTr, ">");

        // Mark scene dirty and save
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

        Debug.Log("[WireDisplayPanel] Done — DisplayPanel wired and scene saved.");
    }

    private static void WireToggle(Toggle tog, DisplaySettingsController target, string methodName)
    {
        if (tog == null) return;
        // Get the MethodInfo for the bool-parameter method
        MethodInfo mi = typeof(DisplaySettingsController).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public);
        if (mi == null) { Debug.LogError("[WireDisplayPanel] Method not found: " + methodName); return; }
        var action = (UnityAction<bool>)System.Delegate.CreateDelegate(typeof(UnityAction<bool>), target, mi);
        UnityEventTools.AddBoolPersistentListener(tog.onValueChanged, action, false);
    }

    private static void WireButton(Button btn, DisplaySettingsController target, string methodName)
    {
        if (btn == null) return;
        MethodInfo mi = typeof(DisplaySettingsController).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public);
        if (mi == null) { Debug.LogError("[WireDisplayPanel] Method not found: " + methodName); return; }
        var action = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), target, mi);
        UnityEventTools.AddPersistentListener(btn.onClick, action);
    }

    private static void SetToggleLabel(Transform toggleTr, string labelText)
    {
        if (toggleTr == null) return;
        Transform labelTr = toggleTr.Find("Label");
        if (labelTr == null) return;
        // Replace legacy Text with TMP if needed
        Text legacyText = labelTr.GetComponent<Text>();
        if (legacyText != null)
        {
            string existing = legacyText.text;
            Object.DestroyImmediate(legacyText);
            TextMeshProUGUI tmp = labelTr.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = labelText;
            tmp.fontSize = 18;
            tmp.color = Color.white;
        }
        else
        {
            TextMeshProUGUI tmp = labelTr.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = labelText;
        }
    }

    private static void SetButtonLabel(Transform btnTr, string labelText)
    {
        if (btnTr == null) return;
        // Default button has a "Text (TMP)" child or "Text" child
        Transform textTr = btnTr.Find("Text (TMP)");
        if (textTr == null) textTr = btnTr.Find("Text");
        if (textTr == null) return;
        Text legacyText = textTr.GetComponent<Text>();
        if (legacyText != null)
        {
            Object.DestroyImmediate(legacyText);
            TextMeshProUGUI tmp = textTr.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = labelText;
            tmp.fontSize = 18;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
        }
        else
        {
            TextMeshProUGUI tmp = textTr.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = labelText;
        }
    }
}
