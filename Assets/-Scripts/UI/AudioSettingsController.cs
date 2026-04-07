using UnityEngine;
using UnityEngine.UI;

public class AudioSettingsController : MonoBehaviour
{
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider bgmSlider;

    // True until OnEnable finishes loading saved values — prevents slider Awake
    // from firing onValueChanged with Inspector defaults and overwriting prefs.
    private bool _initializing = true;

    void OnEnable()
    {
        _initializing = true;

        float master = SettingsManager.Instance != null ? SettingsManager.Instance.MasterVolume : PlayerPrefs.GetFloat(SettingsManager.KeyMasterVolume, 1f);
        float sfx    = SettingsManager.Instance != null ? SettingsManager.Instance.SFXVolume    : PlayerPrefs.GetFloat(SettingsManager.KeySFXVolume,    1f);
        float bgm    = SettingsManager.Instance != null ? SettingsManager.Instance.BGMVolume    : PlayerPrefs.GetFloat(SettingsManager.KeyBGMVolume,    1f);

        if (masterSlider != null) masterSlider.SetValueWithoutNotify(master);
        if (sfxSlider    != null) sfxSlider.SetValueWithoutNotify(sfx);
        if (bgmSlider    != null) bgmSlider.SetValueWithoutNotify(bgm);

        _initializing = false;
    }

    public void OnMasterChanged(float value)
    {
        if (_initializing) return;
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.MasterVolume = value;
        PlayerPrefs.Save();
    }

    public void OnSFXChanged(float value)
    {
        if (_initializing) return;
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.SFXVolume = value;
        PlayerPrefs.Save();
    }

    public void OnBGMChanged(float value)
    {
        if (_initializing) return;
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.BGMVolume = value;
        PlayerPrefs.Save();
    }
}
