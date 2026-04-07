using UnityEngine;
using UnityEngine.UI;

public class AudioSettingsController : MonoBehaviour
{
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider bgmSlider;

    void OnEnable()
    {
        float master = SettingsManager.Instance != null ? SettingsManager.Instance.MasterVolume : PlayerPrefs.GetFloat(SettingsManager.KeyMasterVolume, 1f);
        float sfx    = SettingsManager.Instance != null ? SettingsManager.Instance.SFXVolume    : PlayerPrefs.GetFloat(SettingsManager.KeySFXVolume,    1f);
        float bgm    = SettingsManager.Instance != null ? SettingsManager.Instance.BGMVolume    : PlayerPrefs.GetFloat(SettingsManager.KeyBGMVolume,    1f);

        if (masterSlider != null) masterSlider.SetValueWithoutNotify(master);
        if (sfxSlider    != null) sfxSlider.SetValueWithoutNotify(sfx);
        if (bgmSlider    != null) bgmSlider.SetValueWithoutNotify(bgm);
    }

    public void OnMasterChanged(float value)
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.MasterVolume = value;
        PlayerPrefs.Save();
    }

    public void OnSFXChanged(float value)
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.SFXVolume = value;
        PlayerPrefs.Save();
    }

    public void OnBGMChanged(float value)
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.BGMVolume = value;
        PlayerPrefs.Save();
    }
}
