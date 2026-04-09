using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DisplaySettingsController : MonoBehaviour
{
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Toggle crtToggle;
    [SerializeField] private TextMeshProUGUI resolutionLabel;
    [SerializeField] private Toggle screenshakeToggle;

    private static readonly (int w, int h)[] Resolutions =
    {
        (1280, 720),
        (1920, 1080),
        (2560, 1440),
    };

    private int _resIndex;
    private bool _initializing;

    void OnEnable()
    {
        //_initializing = true;

        //bool fs = SettingsManager.Instance != null
        //    ? SettingsManager.Instance.Fullscreen
        //    : PlayerPrefs.GetInt(SettingsManager.KeyFullscreen, 1) == 1;
        //bool crt = SettingsManager.Instance != null
        //    ? SettingsManager.Instance.CRTFilter
        //    : PlayerPrefs.GetInt(SettingsManager.KeyCRTFilter, 1) == 1;

        //_resIndex = SettingsManager.Instance != null
        //    ? SettingsManager.Instance.ResolutionIndex
        //    : PlayerPrefs.GetInt(SettingsManager.KeyResolution, 1);
        //if (_resIndex < 0 || _resIndex >= Resolutions.Length) _resIndex = 1;

        //if (fullscreenToggle != null) fullscreenToggle.SetIsOnWithoutNotify(fs);
        //if (crtToggle        != null) crtToggle.SetIsOnWithoutNotify(crt);
        //RefreshResolutionLabel();

        //_initializing = false; 
        //above script - resolution and crt turning back on not working, fixed by script below by unsbuscribing

        _initializing = true;

        fullscreenToggle.onValueChanged.RemoveListener(OnFullscreenChanged);
        crtToggle.onValueChanged.RemoveListener(OnCRTChanged);

        bool fs = SettingsManager.Instance != null
            ? SettingsManager.Instance.Fullscreen
            : PlayerPrefs.GetInt(SettingsManager.KeyFullscreen, 1) == 1;

        bool crt = SettingsManager.Instance != null
            ? SettingsManager.Instance.CRTFilter
            : PlayerPrefs.GetInt(SettingsManager.KeyCRTFilter, 1) == 1;

        bool ss = SettingsManager.Instance != null
         ? SettingsManager.Instance.ScreenShake
         : PlayerPrefs.GetInt(SettingsManager.KeyScreenShake, 1) == 1;

        _resIndex = SettingsManager.Instance != null
            ? SettingsManager.Instance.ResolutionIndex
            : PlayerPrefs.GetInt(SettingsManager.KeyResolution, 1);

        fullscreenToggle.isOn = fs;
        crtToggle.isOn = crt;
        screenshakeToggle.isOn = ss;

        RefreshResolutionLabel();

        fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
        crtToggle.onValueChanged.AddListener(OnCRTChanged);
        screenshakeToggle.onValueChanged.AddListener(OnScreenShakeChanged);

        _initializing = false;
    }

    public void OnFullscreenChanged(bool value)
    {
        if (_initializing) return;

        if (SettingsManager.Instance != null) SettingsManager.Instance.Fullscreen = value;
        else { PlayerPrefs.SetInt(SettingsManager.KeyFullscreen, value ? 1 : 0); Screen.fullScreen = value; }

        PlayerPrefs.Save();
    }

    public void OnCRTChanged(bool value)
    {
        if (_initializing) return;

        if (SettingsManager.Instance != null) SettingsManager.Instance.CRTFilter = value;
        else PlayerPrefs.SetInt(SettingsManager.KeyCRTFilter, value ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void OnScreenShakeChanged(bool value)
    {
        if (_initializing) return;

        if (SettingsManager.Instance != null) SettingsManager.Instance.ScreenShake = value;
        else PlayerPrefs.SetInt(SettingsManager.KeyScreenShake, value ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void OnPrevResolution()
    {
        _resIndex = (_resIndex - 1 + Resolutions.Length) % Resolutions.Length;
        ApplyResolution();
    }

    public void OnNextResolution()
    {
        _resIndex = (_resIndex + 1) % Resolutions.Length;
        ApplyResolution();
    }

    private void ApplyResolution()
    {
        if (SettingsManager.Instance != null) SettingsManager.Instance.ResolutionIndex = _resIndex;
        else PlayerPrefs.SetInt(SettingsManager.KeyResolution, _resIndex);
        var (w, h) = Resolutions[_resIndex];
        bool fs = SettingsManager.Instance != null ? SettingsManager.Instance.Fullscreen : Screen.fullScreen;

        Screen.SetResolution(w, h, fs); 
        
        RefreshResolutionLabel();
        PlayerPrefs.Save();
    }

    private void RefreshResolutionLabel()
    {
        if (resolutionLabel == null) return;
        var (w, h) = Resolutions[Mathf.Clamp(_resIndex, 0, Resolutions.Length - 1)];
        resolutionLabel.text = $"{w} x {h}";
    }
}
