#if UNITY_EDITOR
using System;
using UnityEditor;

/// <summary>
/// Retired legacy builder. The authored mockup scene is migrated in place by the
/// explicit WordListHubSceneMigrationTool and must never be regenerated from mainGame.
/// </summary>
public static class WordListHubMockupBuilder
{
    [MenuItem("Tools/Mockups/Build Word List Hub Preview Scene")]
    public static void BuildPreviewScene()
    {
        throw new InvalidOperationException(
            "The legacy preview builder is retired. Use Tools/Mockups/Migrate Word List Hub Scene Layout.");
    }
}
#endif
