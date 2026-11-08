#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using BrewedInk.CRT;
using Words.UiMockups.WordListHub;

namespace Words.UiMockups.Editor
{
    /// <summary>Explicit, recoverable merge of the authored Hub Canvas into mainGame.</summary>
    public static class WordListHubProductionMergeTool
    {
        private const string MainScene = "Assets/Scenes/mainGame.unity";
        private const string PreviewScene = "Assets/Scenes/WordListHubMockup.unity";
        private const string PreviewCanvas = "WordListHubMockupCanvas";
        private const string ProductionCanvas = "ProductionWordListHubCanvas";
        private const string ProductionBridge = "ProductionWordListHubBridgeHost";
        private const string ReturnButtonName = "ProductionReturnToChallengesButton";
        private const int SortingOrder = 100;
        private const float HubCanvasPlaneDistance = 100f;
        private const float CloseButtonSize = 44f;
        private const float CloseButtonGap = 8f;

        private sealed class Context
        {
            public Scene main, preview;
            public Canvas sourceCanvas;
            public Camera mainCamera;
            public AudioSource menuClickAudioSource;
            public CRTCameraBehaviour crtCamera;
            public WordListHubMockupController sourceController;
            public WordListHubSceneBindings sourceBindings;
            public LevelPanelController levelPanel;
            public UIController uiController;
            public GameCoordinator coordinator;
            public LevelCompletePanelController completion;
        }

        /// <summary>Opens both existing scenes additively for inspection; never saves them.</summary>
        public static void PreflightMainGame()
        {
            CheckEditorState();
            string mainFile = ProjectFile(MainScene), previewFile = ProjectFile(PreviewScene);
            if (!File.Exists(mainFile) || !File.Exists(previewFile)
                || !File.Exists(mainFile + ".meta") || !File.Exists(previewFile + ".meta"))
                throw new FileNotFoundException("Both existing scenes and their .meta files are required.");
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!string.Equals(Path.GetFileName(projectRoot), "words", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected Unity project root; refusing to operate here.");
            RequireInside(projectRoot, mainFile);
            RequireInside(projectRoot, previewFile);
            Context c = OpenAndValidate();
            FindOwnedRoots(c);
            Debug.Log("[WordListHubProductionMergeTool] Preflight passed. mainGame=" + HashFile(mainFile)
                + ", preview=" + HashFile(previewFile) + ". Both scenes remain open and were not saved.");
        }

        /// <summary>Clears only the owned production Hub footer in the saved mainGame scene.</summary>
        public static void ClearExistingProductionFooter()
        {
            CheckEditorState();
            string mainFile = ProjectFile(MainScene), metaFile = ProjectFile(MainScene) + ".meta";
            string previewFile = ProjectFile(PreviewScene);
            if (!File.Exists(mainFile) || !File.Exists(metaFile) || !File.Exists(previewFile))
                throw new FileNotFoundException("The target scene, its .meta, and the unchanged Hub preview are required.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            RequireInside(projectRoot, mainFile);
            RequireInside(projectRoot, metaFile);
            RequireInside(projectRoot, previewFile);

            Scene main = SceneManager.GetSceneByPath(MainScene);
            if (!main.IsValid() || !main.isLoaded)
                main = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Additive);
            if (!main.IsValid() || !main.isLoaded || main.isDirty)
                throw new InvalidOperationException("mainGame must be open and clean before clearing the production footer.");

            Context c = new Context
            {
                main = main,
                uiController = One<UIController>(main, _ => true, "UIController"),
                completion = One<LevelCompletePanelController>(main, _ => true, "LevelCompletePanelController")
            };
            List<GameObject> roots = FindOwnedRoots(c);
            GameObject root = roots.SingleOrDefault(item => item != null && item.name == ProductionCanvas);
            if (root == null)
                throw new InvalidOperationException("The owned production Hub Canvas is missing.");
            WordListHubMockupController controller = root.GetComponentInChildren<WordListHubMockupController>(true);
            WordListHubSceneBindings bindings = root.GetComponentInChildren<WordListHubSceneBindings>(true);
            if (controller == null || !controller.IsProductionMode || bindings == null
                || bindings.hostRoot != root || bindings.footer == null
                || !IsChildOf(bindings.footer.transform, root.transform))
                throw new InvalidOperationException("The owned production footer binding is missing or outside its Canvas.");
            if (string.IsNullOrEmpty(bindings.footer.text))
            {
                Debug.Log("[WordListHubProductionMergeTool] Production footer is already empty; no scene save was needed.");
                return;
            }

            string mainHash = HashFile(mainFile), metaHash = HashFile(metaFile), previewHash = HashFile(previewFile);
            string backup = BackupTarget(mainFile, metaFile, mainHash, metaHash, previewHash);
            string oldText = bindings.footer.text;
            try
            {
                bindings.footer.text = string.Empty;
                EditorUtility.SetDirty(bindings.footer);
                EditorSceneManager.MarkSceneDirty(main);
                if (!EditorSceneManager.SaveScene(main, MainScene))
                    throw new IOException("Unity could not save mainGame. Recoverable backup: " + backup);
                if (main.isDirty || bindings.footer.text.Length != 0
                    || HashFile(metaFile) != metaHash || HashFile(previewFile) != previewHash)
                    throw new IOException("Production footer save validation failed. Recoverable backup: " + backup);
                Debug.Log("[WordListHubProductionMergeTool] Cleared only the owned production Hub footer and saved mainGame. Preview/meta unchanged. Backup: " + backup);
            }
            catch (Exception failure)
            {
                if (HashFile(mainFile) == mainHash && HashFile(metaFile) == metaHash)
                {
                    bindings.footer.text = oldText;
                    EditorUtility.SetDirty(bindings.footer);
                    EditorSceneManager.MarkSceneDirty(main);
                    EditorSceneManager.CloseScene(main, true);
                    Scene restored = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Additive);
                    if (!restored.IsValid() || !restored.isLoaded || restored.isDirty)
                        Debug.LogError("[WordListHubProductionMergeTool] Could not reload the unchanged clean mainGame after footer save failure. Backup: " + backup);
                }
                Debug.LogError("[WordListHubProductionMergeTool] Footer update failed. Backup: " + backup + "\n" + failure);
                throw;
            }
        }

        /// <summary>Assigns the shared menu-click source and the out-of-Canvas completion return click.</summary>
        public static void ApplyMenuClickSoundReferences()
        {
            CheckEditorState();
            Scene main = SceneManager.GetSceneByPath(MainScene);
            if (!main.IsValid() || !main.isLoaded || main.isDirty)
                throw new InvalidOperationException("mainGame must already be open and clean before adding click-sound references.");

            string mainFile = ProjectFile(MainScene), metaFile = mainFile + ".meta";
            string previewFile = ProjectFile(PreviewScene);
            if (!File.Exists(mainFile) || !File.Exists(metaFile) || !File.Exists(previewFile))
                throw new FileNotFoundException("mainGame, its .meta, and the unchanged Hub preview are required.");

            AudioSource menuClickAudioSource = FindMenuClickAudioSource(main);
            GameObject productionRoot = RootNamed(main, ProductionCanvas);
            if (productionRoot == null)
                throw new InvalidOperationException("The owned production Hub Canvas is missing.");
            WordListHubSceneBindings bindings = productionRoot.GetComponentInChildren<WordListHubSceneBindings>(true);
            if (bindings == null || bindings.hostRoot != productionRoot || bindings.challenges.detailPane == null)
                throw new InvalidOperationException("The owned Challenges detail pane is missing.");
            GameObject obsoleteTrackLabel = FindUniqueOwnedObject(productionRoot, "ProductionChallengeTrackLabel");
            if (obsoleteTrackLabel != null)
                RequireOwnedObject(obsoleteTrackLabel, bindings.challenges.detailPane,
                    "obsolete separate challenge track label");
            WordListHubGameBridge bridge = One<WordListHubGameBridge>(main,
                component => component.gameObject.name == ProductionBridge, "production Hub bridge");
            LevelCompletePanelController completion = One<LevelCompletePanelController>(main,
                _ => true, "LevelCompletePanelController");
            Button returnButton = GetRef<Button>(completion, "returnToChallengesButton");
            if (returnButton == null || !IsChildOf(returnButton.transform, completion.transform)
                || returnButton.gameObject.name != ReturnButtonName)
                throw new InvalidOperationException("The production completion return Button is missing or is not owned by the completion panel.");
            if (bridge.MenuClickAudioSource != null && bridge.MenuClickAudioSource != menuClickAudioSource)
                throw new InvalidOperationException("The production bridge already references a different click AudioSource.");

            string mainHash = HashFile(mainFile), metaHash = HashFile(metaFile), previewHash = HashFile(previewFile);
            string backup = BackupTarget(mainFile, metaFile, mainHash, metaHash, previewHash);
            bool returnSoundAlreadyPresent = HasPersistentMenuPlay(returnButton, menuClickAudioSource);
            try
            {
                SetSerialized(bridge, "menuClickAudioSource", menuClickAudioSource);
                EditorUtility.SetDirty(bridge);
                if (!returnSoundAlreadyPresent)
                {
                    UnityEventTools.AddPersistentListener(returnButton.onClick, menuClickAudioSource.Play);
                    EditorUtility.SetDirty(returnButton);
                }
                if (obsoleteTrackLabel != null)
                    UnityEngine.Object.DestroyImmediate(obsoleteTrackLabel);

                if (GetPersistentMenuPlayCount(returnButton, menuClickAudioSource) != 1
                    || GetRef<AudioSource>(bridge, "menuClickAudioSource") != menuClickAudioSource)
                    throw new InvalidOperationException("Click-sound scene references failed pre-save validation.");
                if (HashFile(mainFile) != mainHash || HashFile(metaFile) != metaHash
                    || HashFile(previewFile) != previewHash)
                    throw new IOException("A scene file changed during click-sound setup; refusing to save.");

                EditorSceneManager.MarkSceneDirty(main);
                if (!EditorSceneManager.SaveScene(main, MainScene))
                    throw new IOException("Unity could not save click-sound references. Recoverable backup: " + backup);
                if (main.isDirty || HashFile(metaFile) != metaHash || HashFile(previewFile) != previewHash)
                    throw new IOException("Saved click-sound validation failed. Recoverable backup: " + backup);
                string savedScene = File.ReadAllText(mainFile);
                if (savedScene.Contains("ProductionChallengeTrackLabel", StringComparison.Ordinal)
                    || savedScene.Contains("trackLabel:", StringComparison.Ordinal))
                    throw new IOException("Saved mainGame still contains the retired separate challenge track label binding.");
                Debug.Log("[WordListHubProductionMergeTool] Saved only click-sound references and retired track label on mainGame. Bridge source="
                    + Id(menuClickAudioSource) + ", completion return Button=" + Id(returnButton)
                    + ", backup=" + backup);
            }
            catch (Exception failure)
            {
                Debug.LogError("[WordListHubProductionMergeTool] Click-sound setup failed. Preserve the open scene and inspect backup: "
                    + backup + "\n" + failure);
                throw;
            }
        }

        /// <summary>Corrects only the owned production Canvas, challenge projection, and generated chrome in mainGame.</summary>
        public static void ApplyHubCorrections()
        {
            CheckEditorState();
            Context c = OpenAndValidate();
            string mainFile = ProjectFile(MainScene), metaFile = ProjectFile(MainScene) + ".meta";
            string previewFile = ProjectFile(PreviewScene);
            List<GameObject> ownedRoots = FindOwnedRoots(c);
            GameObject root = ownedRoots.SingleOrDefault(item => item != null && item.name == ProductionCanvas);
            GameObject bridgeHost = ownedRoots.SingleOrDefault(item => item != null && item.name == ProductionBridge);
            if (root == null || bridgeHost == null)
                throw new InvalidOperationException("The owned production Canvas and bridge must already exist before applying targeted corrections.");

            WordListHubMockupController controller = root.GetComponentInChildren<WordListHubMockupController>(true);
            WordListHubSceneBindings bindings = root.GetComponentInChildren<WordListHubSceneBindings>(true);
            WordListHubGameBridge bridge = bridgeHost.GetComponent<WordListHubGameBridge>();
            Button returnButton = GetRef<Button>(c.completion, "returnToChallengesButton");
            if (controller == null || bindings == null || bridge == null || !controller.IsProductionMode
                || bindings.hostRoot != root || bindings.card == null || bindings.challenges.root == null
                || bindings.closeHub.button == null || returnButton == null)
                throw new InvalidOperationException("The owned production Hub bindings are incomplete; no changes were made.");
            Canvas canvas = root.GetComponent<Canvas>();
            if (canvas == null || c.mainCamera == null || c.crtCamera == null)
                throw new InvalidOperationException("The production Canvas or existing CRT camera is missing.");
            if (!File.Exists(mainFile) || !File.Exists(metaFile) || !File.Exists(previewFile))
                throw new FileNotFoundException("mainGame, its .meta, and the unchanged preview scene are required.");
            if (c.main.isDirty || c.preview.isDirty)
                throw new InvalidOperationException("mainGame and the Hub preview must be clean before applying corrections.");

            string mainHash = HashFile(mainFile), metaHash = HashFile(metaFile), previewHash = HashFile(previewFile);
            string backup = BackupTarget(mainFile, metaFile, mainHash, metaHash, previewHash);
            Scene priorActiveScene = SceneManager.GetActiveScene();
            bool saved = false;
            string savedHash = null;
            try
            {
                GameObject previousButton = FindUniqueOwnedObject(root, "ProductionChallengePrevious");
                GameObject nextButton = FindUniqueOwnedObject(root, "ProductionChallengeNext");
                GameObject settingsButton = FindUniqueOwnedObject(root, "ProductionOpenSettingsButton");
                RequireOwnedObject(previousButton, bindings.challenges.root.transform, "obsolete previous challenge button");
                RequireOwnedObject(nextButton, bindings.challenges.root.transform, "obsolete next challenge button");
                RequireOwnedObject(settingsButton, root.transform, "obsolete production Settings button");
                if (bindings.openSettings.button != null
                    && (bindings.openSettings.button.gameObject != settingsButton
                        || bindings.openSettings.label != ButtonLabel(bindings.openSettings.button)))
                    throw new InvalidOperationException("Production Settings binding does not point to the owned generated button.");

                var bindingObject = new SerializedObject(bindings);
                SetRef(bindingObject, "openSettings.button", null);
                SetRef(bindingObject, "openSettings.label", null);
                SetRubyPreviewBinding(bindingObject, "challenges.rubyWords",
                    bindings.library.root.transform, bindings.challenges.root.transform,
                    bindings.libraryRubyWords);
                bindingObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(bindings);
                SetSerialized(bridge, "menuClickAudioSource", c.menuClickAudioSource);
                EditorUtility.SetDirty(bridge);

                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = c.mainCamera;
                canvas.planeDistance = HubCanvasPlaneDistance;
                canvas.sortingOrder = SortingOrder;
                EditorUtility.SetDirty(canvas);
                PlaceCloseButton(bindings.closeHub.button, bindings.card);
                EditorUtility.SetDirty(bindings.closeHub.button);

                foreach (GameObject obsolete in new[] { previousButton, nextButton, settingsButton })
                {
                    if (obsolete == null) continue;
                    Button button = obsolete.GetComponent<Button>();
                    ClearPersistentListeners(button);
                    UnityEngine.Object.DestroyImmediate(obsolete);
                }

                ValidateProductionRoot(root, bridgeHost, controller, bindings, returnButton, c);
                if (HashFile(mainFile) != mainHash || HashFile(metaFile) != metaHash
                    || HashFile(previewFile) != previewHash)
                    throw new IOException("A scene file changed during corrections; refusing to save.");
                EditorSceneManager.MarkSceneDirty(c.main);
                if (!EditorSceneManager.SaveScene(c.main, MainScene))
                    throw new IOException("Unity could not save mainGame corrections. Recoverable backup: " + backup);
                savedHash = HashFile(mainFile);
                saved = true;
                if (c.main.isDirty || HashFile(metaFile) != metaHash || HashFile(previewFile) != previewHash)
                    throw new IOException("Saved correction validation failed. Recoverable backup: " + backup);
                Debug.Log("[WordListHubProductionMergeTool] Corrected only production Hub Canvas camera, Challenge ruby binding, obsolete controls, and Close placement. Preview/meta unchanged. Backup: " + backup);
            }
            catch (Exception failure)
            {
                bool filesStillOwned = HashFile(metaFile) == metaHash && HashFile(previewFile) == previewHash;
                if (saved && filesStillOwned && HashFile(mainFile) == savedHash)
                {
                    File.Copy(Path.Combine(backup, Path.GetFileName(mainFile)), mainFile, true);
                    if (HashFile(mainFile) == mainHash)
                        ReloadHashVerifiedMainGame(c.main, priorActiveScene, backup);
                    else
                        Debug.LogError("[WordListHubProductionMergeTool] Could not restore the verified pre-correction scene file. Backup: " + backup);
                }
                else if (!saved && filesStillOwned && HashFile(mainFile) == mainHash)
                    ReloadHashVerifiedMainGame(c.main, priorActiveScene, backup);
                else
                    Debug.LogError("[WordListHubProductionMergeTool] Scene changed outside the guarded correction path; left it untouched. Backup: " + backup);
                Debug.LogError("[WordListHubProductionMergeTool] Targeted Hub correction failed. Backup: " + backup + "\n" + failure);
                throw;
            }
        }

        private static GameObject FindUniqueOwnedObject(GameObject root, string name)
        {
            GameObject[] matches = root.GetComponentsInChildren<Transform>(true)
                .Where(transform => transform.gameObject.name == name)
                .Select(transform => transform.gameObject).ToArray();
            if (matches.Length > 1)
                throw new InvalidOperationException("Multiple objects named " + name + " exist under the owned production Canvas.");
            return matches.Length == 0 ? null : matches[0];
        }

        private static void RequireOwnedObject(GameObject candidate, Transform expectedOwner, string label)
        {
            if (candidate != null && !IsChildOf(candidate.transform, expectedOwner))
                throw new InvalidOperationException("Refusing to remove " + label + " outside its expected production owner.");
        }

        private static void ReloadHashVerifiedMainGame(Scene main, Scene priorActiveScene, string backup)
        {
            if (!EditorSceneManager.CloseScene(main, true))
                throw new InvalidOperationException("Unity refused to discard the tool's unsaved in-memory correction. Backup: " + backup);
            Scene restored = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Additive);
            if (!restored.IsValid() || !restored.isLoaded || restored.isDirty)
                throw new InvalidOperationException("Could not reload the hash-verified saved mainGame. Backup: " + backup);
            Scene active = priorActiveScene.Equals(main) ? restored : priorActiveScene;
            if (active.IsValid() && active.isLoaded && !SceneManager.GetActiveScene().Equals(active))
                SceneManager.SetActiveScene(active);
        }

        /// <summary>Backs up target scene+meta, clones authored UI, validates, then saves mainGame only.</summary>
        public static void ApplyMainGameMerge()
        {
            CheckEditorState();
            PreflightMainGame();
            Context c = OpenAndValidate();
            string mainFile = ProjectFile(MainScene), previewFile = ProjectFile(PreviewScene);
            string metaFile = mainFile + ".meta";
            string mainHash = HashFile(mainFile), previewHash = HashFile(previewFile), metaHash = HashFile(metaFile);
            string backup = BackupTarget(mainFile, metaFile, mainHash, metaHash, previewHash);
            Dictionary<string, string> before = CaptureExistingObjects(c);
            string uiInvariant = SerializedPropertiesExcept(c.uiController, "wordListHubBridge");
            string panelJson = EditorJsonUtility.ToJson(c.levelPanel);
            string coordinatorJson = EditorJsonUtility.ToJson(c.coordinator);
            string completionId = Id(c.completion), completionTexts = CompletionTextRefs(c.completion);
            List<Button> levelButtons = FindLevelButtons(c.main, c.uiController);
            List<GameObject> oldOwned = FindOwnedRoots(c);
            GameObject productionRoot = null, bridgeHost = null;
            Button returnButton = null;

            Scene priorActiveScene = SceneManager.GetActiveScene();
            bool saveCompleted = false;
            try
            {
                if (!SceneManager.GetActiveScene().Equals(c.main) && !SceneManager.SetActiveScene(c.main))
                    throw new InvalidOperationException("Could not activate mainGame for merge.");
                productionRoot = CloneCanvas(c, out WordListHubMockupController controller,
                    out WordListHubSceneBindings bindings);
                BuildProductionChromeAndLibraryControls(bindings);
                BuildChallengePage(bindings);
                returnButton = CloneReturnButton(bindings.library.play.button, c.completion.transform);
                bridgeHost = CreateBridgeHost(c, controller);
                WordListHubGameBridge bridge = bridgeHost.GetComponent<WordListHubGameBridge>();
                SetSerialized(c.uiController, "wordListHubBridge", bridge);
                EditorUtility.SetDirty(c.uiController);
                SetCompletionReferences(c.completion, returnButton, bridge);
                RemoveOnlyKnownDuplicateCallbacks(levelButtons, c.uiController);

                ValidateProductionRoot(productionRoot, bridgeHost, controller, bindings, returnButton, c);
                ValidatePreservedObjects(c, before, uiInvariant, panelJson, coordinatorJson,
                    completionId, completionTexts, levelButtons, productionRoot, bridgeHost, returnButton, oldOwned);

                // Replace only roots verified as owned by this tool; unrelated same-name roots fail preflight.
                foreach (GameObject old in oldOwned)
                    if (old != null) UnityEngine.Object.DestroyImmediate(old);
                productionRoot.name = ProductionCanvas;
                bridgeHost.name = ProductionBridge;
                productionRoot.SetActive(false); // bridge injects the backend before showing it at runtime

                if (HashFile(mainFile) != mainHash || HashFile(metaFile) != metaHash || HashFile(previewFile) != previewHash)
                    throw new IOException("A source or target scene file changed during merge; refusing to save.");
                EditorSceneManager.MarkSceneDirty(c.main);
                if (!EditorSceneManager.SaveScene(c.main, MainScene))
                    throw new IOException("Unity could not save mainGame. Recoverable backup: " + backup);
                saveCompleted = true;
                if (c.main.isDirty || HashFile(metaFile) != metaHash || HashFile(previewFile) != previewHash)
                    throw new IOException("Post-save validation failed. Recoverable backup: " + backup);
                Debug.Log("[WordListHubProductionMergeTool] Saved mainGame only; preview scene and .meta unchanged. Backup: " + backup);
            }
            catch (Exception failure)
            {
                if (!saveCompleted && File.Exists(mainFile) && HashFile(mainFile) == mainHash
                    && File.Exists(metaFile) && HashFile(metaFile) == metaHash)
                {
                    try
                    {
                        if (!EditorSceneManager.CloseScene(c.main, true))
                            throw new InvalidOperationException("Unity refused to discard the tool's unsaved mainGame changes.");
                        Scene restoredMain = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Additive);
                        if (!restoredMain.IsValid() || !restoredMain.isLoaded || restoredMain.isDirty)
                            throw new InvalidOperationException("Unity could not reload the unchanged clean mainGame from disk.");
                        Scene activeAfterRestore = priorActiveScene.Equals(c.main) ? restoredMain : priorActiveScene;
                        if (activeAfterRestore.IsValid() && activeAfterRestore.isLoaded
                            && !SceneManager.GetActiveScene().Equals(activeAfterRestore))
                            SceneManager.SetActiveScene(activeAfterRestore);
                        Debug.LogWarning("[WordListHubProductionMergeTool] Discarded only this tool's unsaved changes and reloaded the hash-verified clean mainGame. Backup: " + backup);
                    }
                    catch (Exception restoreFailure)
                    {
                        Debug.LogError("[WordListHubProductionMergeTool] Could not restore the clean in-memory target. Preserve and inspect it; backup: "
                            + backup + "\n" + restoreFailure);
                    }
                }
                else if (!saveCompleted)
                    Debug.LogError("[WordListHubProductionMergeTool] mainGame changed on disk during the failed merge; left the in-memory scene untouched for inspection. Backup: " + backup);
                Debug.LogError("[WordListHubProductionMergeTool] Merge failed. Backup: " + backup + "\n" + failure);
                throw;
            }
        }

        private static Context OpenAndValidate()
        {
            Scene main = SceneManager.GetSceneByPath(MainScene);
            if (!main.IsValid() || !main.isLoaded) main = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Additive);
            Scene preview = SceneManager.GetSceneByPath(PreviewScene);
            if (!preview.IsValid() || !preview.isLoaded) preview = EditorSceneManager.OpenScene(PreviewScene, OpenSceneMode.Additive);
            if (!main.IsValid() || !main.isLoaded || !preview.IsValid() || !preview.isLoaded)
                throw new InvalidOperationException("Could not open both existing scenes additively.");
            if (main.isDirty || preview.isDirty)
                throw new InvalidOperationException("mainGame or Hub preview has unsaved edits; preserve them, then retry.");
            var c = new Context
            {
                main = main,
                preview = preview,
                sourceCanvas = One<Canvas>(preview, x => x.name == PreviewCanvas, "preview Hub Canvas"),
                mainCamera = One<Camera>(main, x => x.gameObject.name == "Main Camera", "Main Camera"),
                menuClickAudioSource = FindMenuClickAudioSource(main),
                crtCamera = One<CRTCameraBehaviour>(main, _ => true, "Main Camera CRT effect"),
                sourceController = One<WordListHubMockupController>(preview, _ => true, "preview Hub controller"),
                sourceBindings = One<WordListHubSceneBindings>(preview, _ => true, "preview Hub bindings"),
                levelPanel = One<LevelPanelController>(main, _ => true, "LevelPanelController"),
                uiController = One<UIController>(main, _ => true, "UIController"),
                coordinator = One<GameCoordinator>(main, _ => true, "GameCoordinator"),
                completion = One<LevelCompletePanelController>(main, _ => true, "LevelCompletePanelController")
            };
            One<EventSystem>(main, _ => true, "existing EventSystem");
            if (c.crtCamera.GetComponent<Camera>() != c.mainCamera
                || !c.mainCamera.enabled || !c.crtCamera.enabled || !c.crtCamera.effectActive)
                throw new InvalidOperationException("Production Hub must use the enabled existing Main Camera CRT effect.");
            if (GetRef<WordListHubSceneBindings>(c.sourceController, "sceneBindings") != c.sourceBindings
                || c.sourceCanvas.GetComponent<GraphicRaycaster>() == null
                || c.sourceCanvas.GetComponentsInChildren<EventSystem>(true).Length != 0
                || c.sourceCanvas.GetComponentsInChildren<Camera>(true).Length != 0)
                throw new InvalidOperationException("Source Canvas must own controller/bindings and contain no EventSystem or Camera.");
            var b = c.sourceBindings;
            if (b.card == null || b.hubRoot == null || b.library.root == null || b.navLibrary.button == null
                || b.navChallenges.button == null || b.library.createList.button == null || b.library.play.button == null
                || b.challenges.root == null || b.challenges.dailyChallenge.button == null
                || b.challenges.weeklyChallenge.button == null || b.challenges.description == null || b.challenges.status == null)
                throw new InvalidOperationException("Authored Hub is missing required page or challenge style controls.");
            RequireInsideTransform(b.library.root.transform, b.hubRoot, "Library page root");
            RequireInsideTransform(b.challenges.root.transform, b.hubRoot, "preview Challenges page root");
            RequireInsideTransform(b.card, c.sourceCanvas.transform, "Hub card");
            RequireInsideTransform(b.navLibrary.button.transform, c.sourceCanvas.transform, "Library navigation style button");
            RequireInsideTransform(b.navChallenges.button.transform, c.sourceCanvas.transform, "Challenges navigation style button");
            RequireInsideTransform(b.library.createList.button.transform, b.library.root.transform, "Library action style button");
            RequireInsideTransform(b.challenges.dailyChallenge.button.transform, b.challenges.root.transform, "challenge style button");
            RequireInsideTransform(b.challenges.weeklyChallenge.button.transform, b.challenges.root.transform, "weekly style button");
            RequireLibraryPageBindings(b.library, b.library.root.transform, b.card);
            RequireInsideTransform(b.challenges.description.transform, b.challenges.root.transform, "challenge description");
            RequireInsideTransform(b.challenges.status.transform, b.challenges.root.transform, "challenge status");
            if (Components<EventSystem>(main).Length != 1
                || Components<Camera>(main).Count(x => x.gameObject.name == "Main Camera") != 1)
                throw new InvalidOperationException("mainGame must retain its single existing EventSystem and Main Camera.");
            FindLevelButtons(main, c.uiController);
            return c;
        }

        private static GameObject CloneCanvas(Context c, out WordListHubMockupController controller,
            out WordListHubSceneBindings bindings)
        {
            var staging = new GameObject("Production Hub Staging");
            staging.SetActive(false);
            SceneManager.MoveGameObjectToScene(staging, c.main);
            GameObject clone = UnityEngine.Object.Instantiate(c.sourceCanvas.gameObject, staging.transform, false);
            clone.name = ProductionCanvas + "_staging";
            clone.SetActive(false);
            clone.transform.SetParent(null, false);
            UnityEngine.Object.DestroyImmediate(staging);
            SceneManager.MoveGameObjectToScene(clone, c.main);
            Canvas canvas = clone.GetComponent<Canvas>();
            controller = clone.GetComponentInChildren<WordListHubMockupController>(true);
            bindings = clone.GetComponentInChildren<WordListHubSceneBindings>(true);
            if (canvas == null || clone.GetComponent<GraphicRaycaster>() == null || controller == null || bindings == null)
                throw new InvalidOperationException("Cloned authored Canvas is incomplete.");
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = c.mainCamera;
            canvas.planeDistance = HubCanvasPlaneDistance;
            canvas.sortingOrder = SortingOrder;
            SetSerialized(controller, "productionMode", true);
            SetSerialized(controller, "sceneBindings", bindings);
            bindings.hostRoot = clone;
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(bindings);
            return clone;
        }

        private static void BuildProductionChromeAndLibraryControls(WordListHubSceneBindings bindings)
        {
            var so = new SerializedObject(bindings);
            if (bindings.footer == null)
                throw new InvalidOperationException("The authored footer label is required on the production Canvas.");
            bindings.footer.text = string.Empty;
            EditorUtility.SetDirty(bindings.footer);
            SetRef(so, "openSettings.button", null);
            SetRef(so, "openSettings.label", null);
            Button close = CloneButton(bindings.navLibrary.button, bindings.card.parent,
                "ProductionCloseHubButton", "Close");
            PlaceCloseButton(close, bindings.card);
            SetRef(so, "closeHub.button", close);
            SetRef(so, "closeHub.label", ButtonLabel(close));

            Button createStyle = bindings.library.createList.button;
            Button import = CloneButton(createStyle, createStyle.transform.parent,
                "ProductionImportListButton", "Import");
            Button export = CloneButton(createStyle, createStyle.transform.parent,
                "ProductionExportListButton", "Export");
            PlaceToolbarButton(import, createStyle, 1);
            PlaceToolbarButton(export, createStyle, 2);
            SetRef(so, "library.importList.button", import);
            SetRef(so, "library.importList.label", ButtonLabel(import));
            SetRef(so, "library.exportList.button", export);
            SetRef(so, "library.exportList.label", ButtonLabel(export));
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bindings);
        }

        private static void BuildChallengePage(WordListHubSceneBindings bindings)
        {
            GameObject oldPage = bindings.challenges.root;
            GameObject libraryPage = bindings.library.root;
            GameObject page = UnityEngine.Object.Instantiate(libraryPage, bindings.hubRoot, false);
            page.name = "ProductionChallengesPage";
            page.SetActive(false);
            oldPage.SetActive(false);
            var so = new SerializedObject(bindings);
            SetRef(so, "challenges.root", page);
            ScrollRect challengeScroll = MapComponent(libraryPage.transform, page.transform, bindings.library.listScroll);
            SetRef(so, "challenges.listScroll", challengeScroll);
            SetRef(so, "challenges.listContent", MapTransform(libraryPage.transform, page.transform, bindings.library.listContent));
            Transform challengeListContent = MapTransform(libraryPage.transform, page.transform,
                bindings.library.listContent);
            if (challengeListContent == null)
                throw new InvalidOperationException("Could not map the authored Library list content into the cloned Challenges page.");
            GameObject challengeRowTemplateObject = UnityEngine.Object.Instantiate(
                bindings.library.rowTemplate.gameObject, challengeListContent, false);
            challengeRowTemplateObject.name = "ProductionChallengeRowTemplate";
            challengeRowTemplateObject.SetActive(false);
            WordListHubLibraryRowView challengeRowTemplate = challengeRowTemplateObject
                .GetComponent<WordListHubLibraryRowView>();
            if (challengeRowTemplate == null)
                throw new InvalidOperationException("Cloned Library row template style lacks WordListHubLibraryRowView.");
            SetRef(so, "challenges.rowTemplate", challengeRowTemplate);
            TMP_Text emptyNotice = CloneText(bindings.challenges.description,
                challengeScroll.viewport, "ProductionChallengeEmptyNotice", string.Empty);
            PlaceChallengeEmptyNotice(emptyNotice);
            SetRef(so, "challenges.emptyNotice", emptyNotice);
            SetRef(so, "challenges.detailTitle", MapComponent(libraryPage.transform, page.transform, bindings.library.detailTitle));
            SetRef(so, "challenges.detailRecords", MapComponent(libraryPage.transform, page.transform, bindings.library.detailDescription));
            SetRef(so, "challenges.detailMetadata", MapComponent(libraryPage.transform, page.transform, bindings.library.detailMetadata));
            SetRef(so, "challenges.detailWordsLabel", MapComponent(libraryPage.transform, page.transform, bindings.library.detailWordsLabel));
            SetRef(so, "challenges.detailWords", MapComponent(libraryPage.transform, page.transform, bindings.library.detailWords));
            SetRubyPreviewBinding(so, "challenges.rubyWords", libraryPage.transform, page.transform,
                bindings.libraryRubyWords);
            SetRef(so, "challenges.detailPane", MapTransform(libraryPage.transform, page.transform, bindings.library.detailPane));
            SetRef(so, "challenges.play.button", MapComponent(libraryPage.transform, page.transform, bindings.library.play.button));
            SetRef(so, "challenges.play.label", MapComponent(libraryPage.transform, page.transform, bindings.library.play.label));
            SetRef(so, "challenges.copyToCustom.button", MapComponent(libraryPage.transform, page.transform, bindings.library.copyEditWords.button));
            SetRef(so, "challenges.copyToCustom.label", MapComponent(libraryPage.transform, page.transform, bindings.library.copyEditWords.label));

            TMP_Text description = CloneText(bindings.challenges.description,
                page.transform, "ProductionChallengeDescription", string.Empty);
            PlaceChallengeDescription(description);
            SetRef(so, "challenges.description", description);
            SetRef(so, "challenges.status", MapComponent(libraryPage.transform, page.transform,
                bindings.library.detailStatus));
            so.ApplyModifiedPropertiesWithoutUndo();
            HideLibraryOnlyControls(bindings, page.transform);
            foreach (Button button in page.GetComponentsInChildren<Button>(true)) ClearPersistentListeners(button);
            TMP_Text pageTitle = page.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(text => text != null && text.name == "ListPaneTitle");
            if (pageTitle != null) pageTitle.text = "Challenges";
            int index = bindings.pages.FindIndex(x => x.page == WordListHubPage.Challenges);
            if (index < 0) bindings.pages.Add(new WordListHubSceneBindings.PageBinding { page = WordListHubPage.Challenges, root = page });
            else
            {
                if (bindings.pages.FindLastIndex(x => x.page == WordListHubPage.Challenges) != index)
                    throw new InvalidOperationException("Duplicate Challenges page entries exist.");
                bindings.pages[index].root = page;
            }
            EditorUtility.SetDirty(bindings);
        }

        private static void HideLibraryOnlyControls(WordListHubSceneBindings bindings, Transform pageRoot)
        {
            WordListHubSceneBindings.LibraryPageBinding library = bindings.library;
            Transform sourceRoot = library.root.transform;
            HideMapped(sourceRoot, pageRoot, library.allLists.button);
            HideMapped(sourceRoot, pageRoot, library.favorites.button);
            HideMapped(sourceRoot, pageRoot, library.searchInput);
            HideMapped(sourceRoot, pageRoot, library.languageFilter);
            HideMapped(sourceRoot, pageRoot, library.playedFilter);
            HideMapped(sourceRoot, pageRoot, library.sortFilter);
            HideMapped(sourceRoot, pageRoot, library.directionFilter);
            HideMapped(sourceRoot, pageRoot, library.filtersMenu.toggleButton);
            HideMapped(sourceRoot, pageRoot, library.filtersMenu.panel);
            HideMapped(sourceRoot, pageRoot, library.createList.button);
            HideMapped(sourceRoot, pageRoot, library.importList.button);
            HideMapped(sourceRoot, pageRoot, library.exportList.button);
            HideMapped(sourceRoot, pageRoot, library.delete.button);
            HideMapped(sourceRoot, pageRoot, library.edit.button);
            HideMapped(sourceRoot, pageRoot, library.publish.button);
            HideMapped(sourceRoot, pageRoot, library.unpublish.button);
            if (library.groups != null)
                foreach (WordListHubSceneBindings.GroupBinding group in library.groups)
                {
                    if (group == null) continue;
                    HideMapped(sourceRoot, pageRoot, group.button);
                    HideMapped(sourceRoot, pageRoot, group.label);
                }
            if (library.authoredRows != null)
                foreach (WordListHubSceneBindings.LibraryRowBinding row in library.authoredRows)
                    if (row != null && row.root != null)
                    {
                        Transform mapped = MapTransform(sourceRoot, pageRoot, row.root.transform);
                        if (mapped == null) throw new InvalidOperationException("An authored Library row is outside its source page.");
                        mapped.gameObject.SetActive(false);
                    }
            if (library.emptyNotice != null)
                HideMapped(sourceRoot, pageRoot, library.emptyNotice);

            Transform clonedListContent = MapTransform(sourceRoot, pageRoot, library.listContent);
            Transform clonedDetailPane = MapTransform(sourceRoot, pageRoot, library.detailPane);
            if (clonedListContent == null || clonedDetailPane == null
                || !IsChildOf(clonedListContent, pageRoot) || !IsChildOf(clonedDetailPane, pageRoot))
                throw new InvalidOperationException("The cloned challenge page lacks a Library list/detail style shell.");
        }

        private static void HideMapped(Transform sourceRoot, Transform cloneRoot, Component source)
        {
            if (source == null) return;
            Component mapped = MapComponent(sourceRoot, cloneRoot, source);
            if (mapped == null)
                throw new InvalidOperationException("A Library-only control could not be mapped into the cloned Challenges page: " + source.name);
            mapped.gameObject.SetActive(false);
        }

        private static void HideMapped(Transform sourceRoot, Transform cloneRoot, GameObject source)
        {
            if (source == null) return;
            Transform mapped = MapTransform(sourceRoot, cloneRoot, source.transform);
            if (mapped == null)
                throw new InvalidOperationException("A Library-only object could not be mapped into the cloned Challenges page: " + source.name);
            mapped.gameObject.SetActive(false);
        }

        private static void Hide(Component component)
        {
            if (component != null) component.gameObject.SetActive(false);
        }

        private static void Hide(Button button)
        {
            if (button != null) button.gameObject.SetActive(false);
        }

        private static GameObject CreateBridgeHost(Context c, WordListHubMockupController controller)
        {
            var host = new GameObject(ProductionBridge + "_staging");
            SceneManager.MoveGameObjectToScene(host, c.main);
            WordListHubGameBridge bridge = host.AddComponent<WordListHubGameBridge>();
            SetSerialized(bridge, "hubController", controller);
            SetSerialized(bridge, "levelPanelController", c.levelPanel);
            SetSerialized(bridge, "gameCoordinator", c.coordinator);
            SetSerialized(bridge, "levelCompletePanel", c.completion);
            SetSerialized(bridge, "menuClickAudioSource", c.menuClickAudioSource);
            EditorUtility.SetDirty(bridge);
            return host;
        }

        private static Button CloneReturnButton(Button style, Transform parent)
        {
            Button button = CloneButton(style, parent, ReturnButtonName, "Return to Challenges");
            RectTransform rect = button.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .1f);
                rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero;
            }
            button.transform.SetAsLastSibling();
            return button;
        }

        private static void SetCompletionReferences(LevelCompletePanelController panel,
            Button returnButton, WordListHubGameBridge bridge)
        {
            var so = new SerializedObject(panel);
            SetRef(so, "returnToChallengesButton", returnButton);
            SetRef(so, "wordListHubBridge", bridge);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(panel);
        }

        private static void ValidateProductionRoot(GameObject root, GameObject bridgeHost,
            WordListHubMockupController controller, WordListHubSceneBindings bindings,
            Button returnButton, Context c)
        {
            WordListHubGameBridge bridge = bridgeHost.GetComponent<WordListHubGameBridge>();
            Canvas canvas = root.GetComponent<Canvas>();
            WordListHubGameBridge uiBridge = GetRef<WordListHubGameBridge>(c.uiController, "wordListHubBridge");
            Button completionReturn = GetRef<Button>(c.completion, "returnToChallengesButton");
            WordListHubGameBridge completionBridge = GetRef<WordListHubGameBridge>(c.completion, "wordListHubBridge");
            var failed = new List<string>();
            if (canvas == null) failed.Add("canvas missing");
            if (root.transform.parent != null) failed.Add("production Canvas transform.parent is not null");
            if (canvas != null && !canvas.isRootCanvas) failed.Add("Canvas.isRootCanvas=false");
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceCamera)
                failed.Add("Canvas.renderMode=" + canvas.renderMode + " expected ScreenSpaceCamera");
            if (canvas != null && canvas.worldCamera != c.mainCamera)
                failed.Add("Canvas.worldCamera=" + Describe(canvas.worldCamera) + " expected " + Describe(c.mainCamera));
            if (canvas != null && Mathf.Abs(canvas.planeDistance - HubCanvasPlaneDistance) > 0.01f)
                failed.Add("Canvas.planeDistance=" + canvas.planeDistance + " expected " + HubCanvasPlaneDistance);
            if (canvas != null && canvas.sortingOrder != SortingOrder)
                failed.Add("Canvas.sortingOrder=" + canvas.sortingOrder + " expected " + SortingOrder);
            if (root.activeSelf) failed.Add("root.activeSelf=true");
            if (!controller.IsProductionMode) failed.Add("controller.productionMode=false");
            if (bindings.hostRoot != root)
                failed.Add("bindings.hostRoot=" + Describe(bindings.hostRoot) + " expected " + Describe(root));
            if (bridge == null) failed.Add("bridge component missing");
            if (bridge != null && bridge.HubController != controller)
                failed.Add("bridge.hubController=" + Describe(bridge.HubController) + " expected " + Describe(controller));
            if (bridge != null && bridge.MenuClickAudioSource != c.menuClickAudioSource)
                failed.Add("bridge.menuClickAudioSource=" + Describe(bridge.MenuClickAudioSource)
                    + " expected " + Describe(c.menuClickAudioSource));
            if (uiBridge != bridge) failed.Add("UIController.wordListHubBridge=" + Describe(uiBridge) + " expected " + Describe(bridge));
            if (completionReturn != returnButton)
                failed.Add("completion.returnToChallengesButton=" + Describe(completionReturn) + " expected " + Describe(returnButton));
            if (completionBridge != bridge)
                failed.Add("completion.wordListHubBridge=" + Describe(completionBridge) + " expected " + Describe(bridge));
            if (failed.Count != 0)
                throw new InvalidOperationException("Production Canvas/bridge/completion validation failed: "
                    + string.Join("; ", failed));
            if (root.GetComponentsInChildren<EventSystem>(true).Length != 0
                || root.GetComponentsInChildren<Camera>(true).Length != 0
                || Components<EventSystem>(c.main).Length != 1)
                throw new InvalidOperationException("The cloned Hub duplicated an EventSystem or camera.");
            if (bindings.closeHub.button == null || bindings.openSettings.button != null
                || bindings.openSettings.label != null
                || bindings.library.importList.button == null || bindings.library.exportList.button == null)
                throw new InvalidOperationException("Production close/settings/import/export bindings are invalid.");
            if (bindings.closeHub.button.transform.parent != bindings.card.parent)
                throw new InvalidOperationException("Production close button must be outside the card on its unclipped parent.");
            ValidateClosePlacement(bindings.closeHub.button, bindings.card);
            RequireInsideTransform(bindings.library.importList.button.transform, root.transform, "production import button");
            RequireInsideTransform(bindings.library.exportList.button.transform, root.transform, "production export button");
            ValidateChallengePageBindings(bindings);
        }

        private static List<GameObject> FindOwnedRoots(Context c)
        {
            GameObject canvas = RootNamed(c.main, ProductionCanvas);
            GameObject bridgeHost = RootNamed(c.main, ProductionBridge);
            if (canvas == null && bridgeHost == null)
            {
                bool orphanReturn = c.completion.GetComponentsInChildren<Transform>(true)
                    .Any(x => x.gameObject.name == ReturnButtonName);
                bool orphanBridge = GetRef<WordListHubGameBridge>(c.uiController, "wordListHubBridge") != null
                    || GetRef<WordListHubGameBridge>(c.completion, "wordListHubBridge") != null;
                if (orphanReturn || orphanBridge)
                    throw new InvalidOperationException("Production Hub references exist without both owned roots; refusing to add duplicates.");
                return new List<GameObject>();
            }
            if (canvas == null || bridgeHost == null)
                throw new InvalidOperationException("Partial same-named production roots exist; refusing replacement.");
            var controller = canvas.GetComponentInChildren<WordListHubMockupController>(true);
            var bindings = canvas.GetComponentInChildren<WordListHubSceneBindings>(true);
            var bridge = bridgeHost.GetComponent<WordListHubGameBridge>();
            Button returnButton = GetRef<Button>(c.completion, "returnToChallengesButton");
            if (controller == null || bindings == null || !controller.IsProductionMode || bindings.hostRoot != canvas
                || bridge == null || bridge.HubController != controller
                || GetRef<WordListHubGameBridge>(c.completion, "wordListHubBridge") != bridge
                || returnButton == null || returnButton.gameObject.name != ReturnButtonName
                || returnButton.transform.parent != c.completion.transform)
                throw new InvalidOperationException("Same-named root is not owned by this tool; refusing to replace it.");
            return new List<GameObject> { canvas, bridgeHost, returnButton.gameObject };
        }

        private static GameObject RootNamed(Scene scene, string name)
        {
            GameObject[] matches = scene.GetRootGameObjects().Where(x => x.name == name).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("Multiple roots named " + name + " exist.");
            return matches.Length == 0 ? null : matches[0];
        }

        private static Dictionary<string, string> CaptureExistingObjects(Context c)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            List<GameObject> oldRoots = FindOwnedRoots(c);
            foreach (GameObject go in AllObjects(c.main))
            {
                if (oldRoots.Any(root => IsChildOf(go.transform, root.transform))) continue;
                string parent = go.transform.parent == null ? "root" : Id(go.transform.parent.gameObject);
                result[Id(go)] = go.name + "|" + go.activeSelf + "|" + go.layer + "|" + go.tag + "|" + parent
                    + "|" + go.transform.GetSiblingIndex() + "|" + go.transform.localPosition + "|" + go.transform.localRotation
                    + "|" + go.transform.localScale + "|" + string.Join(",", go.GetComponents<Component>()
                        .Select(x => x == null ? "missing" : x.GetType().FullName));
            }
            return result;
        }

        private static void ValidatePreservedObjects(Context c, Dictionary<string, string> before,
            string uiInvariant, string panelJson, string coordinatorJson, string completionId,
            string completionTexts, List<Button> levelButtons, GameObject root, GameObject bridge,
            Button returnButton, List<GameObject> oldRoots)
        {
            List<GameObject> objects = AllObjects(c.main);
            foreach (var entry in before)
            {
                GameObject go = objects.FirstOrDefault(x => Id(x) == entry.Key);
                if (go == null) throw new InvalidOperationException("Existing mainGame object disappeared: " + entry.Key);
                string parent = go.transform.parent == null ? "root" : Id(go.transform.parent.gameObject);
                string signature = go.name + "|" + go.activeSelf + "|" + go.layer + "|" + go.tag + "|" + parent
                    + "|" + go.transform.GetSiblingIndex() + "|" + go.transform.localPosition + "|" + go.transform.localRotation
                    + "|" + go.transform.localScale + "|" + string.Join(",", go.GetComponents<Component>()
                        .Select(x => x == null ? "missing" : x.GetType().FullName));
                if (signature != entry.Value) throw new InvalidOperationException("Existing object changed: " + go.name);
            }
            HashSet<string> oldIds = new HashSet<string>(before.Keys, StringComparer.Ordinal);
            foreach (GameObject go in objects)
                if (!oldIds.Contains(Id(go)) && !oldRoots.Any(old => IsChildOf(go.transform, old.transform))
                    && !IsChildOf(go.transform, root.transform) && !IsChildOf(go.transform, bridge.transform)
                    && !IsChildOf(go.transform, returnButton.transform))
                    throw new InvalidOperationException("Unexpected new mainGame object outside owned roots: " + go.name);
            if (SerializedPropertiesExcept(c.uiController, "wordListHubBridge") != uiInvariant
                || EditorJsonUtility.ToJson(c.levelPanel) != panelJson
                || EditorJsonUtility.ToJson(c.coordinator) != coordinatorJson
                || Id(c.completion) != completionId || CompletionTextRefs(c.completion) != completionTexts
                || Components<EventSystem>(c.main).Length != 1
                || Components<Camera>(c.main).Count(x => x.gameObject.name == "Main Camera") != 1)
                throw new InvalidOperationException("Existing gameplay manager refs, completion text refs, EventSystem, or camera changed.");
            foreach (Button button in levelButtons)
                if (CountPlayCallbacks(button, c.uiController) != 1)
                    throw new InvalidOperationException("Known duplicate LevelBtn callback was not reduced to one.");
        }

        private static List<Button> FindLevelButtons(Scene scene, UIController ui)
        {
            List<Button> buttons = AllObjects(scene).Where(x => x.name == "LevelBtn" && x.activeSelf)
                .Select(x => x.GetComponent<Button>()).Where(x => x != null).ToList();
            if (buttons.Count != 2 || buttons.Any(x => CountPlayCallbacks(x, ui) < 1 || CountPlayCallbacks(x, ui) > 2))
                throw new InvalidOperationException("Expected two active LevelBtn controls with one known duplicate callback each.");
            return buttons;
        }

        private static int CountPlayCallbacks(Button button, UIController ui) =>
            Enumerable.Range(0, button.onClick.GetPersistentEventCount()).Count(i =>
                button.onClick.GetPersistentTarget(i) == ui
                && button.onClick.GetPersistentMethodName(i) == "OnPlayBtnClicked");

        private static void RemoveOnlyKnownDuplicateCallbacks(List<Button> buttons, UIController ui)
        {
            foreach (Button button in buttons)
            {
                List<int> indices = Enumerable.Range(0, button.onClick.GetPersistentEventCount())
                    .Where(i => button.onClick.GetPersistentTarget(i) == ui
                        && button.onClick.GetPersistentMethodName(i) == "OnPlayBtnClicked").ToList();
                if (indices.Count == 2) UnityEventTools.RemovePersistentListener(button.onClick, indices[1]);
                else if (indices.Count != 1) throw new InvalidOperationException("Unknown LevelBtn callbacks; refusing to edit them.");
                EditorUtility.SetDirty(button);
            }
        }

        private static Button CloneButton(Button style, Transform parent, string name, string text)
        {
            GameObject go = UnityEngine.Object.Instantiate(style.gameObject, parent, false);
            go.name = name;
            Button button = go.GetComponent<Button>();
            if (button == null) throw new InvalidOperationException("Authored button clone lacks Button: " + name);
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(button.onClick, i);
            TMP_Text label = ButtonLabel(button);
            if (label != null) label.text = text;
            return button;
        }

        private static TMP_Text CloneText(TMP_Text style, Transform parent, string name, string text)
        {
            GameObject go = UnityEngine.Object.Instantiate(style.gameObject, parent, false);
            go.name = name;
            TMP_Text result = go.GetComponent<TMP_Text>() ?? go.GetComponentInChildren<TMP_Text>(true);
            if (result == null) throw new InvalidOperationException("Authored text clone lacks TMP_Text: " + name);
            result.text = text;
            return result;
        }

        private static TMP_Text ButtonLabel(Button button) => button.GetComponentInChildren<TMP_Text>(true);

        private static void ClearPersistentListeners(Button button)
        {
            if (button == null) return;
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(button.onClick, i);
        }

        private static void PlaceCloseButton(Button button, RectTransform card)
        {
            RectTransform rect = button != null ? button.transform as RectTransform : null;
            if (rect == null || card == null || card.parent == null)
                throw new InvalidOperationException("Production close placement requires the authored card and a parent RectTransform.");
            rect.SetParent(card.parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(CloseButtonSize, CloseButtonSize);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            LayoutElement layout = button.GetComponent<LayoutElement>();
            if (layout == null) layout = button.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            PositionCloseButton(rect, card);
            rect.SetAsLastSibling();
        }

        private static void PositionCloseButton(RectTransform closeRect, RectTransform card)
        {
            if (closeRect == null || card == null || closeRect.parent != card.parent)
                throw new InvalidOperationException("Production close button must be a sibling of the card.");
            closeRect.localPosition = WordListHubMockupController.CalculateProductionCloseButtonLocalPosition(
                card, CloseButtonGap, CloseButtonSize);
        }

        private static void ValidateClosePlacement(Button button, RectTransform card)
        {
            RectTransform closeRect = button != null ? button.transform as RectTransform : null;
            if (closeRect == null || card == null || closeRect.parent != card.parent
                || Mathf.Abs(closeRect.rect.width - CloseButtonSize) > 0.5f
                || Mathf.Abs(closeRect.rect.height - CloseButtonSize) > 0.5f)
                throw new InvalidOperationException("Production Close must be a 44x44 sibling outside the card.");
            Vector3 cardTopRightInParent = card.localPosition + card.localRotation * Vector3.Scale(
                new Vector3(card.rect.xMax, card.rect.yMax, 0f), card.localScale);
            Vector3 expectedCenter = cardTopRightInParent
                + new Vector3(CloseButtonGap + CloseButtonSize * 0.5f, -CloseButtonSize * 0.5f, 0f);
            if (Vector3.Distance(closeRect.localPosition, expectedCenter) > 0.5f)
                throw new InvalidOperationException("Production Close must sit 8 units right of the card and align with its top edge.");
        }

        private static void PlaceToolbarButton(Button button, Button prototype, int offsetIndex)
        {
            RectTransform rect = button != null ? button.transform as RectTransform : null;
            RectTransform source = prototype != null ? prototype.transform as RectTransform : null;
            if (rect == null || source == null) return;
            rect.anchorMin = source.anchorMin;
            rect.anchorMax = source.anchorMax;
            rect.pivot = source.pivot;
            rect.sizeDelta = source.sizeDelta;
            float width = source.rect.width > 0f ? source.rect.width : source.sizeDelta.x;
            rect.anchoredPosition = source.anchoredPosition
                - new Vector2((width + 8f) * offsetIndex, 0f);
        }

        private static void PlaceChallengeDescription(TMP_Text text)
        {
            RectTransform rect = text != null ? text.transform as RectTransform : null;
            if (rect == null) return;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -4f);
            rect.sizeDelta = new Vector2(-48f, 32f);
            text.raycastTarget = false;
        }

        private static void PlaceChallengeEmptyNotice(TMP_Text text)
        {
            RectTransform rect = text != null ? text.transform as RectTransform : null;
            if (rect == null) return;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = new Vector2(12f, 12f);
            rect.offsetMax = new Vector2(-12f, -12f);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.gameObject.SetActive(false);
        }

        private static void RequireLibraryPageBindings(
            WordListHubSceneBindings.LibraryPageBinding page, Transform pageRoot, Transform authoredStyleRoot)
        {
            if (page.listScroll == null || page.listContent == null || page.rowTemplate == null
                || page.detailPane == null || page.detailTitle == null
                || page.detailDescription == null || page.detailMetadata == null || page.detailWordsLabel == null
                || page.detailWords == null || page.play.button == null || page.play.label == null
                || page.copyEditWords.button == null || page.copyEditWords.label == null)
                throw new InvalidOperationException("The authored Library page is missing a renderer-required scroll, row, detail, Play, or Copy binding.");
            RequireInsideTransform(page.listScroll.transform, pageRoot, "Library list scroll");
            RequireInsideTransform(page.listContent, page.listScroll.transform, "Library list content");
            RequireInsideTransform(page.rowTemplate.transform, authoredStyleRoot, "Library row template style");
            if (page.emptyNotice != null)
                RequireInsideTransform(page.emptyNotice.transform, page.listScroll.transform, "Library empty notice");
            RequireInsideTransform(page.detailPane, pageRoot, "Library detail pane");
            RequireInsideTransform(page.detailTitle.transform, page.detailPane, "Library detail title");
            RequireInsideTransform(page.detailDescription.transform, page.detailPane, "Library detail description");
            RequireInsideTransform(page.detailMetadata.transform, page.detailPane, "Library detail metadata");
            RequireInsideTransform(page.detailWordsLabel.transform, page.detailPane, "Library detail words label");
            RequireInsideTransform(page.detailWords.transform, page.detailPane, "Library detail words");
            RequireInsideTransform(page.play.button.transform, page.detailPane, "Library Play action");
            RequireInsideTransform(page.play.label.transform, page.detailPane, "Library Play label");
            RequireInsideTransform(page.copyEditWords.button.transform, page.detailPane, "Library Copy action");
            RequireInsideTransform(page.copyEditWords.label.transform, page.detailPane, "Library Copy label");
        }

        private static void ValidateChallengePageBindings(WordListHubSceneBindings bindings)
        {
            WordListHubSceneBindings.ChallengesPageBinding page = bindings.challenges;
            if (page.root == null || page.listScroll == null || page.listContent == null || page.rowTemplate == null
                || page.emptyNotice == null || page.detailTitle == null
                || page.detailRecords == null || page.detailMetadata == null || page.detailWordsLabel == null
                || page.detailWords == null || page.detailPane == null || page.play.button == null
                || page.play.label == null || page.copyToCustom.button == null || page.copyToCustom.label == null
                || page.description == null || page.status == null || page.rubyWords == null
                || page.rubyWords.root == null || page.rubyWords.content == null
                || page.rubyWords.scrollRect == null || page.rubyWords.viewport == null)
                throw new InvalidOperationException("Production Challenges is missing a renderer-required binding.");
            Transform root = page.root.transform;
            if (page.rowTemplate.gameObject.activeSelf)
                throw new InvalidOperationException("Production Challenges row template must remain inactive.");
            RequireInsideTransform(page.listScroll.transform, root, "Challenges list scroll");
            RequireInsideTransform(page.listContent, page.listScroll.transform, "Challenges list content");
            RequireInsideTransform(page.rowTemplate.transform, page.listContent, "Challenges row template");
            RequireInsideTransform(page.emptyNotice.transform, page.listScroll.transform, "Challenges empty notice");
            RequireInsideTransform(page.detailPane, root, "Challenges detail pane");
            RequireInsideTransform(page.detailTitle.transform, page.detailPane, "Challenges detail title");
            RequireInsideTransform(page.detailRecords.transform, page.detailPane, "Challenges detail records");
            RequireInsideTransform(page.detailMetadata.transform, page.detailPane, "Challenges detail metadata");
            RequireInsideTransform(page.detailWordsLabel.transform, page.detailPane, "Challenges detail words label");
            RequireInsideTransform(page.detailWords.transform, page.detailPane, "Challenges detail words");
            RequireInsideTransform(page.rubyWords.root, page.detailPane, "Challenges ruby words root");
            RequireInsideTransform(page.rubyWords.content, page.rubyWords.root, "Challenges ruby words content");
            RequireInsideTransform(page.rubyWords.viewport, page.detailPane, "Challenges ruby words viewport");
            RequireInsideTransform(page.rubyWords.scrollRect.transform, page.detailPane, "Challenges ruby words scroll rect");
            RequireInsideTransform(page.play.button.transform, page.detailPane, "Challenges Play action");
            RequireInsideTransform(page.play.label.transform, page.detailPane, "Challenges Play label");
            RequireInsideTransform(page.copyToCustom.button.transform, page.detailPane, "Challenges Copy action");
            RequireInsideTransform(page.copyToCustom.label.transform, page.detailPane, "Challenges Copy label");
            RequireInsideTransform(page.description.transform, root, "Challenges description");
            RequireInsideTransform(page.status.transform, root, "Challenges status");
            if (page.detailWords.enabled)
                throw new InvalidOperationException("Challenges legacy word TMP must remain disabled in production.");
            int entries = bindings.pages.Count(x => x.page == WordListHubPage.Challenges && x.root == page.root);
            if (entries != 1 || bindings.pages.Count(x => x.page == WordListHubPage.Challenges) != 1)
                throw new InvalidOperationException("Production challenge page does not match exactly one Challenges page entry.");
        }

        private static void RequireInsideTransform(Transform candidate, Transform root, string label)
        {
            if (candidate == null || root == null || !IsChildOf(candidate, root))
                throw new InvalidOperationException(label + " is missing or is not owned by its expected page/root.");
        }

        private static T MapComponent<T>(Transform root, Transform cloneRoot, T component) where T : Component
        {
            Transform mapped = component == null ? null : MapTransform(root, cloneRoot, component.transform);
            return mapped == null ? null : mapped.GetComponent<T>();
        }

        private static Transform MapTransform(Transform root, Transform cloneRoot, Transform source)
        {
            var path = new Stack<int>();
            Transform current = source;
            while (current != null && current != root) { path.Push(current.GetSiblingIndex()); current = current.parent; }
            if (current != root) return null;
            current = cloneRoot;
            while (path.Count > 0) { int i = path.Pop(); if (i >= current.childCount) return null; current = current.GetChild(i); }
            return current;
        }

        private static void SetSerialized(UnityEngine.Object target, string path, bool value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(path);
            if (p == null) throw new InvalidOperationException("Missing serialized field " + path);
            p.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerialized(UnityEngine.Object target, string path, UnityEngine.Object value)
        {
            var so = new SerializedObject(target); SetRef(so, path, value); so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetRef(SerializedObject so, string path, UnityEngine.Object value)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p == null) throw new InvalidOperationException("Missing serialized binding " + path);
            p.objectReferenceValue = value;
        }

        private static void SetRubyPreviewBinding(
            SerializedObject bindings,
            string destinationPath,
            Transform libraryPage,
            Transform challengePage,
            WordListHubSceneBindings.RubyPreviewBinding source)
        {
            if (source == null)
                throw new InvalidOperationException("The authored Library ruby-word binding is missing.");
            SetRef(bindings, destinationPath + ".root",
                MapTransform(libraryPage, challengePage, source.root) as RectTransform);
            SetRef(bindings, destinationPath + ".viewport",
                MapTransform(libraryPage, challengePage, source.viewport) as RectTransform);
            SetRef(bindings, destinationPath + ".content",
                MapTransform(libraryPage, challengePage, source.content) as RectTransform);
            SetRef(bindings, destinationPath + ".scrollRect",
                MapComponent(libraryPage, challengePage, source.scrollRect));
            SetRef(bindings, destinationPath + ".scrollbar",
                MapComponent(libraryPage, challengePage, source.scrollbar));

            SerializedProperty fontScale = bindings.FindProperty(destinationPath + ".previewFontScale");
            SerializedProperty sourceEntries = bindings.FindProperty("libraryRubyWords.authoredEntries");
            SerializedProperty destinationEntries = bindings.FindProperty(destinationPath + ".authoredEntries");
            SerializedProperty destinationWords = bindings.FindProperty(destinationPath + ".previewWords");
            if (fontScale == null || sourceEntries == null || destinationEntries == null || destinationWords == null)
                throw new InvalidOperationException("The production challenge ruby-word binding is incomplete.");
            fontScale.floatValue = source.previewFontScale;
            destinationEntries.arraySize = sourceEntries.arraySize;
            for (int i = 0; i < sourceEntries.arraySize; i++)
            {
                GameObject sourceEntry = sourceEntries.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                Transform mappedEntry = sourceEntry == null ? null
                    : MapTransform(libraryPage, challengePage, sourceEntry.transform);
                if (sourceEntry != null && mappedEntry == null)
                    throw new InvalidOperationException("Could not map authored Library ruby entry into Challenges.");
                destinationEntries.GetArrayElementAtIndex(i).objectReferenceValue =
                    mappedEntry != null ? mappedEntry.gameObject : null;
            }
            destinationWords.arraySize = 0;
        }

        private static T GetRef<T>(UnityEngine.Object target, string path) where T : UnityEngine.Object =>
            new SerializedObject(target).FindProperty(path)?.objectReferenceValue as T;

        private static string SerializedPropertiesExcept(UnityEngine.Object target, string excludedPath)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.GetIterator();
            var fingerprint = new StringBuilder();
            while (property.Next(true))
            {
                if (property.propertyPath == excludedPath
                    || property.propertyPath.StartsWith(excludedPath + ".", StringComparison.Ordinal)) continue;
                fingerprint.Append(property.propertyPath).Append('|')
                    .Append(property.propertyType).Append('|')
                    .Append(SerializedPropertyValue(property)).Append('\n');
            }
            return fingerprint.ToString();
        }

        private static string SerializedPropertyValue(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue == null ? "null" : Id(property.objectReferenceValue);
                case SerializedPropertyType.Boolean: return property.boolValue.ToString();
                case SerializedPropertyType.Integer: return property.intValue.ToString();
                case SerializedPropertyType.Float: return property.floatValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                case SerializedPropertyType.String: return property.stringValue ?? string.Empty;
                case SerializedPropertyType.Enum: return property.enumValueIndex.ToString();
                case SerializedPropertyType.Vector2: return property.vector2Value.ToString();
                case SerializedPropertyType.Vector3: return property.vector3Value.ToString();
                case SerializedPropertyType.Vector4: return property.vector4Value.ToString();
                case SerializedPropertyType.Color: return property.colorValue.ToString();
                case SerializedPropertyType.Rect: return property.rectValue.ToString();
                case SerializedPropertyType.Quaternion: return property.quaternionValue.ToString();
                default: return property.isArray ? property.arraySize.ToString() : property.displayName;
            }
        }

        private static string CompletionTextRefs(LevelCompletePanelController panel)
        {
            var so = new SerializedObject(panel);
            return string.Join("|", new[] { "levelResultTMP", "starsTMP", "timeSpentTMP", "retriesTMP" }
                .Select(path => { var p = so.FindProperty(path); return p == null || p.objectReferenceValue == null ? "null" : Id(p.objectReferenceValue); }));
        }

        private static T One<T>(Scene scene, Func<T, bool> predicate, string label) where T : Component
        {
            T[] found = Components<T>(scene).Where(predicate).ToArray();
            if (found.Length != 1) throw new InvalidOperationException("Expected exactly one " + label + "; found " + found.Length + ".");
            return found[0];
        }

        private static T[] Components<T>(Scene scene) where T : Component => AllObjects(scene)
            .Select(go => go.GetComponent<T>()).Where(x => x != null).ToArray();

        private static AudioSource FindMenuClickAudioSource(Scene scene)
        {
            AudioSource[] candidates = Components<AudioSource>(scene).Where(source => source.clip != null
                && AssetDatabase.GetAssetPath(source.clip) == "Assets/-Audio/menuClick.wav"
                && source.outputAudioMixerGroup != null
                && AssetDatabase.GetAssetPath(source.outputAudioMixerGroup.audioMixer) == "Assets/-Audio/GameMixer.mixer")
                .ToArray();
            AudioSource[] referenced = candidates.Where(source => AllObjects(scene)
                .SelectMany(go => go.GetComponents<Button>())
                .Any(button => HasPersistentMenuPlay(button, source))).ToArray();
            if (referenced.Length != 1)
                throw new InvalidOperationException("Expected one SFX-routed menuClick.wav AudioSource referenced by existing Button events; found "
                    + referenced.Length + ".");
            return referenced[0];
        }

        private static bool HasPersistentMenuPlay(Button button, AudioSource source)
        {
            return GetPersistentMenuPlayCount(button, source) > 0;
        }

        private static int GetPersistentMenuPlayCount(Button button, AudioSource source)
        {
            if (button == null || source == null) return 0;
            int matches = 0;
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentTarget(i) == source
                    && button.onClick.GetPersistentMethodName(i) == nameof(AudioSource.Play)
                    && button.onClick.GetPersistentListenerState(i) != UnityEventCallState.Off)
                    matches++;
            }
            return matches;
        }

        private static List<GameObject> AllObjects(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();

        private static bool IsChildOf(Transform child, Transform root)
        {
            for (Transform t = child; t != null; t = t.parent) if (t == root) return true;
            return false;
        }

        private static string Id(UnityEngine.Object value) => GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();

        private static string Describe(UnityEngine.Object value)
        {
            if (value == null) return "<null>";
            GameObject go = value as GameObject;
            if (go == null && value is Component component) go = component.gameObject;
            return value.GetType().Name + ":" + (go != null ? go.name : value.name)
                + "#" + value.GetInstanceID();
        }

        private static void CheckEditorState()
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before merging the Hub.");
            if (EditorApplication.isCompiling) throw new InvalidOperationException("Wait for compilation before merging the Hub.");
        }

        private static string ProjectFile(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));

        private static void RequireInside(string root, string path)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Resolved path escaped its intended root: " + path);
        }

        private static string BackupTarget(string target, string meta, string targetHash, string metaHash, string sourceHash)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outerRoot = Directory.GetParent(projectRoot).FullName;
            string backupRoot = Path.GetFullPath(Path.Combine(outerRoot, "_scene_backups"));
            RequireInside(outerRoot, backupRoot);
            Directory.CreateDirectory(backupRoot);
            string folder = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
                + "_WordListHubMerge_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(folder);
            File.Copy(target, Path.Combine(folder, Path.GetFileName(target)), false);
            File.Copy(meta, Path.Combine(folder, Path.GetFileName(meta)), false);
            File.WriteAllText(Path.Combine(folder, "manifest.txt"),
                "Target: " + MainScene + "\nTarget SHA-256: " + targetHash + "\nTarget meta SHA-256: " + metaHash
                + "\nSource: " + PreviewScene + "\nSource SHA-256: " + sourceHash + "\n", new UTF8Encoding(false));
            return folder;
        }

        private static string HashFile(string path)
        {
            using (SHA256 sha = SHA256.Create()) using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
#endif
