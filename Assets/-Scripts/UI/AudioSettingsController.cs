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

        float master = SettingsManager.Instance != null
            ? SettingsManager.Instance.MasterVolume
            : PlayerPrefs.GetFloat(SettingsManager.KeyMasterVolume, 1f);
        float sfx = SettingsManager.Instance != null
            ? SettingsManager.Instance.SFXVolume
            : PlayerPrefs.GetFloat(SettingsManager.KeySFXVolume, 1f);
        float bgm = SettingsManager.Instance != null
            ? SettingsManager.Instance.BGMVolume
            : PlayerPrefs.GetFloat(SettingsManager.KeyBGMVolume, 1f);

        if (masterSlider != null) masterSlider.SetValueWithoutNotify(master);
        if (sfxSlider    != null) sfxSlider.SetValueWithoutNotify(sfx);
        if (bgmSlider    != null) bgmSlider.SetValueWithoutNotify(bgm);

        _initializing = false;
    }

    public void OnMasterChanged(float value)
    {
        if (_initializing) return;
        if (SettingsManager.Instance != null) SettingsManager.Instance.MasterVolume = value;
        else PlayerPrefs.SetFloat(SettingsManager.KeyMasterVolume, value);
        PlayerPrefs.Save();
    }

    public void OnSFXChanged(float value)
    {
        if (_initializing) return;
        if (SettingsManager.Instance != null) SettingsManager.Instance.SFXVolume = value;
        else PlayerPrefs.SetFloat(SettingsManager.KeySFXVolume, value);
        PlayerPrefs.Save();
    }

    public void OnBGMChanged(float value)
    {
        if (_initializing) return;
        if (SettingsManager.Instance != null) SettingsManager.Instance.BGMVolume = value;
        else PlayerPrefs.SetFloat(SettingsManager.KeyBGMVolume, value);
        PlayerPrefs.Save();
    }
}
