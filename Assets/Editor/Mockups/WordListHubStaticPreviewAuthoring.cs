#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
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
        EnsureLibraryRecordFields(bindings.library);
        EnsureCommunityRecordFields(bindings.community);
        EnsureCommunityRows(bindings.community, fixtures);
        EnsureAchievementCards(bindings.achievements, fixtures);
        AlignCommunityToLibraryLayout(bindings);
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
        RegisterHierarchy(bindings.community.root != null ? bindings.community.root.transform : null, label);
        RegisterHierarchy(bindings.library.rowTemplate != null ? bindings.library.rowTemplate.transform : null, label);
        RegisterHierarchy(bindings.community.rowTemplate != null ? bindings.community.rowTemplate.transform : null, label);
        RegisterHierarchy(bindings.achievements.cardContent, label);
        RegisterHierarchy(bindings.library.detailWords != null ? bindings.library.detailWords.transform.parent : null, label);
        RegisterHierarchy(bindings.community.detailSummary != null ? bindings.community.detailSummary.transform : null, label);
        RegisterHierarchy(bindings.modal.editList.root != null ? bindings.modal.editList.root.transform : null, label);
    }

    private static void AlignCommunityToLibraryLayout(WordListHubSceneBindings bindings)
    {
        WordListHubSceneBindings.LibraryPageBinding library = bindings.library;
        WordListHubSceneBindings.CommunityPageBinding community = bindings.community;
        Canvas.ForceUpdateCanvases();

        RectTransform libraryListPane = library.listScroll.transform.parent as RectTransform;
        RectTransform communityListPane = community.listScroll.transform.parent as RectTransform;
        CopyRectTransformLayout(libraryListPane, communityListPane);
        CopyRectTransformLayout(library.listScroll.transform as RectTransform,
            community.listScroll.transform as RectTransform);
        CopyRectTransformLayout(library.listScroll.viewport, community.listScroll.viewport);
        CopyContentRectTransformLayout(library.listContent as RectTransform,
            community.listContent as RectTransform);
        CopyRectTransformLayout(library.detailPane as RectTransform,
            community.detailPane as RectTransform);
        AlignCommunityToolbar(library, community);
        AlignCommunityListTitle(library, community);

        CopyTextRectWithinPane(library.detailPane as RectTransform, library.detailTitle,
            community.detailPane as RectTransform, community.detailTitle);
        CopyTextRectWithinPane(library.detailPane as RectTransform, library.detailMetadata,
            community.detailPane as RectTransform, community.detailAuthor);
        CopyTextRectWithinPane(library.detailPane as RectTransform, library.detailDescription,
            community.detailPane as RectTransform, community.detailDescription);

        RectTransform libraryWordsPanel = library.detailPane.Find("DetailWordsPanel") as RectTransform;
        RectTransform communitySamplePanel = community.detailSummary != null
            ? community.detailSummary.transform.parent as RectTransform : null;
        CopyRectTransformLayout(libraryWordsPanel, communitySamplePanel);

        if (community.detailSummary != null && communitySamplePanel != null)
        {
            RectTransform sampleWords = community.detailSummary.rectTransform;
            sampleWords.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Max(0f, communitySamplePanel.rect.width - 20f));
            sampleWords.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(0f, communitySamplePanel.rect.height - 40f));
            sampleWords.anchoredPosition = new Vector2(0f, -12f);
        }

        if (community.detailMetadata != null && communitySamplePanel != null)
        {
            RectTransform sampleCaption = community.detailMetadata.rectTransform;
            sampleCaption.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Max(0f, communitySamplePanel.rect.width - 40f));
            sampleCaption.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 22f);
            sampleCaption.anchoredPosition = new Vector2(0f, communitySamplePanel.rect.height * 0.5f - 15f);
        }

        AlignCommunityActions(library, community, community.detailPane as RectTransform);
    }

    private static void CopyRectTransformLayout(RectTransform source, RectTransform target)
    {
        if (source == null || target == null)
            return;

        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition3D = source.anchoredPosition3D;
        target.sizeDelta = source.sizeDelta;
    }

    private static void CopyContentRectTransformLayout(RectTransform source, RectTransform target)
    {
        if (source == null || target == null)
            return;

        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition3D = source.anchoredPosition3D;
        target.sizeDelta = new Vector2(source.sizeDelta.x, target.sizeDelta.y);
    }

    private static void CopyTextRectWithinPane(
        RectTransform sourcePane,
        TMP_Text source,
        RectTransform targetPane,
        TMP_Text target)
    {
        if (sourcePane == null || source == null || targetPane == null || target == null)
            return;

        RectTransform sourceRect = source.rectTransform;
        RectTransform targetRect = target.rectTransform;
        targetRect.anchorMin = sourceRect.anchorMin;
        targetRect.anchorMax = sourceRect.anchorMax;
        targetRect.pivot = sourceRect.pivot;
        targetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, sourceRect.rect.width);
        targetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, sourceRect.rect.height);
        Vector3 paneLocalPosition = sourcePane.InverseTransformPoint(sourceRect.position);
        Vector3 targetLocalPosition = targetPane.TransformPoint(paneLocalPosition);
        targetRect.position = targetLocalPosition;
    }

    private static void AlignCommunityToolbar(
        WordListHubSceneBindings.LibraryPageBinding library,
        WordListHubSceneBindings.CommunityPageBinding community)
    {
        if (library.searchInput != null && community.searchInput != null)
            CopyRectTransformLayout(library.searchInput.GetComponent<RectTransform>(),
                community.searchInput.GetComponent<RectTransform>());
        if (library.filtersMenu.toggleButton != null && community.connectionMenu.toggleButton != null)
            CopyRectTransformLayout(library.filtersMenu.toggleButton.GetComponent<RectTransform>(),
                community.connectionMenu.toggleButton.GetComponent<RectTransform>());
        if (library.filtersMenu.panel != null && community.connectionMenu.panel != null
            && community.connectionMenu.toggleButton != null)
        {
            CopyRectTransformLayout(library.filtersMenu.panel.GetComponent<RectTransform>(),
                community.connectionMenu.panel.GetComponent<RectTransform>());
            RectTransform popup = community.connectionMenu.panel.GetComponent<RectTransform>();
            RectTransform toggle = community.connectionMenu.toggleButton.GetComponent<RectTransform>();
            if (popup != null && toggle != null)
            {
                float toggleRight = toggle.anchoredPosition.x
                    + (1f - toggle.pivot.x) * toggle.rect.width;
                float popupRightOffset = (1f - popup.pivot.x) * popup.rect.width;
                popup.anchoredPosition = new Vector2(
                    toggleRight - popupRightOffset, popup.anchoredPosition.y);
            }
        }
    }

    private static void AlignCommunityListTitle(
        WordListHubSceneBindings.LibraryPageBinding library,
        WordListHubSceneBindings.CommunityPageBinding community)
    {
        RectTransform libraryPane = library.listScroll != null
            ? library.listScroll.transform.parent as RectTransform : null;
        RectTransform communityPane = community.listScroll != null
            ? community.listScroll.transform.parent as RectTransform : null;
        RectTransform libraryTitle = libraryPane != null
            ? libraryPane.Find("ListPaneTitle") as RectTransform : null;
        RectTransform communityTitle = communityPane != null
            ? communityPane.Find("CommunityListTitle") as RectTransform : null;
        CopyRectTransformLayout(libraryTitle, communityTitle);
    }

    private static void AlignCommunityActions(
        WordListHubSceneBindings.LibraryPageBinding library,
        WordListHubSceneBindings.CommunityPageBinding community,
        RectTransform detailPane)
    {
        if (detailPane == null || library.play == null || library.play.button == null)
            return;

        WordListHubSceneBindings.ButtonBinding[] actions =
        {
            community.like,
            community.download,
            community.play,
            community.report
        };
        const float gap = 12f;
        const float sideInset = 12f;
        float width = Mathf.Min(120f,
            (detailPane.rect.width - 2f * sideInset - 3f * gap) / actions.Length);
        float height = ((RectTransform)library.play.button.transform).rect.height;
        float totalWidth = actions.Length * width + (actions.Length - 1) * gap;
        float start = -totalWidth * 0.5f;
        float y = ((RectTransform)library.play.button.transform).anchoredPosition.y;

        for (int i = 0; i < actions.Length; i++)
        {
            if (actions[i] == null || actions[i].button == null)
                continue;
            RectTransform actionRect = actions[i].button.transform as RectTransform;
            actionRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            actionRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            float centerX = start + width * 0.5f + i * (width + gap);
            actionRect.anchoredPosition = new Vector2(centerX, y);
        }
    }

    private static void RegisterHierarchy(Transform target, string label)
    {
        if (target != null)
            Undo.RegisterFullObjectHierarchyUndo(target.gameObject, label);
    }

    private static void EnsureLibraryRecordFields(WordListHubSceneBindings.LibraryPageBinding library)
    {
        if (library == null)
            return;

        if (library.rowTemplate != null)
        {
            WordListHubLibraryRowView template = library.rowTemplate;
            template.recordStars = EnsureTextChild(template.transform, "PersonalStars", template.metadata);
            EditorUtility.SetDirty(template);
        }

        if (library.authoredRows == null)
            return;
        foreach (WordListHubSceneBindings.LibraryRowBinding row in library.authoredRows)
        {
            if (row == null || row.root == null)
                continue;
            row.recordStars = EnsureTextChild(row.root.transform, "PersonalStars", row.metadata);
            WordListHubLibraryRowView view = row.root.GetComponent<WordListHubLibraryRowView>();
            if (view != null)
            {
                view.recordStars = row.recordStars;
                EditorUtility.SetDirty(view);
            }
        }
    }

    private static void EnsureCommunityRecordFields(WordListHubSceneBindings.CommunityPageBinding community)
    {
        if (community == null || community.rowTemplate == null)
            return;

        WordListHubDiscoveryRowView template = community.rowTemplate;
        template.recordStars = EnsureTextChild(template.transform, "PersonalStars", template.metadata);
        template.bestTime = EnsureTextChild(template.transform, "BestTime", template.metadata);
        EditorUtility.SetDirty(template);
    }

    private static TMP_Text EnsureTextChild(Transform parent, string childName, TMP_Text styleSource)
    {
        if (parent == null)
            return null;

        Transform existing = parent.Find(childName);
        TMP_Text existingText = existing != null ? existing.GetComponent<TMP_Text>() : null;
        if (existingText != null)
            return existingText;

        if (styleSource == null)
            throw new InvalidOperationException("Cannot create " + childName + " without a row text style source.");

        GameObject field = UnityEngine.Object.Instantiate(styleSource.gameObject, parent, false);
        Undo.RegisterCreatedObjectUndo(field, "Create Word List Hub " + childName + " field");
        field.name = childName;
        field.SetActive(true);
        TMP_Text text = field.GetComponent<TMP_Text>();
        if (text == null)
            throw new InvalidOperationException("The cloned row text style has no TMP_Text component.");
        text.text = string.Empty;
        text.raycastTarget = false;
        EditorUtility.SetDirty(text);
        return text;
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
            {
                existing.recordStars = EnsureTextChild(existing.root.transform, "PersonalStars", existing.metadata);
                existing.bestTime = EnsureTextChild(existing.root.transform, "BestTime", existing.metadata);
                WordListHubDiscoveryRowView existingView = existing.root.GetComponent<WordListHubDiscoveryRowView>();
                if (existingView != null)
                {
                    existingView.recordStars = existing.recordStars;
                    existingView.bestTime = existing.bestTime;
                    EditorUtility.SetDirty(existingView);
                }
                continue;
            }

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
            existing.recordStars = view.recordStars;
            existing.bestTime = view.bestTime;
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
