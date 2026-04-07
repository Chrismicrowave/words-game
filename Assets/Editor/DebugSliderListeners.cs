using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class DebugSliderListeners
{
    public static void Execute()
    {
        string[] sliderPaths = new[]
        {
            "--- UI ---/Menus/SettingsPanel/Card/ContentArea/AudioPanel/MasterRow/MasterSlider",
            "--- UI ---/Menus/SettingsPanel/Card/ContentArea/AudioPanel/SFXRow/SFXSlider",
            "--- UI ---/Menus/SettingsPanel/Card/ContentArea/AudioPanel/BGMRow/BGMSlider"
        };

        foreach (var path in sliderPaths)
        {
            var go = GameObject.Find(path);
            if (go == null)
            {
                // Try including inactive
                var all = Resources.FindObjectsOfTypeAll<Slider>();
                foreach (var s in all)
                    if (s.gameObject.name == path.Substring(path.LastIndexOf('/') + 1))
                        { go = s.gameObject; break; }
            }

            if (go == null) { Debug.Log($"[SliderDebug] NOT FOUND: {path}"); continue; }

            var slider = go.GetComponent<Slider>();
            if (slider == null) { Debug.Log($"[SliderDebug] No Slider on {path}"); continue; }

            int count = slider.onValueChanged.GetPersistentEventCount();
            Debug.Log($"[SliderDebug] {go.name} | value={slider.value:F3} | persistent listeners={count}");
            for (int i = 0; i < count; i++)
            {
                Debug.Log($"  [{i}] target={slider.onValueChanged.GetPersistentTarget(i)} method={slider.onValueChanged.GetPersistentMethodName(i)}");
            }
        }

        Debug.Log($"[PrefsDebug] Master={PlayerPrefs.GetFloat("settings_masterVolume", -1f):F3}");
        Debug.Log($"[PrefsDebug] SFX={PlayerPrefs.GetFloat("settings_sfxVolume", -1f):F3}");
        Debug.Log($"[PrefsDebug] BGM={PlayerPrefs.GetFloat("settings_bgmVolume", -1f):F3}");
    }
}
