using System;
using UnityEngine;

public class TimerSystem : SingletonBehaviour<TimerSystem>
{
    public float CurrentPhaseDuration { get; private set; }
    public float TotalElapsedTime { get; private set; }
    public bool IsRunning { get; private set; }

    private float phaseStartTime;
    private float pausedDuration;
    private bool overlaySuspended;
    private bool overlayWasRunning;
    private float overlaySuspendedAt;
    private Func<float> timeSource = () => Time.time;

    public bool IsOverlaySuspended => overlaySuspended;

    public event Action<float, float> OnTimerUpdated; // phaseDuration, totalElapsed

    void Update()
    {
        if (IsRunning)
        {
            CurrentPhaseDuration = timeSource() - phaseStartTime - pausedDuration;
            OnTimerUpdated?.Invoke(CurrentPhaseDuration, TotalElapsedTime + CurrentPhaseDuration);
        }
    }

    public void StartTimer()
    {
        if (!IsRunning)
        {
            IsRunning = true;
            phaseStartTime = timeSource();
            pausedDuration = 0f;
        }
    }

    public void PauseTimer()
    {
        if (IsRunning)
        {
            CurrentPhaseDuration = timeSource() - phaseStartTime - pausedDuration;
            IsRunning = false;
        }
        CancelOverlaySuspension();
    }
    public void StopAndAccumulate()
    {
        CancelOverlaySuspension();
        if (IsRunning)
        {
            CurrentPhaseDuration = timeSource() - phaseStartTime - pausedDuration;
            TotalElapsedTime += CurrentPhaseDuration;
            IsRunning = false;
            CurrentPhaseDuration = 0f;
        }
    }

    public void ResetPhaseTimer()
    {
        CancelOverlaySuspension();
        IsRunning = false;
        CurrentPhaseDuration = 0f;
        OnTimerUpdated?.Invoke(0f, TotalElapsedTime);
    }

    public void ResetAll()
    {
        CancelOverlaySuspension();
        IsRunning = false;
        CurrentPhaseDuration = 0f;
        TotalElapsedTime = 0f;
        OnTimerUpdated?.Invoke(0f, 0f);
    }

    /// <summary>Suspends a running phase timer while an overlay owns keyboard input.</summary>
    public bool SuspendForOverlay()
    {
        if (overlaySuspended) return overlayWasRunning;
        overlayWasRunning = IsRunning;
        overlaySuspendedAt = timeSource();
        if (IsRunning)
        {
            CurrentPhaseDuration = overlaySuspendedAt - phaseStartTime - pausedDuration;
            IsRunning = false;
            OnTimerUpdated?.Invoke(CurrentPhaseDuration, TotalElapsedTime + CurrentPhaseDuration);
        }
        overlaySuspended = true;
        return overlayWasRunning;
    }

    /// <summary>Resumes only the timer that was running at overlay open; reset calls cancel it.</summary>
    public void ResumeFromOverlay(bool shouldResume)
    {
        if (!overlaySuspended) return;
        float now = timeSource();
        if (shouldResume && overlayWasRunning)
        {
            phaseStartTime += now - overlaySuspendedAt;
            IsRunning = true;
        }
        CancelOverlaySuspension();
    }

    internal void SetTimeSourceForTests(Func<float> source)
    {
        timeSource = source ?? (() => Time.time);
    }

    private void CancelOverlaySuspension()
    {
        overlaySuspended = false;
        overlayWasRunning = false;
        overlaySuspendedAt = 0f;
    }
}
