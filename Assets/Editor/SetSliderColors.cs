using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor.SceneManagement;

public class SetSliderColors
{
    public static void Run()
    {
        string[] sliderPaths = {
            "--- UI ---/Menus/SettingsPanel/Card/ContentArea/AudioPanel/MasterRow/MasterSlider",
            "--- UI ---/Menus/SettingsPanel/Card/ContentArea/AudioPanel/SFXRow/SFXSlider",
            "--- UI ---/Menus/SettingsPanel/Card/ContentArea/AudioPanel/BGMRow/BGMSlider"
        };

        Color orange = new Color(1f, 0.5f, 0f, 1f);
        Color darkGrey = new Color(0.2f, 0.2f, 0.2f, 1f);

        foreach (var path in sliderPaths)
        {
            var go = GameObject.Find(path);
            if (go == null) { Debug.LogError("Not found: " + path); continue; }

            // Background
            var bg = go.transform.Find("Background");
            if (bg != null)
            {
                var img = bg.GetComponent<Image>();
                if (img != null) { img.color = darkGrey; EditorUtility.SetDirty(img); }
            }

            // Fill
            var fill = go.transform.Find("Fill Area/Fill");
            if (fill != null)
            {
                var img = fill.GetComponent<Image>();
                if (img != null) { img.color = orange; EditorUtility.SetDirty(img); }
            }

            // Handle (if exists)
            var handle = go.transform.Find("Handle Slide Area/Handle");
            if (handle != null)
            {
                var img = handle.GetComponent<Image>();
                if (img != null) { img.color = orange; EditorUtility.SetDirty(img); }
            }
        }

        EditorSceneManager.SaveOpenScenes();
        Debug.Log("Slider colors set.");
    }
}
