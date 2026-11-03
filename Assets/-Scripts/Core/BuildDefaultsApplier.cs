using UnityEngine;

/// <summary>
/// Writes build defaults to PlayerPrefs on first launch (keys that don't exist yet).
/// Runs before SettingsManager so its Awake picks up the correct values.
/// Tweak all defaults here in the Inspector.
/// </summary>
[DefaultExecutionOrder(-50)]
public class BuildDefaultsApplier : MonoBehaviour
{
    [Header("Master Config")]
    [SerializeField] private GameConfig config;

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
        GameConfigProfile profile = config != null ? config.activeProfile : null;
        if (profile != null)
        {
            if (profile.resetLocalDataOnFirstRun)
            {
                GameResetResult resetResult = GameDataResetService.ApplyFirstRunReset(profile);
                if (!resetResult.Succeeded)
                    Debug.LogError("[BuildDefaultsApplier] The configured first-run local reset failed: " + resetResult.Message);
            }
        }

        ApplyMissingDefaults();
    }

    /// <summary>
    /// Fills only missing preference keys from the active profile (or legacy Inspector
    /// defaults when no profile is assigned). This is also used after an explicit
    /// settings reset; it never invokes the optional first-run data reset.
    /// </summary>
    public void ApplyMissingDefaults()
    {
        GameConfigProfile profile = config != null ? config.activeProfile : null;
        if (profile != null)
            ApplyProfileDefaults(profile.defaults ?? new GameDefaultSettings());
        else
            ApplyLegacyDefaults();

        PlayerPrefs.Save();
    }

    private void ApplyLegacyDefaults()
    {
        SetInt(SettingsManager.KeyFullscreen,    defaultFullscreen    ? 1 : 0);
        SetInt(SettingsManager.KeyResolution,    defaultResolutionIndex);
        SetInt(SettingsManager.KeyCRTFilter,     defaultCRT           ? 1 : 0);
        SetInt(SettingsManager.KeyScreenShake,   defaultScreenShake   ? 1 : 0);
        SetInt(SettingsManager.KeyActionPrompts, defaultActionPrompts ? 1 : 0);
        SetFloat(SettingsManager.KeyMasterVolume, defaultMasterVolume);
        SetFloat(SettingsManager.KeySFXVolume,    defaultSFXVolume);
        SetFloat(SettingsManager.KeyBGMVolume,    defaultBGMVolume);
        SetInt(SettingsManager.KeyWordsPanelOn, defaultWordsPanel ? 1 : 0);
        SetInt(SettingsManager.KeyTimerPanelOn, defaultTimerPanel ? 1 : 0);
        SetInt(SettingsManager.KeyInfoPanelOn,  defaultInfoPanel  ? 1 : 0);
        SetStr(SettingsManager.KeyActiveTab,    defaultTab == DefaultTab.Daily ? "daily" : "mylist");
    }

    private void ApplyProfileDefaults(GameDefaultSettings defaults)
    {
        SetInt(SettingsManager.KeyFullscreen, defaults.fullscreen ? 1 : 0);
        SetInt(SettingsManager.KeyResolution, defaults.resolutionIndex);
        SetInt(SettingsManager.KeyCRTFilter, defaults.crt ? 1 : 0);
        SetInt(SettingsManager.KeyScreenShake, defaults.screenShake ? 1 : 0);
        SetInt(SettingsManager.KeyActionPrompts, defaults.actionPrompts ? 1 : 0);
        SetFloat(SettingsManager.KeyMasterVolume, defaults.masterVolume);
        SetFloat(SettingsManager.KeySFXVolume, defaults.sfxVolume);
        SetFloat(SettingsManager.KeyBGMVolume, defaults.bgmVolume);
        SetInt(SettingsManager.KeyShowPinyin, defaults.showPinyin ? 1 : 0);
        string language = string.IsNullOrWhiteSpace(defaults.uiLanguage) ? "en" : defaults.uiLanguage;
        string playerName = string.IsNullOrWhiteSpace(defaults.playerName)
            ? SettingsManager.DefaultPlayerName
            : defaults.playerName;
        SetStr(SettingsManager.KeyUILanguage, language);
        SetStr(SettingsManager.KeyPlayerName, playerName);
        SetInt(SettingsManager.KeyWordsPanelOn, defaults.wordsPanel ? 1 : 0);
        SetInt(SettingsManager.KeyTimerPanelOn, defaults.timerPanel ? 1 : 0);
        SetInt(SettingsManager.KeyInfoPanelOn, defaults.infoPanel ? 1 : 0);
        string initialTab = defaults.initialChallengeTab == InitialChallengeTab.Custom
            ? "Custom"
            : "Challenges";
        SetStr("LevelPanel_LastTab", initialTab);
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
