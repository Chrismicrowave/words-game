// Assets/-Scripts/Core/GameStateManager.cs
using System;
using UnityEngine;

public class GameStateManager : MonoBehaviour
{
    public static GameStateManager Instance { get; private set; }

    public GameState CurrentState { get; private set; } = GameState.Idle;

    // State transition events
    public event Action<GameState, GameState> OnStateChanged;
    public event Action OnPhaseStarted;
    public event Action<StepResult, Step> OnStepProcessed;
    public event Action OnPhaseCompleted;
    public event Action OnPhaseFailed;
    public event Action OnPhaseRestarted;
    public event Action OnAllPhasesCompleted;
    public event Action OnGameReset;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    public void TransitionTo(GameState newState)
    {
        if (newState == CurrentState) return;

        GameState oldState = CurrentState;
        CurrentState = newState;
        OnStateChanged?.Invoke(oldState, newState);

        switch (newState)
        {
            case GameState.Playing:
                OnPhaseStarted?.Invoke();
                break;
            case GameState.PhaseComplete:
                OnPhaseCompleted?.Invoke();
                break;
            case GameState.PhaseFailed:
                OnPhaseFailed?.Invoke();
                break;
            case GameState.AllComplete:
                OnAllPhasesCompleted?.Invoke();
                break;
        }
    }

    public void RaiseStepProcessed(StepResult result, Step step)
    {
        OnStepProcessed?.Invoke(result, step);
    }

    public void RaisePhaseRestarted()
    {
        OnPhaseRestarted?.Invoke();
    }

    public void RaiseGameReset()
    {
        OnGameReset?.Invoke();
    }
}
