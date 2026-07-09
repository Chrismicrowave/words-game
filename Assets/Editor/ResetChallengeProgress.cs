#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tools for challenge progression debugging.
/// Tools > Words > Reset Challenge Progress
/// </summary>
public class ResetChallengeProgress
{
    [MenuItem("Tools/Words/Reset Challenge Progress")]
    public static void Execute()
    {
        if (!EditorUtility.DisplayDialog("Reset Challenge Progress",
            "Reset all challenge unlocks and star ratings back to level 1 only?",
            "Reset", "Cancel"))
            return;

        ChallengeProgression.ResetAll();
        Debug.Log("[ResetProgress] Challenge progress reset to level 1.");
    }
}
#endif
