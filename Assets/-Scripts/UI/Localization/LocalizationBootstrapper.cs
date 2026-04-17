using System.Collections;
using UnityEngine;
using UnityEngine.Localization.Settings;

/// <summary>
/// Restores the persisted UI locale on startup.
/// Attach to the same GameObject as SettingsManager.
/// Must run AFTER SettingsManager.Awake() — set Script Execution Order accordingly.
/// </summary>
public class LocalizationBootstrapper : MonoBehaviour
{
    IEnumerator Start()
    {
        yield return LocalizationSettings.InitializationOperation;

        string saved = SettingsManager.Instance != null
            ? SettingsManager.Instance.UILanguageCode
            : PlayerPrefs.GetString(SettingsManager.KeyUILanguage, "en");

        var locale = LocalizationSettings.AvailableLocales.GetLocale(saved);
        if (locale != null)
            LocalizationSettings.SelectedLocale = locale;
    }
}
