using System;
using UnityEngine;
using UnityEngine.Audio;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    [SerializeField] private AudioMixer mainMixer;

    // Must match DisplaySettingsController.Resolutions order
    private static readonly (int w, int h)[] Resolutions =
    {
        (1280, 720),
        (1920, 1080),
        (2560, 1440),
    };

    public const string KeyMasterVolume  = "settings_masterVolume";
    public const string KeySFXVolume     = "settings_sfxVolume";
    public const string KeyBGMVolume     = "settings_bgmVolume";
    public const string KeyFullscreen    = "settings_fullscreen";
    public const string KeyResolution    = "settings_resolution";
    public const string KeyQuality       = "settings_quality";
    public const string KeyActionPrompts = "settings_actionPrompts";
    public const string KeyCRTFilter     = "settings_crtFilter";
    public const string KeyScreenShake   = "settings_screenShake";

    // Panel visibility keys (used by UIController + BuildDefaultsApplier)
    public const string KeyWordsPanelOn = "WordsPanelOn";
    public const string KeyTimerPanelOn = "TimerPanelOn";
    public const string KeyInfoPanelOn  = "InfoPanelOn";
    public const string KeyActiveTab    = "ActiveTab";

    public float MasterVolume
    {
        get => PlayerPrefs.GetFloat(KeyMasterVolume, 0.7f);
        set { PlayerPrefs.SetFloat(KeyMasterVolume, value); ApplyAudio(); }
    }

    public float SFXVolume
    {
        get => PlayerPrefs.GetFloat(KeySFXVolume, 0.7f);
        set { PlayerPrefs.SetFloat(KeySFXVolume, value); ApplyAudio(); }
    }

    public float BGMVolume
    {
        get => PlayerPrefs.GetFloat(KeyBGMVolume, 0.7f);
        set { PlayerPrefs.SetFloat(KeyBGMVolume, value); ApplyAudio(); }
    }

    public bool Fullscreen
    {
        get => PlayerPrefs.GetInt(KeyFullscreen, 1) == 1;
        set { PlayerPrefs.SetInt(KeyFullscreen, value ? 1 : 0); ApplyDisplay(); }
    }

    public int ResolutionIndex
    {
        get => PlayerPrefs.GetInt(KeyResolution, 1);
        set { PlayerPrefs.SetInt(KeyResolution, value); ApplyDisplay(); }
    }

    public int QualityLevel
    {
        get => PlayerPrefs.GetInt(KeyQuality, QualitySettings.GetQualityLevel());
        set { PlayerPrefs.SetInt(KeyQuality, value); QualitySettings.SetQualityLevel(value); }
    }

    public bool ShowActionPrompts
    {
        get => PlayerPrefs.GetInt(KeyActionPrompts, 1) == 1;
        set => PlayerPrefs.SetInt(KeyActionPrompts, value ? 1 : 0);
    }

    public bool CRTFilter
    {
        get => PlayerPrefs.GetInt(KeyCRTFilter, 1) == 1;
        set { PlayerPrefs.SetInt(KeyCRTFilter, value ? 1 : 0); ApplyCRT(); }
    }

    public bool ScreenShake
    {
        get => PlayerPrefs.GetInt(KeyScreenShake, 1) == 1;
        set => PlayerPrefs.SetInt(KeyScreenShake, value ? 1 : 0);
        
    }

    public event Action OnSettingsChanged;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        Load();
    }

    void Start() { }

    public void Load()
    {
        ApplyAudio();
        ApplyDisplay();
        QualitySettings.SetQualityLevel(QualityLevel);
        ApplyCRT();
    }

    public void Save()
    {
        PlayerPrefs.Save();
        OnSettingsChanged?.Invoke();
    }

    public void ResetToDefaults()
    {
        PlayerPrefs.DeleteKey(KeyMasterVolume);
        PlayerPrefs.DeleteKey(KeySFXVolume);
        PlayerPrefs.DeleteKey(KeyBGMVolume);
        PlayerPrefs.DeleteKey(KeyFullscreen);
        PlayerPrefs.DeleteKey(KeyResolution);
        PlayerPrefs.DeleteKey(KeyQuality);
        PlayerPrefs.DeleteKey(KeyActionPrompts);
        PlayerPrefs.DeleteKey(KeyCRTFilter);
        PlayerPrefs.DeleteKey(KeyScreenShake);
        PlayerPrefs.Save();
        Load();
        OnSettingsChanged?.Invoke();
    }

    private void ApplyAudio()
    {
        if (mainMixer == null) return;
        mainMixer.SetFloat("MasterVolume", ToDb(MasterVolume));
        mainMixer.SetFloat("SFXVolume",    ToDb(SFXVolume));
        mainMixer.SetFloat("BGMVolume",    ToDb(BGMVolume));
    }

    private void ApplyDisplay()
    {
        QualitySettings.vSyncCount = 0;
        int resIdx = Mathf.Clamp(ResolutionIndex, 0, Resolutions.Length - 1);
        var (w, h) = Resolutions[resIdx];

        FullScreenMode mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        Screen.SetResolution(w, h, mode);

    }
    
    private void ApplyCRT()
    {
        if (FilterManager.Instance != null)
            FilterManager.Instance.SetFilter(0, CRTFilter);
    }

 

    private static float ToDb(float linear) =>
        linear > 0.001f ? Mathf.Log10(linear) * 20f : -80f;
}
