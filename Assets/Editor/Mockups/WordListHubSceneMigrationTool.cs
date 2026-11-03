#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Words.UiMockups.WordListHub;

namespace Words.UiMockups.Editor
{
    /// <summary>
    /// Adds missing authored pages, templates, dialogs and selection visuals to
    /// the existing Word List Hub scene while preserving its existing Library UI.
    /// </summary>
    public static class WordListHubSceneMigrationTool
    {
        private const string ScenePath = "Assets/Scenes/UI-Mockups/WordListHubMockup.unity";
        private const string ControllerPath = "Card/HubGenerated";
        private const string LibraryPageName = "PageContent";
        private const string TemplateRootName = "SceneTemplates";
        private const string LibraryDeleteButtonName = "Action_Delete";
        private const string SelectionVisualName = "SelectedState";
        private static readonly Vector2 LibraryDeleteButtonPosition = new Vector2(-93f, -233f);

        private static readonly Color Ink = new Color(0.17f, 0.17f, 0.15f, 1f);
        private static readonly Color MutedInk = new Color(0.39f, 0.39f, 0.35f, 1f);
        private static readonly Color Accent = new Color(0.722f, 0.467f, 0.353f, 1f);
        private static readonly Color AccentDark = new Color(0.49f, 0.27f, 0.20f, 1f);
        private static readonly Color Paper = new Color(0.963f, 0.953f, 0.889f, 1f);
        private static readonly Color PaperAlt = new Color(0.925f, 0.925f, 0.846f, 1f);
        private static readonly Color White = new Color(1f, 0.995f, 0.96f, 1f);
        private static readonly Color DefaultSelectionTint = new Color(0.722f, 0.467f, 0.353f, 0.24f);

        private sealed class Context
        {
            public Scene scene;
            public WordListHubMockupController controller;
            public Transform panel;
            public RectTransform card;
            public Transform hubRoot;
            public Transform libraryRoot;
            public Transform navigationRoot;
            public Transform libraryListPane;
            public Transform libraryContent;
            public Transform libraryDetailPane;
            public Button libraryDeleteButton;
            public Transform templateRoot;
            public Button buttonTemplate;
            public TMP_Dropdown dropdownTemplate;
            public TMP_InputField inputTemplate;
            public TMP_Text textTemplate;
            public Image imageTemplate;
            public Transform communityRoot;
            public Transform achievementsRoot;
            public Transform challengesRoot;
            public Transform modalRoot;
            public WordListHubSceneBindings bindings;
        }

        [MenuItem("Tools/Mockups/Migrate Word List Hub Scene Layout")]
        public static void MigratePreviewScene()
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before migrating the Word List Hub scene.");
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("Wait for script compilation before migrating the Word List Hub scene.");
            string sceneFilePath = GetProjectPath(ScenePath);
            if (!File.Exists(sceneFilePath))
                throw new FileNotFoundException("The existing Word List Hub scene was not found; migration will not generate a replacement.", ScenePath);

            string initialSceneHash = HashFile(sceneFilePath);
            string metaPath = sceneFilePath + ".meta";
            bool metaOriginallyExisted = File.Exists(metaPath);
            string initialMetaHash = metaOriginallyExisted ? HashFile(metaPath) : string.Empty;
            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool targetWasLoaded = scene.IsValid() && scene.isLoaded;
            bool targetWasActive = targetWasLoaded && previousActiveScene.Equals(scene);
            bool openedForMigration = !targetWasLoaded;
            if (openedForMigration)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Unity could not open the existing Word List Hub scene additively.");
            if (scene.isDirty)
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded
                    && !SceneManager.GetActiveScene().Equals(previousActiveScene))
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }
                throw new InvalidOperationException("The Word List Hub scene has unsaved edits. Save or preserve them before running migration.");
            }

            bool saveCompleted = false;
            bool preserveFailedScene = false;
            try
            {
                if (!SceneManager.GetActiveScene().Equals(scene) && !SceneManager.SetActiveScene(scene))
                    throw new InvalidOperationException("Could not make the Word List Hub scene active for migration.");

                string backupPath = BackupExistingScene(sceneFilePath, metaPath);

                Context context = ReadContext(scene);
                WordListHubFiltersMenuAuthoring.EnsureForMigration(context.libraryRoot, context.controller);
                string originalLibrarySignature = CaptureLibrarySignature(context.libraryRoot);
                var allowedLibraryAdditions = new List<Transform>();
                var addedBranchSignatures = new Dictionary<Transform, string>();
                Transform existingDeleteBranch = context.libraryDetailPane.Find(LibraryDeleteButtonName);
                context.libraryDeleteButton = EnsureLibraryDeleteButton(context);
                if (existingDeleteBranch == null)
                    RegisterAddedBranch(context, context.libraryDeleteButton.transform, allowedLibraryAdditions, addedBranchSignatures);

                context.templateRoot = EnsureTemplateRoot(context.hubRoot);
                context.communityRoot = EnsurePageRoot(context, "CommunityPage");
                context.achievementsRoot = EnsurePageRoot(context, "AchievementsPage");
                context.challengesRoot = EnsurePageRoot(context, "ChallengesPage");
                context.modalRoot = EnsureModalRoot(context);

                BuildCommunityPage(context);
                BuildAchievementsPage(context);
                Button filterButtonTemplate = RequireComponent<Button>(
                    RequireChild(context.libraryRoot, "View_All").gameObject);
                WordListHubFiltersMenuAuthoring.EnsureCommunityMenuForMigration(
                    context.communityRoot, filterButtonTemplate);
                WordListHubFiltersMenuAuthoring.EnsureAchievementsMenuForMigration(
                    context.achievementsRoot, filterButtonTemplate);
                BuildChallengesPage(context);
                BuildModal(context);
                BuildTemplates(context);
                EnsureSelectionVisuals(context, allowedLibraryAdditions, addedBranchSignatures);
                BindScene(context);
                ClearBoundPersistentCallbacks(context.bindings, context.controller);
                ValidateScene(context);

                string migratedLibrarySignature = CaptureLibrarySignature(context.libraryRoot, allowedLibraryAdditions);
                if (!string.Equals(originalLibrarySignature, migratedLibrarySignature, StringComparison.Ordinal))
                    throw new InvalidOperationException("Migration changed the authored Library hierarchy or its layout; refusing to save.");
                foreach (KeyValuePair<Transform, string> addedBranch in addedBranchSignatures)
                {
                    if (!string.Equals(addedBranch.Value, CaptureLibrarySignature(addedBranch.Key), StringComparison.Ordinal))
                        throw new InvalidOperationException("Migration changed a newly added authored branch after creation; refusing to save.");
                }
                if (!string.Equals(initialSceneHash, HashFile(sceneFilePath), StringComparison.Ordinal))
                    throw new IOException("The saved scene changed outside this migration while it was running; refusing to overwrite it.");
                if (!DiskFilesMatchInitialState(
                    sceneFilePath, initialSceneHash, metaPath, metaOriginallyExisted, initialMetaHash))
                    throw new IOException("The scene .meta changed during migration; refusing to replace its GUID.");

                EditorUtility.SetDirty(context.controller);
                EditorUtility.SetDirty(context.bindings);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new IOException("Unity could not save the migrated Word List Hub scene.");
                saveCompleted = true;

                if (metaOriginallyExisted
                    && (!File.Exists(metaPath) || !string.Equals(initialMetaHash, HashFile(metaPath), StringComparison.Ordinal)))
                    throw new IOException("The scene .meta changed during save. Restore the preserved meta from " + backupPath);

                Debug.Log("[WordListHubSceneMigrationTool] Migrated " + ScenePath
                    + ". Existing Library hierarchy preserved. Backup: " + backupPath);
            }
            catch (Exception migrationFailure)
            {
                Debug.LogError("[WordListHubSceneMigrationTool] Original migration failure:\n" + migrationFailure);
                if (!saveCompleted)
                {
                    if (!DiskFilesMatchInitialState(
                        sceneFilePath, initialSceneHash, metaPath, metaOriginallyExisted, initialMetaHash))
                    {
                        preserveFailedScene = true;
                        throw new IOException(
                            "Migration failed and the target Scene or .meta changed on disk, or its disk state could not be verified. The in-memory target was not reloaded; inspect it before continuing.",
                            migrationFailure);
                    }

                    try
                    {
                        scene = RestoreTargetSceneAfterFailure(
                            scene, ScenePath, targetWasLoaded, targetWasActive, previousActiveScene);
                    }
                    catch (Exception restoreFailure)
                    {
                        preserveFailedScene = true;
                        var combinedFailure = new AggregateException(
                            "Both the migration and target-scene recovery failed.",
                            migrationFailure,
                            restoreFailure);
                        Debug.LogError("[WordListHubSceneMigrationTool] Full migration and recovery failures:\n"
                            + combinedFailure);
                        throw new InvalidOperationException(
                            "Migration failed and Unity could not safely restore the target Scene from its unchanged disk copy. The target may remain dirty; no disk files were overwritten by recovery. Full original and recovery exceptions are included below and logged above.\n"
                            + combinedFailure,
                            combinedFailure);
                    }
                }
                else
                {
                    preserveFailedScene = true;
                }
                throw;
            }
            finally
            {
                Scene activeSceneToRestore = previousActiveScene;
                if (targetWasActive && targetWasLoaded && scene.IsValid() && scene.isLoaded)
                    activeSceneToRestore = scene;
                if (activeSceneToRestore.IsValid() && activeSceneToRestore.isLoaded
                    && !SceneManager.GetActiveScene().Equals(activeSceneToRestore))
                {
                    SceneManager.SetActiveScene(activeSceneToRestore);
                }
                if (openedForMigration && !preserveFailedScene && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Scene RestoreTargetSceneAfterFailure(
            Scene targetScene,
            string targetScenePath,
            bool targetWasLoaded,
            bool targetWasActive,
            Scene previousActiveScene)
        {
            Scene scratchScene = default(Scene);
            Scene restoredScene = default(Scene);
            try
            {
                if (targetScene.IsValid() && targetScene.isLoaded && SceneManager.sceneCount <= 1)
                {
                    scratchScene = EditorSceneManager.NewScene(
                        NewSceneSetup.EmptyScene,
                        NewSceneMode.Additive);
                    if (!scratchScene.IsValid() || !scratchScene.isLoaded)
                        throw new InvalidOperationException("Unity could not create the temporary Empty Additive recovery Scene.");
                }

                if (targetScene.IsValid() && targetScene.isLoaded
                    && !EditorSceneManager.CloseScene(targetScene, true))
                {
                    throw new InvalidOperationException("Unity refused to close the failed target Scene for recovery.");
                }

                if (!targetWasLoaded)
                    return default(Scene);

                restoredScene = EditorSceneManager.OpenScene(targetScenePath, OpenSceneMode.Additive);
                if (!restoredScene.IsValid() || !restoredScene.isLoaded || restoredScene.isDirty)
                    throw new InvalidOperationException("Unity did not reload a clean target Scene from disk.");

                Scene activeSceneToRestore = targetWasActive ? restoredScene : previousActiveScene;
                if (activeSceneToRestore.IsValid() && activeSceneToRestore.isLoaded
                    && !SceneManager.GetActiveScene().Equals(activeSceneToRestore)
                    && !SceneManager.SetActiveScene(activeSceneToRestore))
                {
                    throw new InvalidOperationException("Unity could not restore the previously active Scene after target recovery.");
                }
                return restoredScene;
            }
            finally
            {
                Scene activeSceneToRestore = targetWasActive && restoredScene.IsValid() && restoredScene.isLoaded
                    ? restoredScene
                    : previousActiveScene;
                if (activeSceneToRestore.IsValid() && activeSceneToRestore.isLoaded
                    && !SceneManager.GetActiveScene().Equals(activeSceneToRestore))
                {
                    SceneManager.SetActiveScene(activeSceneToRestore);
                }

                if (scratchScene.IsValid() && scratchScene.isLoaded)
                {
                    if (SceneManager.sceneCount > 1)
                    {
                        if (!EditorSceneManager.CloseScene(scratchScene, true))
                            Debug.LogError("[WordListHubSceneMigrationTool] Unity could not close its temporary recovery Scene.");
                    }
                    else
                    {
                        Debug.LogError("[WordListHubSceneMigrationTool] Temporary recovery Scene was kept open because closing it would unload the last loaded Scene.");
                    }
                }
            }
        }

        private static bool DiskFilesMatchInitialState(
            string sceneFilePath,
            string initialSceneHash,
            string metaPath,
            bool metaOriginallyExisted,
            string initialMetaHash)
        {
            try
            {
                if (!File.Exists(sceneFilePath)
                    || !string.Equals(initialSceneHash, HashFile(sceneFilePath), StringComparison.Ordinal))
                {
                    return false;
                }

                bool metaExistsNow = File.Exists(metaPath);
                if (metaExistsNow != metaOriginallyExisted)
                    return false;
                return !metaOriginallyExisted
                    || string.Equals(initialMetaHash, HashFile(metaPath), StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        [MenuItem("Tools/Mockups/Author Compact Community and Achievement Menus")]
        public static void AuthorCompactMenusAndCarouselSensitivity()
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before authoring the compact filter menus.");
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("Wait for script compilation before authoring the compact filter menus.");

            string sceneFilePath = GetProjectPath(ScenePath);
            if (!File.Exists(sceneFilePath))
                throw new FileNotFoundException("The Word List Hub scene was not found.", ScenePath);

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedForAuthoring = !scene.IsValid() || !scene.isLoaded;
            if (openedForAuthoring)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Unity could not open the Word List Hub scene additively.");
            if (scene.isDirty)
                throw new InvalidOperationException("The Word List Hub scene has unsaved edits; preserving them and stopping authoring.");

            try
            {
                if (!SceneManager.GetActiveScene().Equals(scene) && !SceneManager.SetActiveScene(scene))
                    throw new InvalidOperationException("Could not make the Word List Hub scene active for authoring.");

                WordListHubSceneBindings bindings = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<WordListHubSceneBindings>(true))
                    .SingleOrDefault();
                if (bindings == null || bindings.library == null || bindings.library.allLists == null
                    || bindings.library.allLists.button == null || bindings.community == null
                    || bindings.community.root == null || bindings.achievements == null
                    || bindings.achievements.root == null)
                    throw new InvalidOperationException("The existing scene does not have the Library, Community and Achievement bindings needed for authoring.");

                Button styleTemplate = bindings.library.allLists.button;
                Transform communityRoot = bindings.community.root.transform;
                Transform achievementsRoot = bindings.achievements.root.transform;
                WordListHubFiltersMenuAuthoring.EnsureCommunityMenuForMigration(communityRoot, styleTemplate);
                WordListHubFiltersMenuAuthoring.EnsureAchievementsMenuForMigration(achievementsRoot, styleTemplate);
                WordListHubFiltersMenuAuthoring.BindExistingCommunityMenu(communityRoot, bindings.community);
                WordListHubFiltersMenuAuthoring.BindExistingAchievementsMenu(achievementsRoot, bindings.achievements);

                ScrollRect achievementScroll = achievementsRoot.GetComponentsInChildren<ScrollRect>(true)
                    .SingleOrDefault(scroll => scroll.name == "AchievementsScroll");
                if (achievementScroll == null)
                    throw new InvalidOperationException("The existing AchievementsScroll was not found.");
                achievementScroll.horizontal = true;
                achievementScroll.vertical = false;
                achievementScroll.movementType = ScrollRect.MovementType.Clamped;
                achievementScroll.scrollSensitivity = 40f;

                EditorUtility.SetDirty(bindings);
                EditorUtility.SetDirty(achievementScroll);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new IOException("Unity could not save the compact menu authoring changes.");

                Debug.Log("[WordListHubSceneMigrationTool] Authored compact Community/Achievement menus and set achievement carousel sensitivity to 40.");
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded
                    && !SceneManager.GetActiveScene().Equals(previousActiveScene))
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }
                if (openedForAuthoring && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Context ReadContext(Scene scene)
        {
            WordListHubMockupController[] controllers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<WordListHubMockupController>(true))
                .ToArray();
            if (controllers.Length != 1)
                throw new InvalidOperationException("Expected exactly one WordListHubMockupController in the existing scene.");

            WordListHubMockupController controller = controllers[0];
            Transform panel = controller.transform;
            Transform card = RequireChild(panel, "Card");
            Transform hubRoot = RequireChild(card, "HubGenerated");
            Transform libraryRoot = RequireChild(hubRoot, LibraryPageName);
            Transform navigationRoot = RequireChild(hubRoot, "GlobalNavigation");
            Transform libraryListPane = RequireChild(libraryRoot, "LibraryListPane");
            Transform libraryScroll = RequireChild(libraryListPane, "LibraryScroll");
            ScrollRect listScroll = RequireComponent<ScrollRect>(libraryScroll.gameObject);
            Transform libraryContent = listScroll.content;
            if (libraryContent == null)
                throw new InvalidOperationException("The authored Library ScrollRect has no Content reference.");
            Transform libraryDetailPane = RequireChild(libraryRoot, "LibraryDetailPane");
            Button buttonTemplate = RequireComponent<Button>(RequireChild(libraryRoot, "View_All").gameObject);
            TMP_Dropdown dropdownTemplate = RequireComponent<TMP_Dropdown>(
                RequireChild(libraryRoot, "Filter_Language").gameObject);
            TMP_InputField inputTemplate = RequireComponent<TMP_InputField>(
                RequireChild(libraryRoot, "LibrarySearch").gameObject);
            TMP_Text textTemplate = RequireComponent<TMP_Text>(
                RequireChild(RequireChild(libraryDetailPane, "DetailHeaderPanel"), "DetailTitle").gameObject);
            Image imageTemplate = RequireComponent<Image>(libraryListPane.gameObject);

            return new Context
            {
                scene = scene,
                controller = controller,
                panel = panel,
                card = RequireComponent<RectTransform>(card.gameObject),
                hubRoot = hubRoot,
                libraryRoot = libraryRoot,
                navigationRoot = navigationRoot,
                libraryListPane = libraryListPane,
                libraryContent = libraryContent,
                libraryDetailPane = libraryDetailPane,
                buttonTemplate = buttonTemplate,
                dropdownTemplate = dropdownTemplate,
                inputTemplate = inputTemplate,
                textTemplate = textTemplate,
                imageTemplate = imageTemplate
            };
        }

        private static Transform EnsureTemplateRoot(Transform hubRoot)
        {
            Transform root = hubRoot.Find(TemplateRootName);
            if (root == null)
            {
                GameObject go = new GameObject(TemplateRootName, typeof(RectTransform));
                root = go.transform;
                root.SetParent(hubRoot, false);
                RectTransform rect = (RectTransform)root;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
            }
            root.gameObject.SetActive(false);
            return root;
        }

        private static Transform EnsurePageRoot(Context context, string name)
        {
            Transform page = context.hubRoot.Find(name);
            if (page == null)
            {
                GameObject go = new GameObject(name, typeof(RectTransform));
                page = go.transform;
                page.SetParent(context.hubRoot, false);
                CopyRect((RectTransform)context.libraryRoot, (RectTransform)page);
                Transform footer = context.hubRoot.Find("SampleDataWatermark");
                if (footer != null)
                    page.SetSiblingIndex(footer.GetSiblingIndex());
            }
            page.gameObject.SetActive(false);
            return page;
        }

        private static Transform EnsureModalRoot(Context context)
        {
            Transform root = context.hubRoot.Find("WordListHubModal");
            if (root == null)
            {
                GameObject go = new GameObject("WordListHubModal", typeof(RectTransform), typeof(Image));
                root = go.transform;
                root.SetParent(context.hubRoot, false);
                RectTransform rect = (RectTransform)root;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                Image image = go.GetComponent<Image>();
                image.color = new Color(0.05f, 0.05f, 0.05f, 0.56f);
                image.type = Image.Type.Sliced;
                image.raycastTarget = true;
            }
            root.gameObject.SetActive(false);
            return root;
        }

        private static void BuildCommunityPage(Context context)
        {
            Transform page = context.communityRoot;
            EnsureText(context, page, "CommunityHeading", "DISCOVER COMMUNITY LISTS", 20f, MutedInk,
                TextAlignmentOptions.MidlineLeft, new Vector2(-540f, 267f), new Vector2(420f, 36f), true);
            EnsureDropdown(context, page, "MockConnectionState",
                new[] { "Network: Online", "Network: Offline" }, 0,
                new Vector2(0f, 267f), new Vector2(200f, 40f));
            EnsureInput(context, page, "CommunitySearch", "Search shared lists…",
                new Vector2(480f, 267f), new Vector2(300f, 42f), TMP_InputField.LineType.SingleLine);

            Transform listPane = EnsurePanel(context, page, "CommunityListPane", context.libraryListPane,
                new Vector2(-460f, -30f), new Vector2(580f, 510f));
            EnsureText(context, listPane, "CommunityListTitle", "POPULAR THIS WEEK", 20f, MutedInk,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 226f), new Vector2(520f, 34f), true);
            ScrollRect scroll = EnsureScroll(context, listPane, "CommunityScroll",
                new Vector2(0f, -22f), new Vector2(550f, 445f));
            TMP_Text empty = EnsureText(context, listPane, "CommunityEmptyNotice",
                "No shared lists match this search.", 19f, MutedInk,
                TextAlignmentOptions.Center, new Vector2(0f, -22f), new Vector2(500f, 70f), false);
            empty.gameObject.SetActive(false);

            Transform detail = EnsurePanel(context, page, "CommunityDetailPane", context.libraryDetailPane,
                new Vector2(320f, -30f), new Vector2(900f, 510f));
            BuildCommunityDetail(context, detail);
        }

        private static void BuildCommunityDetail(Context context, Transform pane)
        {
            EnsureText(context, pane, "DiscoveryEyebrow", "COMMUNITY WORD LIST", 17f, MutedInk,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 222f), new Vector2(800f, 24f), true);
            EnsureText(context, pane, "DiscoveryDetailTitle", "Community list title", 28.2f, Ink,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 166f), new Vector2(760f, 52f), true);
            EnsureText(context, pane, "DiscoveryAuthor", "BY  Community member", 15f, AccentDark,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 112f), new Vector2(760f, 24f), true);
            EnsureText(context, pane, "DiscoveryDetailDesc", "A shared word list.", 18f, MutedInk,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 57f), new Vector2(760f, 58f), false);

            Transform sample = EnsurePanel(context, pane, "DiscoverySample", context.libraryDetailPane,
                new Vector2(0f, -32f), new Vector2(760f, 120f));
            EnsureText(context, sample, "SampleLabel", "SAMPLE CONTENT  ·  English  ·  0 words", 16f, MutedInk,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 36f), new Vector2(700f, 22f), true);
            EnsureText(context, sample, "SampleWords", "sample  words", 18f, Ink,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, -4f), new Vector2(700f, 48f), true);
            EnsureText(context, pane, "CommunityTrustNote",
                "Downloads are read-only and appear in Community Downloads. Likes are separate.",
                15f, MutedInk, TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, -108f), new Vector2(760f, 24f), false);

            EnsureButton(context, pane, "Action_Like", "♡ Like  ·  0", 172f, 60f, PaperAlt,
                new Vector2(-252f, -170f), 16f, false);
            EnsureButton(context, pane, "Action_Download", "Download", 250f, 60f,
                AccentDark, new Vector2(30f, -170f), 16f, true);
            EnsureButton(context, pane, "Action_Play", "Play", 172f, 60f, AccentDark,
                new Vector2(260f, -170f), 16f, true);
            EnsureButton(context, pane, "Action_Report", "Report & hide for me", 190f, 50f,
                PaperAlt, new Vector2(0f, -237f), 16f, false);
        }

        private static void BuildAchievementsPage(Context context)
        {
            Transform page = context.achievementsRoot;
            EnsureText(context, page, "AchievementsHeading", "ACHIEVEMENTS", 20f, MutedInk,
                TextAlignmentOptions.MidlineLeft, new Vector2(-600f, 267f), new Vector2(320f, 38f), true);
            EnsureText(context, page, "AchievementsCaption", "SAMPLE PROGRESS  ·  LOCAL PREVIEW", 17f, MutedInk,
                TextAlignmentOptions.MidlineRight, new Vector2(480f, 267f), new Vector2(340f, 38f), false);
            EnsureDropdown(context, page, "AchievementFilter",
                new[] { "Show: All", "Show: Unlocked", "Show: In progress" }, 0,
                new Vector2(0f, 216f), new Vector2(210f, 38f));
            EnsureAchievementScroll(context, page);
        }

        private static void EnsureAchievementScroll(Context context, Transform page)
        {
            Transform scrollRoot = page.Find("AchievementsScroll");
            if (scrollRoot != null)
            {
                ScrollRect existingScroll = RequireComponent<ScrollRect>(scrollRoot.gameObject);
                existingScroll.horizontal = true;
                existingScroll.vertical = false;
                existingScroll.movementType = ScrollRect.MovementType.Clamped;
                existingScroll.scrollSensitivity = 40f;
                Transform existingContent = scrollRoot.Find("Viewport/Content");
                HorizontalLayoutGroup existingLayout = existingContent != null
                    ? existingContent.GetComponent<HorizontalLayoutGroup>() : null;
                if (existingLayout != null)
                    existingLayout.childAlignment = TextAnchor.UpperLeft;
                return;
            }

            GameObject scrollObject = new GameObject("AchievementsScroll",
                typeof(RectTransform), typeof(ScrollRect));
            scrollRoot = scrollObject.transform;
            scrollRoot.SetParent(page, false);
            SetCenteredRect((RectTransform)scrollRoot, new Vector2(0f, -28f), new Vector2(1540f, 380f));
            ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            GameObject viewportObject = new GameObject("Viewport",
                typeof(RectTransform), typeof(Image), typeof(Mask));
            Transform viewport = viewportObject.transform;
            viewport.SetParent(scrollRoot, false);
            SetStretch((RectTransform)viewport, 0f, 0f, 0f, 0f);
            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = Color.white;
            viewportImage.type = Image.Type.Sliced;
            viewportImage.raycastTarget = true;
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;

            GameObject contentObject = new GameObject("Content", typeof(RectTransform),
                typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            Transform content = contentObject.transform;
            content.SetParent(viewport, false);
            RectTransform contentRect = (RectTransform)content;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(0f, 1f);
            contentRect.pivot = new Vector2(0f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            HorizontalLayoutGroup layout = contentObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            scroll.content = (RectTransform)content;
            scroll.viewport = (RectTransform)viewport;
        }

        private static void BuildChallengesPage(Context context)
        {
            Transform page = context.challengesRoot;
            Transform panel = EnsurePanel(context, page, "ChallengesMockupPage", context.libraryDetailPane,
                Vector2.zero, new Vector2(900f, 400f));
            EnsureText(context, panel, "ChallengesMockupTitle", "CHALLENGE LEVELS", 32f, Ink,
                TextAlignmentOptions.Center, new Vector2(0f, 95f), new Vector2(780f, 54f), true);
            EnsureText(context, panel, "ChallengesMockupNote",
                "Challenge selection stays in the existing Levels screen.\nThis isolated preview focuses on your word library, community lists and achievements.",
                20f, MutedInk, TextAlignmentOptions.Center,
                new Vector2(0f, 5f), new Vector2(780f, 100f), false);
            EnsureButton(context, panel, "ChallengesDailyButton", "Open Word List", 220f, 48f,
                AccentDark, new Vector2(0f, -96f), 16f, true);
            EnsureButton(context, panel, "ChallengesWeeklyButton", "Weekly challenge", 220f, 48f,
                PaperAlt, new Vector2(0f, -156f), 16f, false);
            EnsureText(context, panel, "ChallengesStatus", string.Empty, 17f, MutedInk,
                TextAlignmentOptions.Center, new Vector2(0f, -190f), new Vector2(780f, 24f), false);
        }

        private static void BuildModal(Context context)
        {
            Transform root = context.modalRoot;
            Transform panel = EnsurePanel(context, root, "DialogPanel", context.libraryDetailPane,
                Vector2.zero, new Vector2(780f, 730f));
            EnsureText(context, panel, "DialogTitle", "Word List Hub", 26f, Ink,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 325f), new Vector2(680f, 42f), true);

            BuildForm(context, panel, "NewListForm", true);
            BuildForm(context, panel, "EditListForm", false);
            BuildConfirmation(context, panel, "ContentChangeConfirmation",
                "Confirm changes to this list.", "Save changes", "Keep editing");
            BuildConfirmation(context, panel, "DeleteListConfirmation",
                "Delete this local list? This cannot be undone.", "Delete local list", "Cancel");
            BuildConfirmation(context, panel, "PlayResultConfirmation",
                "This is a simulated play result. No game scene will start.", "Complete mock run", "Cancel");
        }

        private static void BuildForm(Context context, Transform panel, string name, bool isNew)
        {
            Transform root = EnsureRectChild(panel, name, new Vector2(0f, -15f), new Vector2(700f, 600f));
            EnsureText(context, root, name + "NameLabel", "List name", 17f, MutedInk,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 263f), new Vector2(640f, 24f), true);
            EnsureInput(context, root, isNew ? "NewListTitleInput" : "EditTitleInput", "New word list",
                new Vector2(0f, 221f), new Vector2(640f, 42f), TMP_InputField.LineType.SingleLine);
            EnsureText(context, root, name + "DescriptionLabel", "Description (optional)", 17f, MutedInk,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 183f), new Vector2(640f, 24f), true);
            EnsureInput(context, root, isNew ? "NewListDescriptionInput" : "EditDescriptionInput",
                "Add a short description…", new Vector2(0f, 140f), new Vector2(640f, 48f),
                TMP_InputField.LineType.MultiLineNewline);
            EnsureText(context, root, name + "TagsLabel", "Tags, comma-separated (optional)", 17f, MutedInk,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 98f), new Vector2(640f, 24f), true);
            EnsureInput(context, root, isNew ? "NewListTagsInput" : "EditTagsInput",
                "travel, beginner, daily", new Vector2(0f, 62f), new Vector2(640f, 40f), TMP_InputField.LineType.SingleLine);
            EnsureText(context, root, name + "LanguageLabel", "Language", 17f, MutedInk,
                TextAlignmentOptions.MidlineLeft, new Vector2(-205f, 19f), new Vector2(230f, 22f), true);
            EnsureDropdown(context, root, isNew ? "NewListLanguage" : "EditLanguage",
                new[] { "English", "Chinese", "Mixed" }, 0,
                new Vector2(160f, 19f), new Vector2(220f, 38f));
            EnsureText(context, root, isNew ? "NewListWordsLabel" : "EditWordsLabel",
                isNew ? "Words (one per line; at least one required)" : "Words (one per line)",
                17f, MutedInk, TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, -25f), new Vector2(640f, 24f), true);
            EnsureInput(context, root, isNew ? "NewListWordsInput" : "EditWordsInput",
                "Type or paste one word per line…", new Vector2(0f, -134f), new Vector2(640f, 180f),
                TMP_InputField.LineType.MultiLineNewline);
            EnsureText(context, root, isNew ? "NewListFormValidation" : "EditValidation",
                string.Empty, 16f, AccentDark, TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, -246f), new Vector2(640f, 24f), false);
            EnsureText(context, root, "EditSaveHint", string.Empty, 14f, MutedInk,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, -273f), new Vector2(640f, 22f), false);
            EnsureButton(context, root, isNew ? "NewListCancel" : "EditListCancel", "Cancel",
                140f, 42f, PaperAlt, new Vector2(-105f, -306f), 15f, false);
            EnsureButton(context, root, isNew ? "NewListCreate" : "EditListSave",
                isNew ? "Create list" : "Save changes", 170f, 42f,
                AccentDark, new Vector2(105f, -306f), 15f, true);
            root.gameObject.SetActive(false);
        }

        private static void BuildConfirmation(
            Context context,
            Transform panel,
            string name,
            string message,
            string confirmLabel,
            string cancelLabel)
        {
            Transform root = EnsureRectChild(panel, name, new Vector2(0f, -20f), new Vector2(700f, 360f));
            EnsureText(context, root, name + "Message", message, 20f, MutedInk,
                TextAlignmentOptions.Center, new Vector2(0f, 45f), new Vector2(620f, 120f), false);
            EnsureButton(context, root, name + "Cancel", cancelLabel, 170f, 44f,
                PaperAlt, new Vector2(-110f, -95f), 15f, false);
            EnsureButton(context, root, name + "Confirm", confirmLabel, 205f, 44f,
                AccentDark, new Vector2(110f, -95f), 15f, true);
            root.gameObject.SetActive(false);
        }

        private static void BuildTemplates(Context context)
        {
            EnsureLibraryTemplate(context);
            EnsureDiscoveryTemplate(context);
            EnsureAchievementTemplate(context);
        }

        private static void EnsureSelectionVisuals(
            Context context,
            List<Transform> allowedLibraryAdditions,
            Dictionary<Transform, string> addedBranchSignatures)
        {
            WordListHubSceneBindings bindings = GetOrAddBindings(context);
            Sprite backgroundSprite = ReadControllerSprite(context.controller, "backgroundSprite");
            if (backgroundSprite == null)
                backgroundSprite = ReadControllerSprite(context.controller, "standardSprite");

            foreach (Transform row in context.libraryContent)
            {
                if (!row.name.StartsWith("LibraryItem_", StringComparison.Ordinal))
                    continue;

                GameObject selectedVisual = EnsureSelectionVisual(row, backgroundSprite, out bool wasAdded);
                if (wasAdded)
                    RegisterAddedBranch(context, selectedVisual.transform, allowedLibraryAdditions, addedBranchSignatures);
            }

            WordListHubLibraryRowView libraryTemplate = bindings.library.rowTemplate;
            if (libraryTemplate == null)
                throw new InvalidOperationException("The Library row template was not created before selection visuals were bound.");
            GameObject librarySelection = EnsureSelectionVisual(
                libraryTemplate.transform, backgroundSprite, out bool librarySelectionAdded);
            libraryTemplate.selectedVisual = Assign(
                libraryTemplate.selectedVisual, librarySelection, "library.rowTemplate.selectedVisual");
            if (librarySelectionAdded)
                RegisterAddedBranch(context, librarySelection.transform, allowedLibraryAdditions, addedBranchSignatures);

            WordListHubDiscoveryRowView communityTemplate = bindings.community.rowTemplate;
            if (communityTemplate == null)
                throw new InvalidOperationException("The Community row template was not created before selection visuals were bound.");
            GameObject communitySelection = EnsureSelectionVisual(
                communityTemplate.transform, backgroundSprite, out bool communitySelectionAdded);
            communityTemplate.selectedVisual = Assign(
                communityTemplate.selectedVisual, communitySelection, "community.rowTemplate.selectedVisual");
            if (communitySelectionAdded)
                RegisterAddedBranch(context, communitySelection.transform, allowedLibraryAdditions, addedBranchSignatures);
        }

        private static GameObject EnsureSelectionVisual(Transform rowRoot, Sprite backgroundSprite, out bool wasAdded)
        {
            Transform existing = rowRoot.Find(SelectionVisualName);
            if (existing != null)
            {
                RequireComponent<RectTransform>(existing.gameObject);
                RequireComponent<Image>(existing.gameObject);
                RequireComponent<LayoutElement>(existing.gameObject);
                wasAdded = false;
                return existing.gameObject;
            }

            GameObject selectedState = new GameObject(
                SelectionVisualName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(LayoutElement));
            selectedState.layer = rowRoot.gameObject.layer;
            Transform visual = selectedState.transform;
            visual.SetParent(rowRoot, false);
            visual.SetAsFirstSibling();

            RectTransform rect = (RectTransform)visual;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            Image image = selectedState.GetComponent<Image>();
            if (backgroundSprite == null)
            {
                Image rowBackground = rowRoot.GetComponent<Image>();
                backgroundSprite = rowBackground != null ? rowBackground.sprite : null;
            }
            if (backgroundSprite == null)
            {
                UnityEngine.Object.DestroyImmediate(selectedState);
                throw new InvalidOperationException("No authored background sprite is available for a row selection visual.");
            }
            image.sprite = backgroundSprite;
            image.type = Image.Type.Sliced;
            image.color = DefaultSelectionTint;
            image.raycastTarget = false;

            LayoutElement layout = selectedState.GetComponent<LayoutElement>();
            layout.ignoreLayout = true;
            selectedState.SetActive(false);
            wasAdded = true;
            return selectedState;
        }

        private static void RegisterAddedBranch(
            Context context,
            Transform branch,
            List<Transform> allowedLibraryAdditions,
            Dictionary<Transform, string> addedBranchSignatures)
        {
            if (branch == null || addedBranchSignatures.ContainsKey(branch))
                throw new InvalidOperationException("A newly added authored branch was missing or registered more than once.");

            if (IsWithin(branch, context.libraryRoot))
            {
                if (!IsAuthorizedLibraryAddition(context.libraryRoot, branch))
                    throw new InvalidOperationException("The Library snapshot exception only permits Action_Delete and SelectedState additions.");
                allowedLibraryAdditions.Add(branch);
            }

            addedBranchSignatures.Add(branch, CaptureLibrarySignature(branch));
        }

        private static bool IsAuthorizedLibraryAddition(Transform libraryRoot, Transform branch)
        {
            if (branch == null || branch.parent == null)
                return false;
            if (branch.name == LibraryDeleteButtonName)
                return branch.parent == libraryRoot.Find("LibraryDetailPane");
            if (branch.name != SelectionVisualName)
                return false;

            Transform listPane = libraryRoot.Find("LibraryListPane");
            Transform scrollRoot = listPane != null ? listPane.Find("LibraryScroll") : null;
            ScrollRect scroll = scrollRoot != null ? scrollRoot.GetComponent<ScrollRect>() : null;
            Transform content = scroll != null ? scroll.content : null;
            return content != null
                && branch.parent.parent == content
                && branch.parent.name.StartsWith("LibraryItem_", StringComparison.Ordinal);
        }

        private static void EnsureLibraryTemplate(Context context)
        {
            WordListHubSceneBindings bindings = GetOrAddBindings(context);
            if (bindings.library.rowTemplate != null)
            {
                WordListHubLibraryRowView existingView = bindings.library.rowTemplate;
                existingView.button = RequireComponent<Button>(existingView.gameObject);
                existingView.selectedVisual = RequireChild(existingView.transform, SelectionVisualName).gameObject;
                existingView.favoriteMark = FindText(existingView.transform, "FavoriteMark");
                existingView.favoriteButton = EnsureFavoriteButton(existingView.favoriteMark);
                existingView.bestTime = EnsureBestTimeLabel(existingView.transform);
                existingView.title = FindText(existingView.transform, "ListTitle");
                existingView.metadata = FindText(existingView.transform, "ListMetadata");
                Transform existingBadge = existingView.transform.Find("ListBadge");
                existingView.badgeRoot = existingBadge != null ? existingBadge.gameObject : null;
                existingView.badge = existingBadge != null ? existingBadge.GetComponent<TMP_Text>() : null;
                ClearButtonListeners(existingView.button, context.controller);
                ClearButtonListeners(existingView.favoriteButton, context.controller);
                ValidateTemplate(existingView.transform, "Library row template");
                return;
            }

            Transform source = context.libraryContent.Cast<Transform>()
                .FirstOrDefault(child => child.name.StartsWith("LibraryItem_", StringComparison.Ordinal));
            if (source == null)
                throw new InvalidOperationException("The existing Library has no authored row to clone as its template.");
            GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, context.templateRoot, false);
            clone.name = "LibraryRowTemplate";
            clone.SetActive(false);
            RectTransform rect = RequireComponent<RectTransform>(clone);
            SetCenteredRect(rect, Vector2.zero, new Vector2(680f, 76f));
            LayoutElement layout = clone.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.minHeight = 76f;
                layout.preferredHeight = 76f;
            }
            WordListHubLibraryRowView view = clone.GetComponent<WordListHubLibraryRowView>();
            if (view == null)
                view = clone.AddComponent<WordListHubLibraryRowView>();
            view.button = clone.GetComponent<Button>();
            view.favoriteMark = FindText(clone.transform, "FavoriteMark");
            view.favoriteButton = EnsureFavoriteButton(view.favoriteMark);
            view.bestTime = EnsureBestTimeLabel(clone.transform);
            view.title = FindText(clone.transform, "ListTitle");
            view.metadata = FindText(clone.transform, "ListMetadata");
            Transform badge = clone.transform.Find("ListBadge");
            view.badgeRoot = badge != null ? badge.gameObject : null;
            view.badge = badge != null ? badge.GetComponent<TMP_Text>() : null;
            view.itemId = string.Empty;
            ClearButtonListeners(view.button, context.controller);
            ClearButtonListeners(view.favoriteButton, context.controller);
            SetSliced(clone.transform, clone.transform.Find(SelectionVisualName));
            bindings.library.rowTemplate = view;
        }

        private static void EnsureDiscoveryTemplate(Context context)
        {
            WordListHubSceneBindings bindings = GetOrAddBindings(context);
            if (bindings.community.rowTemplate != null)
            {
                ConfigureCommunityRowTextLayout(bindings.community.rowTemplate);
                ValidateTemplate(bindings.community.rowTemplate.transform, "Community row template");
                return;
            }

            Transform source = context.libraryContent.Cast<Transform>()
                .FirstOrDefault(child => child.name.StartsWith("LibraryItem_", StringComparison.Ordinal));
            if (source == null)
                throw new InvalidOperationException("The existing Library has no authored row to clone as the Community template.");
            GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, context.templateRoot, false);
            clone.name = "CommunityRowTemplate";
            clone.SetActive(false);
            RectTransform rect = RequireComponent<RectTransform>(clone);
            SetCenteredRect(rect, Vector2.zero, new Vector2(520f, 84f));
            LayoutElement layout = clone.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.minWidth = 520f;
                layout.preferredWidth = 520f;
                layout.minHeight = 84f;
                layout.preferredHeight = 84f;
            }

            TMP_Text favorite = FindText(clone.transform, "FavoriteMark");
            TMP_Text title = FindText(clone.transform, "ListTitle");
            TMP_Text metadata = FindText(clone.transform, "ListMetadata");
            Transform badgeRoot = clone.transform.Find("ListBadge");
            TMP_Text summary = badgeRoot != null ? badgeRoot.GetComponent<TMP_Text>() : null;
            if (summary == null)
                summary = CreateText(context, clone.transform, "DiscoverySummary", string.Empty, 15f, MutedInk,
                    TextAlignmentOptions.MidlineLeft, new Vector2(0f, -26f), new Vector2(480f, 16f), false);
            else
            {
                summary.gameObject.name = "DiscoverySummary";
                SetCenteredRect(summary.rectTransform, new Vector2(0f, -26f), new Vector2(480f, 16f));
                summary.fontSize = 15f;
                summary.alignment = TextAlignmentOptions.MidlineLeft;
            }
            summary.textWrappingMode = TextWrappingModes.NoWrap;
            summary.overflowMode = TextOverflowModes.Ellipsis;
            if (title != null)
            {
                title.gameObject.name = "DiscoveryTitle";
                SetCenteredRect(title.rectTransform, new Vector2(-55f, 16f), new Vector2(340f, 24f));
            }
            if (metadata != null)
            {
                metadata.gameObject.name = "DiscoveryMetadata";
                SetCenteredRect(metadata.rectTransform, new Vector2(-55f, -8f), new Vector2(340f, 20f));
            }
            if (favorite != null)
            {
                favorite.gameObject.name = "DiscoveryStatus";
                SetCenteredRect(favorite.rectTransform, new Vector2(185f, 17f), new Vector2(110f, 18f));
                favorite.fontSize = 15f;
                favorite.alignment = TextAlignmentOptions.MidlineRight;
            }
            if (badgeRoot != null && badgeRoot != summary.transform)
                badgeRoot.gameObject.SetActive(false);

            WordListHubDiscoveryRowView view = clone.GetComponent<WordListHubDiscoveryRowView>();
            if (view == null)
                view = clone.AddComponent<WordListHubDiscoveryRowView>();
            view.button = clone.GetComponent<Button>();
            view.title = title;
            view.metadata = metadata;
            view.summary = summary;
            view.status = favorite;
            ClearButtonListeners(view.button, context.controller);
            SetSliced(clone.transform, clone.transform.Find(SelectionVisualName));
            ConfigureCommunityRowTextLayout(view);
            bindings.community.rowTemplate = view;
        }

        private static void ConfigureCommunityRowTextLayout(WordListHubDiscoveryRowView view)
        {
            if (view == null)
                return;

            RectTransform rowRect = view.transform as RectTransform;
            if (rowRect != null)
                rowRect.sizeDelta = new Vector2(rowRect.sizeDelta.x, 84f);
            LayoutElement rowLayout = view.GetComponent<LayoutElement>();
            if (rowLayout != null)
            {
                rowLayout.minHeight = 84f;
                rowLayout.preferredHeight = 84f;
            }

            ConfigureCommunityRowLabel(view.title, new Vector2(-55f, 15.5f), new Vector2(340f, 32f));
            ConfigureCommunityRowLabel(view.metadata, new Vector2(-55f, -18f), new Vector2(340f, 27f));
            ConfigureCommunityRowLabel(view.summary, new Vector2(-55f, -30f), new Vector2(340f, 22f));
            ConfigureCommunityRowLabel(view.status, new Vector2(185f, 0f), new Vector2(110f, 22f));
            if (view.summary != null)
                view.summary.gameObject.SetActive(false);
            if (view.title != null) view.title.fontSize = 22f;
            if (view.metadata != null) view.metadata.fontSize = 18f;
            if (view.summary != null) view.summary.fontSize = 15f;
            if (view.status != null) view.status.fontSize = 15f;
            if (view.status != null)
                view.status.alignment = TextAlignmentOptions.MidlineRight;
        }

        private static void ConfigureCommunityRowLabel(TMP_Text text, Vector2 position, Vector2 size)
        {
            if (text == null)
                return;

            SetCenteredRect(text.rectTransform, position, size);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        private static void EnsureAchievementTemplate(Context context)
        {
            WordListHubSceneBindings bindings = GetOrAddBindings(context);
            if (bindings.achievements.cardTemplate != null)
            {
                ValidateTemplate(bindings.achievements.cardTemplate.transform, "Achievement card template");
                return;
            }

            Transform root = context.templateRoot.Find("AchievementCardTemplate");
            if (root == null)
            {
                // DetailRecordPanel is retired, so use a neutral image root for this unrelated template.
                GameObject go = new GameObject("AchievementCardTemplate",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(context.templateRoot, false);
                go.name = "AchievementCardTemplate";
                ClearChildren(go.transform);
                root = go.transform;
                SetCenteredRect((RectTransform)root, Vector2.zero, new Vector2(460f, 340f));
                Image image = go.GetComponent<Image>();
                if (image != null)
                    image.color = White;

                GameObject iconObject = new GameObject("AchievementIcon",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                Transform iconTransform = iconObject.transform;
                iconTransform.SetParent(root, false);
                SetCenteredRect((RectTransform)iconTransform, new Vector2(-158f, 16f), new Vector2(88f, 88f));
                Image icon = iconObject.GetComponent<Image>();
                icon.sprite = ReadControllerSprite(context.controller, "firstWordLockedIcon");
                icon.type = Image.Type.Sliced;
                icon.color = new Color(0.72f, 0.72f, 0.72f, 1f);
                icon.raycastTarget = false;

                CreateText(context, root, "AchievementState", "LOCKED", 14f, AccentDark,
                    TextAlignmentOptions.MidlineLeft, new Vector2(62f, 115f), new Vector2(296f, 24f), true);
                CreateText(context, root, "AchievementTitle", "Achievement title", 22f, Ink,
                    TextAlignmentOptions.TopLeft, new Vector2(64f, 68f), new Vector2(296f, 54f), true);
                CreateText(context, root, "AchievementDescription", "Achievement description", 18f, MutedInk,
                    TextAlignmentOptions.TopLeft, new Vector2(64f, -8f), new Vector2(296f, 88f), false);
                CreateText(context, root, "AchievementProgress", "Progress", 20f, AccentDark,
                    TextAlignmentOptions.MidlineLeft, new Vector2(64f, -126f), new Vector2(296f, 32f), true);
            }

            root.gameObject.SetActive(false);
            WordListHubAchievementCardView view = root.GetComponent<WordListHubAchievementCardView>();
            if (view == null)
                view = root.gameObject.AddComponent<WordListHubAchievementCardView>();
            view.icon = FindComponent<Image>(root, "AchievementIcon");
            view.state = FindText(root, "AchievementState");
            view.title = FindText(root, "AchievementTitle");
            view.description = FindText(root, "AchievementDescription");
            view.progress = FindText(root, "AchievementProgress");
            SetSliced(root);
            bindings.achievements.cardTemplate = view;
        }

        private static void BindScene(Context context)
        {
            WordListHubSceneBindings bindings = GetOrAddBindings(context);
            bindings.card = Assign(bindings.card, context.card, "card");
            bindings.hubRoot = Assign(bindings.hubRoot, context.hubRoot, "hubRoot");
            bindings.navigationRoot = Assign(bindings.navigationRoot, context.navigationRoot, "navigationRoot");
            bindings.footer = Assign(bindings.footer,
                FindText(RequireChild(RequireChild(context.hubRoot, "SampleDataWatermark"), "NoticeText")), "footer");

            BindPage(bindings.pages, WordListHubPage.Library, context.libraryRoot.gameObject);
            BindPage(bindings.pages, WordListHubPage.Community, context.communityRoot.gameObject);
            BindPage(bindings.pages, WordListHubPage.Achievements, context.achievementsRoot.gameObject);
            BindPage(bindings.pages, WordListHubPage.Challenges, context.challengesRoot.gameObject);

            bindings.navChallenges = BindButtonBinding(bindings.navChallenges,
                RequireButton(context.navigationRoot, "Nav_Challenges"), "navChallenges");
            bindings.navLibrary = BindButtonBinding(bindings.navLibrary,
                RequireButton(context.navigationRoot, "Nav_WordLibrary"), "navLibrary");
            bindings.navCommunity = BindButtonBinding(bindings.navCommunity,
                RequireButton(context.navigationRoot, "Nav_Community"), "navCommunity");
            bindings.navAchievements = BindButtonBinding(bindings.navAchievements,
                RequireButton(context.navigationRoot, "Nav_Achievements"), "navAchievements");
            bindings.resetMockData = BindButtonBinding(bindings.resetMockData,
                RequireButton(context.hubRoot, "ResetMockData"), "resetMockData");

            BindLibrary(context, bindings);
            BindCommunity(context, bindings);
            BindAchievements(context, bindings);
            BindChallenges(context, bindings);
            BindModal(context, bindings);

            SerializedObject serialized = new SerializedObject(context.controller);
            SerializedProperty sceneBindings = serialized.FindProperty("sceneBindings");
            if (sceneBindings == null || !sceneBindings.propertyType.Equals(SerializedPropertyType.ObjectReference))
                throw new InvalidOperationException("The controller sceneBindings field was not found.");
            if (sceneBindings.objectReferenceValue != null && sceneBindings.objectReferenceValue != bindings)
                throw new InvalidOperationException("The controller already references another bindings component.");
            sceneBindings.objectReferenceValue = bindings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            context.bindings = bindings;
        }

        private static void BindLibrary(Context context, WordListHubSceneBindings bindings)
        {
            WordListHubSceneBindings.LibraryPageBinding library = bindings.library
                ?? (bindings.library = new WordListHubSceneBindings.LibraryPageBinding());
            library.root = Assign(library.root, context.libraryRoot.gameObject, "library.root");
            library.allLists = BindButtonBinding(library.allLists, RequireButton(context.libraryRoot, "View_All"), "library.allLists");
            library.favorites = BindButtonBinding(library.favorites, RequireButton(context.libraryRoot, "View_Favorites"), "library.favorites");
            library.searchInput = Assign(library.searchInput, RequireComponent<TMP_InputField>(
                RequireChild(context.libraryRoot, "LibrarySearch").gameObject), "library.searchInput");
            library.languageFilter = Assign(library.languageFilter, RequireComponent<TMP_Dropdown>(
                RequireChild(context.libraryRoot, "Filter_Language").gameObject), "library.languageFilter");
            library.playedFilter = Assign(library.playedFilter, RequireComponent<TMP_Dropdown>(
                RequireChild(context.libraryRoot, "Filter_Played").gameObject), "library.playedFilter");
            library.sortFilter = Assign(library.sortFilter, RequireComponent<TMP_Dropdown>(
                RequireChild(context.libraryRoot, "Filter_Sort").gameObject), "library.sortFilter");
            library.directionFilter = Assign(library.directionFilter, RequireComponent<TMP_Dropdown>(
                RequireChild(context.libraryRoot, "Filter_Direction").gameObject), "library.directionFilter");
            WordListHubFiltersMenuAuthoring.BindExistingMenu(context.libraryRoot, library);
            library.createList = BindButtonBinding(library.createList,
                RequireButton(context.libraryRoot, "CreateList"), "library.createList");
            library.listScroll = Assign(library.listScroll, RequireComponent<ScrollRect>(
                RequireChild(context.libraryListPane, "LibraryScroll").gameObject), "library.listScroll");
            library.listContent = Assign(library.listContent, context.libraryContent, "library.listContent");
            library.detailPane = Assign(library.detailPane, context.libraryDetailPane, "library.detailPane");
            library.rowTemplate = Assign(library.rowTemplate, GetOrAddBindings(context).library.rowTemplate, "library.rowTemplate");

            Transform detail = context.libraryDetailPane;
            library.detailTitle = Assign(library.detailTitle,
                FindText(RequireChild(RequireChild(detail, "DetailHeaderPanel"), "DetailTitle")), "library.detailTitle");
            library.detailDescription = Assign(library.detailDescription,
                FindText(RequireChild(RequireChild(detail, "DetailHeaderPanel"), "DetailDescription")), "library.detailDescription");
            library.detailMetadata = Assign(library.detailMetadata,
                FindText(RequireChild(RequireChild(detail, "DetailHeaderPanel"), "DetailMetadata")), "library.detailMetadata");
            // DetailRecordPanel is retired; do not recreate, require, or bind its record labels.
            // library.recordStars = Assign(library.recordStars,
            //     FindText(RequireChild(RequireChild(detail, "DetailRecordPanel"), "RecordStars")), "library.recordStars");
            // library.recordTime = Assign(library.recordTime,
            //     FindText(RequireChild(RequireChild(detail, "DetailRecordPanel"), "RecordTime")), "library.recordTime");
            Transform detailFavorite = detail.Find("Action_Favorite");
            if (detailFavorite != null)
                detailFavorite.gameObject.SetActive(false);
            // The retired DetailRecordPanel is left entirely under scene ownership.
            Transform wordsPanel = RequireChild(detail, "DetailWordsPanel");
            library.detailWordsLabel = Assign(library.detailWordsLabel,
                FindText(RequireChild(wordsPanel, "DetailWordsLabel")), "library.detailWordsLabel");
            library.detailWords = Assign(library.detailWords,
                FindText(RequireChild(RequireChild(RequireChild(RequireChild(wordsPanel, "DetailWordsScroll"), "Viewport"), "Content"), "DetailWords")), "library.detailWords");
            library.detailStatus = Assign(library.detailStatus,
                FindText(RequireChild(RequireChild(detail, "DetailFeedbackPanel"), "DetailStatus")), "library.detailStatus");
            library.detailStatus.text = string.Empty;
            library.delete = BindButtonBinding(library.delete, context.libraryDeleteButton, "library.delete");
            library.play = BindOptionalButtonBinding(library.play, detail, "Action_PlayLibrary", "library.play");
            library.edit = BindOptionalButtonBinding(library.edit, detail, "Action_Edit", "library.edit");
            library.copyEditWords = BindOptionalButtonBinding(library.copyEditWords, detail, "Action_CopyEditWords", "library.copyEditWords");
            library.publish = BindOptionalButtonBinding(library.publish, detail, "Action_Publish", "library.publish");
            library.unpublish = BindOptionalButtonBinding(library.unpublish, detail, "Action_Unpublish", "library.unpublish");
            if (library.copyEditWords.label != null)
                library.copyEditWords.label.text = "Copy";
            if (library.unpublish.button != null)
                library.unpublish.button.gameObject.SetActive(false);

            if (library.groups == null)
                library.groups = new List<WordListHubSceneBindings.GroupBinding>();
            foreach (Transform child in context.libraryContent)
            {
                if (!child.name.StartsWith("Group_", StringComparison.Ordinal))
                    continue;
                string key = child.name.Substring("Group_".Length);
                AddOrValidateGroup(library.groups, key, RequireComponent<Button>(child.gameObject), FindFirstText(child), child.gameObject);
            }

            if (library.authoredRows == null)
                library.authoredRows = new List<WordListHubSceneBindings.LibraryRowBinding>();
            foreach (Transform child in context.libraryContent)
            {
                if (!child.name.StartsWith("LibraryItem_", StringComparison.Ordinal))
                    continue;
                string listId = child.name.Substring("LibraryItem_".Length);
                Button button = RequireComponent<Button>(child.gameObject);
                TMP_Text favoriteMark = FindText(child, "FavoriteMark");
                Button favoriteButton = EnsureFavoriteButton(favoriteMark);
                TMP_Text bestTime = EnsureBestTimeLabel(child);
                TMP_Text badge = FindText(child, "ListBadge");
                AddOrValidateLibraryRow(library.authoredRows, new WordListHubSceneBindings.LibraryRowBinding
                {
                    listId = listId,
                    root = child.gameObject,
                    selectedVisual = RequireChild(child, SelectionVisualName).gameObject,
                    button = button,
                    favoriteButton = favoriteButton,
                    favoriteMark = favoriteMark,
                    bestTime = bestTime,
                    title = FindText(child, "ListTitle"),
                    metadata = FindText(child, "ListMetadata"),
                    badgeRoot = badge != null ? badge.gameObject : null,
                    badge = badge
                });
                ClearButtonListeners(favoriteButton, context.controller);
            }
        }

        private static Button EnsureFavoriteButton(TMP_Text favoriteMark)
        {
            if (favoriteMark == null)
                return null;

            Button button = favoriteMark.GetComponent<Button>();
            if (button == null)
                button = favoriteMark.gameObject.AddComponent<Button>();
            favoriteMark.raycastTarget = true;
            button.targetGraphic = favoriteMark;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
        }

        private static TMP_Text EnsureBestTimeLabel(Transform row)
        {
            TMP_Text existing = FindText(row, "BestTime");
            if (existing != null)
            {
                RectTransform existingRect = existing.rectTransform;
                if (existingRect != null)
                {
                    existingRect.anchorMin = new Vector2(0.5f, 0.5f);
                    existingRect.anchorMax = new Vector2(0.5f, 0.5f);
                    existingRect.pivot = new Vector2(0.5f, 0.5f);
                    existingRect.anchoredPosition = new Vector2(-255f, 0f);
                    existingRect.sizeDelta = new Vector2(72f, 28f);
                }
                existing.textWrappingMode = TextWrappingModes.NoWrap;
                existing.overflowMode = TextOverflowModes.Ellipsis;
                existing.raycastTarget = false;
                MakeRoomForBestTime(FindText(row, "ListTitle"));
                MakeRoomForBestTime(FindText(row, "ListMetadata"));
                return existing;
            }

            TMP_Text styleSource = FindText(row, "ListMetadata");
            if (styleSource == null)
                return null;

            var labelObject = new GameObject("BestTime", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(row, false);
            RectTransform rect = (RectTransform)labelObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(-255f, 0f);
            rect.sizeDelta = new Vector2(72f, 28f);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.font = styleSource.font;
            label.fontSharedMaterial = styleSource.fontSharedMaterial;
            label.fontSize = styleSource.fontSize;
            label.color = styleSource.color;
            label.fontStyle = styleSource.fontStyle;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            label.text = "—";

            MakeRoomForBestTime(FindText(row, "ListTitle"));
            MakeRoomForBestTime(styleSource);
            return label;
        }

        private static void MakeRoomForBestTime(TMP_Text text)
        {
            if (text == null)
                return;
            RectTransform rect = text.rectTransform;
            if (rect == null || !Mathf.Approximately(rect.anchoredPosition.x, -35f)
                || !Mathf.Approximately(rect.sizeDelta.x, 450f))
                return;

            Vector2 position = rect.anchoredPosition;
            position.x = -15f;
            rect.anchoredPosition = position;
            Vector2 size = rect.sizeDelta;
            size.x = 410f;
            rect.sizeDelta = size;
        }

        private static Button EnsureLibraryDeleteButton(Context context)
        {
            Transform detail = context.libraryDetailPane;
            Transform existing = detail.Find(LibraryDeleteButtonName);
            if (existing != null)
            {
                Button existingButton = RequireComponent<Button>(existing.gameObject);
                if (FindFirstText(existing) == null)
                    throw new InvalidOperationException("The existing Action_Delete button has no TMP label.");
                return existingButton;
            }

            if (detail.GetComponent<LayoutGroup>() != null)
                throw new InvalidOperationException(
                    "LibraryDetailPane uses a LayoutGroup; refusing to place the new Action_Delete button by coordinates.");

            Button source = RequireButton(detail, "Action_PlayLibrary");
            GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, detail, false);
            clone.name = LibraryDeleteButtonName;
            TMP_Text label = FindFirstText(clone.transform);
            if (label == null)
            {
                UnityEngine.Object.DestroyImmediate(clone);
                throw new InvalidOperationException("Action_PlayLibrary cannot supply a TMP label for Action_Delete.");
            }

            label.text = "Delete";
            RectTransform rect = RequireComponent<RectTransform>(clone);
            rect.anchoredPosition = LibraryDeleteButtonPosition;
            return RequireComponent<Button>(clone);
        }

        private static void BindCommunity(Context context, WordListHubSceneBindings bindings)
        {
            WordListHubSceneBindings.CommunityPageBinding community = bindings.community
                ?? (bindings.community = new WordListHubSceneBindings.CommunityPageBinding());
            community.root = Assign(community.root, context.communityRoot.gameObject, "community.root");
            community.connectionState = Assign(community.connectionState, RequireComponent<TMP_Dropdown>(
                RequireChild(context.communityRoot, "MockConnectionState").gameObject), "community.connectionState");
            WordListHubFiltersMenuAuthoring.BindExistingCommunityMenu(context.communityRoot, community);
            community.searchInput = Assign(community.searchInput, RequireComponent<TMP_InputField>(
                RequireChild(context.communityRoot, "CommunitySearch").gameObject), "community.searchInput");
            Transform listPane = RequireChild(context.communityRoot, "CommunityListPane");
            Transform scrollRoot = RequireChild(listPane, "CommunityScroll");
            community.listScroll = Assign(community.listScroll, RequireComponent<ScrollRect>(scrollRoot.gameObject), "community.listScroll");
            community.listContent = Assign(community.listContent,
                community.listScroll.content, "community.listContent");
            community.detailPane = Assign(community.detailPane,
                RequireChild(context.communityRoot, "CommunityDetailPane"), "community.detailPane");
            community.emptyNotice = Assign(community.emptyNotice,
                FindText(RequireChild(listPane, "CommunityEmptyNotice")), "community.emptyNotice");
            community.rowTemplate = Assign(community.rowTemplate,
                GetOrAddBindings(context).community.rowTemplate, "community.rowTemplate");
            community.detailTitle = Assign(community.detailTitle,
                FindText(RequireChild(community.detailPane, "DiscoveryDetailTitle")), "community.detailTitle");
            community.detailAuthor = Assign(community.detailAuthor,
                FindText(RequireChild(community.detailPane, "DiscoveryAuthor")), "community.detailAuthor");
            community.detailDescription = Assign(community.detailDescription,
                FindText(RequireChild(community.detailPane, "DiscoveryDetailDesc")), "community.detailDescription");
            Transform sample = RequireChild(community.detailPane, "DiscoverySample");
            community.detailMetadata = Assign(community.detailMetadata,
                FindText(RequireChild(sample, "SampleLabel")), "community.detailMetadata");
            community.detailSummary = Assign(community.detailSummary,
                FindText(RequireChild(sample, "SampleWords")), "community.detailSummary");
            community.detailTrustNote = Assign(community.detailTrustNote,
                FindText(RequireChild(community.detailPane, "CommunityTrustNote")), "community.detailTrustNote");
            community.like = BindButtonBinding(community.like,
                RequireButton(community.detailPane, "Action_Like"), "community.like");
            community.download = BindButtonBinding(community.download,
                RequireButton(community.detailPane, "Action_Download"), "community.download");
            community.play = BindButtonBinding(community.play,
                RequireButton(community.detailPane, "Action_Play"), "community.play");
            community.report = BindButtonBinding(community.report,
                RequireButton(community.detailPane, "Action_Report"), "community.report");
            if (community.authoredRows == null)
                community.authoredRows = new List<WordListHubSceneBindings.DiscoveryRowBinding>();
            foreach (Transform child in community.listContent)
            {
                if (!child.name.StartsWith("DiscoveryItem_", StringComparison.Ordinal))
                    continue;
                WordListHubDiscoveryRowView view = RequireComponent<WordListHubDiscoveryRowView>(child.gameObject);
                string remoteItemId = child.name.Substring("DiscoveryItem_".Length);
                AddOrValidateDiscoveryRow(community.authoredRows,
                    new WordListHubSceneBindings.DiscoveryRowBinding
                    {
                        remoteItemId = remoteItemId,
                        root = child.gameObject,
                        button = view.button,
                        title = view.title,
                        metadata = view.metadata,
                        summary = view.summary,
                        status = view.status
                    });
            }
        }

        private static void BindAchievements(Context context, WordListHubSceneBindings bindings)
        {
            WordListHubSceneBindings.AchievementsPageBinding page = bindings.achievements
                ?? (bindings.achievements = new WordListHubSceneBindings.AchievementsPageBinding());
            page.root = Assign(page.root, context.achievementsRoot.gameObject, "achievements.root");
            page.filter = Assign(page.filter, RequireComponent<TMP_Dropdown>(
                RequireChild(context.achievementsRoot, "AchievementFilter").gameObject), "achievements.filter");
            WordListHubFiltersMenuAuthoring.BindExistingAchievementsMenu(context.achievementsRoot, page);
            Transform content = RequireChild(RequireChild(RequireChild(context.achievementsRoot, "AchievementsScroll"), "Viewport"), "Content");
            page.cardContent = Assign(page.cardContent, content, "achievements.cardContent");
            page.cardTemplate = Assign(page.cardTemplate, GetOrAddBindings(context).achievements.cardTemplate, "achievements.cardTemplate");
        }

        private static void BindChallenges(Context context, WordListHubSceneBindings bindings)
        {
            WordListHubSceneBindings.ChallengesPageBinding page = bindings.challenges
                ?? (bindings.challenges = new WordListHubSceneBindings.ChallengesPageBinding());
            page.root = Assign(page.root, context.challengesRoot.gameObject, "challenges.root");
            Transform content = RequireChild(context.challengesRoot, "ChallengesMockupPage");
            page.dailyChallenge = BindButtonBinding(page.dailyChallenge,
                RequireButton(content, "ChallengesDailyButton"), "challenges.dailyChallenge");
            page.weeklyChallenge = BindButtonBinding(page.weeklyChallenge,
                RequireButton(content, "ChallengesWeeklyButton"), "challenges.weeklyChallenge");
            page.description = Assign(page.description,
                FindText(RequireChild(content, "ChallengesMockupNote")), "challenges.description");
            page.status = Assign(page.status,
                FindText(RequireChild(content, "ChallengesStatus")), "challenges.status");
        }

        private static void BindModal(Context context, WordListHubSceneBindings bindings)
        {
            WordListHubSceneBindings.ModalBinding modal = bindings.modal
                ?? (bindings.modal = new WordListHubSceneBindings.ModalBinding());
            modal.root = Assign(modal.root, context.modalRoot.gameObject, "modal.root");
            Transform panel = RequireChild(context.modalRoot, "DialogPanel");
            modal.title = Assign(modal.title, FindText(RequireChild(panel, "DialogTitle")), "modal.title");
            modal.newList = BindForm(context, panel, modal.newList, "NewListForm", true);
            modal.editList = BindForm(context, panel, modal.editList, "EditListForm", false);
            modal.contentChange = BindConfirmation(context, panel, modal.contentChange,
                "ContentChangeConfirmation");
            modal.deleteList = BindConfirmation(context, panel, modal.deleteList,
                "DeleteListConfirmation");
            modal.playResult = BindConfirmation(context, panel, modal.playResult,
                "PlayResultConfirmation");
        }

        private static WordListHubSceneBindings.FormBinding BindForm(
            Context context,
            Transform panel,
            WordListHubSceneBindings.FormBinding form,
            string name,
            bool isNew)
        {
            form = form ?? new WordListHubSceneBindings.FormBinding();
            Transform root = RequireChild(panel, name);
            form.root = Assign(form.root, root.gameObject, "modal." + name + ".root");
            form.titleInput = Assign(form.titleInput, RequireComponent<TMP_InputField>(
                RequireChild(root, isNew ? "NewListTitleInput" : "EditTitleInput").gameObject), name + ".titleInput");
            form.descriptionInput = Assign(form.descriptionInput, RequireComponent<TMP_InputField>(
                RequireChild(root, isNew ? "NewListDescriptionInput" : "EditDescriptionInput").gameObject), name + ".descriptionInput");
            form.tagsInput = Assign(form.tagsInput, RequireComponent<TMP_InputField>(
                RequireChild(root, isNew ? "NewListTagsInput" : "EditTagsInput").gameObject), name + ".tagsInput");
            form.wordsInput = Assign(form.wordsInput, RequireComponent<TMP_InputField>(
                RequireChild(root, isNew ? "NewListWordsInput" : "EditWordsInput").gameObject), name + ".wordsInput");
            form.languageDropdown = Assign(form.languageDropdown, RequireComponent<TMP_Dropdown>(
                RequireChild(root, isNew ? "NewListLanguage" : "EditLanguage").gameObject), name + ".languageDropdown");
            form.wordsLabel = Assign(form.wordsLabel, FindText(RequireChild(root, isNew ? "NewListWordsLabel" : "EditWordsLabel")), name + ".wordsLabel");
            form.validationText = Assign(form.validationText, FindText(RequireChild(root, isNew ? "NewListFormValidation" : "EditValidation")), name + ".validationText");
            form.saveHintText = Assign(form.saveHintText, FindText(RequireChild(root, "EditSaveHint")), name + ".saveHintText");
            Button save = RequireButton(root, isNew ? "NewListCreate" : "EditListSave");
            form.saveButton = Assign(form.saveButton, save, name + ".saveButton");
            form.saveButtonLabel = Assign(form.saveButtonLabel, FindFirstText(save.transform), name + ".saveButtonLabel");
            form.cancelButton = Assign(form.cancelButton, RequireButton(root, isNew ? "NewListCancel" : "EditListCancel"), name + ".cancelButton");
            return form;
        }

        private static WordListHubSceneBindings.ConfirmationBinding BindConfirmation(
            Context context,
            Transform panel,
            WordListHubSceneBindings.ConfirmationBinding binding,
            string name)
        {
            binding = binding ?? new WordListHubSceneBindings.ConfirmationBinding();
            Transform root = RequireChild(panel, name);
            binding.root = Assign(binding.root, root.gameObject, "modal." + name + ".root");
            binding.message = Assign(binding.message,
                FindText(RequireChild(root, name + "Message")), name + ".message");
            Button confirm = RequireButton(root, name + "Confirm");
            binding.confirmButton = Assign(binding.confirmButton, confirm, name + ".confirmButton");
            binding.confirmLabel = Assign(binding.confirmLabel, FindFirstText(confirm.transform), name + ".confirmLabel");
            Button cancel = RequireButton(root, name + "Cancel");
            binding.cancelButton = Assign(binding.cancelButton, cancel, name + ".cancelButton");
            binding.cancelLabel = Assign(binding.cancelLabel, FindFirstText(cancel.transform), name + ".cancelLabel");
            return binding;
        }

        private static void ValidateScene(Context context)
        {
            WordListHubSceneBindings bindings = context.bindings;
            if (bindings == null || bindings.gameObject != context.controller.gameObject)
                throw new InvalidOperationException("WordListHubSceneBindings must be on the controller GameObject.");
            if (bindings.pages == null || bindings.pages.Count != 4
                || bindings.pages.Select(page => page.page).Distinct().Count() != 4)
                throw new InvalidOperationException("All four distinct page roots must be bound.");
            if (bindings.library.root != context.libraryRoot.gameObject
                || bindings.library.listContent != context.libraryContent
                || bindings.library.detailPane != context.libraryDetailPane)
                throw new InvalidOperationException("The existing Library roots or content references changed.");
            if (bindings.library.rowTemplate == null || bindings.community.rowTemplate == null
                || bindings.achievements.cardTemplate == null)
                throw new InvalidOperationException("A Scene-authored dynamic row or card template is missing.");
            if (bindings.library.rowTemplate.selectedVisual == null
                || bindings.community.rowTemplate.selectedVisual == null
                || bindings.library.authoredRows.Any(row => row == null || row.selectedVisual == null))
                throw new InvalidOperationException("A Library or Community selection visual is not bound.");
            ValidateTemplate(bindings.library.rowTemplate.transform, "Library row template");
            ValidateTemplate(bindings.community.rowTemplate.transform, "Community row template");
            ValidateTemplate(bindings.achievements.cardTemplate.transform, "Achievement card template");
            if (IsWithin(bindings.library.rowTemplate.transform, bindings.library.listContent)
                || IsWithin(bindings.community.rowTemplate.transform, bindings.community.listContent)
                || IsWithin(bindings.achievements.cardTemplate.transform, bindings.achievements.cardContent))
                throw new InvalidOperationException("A source template is inside visible list content.");
            if (context.modalRoot.gameObject.activeSelf
                || bindings.modal.newList.root.activeSelf
                || bindings.modal.editList.root.activeSelf
                || bindings.modal.contentChange.root.activeSelf
                || bindings.modal.deleteList.root.activeSelf
                || bindings.modal.playResult.root.activeSelf)
                throw new InvalidOperationException("The modal and its forms must start inactive.");
            if (bindings.challenges.dailyChallenge.button == null || bindings.challenges.weeklyChallenge.button == null)
                throw new InvalidOperationException("Both daily and weekly challenge controls must be bound.");
            if (context.panel.GetComponent<WordListHubSceneBindings>() != bindings)
                throw new InvalidOperationException("Bindings must share the controller's GameObject.");

            EventSystem[] eventSystems = context.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<EventSystem>(true)).ToArray();
            if (eventSystems.Length != 1)
                throw new InvalidOperationException("The mockup scene must contain exactly one EventSystem.");
            Canvas[] canvases = context.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Canvas>(true)).ToArray();
            if (canvases.Length == 0 || canvases.Any(canvas => canvas.GetComponent<GraphicRaycaster>() == null))
                throw new InvalidOperationException("Every scene Canvas must have a GraphicRaycaster.");
        }

        private static void ClearBoundPersistentCallbacks(
            WordListHubSceneBindings bindings,
            WordListHubMockupController controller)
        {
            var buttons = new HashSet<Button>();
            var dropdowns = new HashSet<TMP_Dropdown>();
            var inputs = new HashSet<TMP_InputField>();
            AddButton(buttons, bindings.navChallenges);
            AddButton(buttons, bindings.navLibrary);
            AddButton(buttons, bindings.navCommunity);
            AddButton(buttons, bindings.navAchievements);
            AddButton(buttons, bindings.resetMockData);
            AddButton(buttons, bindings.library.allLists);
            AddButton(buttons, bindings.library.favorites);
            AddButton(buttons, bindings.library.createList);
            if (bindings.library.filtersMenu != null)
            {
                if (bindings.library.filtersMenu.toggleButton != null)
                    buttons.Add(bindings.library.filtersMenu.toggleButton);
                if (bindings.library.filtersMenu.sections != null)
                {
                    foreach (WordListHubSceneBindings.FilterSectionBinding section in bindings.library.filtersMenu.sections)
                    {
                        if (section == null || section.options == null)
                            continue;
                        foreach (WordListHubSceneBindings.FilterOptionBinding option in section.options)
                        {
                            if (option != null && option.button != null)
                                buttons.Add(option.button);
                        }
                    }
                }
            }
            foreach (WordListHubSceneBindings.LibraryRowBinding row in bindings.library.authoredRows)
            {
                if (row != null && row.favoriteButton != null)
                    buttons.Add(row.favoriteButton);
            }
            AddButton(buttons, bindings.library.delete);
            AddButton(buttons, bindings.library.play);
            AddButton(buttons, bindings.library.edit);
            AddButton(buttons, bindings.library.copyEditWords);
            AddButton(buttons, bindings.library.publish);
            AddButton(buttons, bindings.library.unpublish);
            AddButton(buttons, bindings.community.like);
            AddButton(buttons, bindings.community.download);
            AddButton(buttons, bindings.community.play);
            AddButton(buttons, bindings.community.report);
            AddButton(buttons, bindings.challenges.dailyChallenge);
            AddButton(buttons, bindings.challenges.weeklyChallenge);
            foreach (WordListHubSceneBindings.GroupBinding group in bindings.library.groups)
            {
                if (group != null && group.button != null)
                    buttons.Add(group.button);
            }
            foreach (WordListHubSceneBindings.LibraryRowBinding row in bindings.library.authoredRows)
            {
                if (row != null && row.button != null)
                    buttons.Add(row.button);
            }
            foreach (WordListHubSceneBindings.DiscoveryRowBinding row in bindings.community.authoredRows)
            {
                if (row != null && row.button != null)
                    buttons.Add(row.button);
            }

            foreach (WordListHubSceneBindings.FormBinding form in new[]
            {
                bindings.modal.newList, bindings.modal.editList
            })
            {
                if (form == null) continue;
                if (form.saveButton != null) buttons.Add(form.saveButton);
                if (form.cancelButton != null) buttons.Add(form.cancelButton);
                if (form.languageDropdown != null) dropdowns.Add(form.languageDropdown);
                AddInput(inputs, form.titleInput);
                AddInput(inputs, form.descriptionInput);
                AddInput(inputs, form.tagsInput);
                AddInput(inputs, form.wordsInput);
            }
            foreach (WordListHubSceneBindings.ConfirmationBinding confirmation in new[]
            {
                bindings.modal.contentChange, bindings.modal.deleteList, bindings.modal.playResult
            })
            {
                if (confirmation == null) continue;
                if (confirmation.confirmButton != null) buttons.Add(confirmation.confirmButton);
                if (confirmation.cancelButton != null) buttons.Add(confirmation.cancelButton);
            }

            if (bindings.library.searchInput != null) inputs.Add(bindings.library.searchInput);
            if (bindings.community.searchInput != null) inputs.Add(bindings.community.searchInput);
            if (bindings.library.languageFilter != null) dropdowns.Add(bindings.library.languageFilter);
            if (bindings.library.playedFilter != null) dropdowns.Add(bindings.library.playedFilter);
            if (bindings.library.sortFilter != null) dropdowns.Add(bindings.library.sortFilter);
            if (bindings.library.directionFilter != null) dropdowns.Add(bindings.library.directionFilter);
            if (bindings.community.connectionState != null) dropdowns.Add(bindings.community.connectionState);
            if (bindings.achievements.filter != null) dropdowns.Add(bindings.achievements.filter);

            foreach (Button button in buttons)
                ClearControllerPersistentListeners(button.onClick, controller);
            foreach (TMP_Dropdown dropdown in dropdowns)
                ClearControllerPersistentListeners(dropdown.onValueChanged, controller);
            foreach (TMP_InputField input in inputs)
            {
                ClearControllerPersistentListeners(input.onValueChanged, controller);
                ClearControllerPersistentListeners(input.onEndEdit, controller);
                ClearControllerPersistentListeners(input.onSubmit, controller);
            }
        }

        private static void ClearButtonListeners(Button button, WordListHubMockupController controller)
        {
            if (button == null)
                return;
            ClearControllerPersistentListeners(button.onClick, controller);
        }

        private static void ClearDropdownListeners(TMP_Dropdown dropdown, WordListHubMockupController controller)
        {
            if (dropdown == null)
                return;
            ClearControllerPersistentListeners(dropdown.onValueChanged, controller);
        }

        private static void ClearInputListeners(TMP_InputField input, WordListHubMockupController controller)
        {
            if (input == null)
                return;
            ClearControllerPersistentListeners(input.onValueChanged, controller);
            ClearControllerPersistentListeners(input.onEndEdit, controller);
            ClearControllerPersistentListeners(input.onSubmit, controller);
        }

        private static void ClearControllerPersistentListeners(
            UnityEventBase unityEvent,
            WordListHubMockupController controller)
        {
            if (unityEvent == null || controller == null)
                return;
            for (int index = unityEvent.GetPersistentEventCount() - 1; index >= 0; index--)
            {
                if (unityEvent.GetPersistentTarget(index) == controller)
                    UnityEventTools.RemovePersistentListener(unityEvent, index);
            }
        }

        private static void AddButton(HashSet<Button> buttons, WordListHubSceneBindings.ButtonBinding binding)
        {
            if (binding != null && binding.button != null)
                buttons.Add(binding.button);
        }

        private static void AddInput(HashSet<TMP_InputField> inputs, TMP_InputField input)
        {
            if (input != null)
                inputs.Add(input);
        }

        private static void BindPage(
            List<WordListHubSceneBindings.PageBinding> pages,
            WordListHubPage page,
            GameObject root)
        {
            WordListHubSceneBindings.PageBinding existing = pages.FirstOrDefault(binding => binding != null && binding.page == page);
            if (existing == null)
                pages.Add(new WordListHubSceneBindings.PageBinding { page = page, root = root });
            else
                existing.root = Assign(existing.root, root, "pages." + page);
        }

        private static WordListHubSceneBindings.ButtonBinding BindButtonBinding(
            WordListHubSceneBindings.ButtonBinding binding,
            Button button,
            string fieldName)
        {
            binding = binding ?? new WordListHubSceneBindings.ButtonBinding();
            binding.button = Assign(binding.button, button, fieldName + ".button");
            TMP_Text label = FindFirstText(button.transform);
            if (label == null)
                throw new InvalidOperationException("The button has no TMP label: " + fieldName);
            binding.label = Assign(binding.label, label, fieldName + ".label");
            return binding;
        }

        private static WordListHubSceneBindings.ButtonBinding BindOptionalButtonBinding(
            WordListHubSceneBindings.ButtonBinding binding,
            Transform root,
            string objectName,
            string fieldName)
        {
            Transform target = root != null ? root.Find(objectName) : null;
            if (target == null)
            {
                if (binding != null && binding.button != null)
                    throw new InvalidOperationException(fieldName + " points to a button that is not in the existing detail pane.");
                return binding;
            }
            return BindButtonBinding(binding, RequireComponent<Button>(target.gameObject), fieldName);
        }

        private static Button RequireButton(Transform root, string objectName)
        {
            return RequireComponent<Button>(RequireChild(root, objectName).gameObject);
        }

        private static void AddOrValidateGroup(
            List<WordListHubSceneBindings.GroupBinding> groups,
            string groupKey,
            Button button,
            TMP_Text label,
            GameObject root)
        {
            WordListHubSceneBindings.GroupBinding existing = groups.FirstOrDefault(item =>
                item != null && string.Equals(item.groupKey, groupKey, StringComparison.Ordinal));
            if (existing == null)
            {
                groups.Add(new WordListHubSceneBindings.GroupBinding
                {
                    groupKey = groupKey,
                    button = button,
                    label = label
                });
                return;
            }
            if (existing.button != button || existing.label != label)
                throw new InvalidOperationException("Existing group binding no longer matches " + root.name + ".");
        }

        private static void AddOrValidateLibraryRow(
            List<WordListHubSceneBindings.LibraryRowBinding> rows,
            WordListHubSceneBindings.LibraryRowBinding row)
        {
            WordListHubSceneBindings.LibraryRowBinding existing = rows.FirstOrDefault(item =>
                item != null && string.Equals(item.listId, row.listId, StringComparison.Ordinal));
            if (existing == null)
            {
                rows.Add(row);
                return;
            }
            if (existing.root != row.root || existing.button != row.button)
                throw new InvalidOperationException("Existing authored Library row binding no longer matches " + row.listId + ".");
            if (existing.favoriteButton == null) existing.favoriteButton = row.favoriteButton;
            else if (existing.favoriteButton != row.favoriteButton) throw new InvalidOperationException("Existing authored Library favorite reference changed: " + row.listId);
            if (existing.selectedVisual == null) existing.selectedVisual = row.selectedVisual;
            else if (existing.selectedVisual != row.selectedVisual) throw new InvalidOperationException("Existing authored Library row selection reference changed: " + row.listId);
            if (existing.favoriteMark == null) existing.favoriteMark = row.favoriteMark;
            if (existing.bestTime == null) existing.bestTime = row.bestTime;
            else if (existing.bestTime != row.bestTime) throw new InvalidOperationException("Existing authored Library time reference changed: " + row.listId);
            if (existing.title == null) existing.title = row.title;
            if (existing.metadata == null) existing.metadata = row.metadata;
            if (existing.badgeRoot == null) existing.badgeRoot = row.badgeRoot;
            if (existing.badge == null) existing.badge = row.badge;
        }

        private static void AddOrValidateDiscoveryRow(
            List<WordListHubSceneBindings.DiscoveryRowBinding> rows,
            WordListHubSceneBindings.DiscoveryRowBinding row)
        {
            WordListHubSceneBindings.DiscoveryRowBinding existing = rows.FirstOrDefault(item =>
                item != null && string.Equals(item.remoteItemId, row.remoteItemId, StringComparison.Ordinal));
            if (existing == null)
            {
                rows.Add(row);
                return;
            }
            if (existing.root != row.root || existing.button != row.button)
                throw new InvalidOperationException("Existing authored Community row binding no longer matches " + row.remoteItemId + ".");
            if (existing.title == null) existing.title = row.title;
            else if (existing.title != row.title) throw new InvalidOperationException("Community row title reference changed: " + row.remoteItemId);
            if (existing.metadata == null) existing.metadata = row.metadata;
            else if (existing.metadata != row.metadata) throw new InvalidOperationException("Community row metadata reference changed: " + row.remoteItemId);
            if (existing.summary == null) existing.summary = row.summary;
            else if (existing.summary != row.summary) throw new InvalidOperationException("Community row summary reference changed: " + row.remoteItemId);
            if (existing.status == null) existing.status = row.status;
            else if (existing.status != row.status) throw new InvalidOperationException("Community row status reference changed: " + row.remoteItemId);
        }

        private static WordListHubSceneBindings GetOrAddBindings(Context context)
        {
            if (context.bindings != null)
                return context.bindings;
            WordListHubSceneBindings[] all = context.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<WordListHubSceneBindings>(true)).ToArray();
            WordListHubSceneBindings elsewhere = all.FirstOrDefault(binding => binding.gameObject != context.controller.gameObject);
            if (elsewhere != null)
                throw new InvalidOperationException("A WordListHubSceneBindings component exists outside the controller GameObject.");
            context.bindings = context.controller.GetComponent<WordListHubSceneBindings>();
            if (context.bindings == null)
                context.bindings = Undo.AddComponent<WordListHubSceneBindings>(context.controller.gameObject);
            return context.bindings;
        }

        private static void ValidateTemplate(Transform template, string label)
        {
            if (template == null)
                throw new InvalidOperationException(label + " is not assigned.");
            if (template.gameObject.activeSelf)
                throw new InvalidOperationException(label + " must be inactive in the authored scene.");
            if (template.GetComponent<WordListHubRuntimeInstance>() != null)
                throw new InvalidOperationException(label + " must not have a runtime-instance marker.");
        }

        private static bool IsWithin(Transform child, Transform parent)
        {
            return child != null && parent != null && child != parent && child.IsChildOf(parent);
        }

        private static T Assign<T>(T current, T expected, string fieldName) where T : UnityEngine.Object
        {
            if (expected == null)
                throw new InvalidOperationException("Required scene binding is missing: " + fieldName);
            if (current != null && current != expected)
                throw new InvalidOperationException("Existing serialized binding conflicts with the authored scene: " + fieldName);
            return expected;
        }

        private static Transform EnsurePanel(
            Context context,
            Transform parent,
            string name,
            Transform sourcePanel,
            Vector2 position,
            Vector2 size)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return existing;
            GameObject clone = UnityEngine.Object.Instantiate(sourcePanel.gameObject, parent, false);
            clone.name = name;
            ClearChildren(clone.transform);
            SetCenteredRect(RequireComponent<RectTransform>(clone), position, size);
            SetSliced(clone.transform);
            return clone.transform;
        }

        private static ScrollRect EnsureScroll(
            Context context,
            Transform parent,
            string name,
            Vector2 position,
            Vector2 size)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return RequireComponent<ScrollRect>(existing.gameObject);
            Transform source = RequireChild(context.libraryListPane, "LibraryScroll");
            GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
            clone.name = name;
            SetCenteredRect(RequireComponent<RectTransform>(clone), position, size);
            ScrollRect scroll = RequireComponent<ScrollRect>(clone);
            if (scroll.content == null)
                throw new InvalidOperationException("Cloned ScrollRect has no Content reference: " + name);
            ClearChildren(scroll.content);
            scroll.horizontal = false;
            scroll.vertical = true;
            SetSliced(clone.transform);
            return scroll;
        }

        private static Transform EnsureRectChild(Transform parent, string name, Vector2 position, Vector2 size)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return existing;
            GameObject go = new GameObject(name, typeof(RectTransform));
            Transform child = go.transform;
            child.SetParent(parent, false);
            SetCenteredRect((RectTransform)child, position, size);
            return child;
        }

        private static TMP_Text EnsureText(
            Context context,
            Transform parent,
            string name,
            string value,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment,
            Vector2 position,
            Vector2 size,
            bool bold)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return RequireComponent<TMP_Text>(existing.gameObject);
            return CreateText(context, parent, name, value, fontSize, color, alignment, position, size, bold);
        }

        private static TMP_Text CreateText(
            Context context,
            Transform parent,
            string name,
            string value,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment,
            Vector2 position,
            Vector2 size,
            bool bold)
        {
            GameObject clone = UnityEngine.Object.Instantiate(context.textTemplate.gameObject, parent, false);
            clone.name = name;
            TMP_Text text = RequireComponent<TMP_Text>(clone);
            text.text = value ?? string.Empty;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.raycastTarget = false;
            text.enableAutoSizing = false;
            SetCenteredRect(RequireComponent<RectTransform>(clone), position, size);
            return text;
        }

        private static Button EnsureButton(
            Context context,
            Transform parent,
            string name,
            string label,
            float width,
            float height,
            Color color,
            Vector2 position,
            float fontSize,
            bool bold)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return RequireComponent<Button>(existing.gameObject);
            GameObject clone = UnityEngine.Object.Instantiate(context.buttonTemplate.gameObject, parent, false);
            clone.name = name;
            Button button = RequireComponent<Button>(clone);
            ClearButtonListeners(button, context.controller);
            Image image = button.targetGraphic as Image;
            if (image == null)
                image = clone.GetComponent<Image>();
            if (image != null)
            {
                image.color = color;
                image.type = Image.Type.Sliced;
                image.raycastTarget = true;
            }
            TMP_Text text = FindFirstText(clone.transform);
            if (text == null)
                throw new InvalidOperationException("Cloned button template has no TMP label.");
            text.text = label;
            text.fontSize = fontSize;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            SetCenteredRect(RequireComponent<RectTransform>(clone), position, new Vector2(width, height));
            LayoutElement layout = clone.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.minWidth = width;
                layout.preferredWidth = width;
                layout.minHeight = height;
                layout.preferredHeight = height;
            }
            SetSliced(clone.transform);
            return button;
        }

        private static TMP_InputField EnsureInput(
            Context context,
            Transform parent,
            string name,
            string placeholder,
            Vector2 position,
            Vector2 size,
            TMP_InputField.LineType lineType)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return RequireComponent<TMP_InputField>(existing.gameObject);
            GameObject clone = UnityEngine.Object.Instantiate(context.inputTemplate.gameObject, parent, false);
            clone.name = name;
            SetCenteredRect(RequireComponent<RectTransform>(clone), position, size);
            TMP_InputField input = RequireComponent<TMP_InputField>(clone);
            ClearInputListeners(input, context.controller);
            input.lineType = lineType;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.characterLimit = 0;
            if (input.textComponent != null)
                input.textComponent.fontSize = 20f;
            input.SetTextWithoutNotify(string.Empty);
            if (input.placeholder is TMP_Text placeholderText)
            {
                placeholderText.text = placeholder;
                placeholderText.fontSize = 18f;
            }
            SetSliced(clone.transform);
            return input;
        }

        private static TMP_Dropdown EnsureDropdown(
            Context context,
            Transform parent,
            string name,
            IReadOnlyList<string> options,
            int value,
            Vector2 position,
            Vector2 size)
        {
            Transform existing = parent.Find(name);
            TMP_Dropdown dropdown;
            if (existing != null)
            {
                dropdown = RequireComponent<TMP_Dropdown>(existing.gameObject);
            }
            else
            {
                GameObject clone = UnityEngine.Object.Instantiate(context.dropdownTemplate.gameObject, parent, false);
                clone.name = name;
                clone.SetActive(true);
                dropdown = RequireComponent<TMP_Dropdown>(clone);
                SetCenteredRect(RequireComponent<RectTransform>(clone), position, size);
            }
            ClearDropdownListeners(dropdown, context.controller);
            bool hasExpectedOptions = dropdown.options.Count == options.Count;
            for (int index = 0; hasExpectedOptions && index < options.Count; index++)
                hasExpectedOptions = string.Equals(dropdown.options[index].text, options[index], StringComparison.Ordinal);
            if (!hasExpectedOptions)
            {
                dropdown.options.Clear();
                dropdown.AddOptions(options.ToList());
                dropdown.SetValueWithoutNotify(Mathf.Clamp(value, 0, Math.Max(0, options.Count - 1)));
                dropdown.RefreshShownValue();
            }
            SetSliced(dropdown.transform);
            return dropdown;
        }

        private static TMP_Text FindText(Transform root, string name)
        {
            Transform target = root != null ? root.Find(name) : null;
            return target != null ? target.GetComponent<TMP_Text>() : null;
        }

        private static TMP_Text FindText(Transform target)
        {
            return target != null ? target.GetComponent<TMP_Text>() : null;
        }

        private static TMP_Text FindFirstText(Transform root)
        {
            return root != null ? root.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault() : null;
        }

        private static T FindComponent<T>(Transform root, string childName) where T : Component
        {
            Transform child = root != null ? root.Find(childName) : null;
            return child != null ? child.GetComponent<T>() : null;
        }

        private static Transform RequireChild(Transform root, string name)
        {
            Transform child = root != null ? root.Find(name) : null;
            if (child == null)
                throw new InvalidOperationException("Required authored transform is missing: "
                    + (root != null ? root.name + "/" : string.Empty) + name);
            return child;
        }

        private static T RequireComponent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject != null ? gameObject.GetComponent<T>() : null;
            if (component == null)
                throw new InvalidOperationException("Required " + typeof(T).Name + " component is missing on "
                    + (gameObject != null ? gameObject.name : "<null>"));
            return component;
        }

        private static void CopyRect(RectTransform source, RectTransform destination)
        {
            destination.anchorMin = source.anchorMin;
            destination.anchorMax = source.anchorMax;
            destination.pivot = source.pivot;
            destination.anchoredPosition = source.anchoredPosition;
            destination.sizeDelta = source.sizeDelta;
            destination.localScale = source.localScale;
            destination.localRotation = source.localRotation;
        }

        private static void SetCenteredRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static void SetStretch(RectTransform rect, float left, float right, float bottom, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void ClearChildren(Transform parent)
        {
            for (int index = parent.childCount - 1; index >= 0; index--)
                UnityEngine.Object.DestroyImmediate(parent.GetChild(index).gameObject);
        }

        private static void SetSliced(Transform root)
        {
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
                image.type = Image.Type.Sliced;
        }

        private static void SetSliced(Transform root, Transform excludedSubtree)
        {
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (excludedSubtree != null
                    && (image.transform == excludedSubtree || image.transform.IsChildOf(excludedSubtree)))
                    continue;
                image.type = Image.Type.Sliced;
            }
        }

        private static Sprite ReadControllerSprite(WordListHubMockupController controller, string fieldName)
        {
            SerializedObject serialized = new SerializedObject(controller);
            SerializedProperty property = serialized.FindProperty(fieldName);
            return property != null ? property.objectReferenceValue as Sprite : null;
        }

        private static string CaptureLibrarySignature(Transform libraryRoot)
        {
            return CaptureLibrarySignature(libraryRoot, null);
        }

        private static string CaptureLibrarySignature(
            Transform libraryRoot,
            ICollection<Transform> ignoredAddedBranches)
        {
            if (ignoredAddedBranches != null)
            {
                var uniqueBranches = new HashSet<Transform>();
                foreach (Transform branch in ignoredAddedBranches)
                {
                    if (branch == null
                        || !uniqueBranches.Add(branch)
                        || !IsWithin(branch, libraryRoot)
                        || !IsAuthorizedLibraryAddition(libraryRoot, branch))
                    {
                        throw new InvalidOperationException(
                            "The Library snapshot exception only permits unique, newly added Action_Delete or static-row SelectedState branches.");
                    }
                }
            }

            var signature = new StringBuilder();
            AppendLibraryNodeSignature(libraryRoot, signature, ignoredAddedBranches);
            return HashText(signature.ToString());
        }

        private static void AppendLibraryNodeSignature(
            Transform node,
            StringBuilder signature,
            ICollection<Transform> ignoredAddedBranches)
        {
            AppendToken(signature, node.name);
            AppendToken(signature, node.gameObject.activeSelf.ToString());
            AppendToken(signature, node.gameObject.layer.ToString(CultureInfo.InvariantCulture));
            AppendToken(signature, node.gameObject.tag);
            AppendVector3(signature, node.localPosition);
            AppendQuaternion(signature, node.localRotation);
            AppendVector3(signature, node.localScale);

            RectTransform rect = node as RectTransform;
            if (rect != null)
            {
                AppendToken(signature, "RectTransform");
                AppendVector2(signature, rect.anchorMin);
                AppendVector2(signature, rect.anchorMax);
                AppendVector2(signature, rect.pivot);
                AppendVector2(signature, rect.anchoredPosition);
                AppendVector2(signature, rect.sizeDelta);
                AppendVector2(signature, rect.offsetMin);
                AppendVector2(signature, rect.offsetMax);
            }

            Component[] components = node.GetComponents<Component>();
            AppendToken(signature, components.Length.ToString(CultureInfo.InvariantCulture));
            foreach (Component component in components)
            {
                if (component == null)
                {
                    AppendToken(signature, "MissingComponent");
                    continue;
                }
                AppendToken(signature, component.GetType().AssemblyQualifiedName);

                // Exclude UnityEvent fields: migration removes only callbacks that
                // target this controller and snapshots the authored visuals/layout.
                if (component is Graphic
                    || component is LayoutElement
                    || component is LayoutGroup
                    || component is ContentSizeFitter
                    || component is ScrollRect
                    || component is Mask
                    || component is CanvasGroup)
                {
                    AppendToken(signature, EditorJsonUtility.ToJson(component));
                }
                else if (component is Selectable selectable)
                {
                    AppendSelectableSignature(selectable, signature);
                }
            }

            int includedChildCount = node.childCount;
            if (ignoredAddedBranches != null)
            {
                foreach (Transform branch in ignoredAddedBranches)
                {
                    if (branch.parent == node)
                        includedChildCount--;
                }
            }
            AppendToken(signature, includedChildCount.ToString(CultureInfo.InvariantCulture));
            for (int index = 0; index < node.childCount; index++)
            {
                Transform child = node.GetChild(index);
                if (ignoredAddedBranches == null || !ignoredAddedBranches.Contains(child))
                    AppendLibraryNodeSignature(child, signature, ignoredAddedBranches);
            }
        }

        private static void AppendSelectableSignature(Selectable selectable, StringBuilder signature)
        {
            AppendToken(signature, selectable.interactable.ToString());
            AppendToken(signature, selectable.transition.ToString());
            AppendObjectReference(signature, selectable.targetGraphic);
            ColorBlock colors = selectable.colors;
            AppendColor(signature, colors.normalColor);
            AppendColor(signature, colors.highlightedColor);
            AppendColor(signature, colors.pressedColor);
            AppendColor(signature, colors.selectedColor);
            AppendColor(signature, colors.disabledColor);
            AppendToken(signature, colors.colorMultiplier.ToString("R", CultureInfo.InvariantCulture));
            AppendToken(signature, colors.fadeDuration.ToString("R", CultureInfo.InvariantCulture));
            SpriteState sprites = selectable.spriteState;
            AppendObjectReference(signature, sprites.highlightedSprite);
            AppendObjectReference(signature, sprites.pressedSprite);
            AppendObjectReference(signature, sprites.selectedSprite);
            AppendObjectReference(signature, sprites.disabledSprite);
            Navigation navigation = selectable.navigation;
            AppendToken(signature, navigation.mode.ToString());
            AppendToken(signature, navigation.wrapAround.ToString());
            AppendObjectReference(signature, navigation.selectOnUp);
            AppendObjectReference(signature, navigation.selectOnDown);
            AppendObjectReference(signature, navigation.selectOnLeft);
            AppendObjectReference(signature, navigation.selectOnRight);
        }

        private static void AppendObjectReference(StringBuilder signature, UnityEngine.Object value)
        {
            AppendToken(signature, value != null
                ? value.GetInstanceID().ToString(CultureInfo.InvariantCulture)
                : string.Empty);
        }

        private static void AppendColor(StringBuilder signature, Color value)
        {
            AppendToken(signature, value.r.ToString("R", CultureInfo.InvariantCulture));
            AppendToken(signature, value.g.ToString("R", CultureInfo.InvariantCulture));
            AppendToken(signature, value.b.ToString("R", CultureInfo.InvariantCulture));
            AppendToken(signature, value.a.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendVector2(StringBuilder signature, Vector2 value)
        {
            AppendToken(signature, value.x.ToString("R", CultureInfo.InvariantCulture));
            AppendToken(signature, value.y.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendVector3(StringBuilder signature, Vector3 value)
        {
            AppendToken(signature, value.x.ToString("R", CultureInfo.InvariantCulture));
            AppendToken(signature, value.y.ToString("R", CultureInfo.InvariantCulture));
            AppendToken(signature, value.z.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendQuaternion(StringBuilder signature, Quaternion value)
        {
            AppendToken(signature, value.x.ToString("R", CultureInfo.InvariantCulture));
            AppendToken(signature, value.y.ToString("R", CultureInfo.InvariantCulture));
            AppendToken(signature, value.z.ToString("R", CultureInfo.InvariantCulture));
            AppendToken(signature, value.w.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendToken(StringBuilder signature, string value)
        {
            value = value ?? string.Empty;
            signature.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            signature.Append(':');
            signature.Append(value);
            signature.Append('|');
        }

        private static string HashText(string value)
        {
            using (SHA256 algorithm = SHA256.Create())
                return ToHex(algorithm.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }

        private static string HashFile(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return ToHex(algorithm.ComputeHash(stream));
        }

        private static string ToHex(byte[] bytes)
        {
            var result = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
                result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return result.ToString();
        }

        private static string BackupExistingScene(string scenePath, string metaPath)
        {
            string backupDirectory = GetProjectPath("Library/WordListHubSceneMigrationBackups");
            Directory.CreateDirectory(backupDirectory);
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
            string backupPath = Path.Combine(backupDirectory,
                Path.GetFileNameWithoutExtension(scenePath) + "_" + timestamp + ".unity");
            File.Copy(scenePath, backupPath, false);
            if (File.Exists(metaPath))
                File.Copy(metaPath, backupPath + ".meta", false);
            return backupPath;
        }

        private static string GetProjectPath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
        }
    }
}
#endif
