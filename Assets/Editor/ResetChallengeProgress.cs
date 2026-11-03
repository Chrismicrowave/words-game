#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Compatibility menu entry routed through the Master Config preview and confirmation flow.
/// </summary>
public static class ResetChallengeProgress
{
    [MenuItem("Tools/Words/Reset Challenge Progress")]
    public static void Execute()
    {
        MasterConfigWindow.OpenForReset(
            GameResetTargets.EnglishChallengeProgress
            | GameResetTargets.ChineseChallengeProgress
            | GameResetTargets.TimeRecords);
    }
}
#endif
