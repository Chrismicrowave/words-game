using UnityEngine;
using UnityEngine.UI;

public class AudioSettingsController : MonoBehaviour
{
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider bgmSlider;

    void OnEnable()
    {
        if (masterSlider != null)
            masterSlider.value = PlayerPrefs.GetFloat(SettingsManager.KeyMasterVolume, 1f);
        if (sfxSlider != null)
            sfxSlider.value    = PlayerPrefs.GetFloat(SettingsManager.KeySFXVolume,    1f);
        if (bgmSlider != null)
            bgmSlider.value    = PlayerPrefs.GetFloat(SettingsManager.KeyBGMVolume,    1f);
    }

    public void OnMasterChanged(float value)
    {
        PlayerPrefs.SetFloat(SettingsManager.KeyMasterVolume, value);
        if (AudioManager.Instance != null) AudioManager.Instance.SetMasterVolume(value);
    }

    public void OnSFXChanged(float value)
    {
        PlayerPrefs.SetFloat(SettingsManager.KeySFXVolume, value);
        if (AudioManager.Instance != null) AudioManager.Instance.SetSFXVolume(value);
    }

    public void OnBGMChanged(float value)
    {
        PlayerPrefs.SetFloat(SettingsManager.KeyBGMVolume, value);
        if (AudioManager.Instance != null) AudioManager.Instance.SetBGMVolume(value);
    }
}
