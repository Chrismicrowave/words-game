using UnityEngine;
using UnityEngine.UI;

public class AudioSettingsController : MonoBehaviour
{
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider bgmSlider;

    void OnEnable()
    {
        // Read from SettingsManager properties so keys are consistent
        if (masterSlider != null)
            masterSlider.value = SettingsManager.Instance != null
                ? SettingsManager.Instance.MasterVolume
                : PlayerPrefs.GetFloat(SettingsManager.KeyMasterVolume, 1f);
        if (sfxSlider != null)
            sfxSlider.value = SettingsManager.Instance != null
                ? SettingsManager.Instance.SFXVolume
                : PlayerPrefs.GetFloat(SettingsManager.KeySFXVolume, 1f);
        if (bgmSlider != null)
            bgmSlider.value = SettingsManager.Instance != null
                ? SettingsManager.Instance.BGMVolume
                : PlayerPrefs.GetFloat(SettingsManager.KeyBGMVolume, 1f);
    }

    public void OnMasterChanged(float value)
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.MasterVolume = value;
    }

    public void OnSFXChanged(float value)
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.SFXVolume = value;
    }

    public void OnBGMChanged(float value)
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.BGMVolume = value;
    }
}
