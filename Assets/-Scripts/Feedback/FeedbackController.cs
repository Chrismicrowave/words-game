// Assets/-Scripts/Feedback/FeedbackController.cs
using UnityEngine;

public class FeedbackController : MonoBehaviour
{
    [SerializeField] private CameraShakeAndZoom cameraShake;
    [SerializeField] private KeyboardShake keyboardShake;
    [SerializeField] private AudioManager audioKeys;
    [SerializeField] private AudioManager audioResult;

    void OnEnable()
    {
        if (Services.Get<GameStateManager>() != null)
        {
            Services.Get<GameStateManager>().OnStepProcessed += HandleStepProcessed;
            Services.Get<GameStateManager>().OnPhaseStarted += HandlePhaseStarted;
            Services.Get<GameStateManager>().OnPhaseCompleted += HandlePhaseCompleted;
            Services.Get<GameStateManager>().OnPhaseRestarted += HandleRestart;
            Services.Get<GameStateManager>().OnGameReset += HandleRestart;
        }
    }

    void OnDisable()
    {
        if (Services.Get<GameStateManager>() != null)
        {
            Services.Get<GameStateManager>().OnStepProcessed -= HandleStepProcessed;
            Services.Get<GameStateManager>().OnPhaseStarted -= HandlePhaseStarted;
            Services.Get<GameStateManager>().OnPhaseCompleted -= HandlePhaseCompleted;
            Services.Get<GameStateManager>().OnPhaseRestarted -= HandleRestart;
            Services.Get<GameStateManager>().OnGameReset -= HandleRestart;
        }
    }

    void Start()
    {
        // Re-subscribe in case OnEnable ran before GameStateManager.Awake
        OnDisable();
        OnEnable();
    }

    private void HandleStepProcessed(StepResult result, Step step)
    {
        switch (result)
        {
            case StepResult.Correct:
                if (step.Action == StepAction.Hold)
                    OnCorrectHold();
                else
                    OnCorrectRelease();
                break;
            case StepResult.PhaseComplete:
                if (step.Action == StepAction.Hold)
                    OnCorrectHold();
                else
                    OnCorrectRelease();
                break;
            case StepResult.Failed:
                OnFailed();
                break;
        }
    }

    private void HandlePhaseStarted()
    {
        if (Services.Get<SettingsManager>().ScreenShake)
        { 
            cameraShake.ResetFOV(); 
        }
    }

    private void HandlePhaseCompleted()
    {
        audioKeys.ResetPitch();
        audioResult.PlaySound(audioResult.complete);

        if (Services.Get<SettingsManager>().ScreenShake)
        {
            keyboardShake.SetShaking(false);
            keyboardShake.ResetMagnitude();
        }
    }

    private void OnCorrectHold()
    {
        audioKeys.StopAudio();
        audioKeys.AddPitch(0.2f);
        audioKeys.PlaySound(audioKeys.pressed);


        if (Services.Get<SettingsManager>().ScreenShake)
        { 
            cameraShake.MildShake();
            cameraShake.OverZoomCam();

            keyboardShake.SetShaking(true);
            keyboardShake.UpMagnitude();
        }
    }

    private void OnCorrectRelease()
    {
        audioKeys.PlaySound(audioKeys.released);

        if (Services.Get<SettingsManager>().ScreenShake)
        {
            cameraShake.MildShake();
            keyboardShake.DownMagnitude();
        }
    }

    private void OnFailed()
    {
        audioKeys.StopAudio();
        audioKeys.ResetPitch();
        audioResult.StopAudio();
        audioResult.PlaySound(audioResult.fail);

        if (Services.Get<SettingsManager>().ScreenShake)
        {
            cameraShake.StrongShake();
            keyboardShake.SetShaking(false);
            keyboardShake.ResetMagnitude();
        }
    }

    private void HandleRestart()
    {
        audioKeys.SetVolume(1.0f);
        audioKeys.ResetPitch();

        if (Services.Get<SettingsManager>().ScreenShake)
        {
            keyboardShake.SetShaking(false);
            keyboardShake.ResetMagnitude();
            cameraShake.ResetFOV();
        }
    }
}
