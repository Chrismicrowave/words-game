// Assets/-Scripts/Core/GameState.cs
using UnityEngine;

public enum GameState
{
    Idle,
    Playing,
    PhaseFailed,
    PhaseComplete,
    AllComplete
}

public enum StepAction
{
    Hold,
    Release
}

public enum StepResult
{
    Correct,
    Failed,
    PhaseComplete
}

[System.Serializable]
public struct Step
{
    public KeyCode Key;
    public StepAction Action;
    public string Letter;
    public int TargetTextIndex;
}
