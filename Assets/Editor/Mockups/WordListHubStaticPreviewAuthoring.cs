#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Words.UiMockups.WordListHub;

/// <summary>
/// Persists the same fixture-driven layout used by Play Mode into the authored mockup scene.
/// Run explicitly after changing the scene's preview roots or canvas dimensions.
/// </summary>
public static class WordListHubStaticPreviewAuthoring
{
    private const string ScenePath = "Assets/Scenes/UI-Mockups/WordListHubMockup.unity";

    [MenuItem("Tools/Mockups/Synchronize Word List Hub Static Preview")]
    public static void Synchronize()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Static preview synchronization requires Edit Mode.");

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || !string.Equals(scene.path, ScenePath, StringComparison.Ordinal))
            throw new InvalidOperationException("Open the WordListHubMockup scene before synchronizing its static preview.");

        WordListHubSceneBindings bindings = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<WordListHubSceneBindings>(true))
            .SingleOrDefault();
        if (bindings == null)
            throw new InvalidOperationException("The open WordListHubMockup scene has no WordListHubSceneBindings.");

        WordListHubMockupController controller = bindings.GetComponent<WordListHubMockupController>();
        if (controller == null)
            throw new InvalidOperationException("WordListHubMockupController must share the bindings GameObject.");

        string backupPath = CreateSceneBackup(scene);
        Debug.Log("Saved a full pre-sync scene backup to " + backupPath, controller);
        RegisterUndo(bindings, "Synchronize Word List Hub Static Preview");
        MockupWordListStore fixtures = MockupWordListStore.CreateWithFixtures();
        EnsureCommunityRows(bindings.community, fixtures);
        EnsureAchievementCards(bindings.achievements, fixtures);
        MockupWordList editExample = fixtures.FindById("mock-list-travel-copy");
        MockupWordListEditorView.EnsureAuthoredLayout(bindings.modal.editList, editExample.Words);
        EditorUtility.SetDirty(bindings);
        EditorUtility.SetDirty(controller);

        controller.SynchronizeStaticPreview();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Unity could not save the synchronized WordListHubMockup scene.");

        Debug.Log("Word List Hub static preview synchronized and saved from the Play Mode fixture render.", controller);
    }

    private static string CreateSceneBackup(Scene scene)
    {
        string directory = Path.Combine("Temp", "WordListHubStaticPreviewBackups");
        Directory.CreateDirectory(directory);
        string backupPath = Path.Combine(directory,
            "WordListHubMockup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".unity");
        if (!EditorSceneManager.SaveScene(scene, backupPath, true))
            throw new InvalidOperationException("Unity could not create the pre-sync scene backup.");
        return backupPath;
    }

    private static void RegisterUndo(WordListHubSceneBindings bindings, string label)
    {
        Undo.RegisterCompleteObjectUndo(bindings, label);
        Undo.RegisterCompleteObjectUndo(bindings.GetComponent<WordListHubMockupController>(), label);
        RegisterHierarchy(bindings.library.listContent, label);
        RegisterHierarchy(bindings.community.listContent, label);
        RegisterHierarchy(bindings.achievements.cardContent, label);
        RegisterHierarchy(bindings.library.detailWords != null ? bindings.library.detailWords.transform.parent : null, label);
        RegisterHierarchy(bindings.community.detailSummary != null ? bindings.community.detailSummary.transform : null, label);
        RegisterHierarchy(bindings.modal.editList.root != null ? bindings.modal.editList.root.transform : null, label);
    }

    private static void RegisterHierarchy(Transform target, string label)
    {
        if (target != null)
            Undo.RegisterFullObjectHierarchyUndo(target.gameObject, label);
    }

    private static void EnsureCommunityRows(
        WordListHubSceneBindings.CommunityPageBinding community,
        MockupWordListStore fixtures)
    {
        if (community.authoredRows == null)
            community.authoredRows = new System.Collections.Generic.List<WordListHubSceneBindings.DiscoveryRowBinding>();
        if (community.listContent == null || community.rowTemplate == null)
            throw new InvalidOperationException("Community list content and its row template must be assigned.");

        foreach (MockupDiscoveryItem item in fixtures.QueryDiscoveryWithFilters(new MockupDiscoveryQuery()))
        {
            WordListHubSceneBindings.DiscoveryRowBinding existing = community.authoredRows
                .FirstOrDefault(row => row != null && row.remoteItemId == item.RemoteItemId);
            if (existing != null && existing.root != null)
                continue;

            WordListHubDiscoveryRowView view = UnityEngine.Object.Instantiate(
                community.rowTemplate, community.listContent, false);
            view.name = "CommunityPreview_" + item.RemoteItemId;
            Undo.RegisterCreatedObjectUndo(view.gameObject, "Create Community static preview tile");
            if (existing == null)
            {
                existing = new WordListHubSceneBindings.DiscoveryRowBinding();
                community.authoredRows.Add(existing);
            }
            existing.remoteItemId = item.RemoteItemId;
            existing.root = view.gameObject;
            existing.selectedVisual = view.selectedVisual;
            existing.button = view.button;
            existing.title = view.title;
            existing.metadata = view.metadata;
            existing.summary = view.summary;
            existing.status = view.status;
        }
    }

    private static void EnsureAchievementCards(
        WordListHubSceneBindings.AchievementsPageBinding achievements,
        MockupWordListStore fixtures)
    {
        if (achievements.cards == null)
            achievements.cards = new System.Collections.Generic.List<WordListHubSceneBindings.AchievementCardBinding>();
        if (achievements.cardContent == null || achievements.cardTemplate == null)
            throw new InvalidOperationException("Achievement card content and its template must be assigned.");

        foreach (MockupAchievement item in fixtures.Achievements)
        {
            WordListHubSceneBindings.AchievementCardBinding existing = achievements.cards
                .FirstOrDefault(card => card != null && card.achievementId == item.Id);
            if (existing != null && existing.root != null && existing.view != null)
                continue;

            WordListHubAchievementCardView view = UnityEngine.Object.Instantiate(
                achievements.cardTemplate, achievements.cardContent, false);
            view.name = "AchievementPreview_" + item.Id;
            Undo.RegisterCreatedObjectUndo(view.gameObject, "Create Achievement static preview card");
            if (existing == null)
            {
                existing = new WordListHubSceneBindings.AchievementCardBinding();
                achievements.cards.Add(existing);
            }
            existing.achievementId = item.Id;
            existing.root = view.gameObject;
            existing.view = view;
        }
    }
}
#endif
