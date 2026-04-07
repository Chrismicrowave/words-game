using System;
using UnityEngine;
using UnityEngine.Audio;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    [SerializeField] private AudioMixer mainMixer;

    public const string KeyMasterVolume = "MasterVolume";
    public const string KeySFXVolume    = "SFXVolume";
    public const string KeyBGMVolume    = "BGMVolume";
    public const string KeyFullscreen   = "Fullscreen";
    public const string KeyResolution   = "Resolution";
    public const string KeyVSync        = "VSync";
    public const string KeyCRTFilter    = "CRTFilter";
    public const string KeyScreenShake  = "ScreenShake";

    private const string KEY_MASTER_VOL = "settings_masterVolume";
    private const string KEY_SFX_VOL = "settings_sfxVolume";
    private const string KEY_FULLSCREEN = "settings_fullscreen";
    private const string KEY_RESOLUTION = "settings_resolution";
    private const string KEY_QUALITY = "settings_quality";
    private const string KEY_ACTION_PROMPTS = "settings_actionPrompts";

    public float MasterVolume
    {
        get => PlayerPrefs.GetFloat(KEY_MASTER_VOL, 1f);
        set { PlayerPrefs.SetFloat(KEY_MASTER_VOL, value); ApplyAudio(); }
    }

    public float SFXVolume
    {
        get => PlayerPrefs.GetFloat(KEY_SFX_VOL, 1f);
        set { PlayerPrefs.SetFloat(KEY_SFX_VOL, value); ApplyAudio(); }
    }

    public bool Fullscreen
    {
        get => PlayerPrefs.GetInt(KEY_FULLSCREEN, 1) == 1;
        set { PlayerPrefs.SetInt(KEY_FULLSCREEN, value ? 1 : 0); ApplyDisplay(); }
    }

    public int ResolutionIndex
    {
        get => PlayerPrefs.GetInt(KEY_RESOLUTION, -1);
        set { PlayerPrefs.SetInt(KEY_RESOLUTION, value); ApplyDisplay(); }
    }

    public int QualityLevel
    {
        get => PlayerPrefs.GetInt(KEY_QUALITY, QualitySettings.GetQualityLevel());
        set { PlayerPrefs.SetInt(KEY_QUALITY, value); QualitySettings.SetQualityLevel(value); }
    }

    public bool ShowActionPrompts
    {
        get => PlayerPrefs.GetInt(KEY_ACTION_PROMPTS, 1) == 1;
        set => PlayerPrefs.SetInt(KEY_ACTION_PROMPTS, value ? 1 : 0);
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

        Load();
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

    public void ResetDefaults()
    {
        PlayerPrefs.DeleteKey(KEY_MASTER_VOL);
        PlayerPrefs.DeleteKey(KEY_SFX_VOL);
        PlayerPrefs.DeleteKey(KEY_FULLSCREEN);
        PlayerPrefs.DeleteKey(KEY_RESOLUTION);
        PlayerPrefs.DeleteKey(KEY_QUALITY);
        PlayerPrefs.DeleteKey(KEY_ACTION_PROMPTS);
        Load();
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
        PlayerPrefs.Save();
    }

    private void ApplyAudio()
    {
        if (mainMixer == null) return;

        float masterDb = MasterVolume > 0.001f ? Mathf.Log10(MasterVolume) * 20f : -80f;
        float sfxDb = SFXVolume > 0.001f ? Mathf.Log10(SFXVolume) * 20f : -80f;

        mainMixer.SetFloat("MasterVolume", masterDb);
        mainMixer.SetFloat("SFXVolume", sfxDb);
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
