using System;
using UnityEngine;
using UnityEngine.Audio;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    [SerializeField] private AudioMixer mainMixer;

    public const string KeyMasterVolume   = "settings_masterVolume";
    public const string KeySFXVolume      = "settings_sfxVolume";
    public const string KeyBGMVolume      = "settings_bgmVolume";
    public const string KeyFullscreen     = "settings_fullscreen";
    public const string KeyResolution     = "settings_resolution";
    public const string KeyVSync          = "settings_vsync";
    public const string KeyCRTFilter      = "settings_crtFilter";
    public const string KeyScreenShake    = "settings_screenShake";
    public const string KeyQuality        = "settings_quality";
    public const string KeyActionPrompts  = "settings_actionPrompts";

    public float MasterVolume
    {
        get => PlayerPrefs.GetFloat(KeyMasterVolume, 1f);
        set { PlayerPrefs.SetFloat(KeyMasterVolume, value); ApplyAudio(); }
    }

    public float SFXVolume
    {
        get => PlayerPrefs.GetFloat(KeySFXVolume, 1f);
        set { PlayerPrefs.SetFloat(KeySFXVolume, value); ApplyAudio(); }
    }

    public float BGMVolume
    {
        get => PlayerPrefs.GetFloat(KeyBGMVolume, 1f);
        set { PlayerPrefs.SetFloat(KeyBGMVolume, value); ApplyAudio(); }
    }

    public bool Fullscreen
    {
        get => PlayerPrefs.GetInt(KeyFullscreen, 1) == 1;
        set { PlayerPrefs.SetInt(KeyFullscreen, value ? 1 : 0); ApplyDisplay(); }
    }

    public int ResolutionIndex
    {
        get => PlayerPrefs.GetInt(KeyResolution, -1);
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

    public event Action OnSettingsChanged;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        Load(); // Must be Start, not Awake — AudioMixer.SetFloat is ignored in Awake
    }

    public void Load()
    {
        ApplyAudio();
        ApplyDisplay();
        QualitySettings.SetQualityLevel(QualityLevel);
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
        PlayerPrefs.DeleteKey(KeyVSync);
        PlayerPrefs.DeleteKey(KeyCRTFilter);
        PlayerPrefs.DeleteKey(KeyScreenShake);
        PlayerPrefs.DeleteKey(KeyQuality);
        PlayerPrefs.DeleteKey(KeyActionPrompts);
        PlayerPrefs.Save();
        Load();
        OnSettingsChanged?.Invoke();
    }

    private void ApplyAudio()
    {
        if (mainMixer == null) return;

        float masterDb = MasterVolume > 0.001f ? Mathf.Log10(MasterVolume) * 20f : -80f;
        float sfxDb = SFXVolume > 0.001f ? Mathf.Log10(SFXVolume) * 20f : -80f;

        float bgmDb = BGMVolume > 0.001f ? Mathf.Log10(BGMVolume) * 20f : -80f;

        mainMixer.SetFloat("MasterVolume", masterDb);
        mainMixer.SetFloat("SFXVolume", sfxDb);
        mainMixer.SetFloat("BGMVolume", bgmDb);
    }

    private void ApplyDisplay()
    {
        Screen.fullScreen = Fullscreen;

        int resIdx = ResolutionIndex;
        if (resIdx >= 0 && resIdx < Screen.resolutions.Length)
        {
            Resolution res = Screen.resolutions[resIdx];
            Screen.SetResolution(res.width, res.height, Fullscreen);
        }
    }
}
