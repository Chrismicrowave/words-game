using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class ClearSliderHandleRects
{
    public static void Run()
    {
        string[] paths = {
            "--- UI ---/Menus/SettingsPanel/Card/ContentArea/AudioPanel/MasterRow/MasterSlider",
            "--- UI ---/Menus/SettingsPanel/Card/ContentArea/AudioPanel/SFXRow/SFXSlider",
            "--- UI ---/Menus/SettingsPanel/Card/ContentArea/AudioPanel/BGMRow/BGMSlider"
        };
        foreach (var path in paths)
        {
            var go = GameObject.Find(path);
            if (go == null) { Debug.LogError("Not found: " + path); continue; }
            var slider = go.GetComponent<Slider>();
            if (slider == null) { Debug.LogError("No Slider on: " + path); continue; }
            slider.handleRect = null;
            EditorUtility.SetDirty(go);
            Debug.Log("Cleared handleRect: " + path);
        }
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
    }
}
