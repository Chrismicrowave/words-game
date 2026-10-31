using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Words.UiMockups.WordListHub;

internal sealed class WordListHubLayoutPersistence : AssetModificationProcessor
{
    private const string MockupScenePath = "Assets/Scenes/UI-Mockups/WordListHubMockup.unity";

    private static string[] OnWillSaveAssets(string[] paths)
    {
        if (Application.isPlaying
            || EditorApplication.isPlayingOrWillChangePlaymode
            || !paths.Any(path => string.Equals(
                path.Replace('\\', '/'),
                MockupScenePath,
                StringComparison.OrdinalIgnoreCase)))
        {
            return paths;
        }

        Scene scene = SceneManager.GetSceneByPath(MockupScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            return paths;

        WordListHubMockupController controller = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<WordListHubMockupController>(true))
            .FirstOrDefault();
        controller?.CaptureAuthoredLayout();
        return paths;
    }
}
