using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Words.UiMockups.WordListHub;

namespace Words.UiMockups.Editor
{
    /// <summary>Creates the initial editable Hub feedback prefab in an isolated PreviewScene.</summary>
    public static class WordListHubFeedbackPrefabAuthoring
    {
        private const string PrefabFolder = "Assets/-Prefabs/UI";
        private const string PrefabPath = PrefabFolder + "/WordListHubFeedback.prefab";

        public static void CreateAndSave()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                Debug.LogWarning("Feedback prefab already exists; leaving it untouched: " + PrefabPath);
                return;
            }

            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                throw new InvalidOperationException("The project UI Prefabs folder is missing: " + PrefabFolder);

            Scene previewScene = EditorSceneManager.NewPreviewScene();
            GameObject temporaryCanvas = null;
            try
            {
                temporaryCanvas = new GameObject(
                    "FeedbackPrefabAuthoringCanvas",
                    typeof(RectTransform),
                    typeof(Canvas));
                SceneManager.MoveGameObjectToScene(temporaryCanvas, previewScene);
                RectTransform canvasRect = temporaryCanvas.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(1920f, 1080f);

                WordListHubFeedbackView view = WordListHubFeedbackView.Create(
                    canvasRect, null, null);
                view.gameObject.name = "WordListHubFeedback";
                view.gameObject.SetActive(true);

                PrefabUtility.SaveAsPrefabAsset(view.gameObject, PrefabPath, out bool success);
                if (!success)
                    throw new InvalidOperationException("Unity could not save the new feedback prefab at " + PrefabPath);

                Debug.Log("Created editable Hub feedback prefab: " + PrefabPath);
            }
            finally
            {
                if (temporaryCanvas != null)
                    UnityEngine.Object.DestroyImmediate(temporaryCanvas);
                if (previewScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }
    }
}
