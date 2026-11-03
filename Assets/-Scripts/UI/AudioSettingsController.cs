using UnityEngine;
using UnityEngine.UI;

public class AudioSettingsController : MonoBehaviour
{
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider bgmSlider;

    // Stays true until OnEnable finishes restoring saved values.
    // Blocks slider Awake from firing onValueChanged with Inspector defaults.
    private bool _initializing = true;

    void OnEnable()
    {
        _initializing = true;

        float master = Services.Get<SettingsManager>() != null
            ? Services.Get<SettingsManager>().MasterVolume
            : PlayerPrefs.GetFloat(SettingsManager.KeyMasterVolume, 1f);
        float sfx = Services.Get<SettingsManager>() != null
            ? Services.Get<SettingsManager>().SFXVolume
            : PlayerPrefs.GetFloat(SettingsManager.KeySFXVolume, 1f);
        float bgm = Services.Get<SettingsManager>() != null
            ? Services.Get<SettingsManager>().BGMVolume
            : PlayerPrefs.GetFloat(SettingsManager.KeyBGMVolume, 1f);

        if (masterSlider != null) masterSlider.SetValueWithoutNotify(master);
        if (sfxSlider    != null) sfxSlider.SetValueWithoutNotify(sfx);
        if (bgmSlider    != null) bgmSlider.SetValueWithoutNotify(bgm);

        _initializing = false;
    }

    public void OnMasterChanged(float value)
    {
        if (_initializing) return;
        if (Services.Get<SettingsManager>() != null) Services.Get<SettingsManager>().MasterVolume = value;
        else PlayerPrefs.SetFloat(SettingsManager.KeyMasterVolume, value);
        PlayerPrefs.Save();
    }

    public void OnSFXChanged(float value)
    {
        if (_initializing) return;
        if (Services.Get<SettingsManager>() != null) Services.Get<SettingsManager>().SFXVolume = value;
        else PlayerPrefs.SetFloat(SettingsManager.KeySFXVolume, value);
        PlayerPrefs.Save();
    }

    public void OnBGMChanged(float value)
    {
        if (_initializing) return;
        if (Services.Get<SettingsManager>() != null) Services.Get<SettingsManager>().BGMVolume = value;
        else PlayerPrefs.SetFloat(SettingsManager.KeyBGMVolume, value);
        PlayerPrefs.Save();
    }
}
