using UnityEngine;

/// <summary>
/// Writes build defaults to PlayerPrefs on first launch (keys that don't exist yet).
/// Runs before SettingsManager so its Awake picks up the correct values.
/// Tweak all defaults here in the Inspector.
/// </summary>
[DefaultExecutionOrder(-50)]
public class BuildDefaultsApplier : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private bool defaultFullscreen = false;
    [SerializeField] private int  defaultResolutionIndex = 1; // 0=1280x720  1=1920x1080  2=2560x1440

    [Header("Visual Toggles")]
    [SerializeField] private bool defaultCRT         = true;
    [SerializeField] private bool defaultScreenShake = true;
    [SerializeField] private bool defaultActionPrompts = true;

    [Header("Audio (0–1)")]
    [SerializeField] private float defaultMasterVolume = 0.7f;
    [SerializeField] private float defaultSFXVolume    = 0.7f;
    [SerializeField] private float defaultBGMVolume    = 0.7f;

    public enum DefaultTab { Daily, MyList }

    [Header("UI Panels")]
    [SerializeField] private bool       defaultWordsPanel = false;
    [SerializeField] private bool       defaultTimerPanel = false;
    [SerializeField] private bool       defaultInfoPanel  = true;
    [SerializeField] private DefaultTab defaultTab        = DefaultTab.Daily;

    void Awake()
    {
        SetInt(SettingsManager.KeyFullscreen,    defaultFullscreen    ? 1 : 0);
        SetInt(SettingsManager.KeyResolution,    defaultResolutionIndex);
        SetInt(SettingsManager.KeyCRTFilter,     defaultCRT           ? 1 : 0);
        SetInt(SettingsManager.KeyScreenShake,   defaultScreenShake   ? 1 : 0);
        SetInt(SettingsManager.KeyActionPrompts, defaultActionPrompts ? 1 : 0);
        SetFloat(SettingsManager.KeyMasterVolume, defaultMasterVolume);
        SetFloat(SettingsManager.KeySFXVolume,    defaultSFXVolume);
        SetFloat(SettingsManager.KeyBGMVolume,    defaultBGMVolume);
        SetInt("WordsPanelOn", defaultWordsPanel ? 1 : 0);
        SetInt("TimerPanelOn", defaultTimerPanel ? 1 : 0);
        SetInt("InfoPanelOn",  defaultInfoPanel  ? 1 : 0);
        SetStr("ActiveTab",    defaultTab == DefaultTab.Daily ? "daily" : "mylist");
    }

    private void SetInt(string key, int value)
    {
        if (!PlayerPrefs.HasKey(key)) PlayerPrefs.SetInt(key, value);
    }

    private void SetFloat(string key, float value)
    {
        if (!PlayerPrefs.HasKey(key)) PlayerPrefs.SetFloat(key, value);
    }

    private void SetStr(string key, string value)
    {
        if (!PlayerPrefs.HasKey(key)) PlayerPrefs.SetString(key, value);
    }
}
