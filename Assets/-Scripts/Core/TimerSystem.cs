using System;
using UnityEngine;

public class TimerSystem : MonoBehaviour
{
    public static TimerSystem Instance { get; private set; }

    public float CurrentPhaseDuration { get; private set; }
    public float TotalElapsedTime { get; private set; }
    public bool IsRunning { get; private set; }

    private float phaseStartTime;

    public event Action<float, float> OnTimerUpdated; // phaseDuration, totalElapsed

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Update()
    {
        if (IsRunning)
        {
            CurrentPhaseDuration = Time.time - phaseStartTime;
            OnTimerUpdated?.Invoke(CurrentPhaseDuration, TotalElapsedTime + CurrentPhaseDuration);
        }
    }

    public void StartTimer()
    {
        if (!IsRunning)
        {
            IsRunning = true;
            phaseStartTime = Time.time;
        }
    }

    public void StopAndAccumulate()
    {
        if (IsRunning)
        {
            TotalElapsedTime += CurrentPhaseDuration;
            IsRunning = false;
            CurrentPhaseDuration = 0f;
        }
    }

    public void ResetPhaseTimer()
    {
        IsRunning = false;
        CurrentPhaseDuration = 0f;
        OnTimerUpdated?.Invoke(0f, TotalElapsedTime);
    }

    public void ResetAll()
    {
        IsRunning = false;
        CurrentPhaseDuration = 0f;
        TotalElapsedTime = 0f;
        OnTimerUpdated?.Invoke(0f, 0f);
    }
}
