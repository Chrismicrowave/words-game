#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Words.UiMockups.WordListHub;

/// <summary>
/// One-time builder for the isolated Word List Hub preview scene. It clones the
/// production LevelPanel's visual tree but never saves or edits the source scene.
/// </summary>
public static class WordListHubMockupBuilder
{
    private const string SourceScenePath = "Assets/Scenes/mainGame.unity";
    private const string OutputFolder = "Assets/Scenes/UI-Mockups";
    private const string OutputScenePath = OutputFolder + "/WordListHubMockup.unity";
    private const string FontFolder = "Assets/UI-Mockups/WordListHub/Fonts";
    private const string AchievementIconFolder = "Assets/UI-Mockups/WordListHub/Art/Placeholders";
    private const string ChineseFontGuid = "428be41dc6b96d74cb9084b697519d27";

    [MenuItem("Tools/Mockups/Build Word List Hub Preview Scene")]
    public static void BuildPreviewScene()
    {
        BuildPreviewSceneCore(false);
    }

    public static void RebuildPreviewSceneForReview()
    {
        BuildPreviewSceneCore(true);
    }

    private static void BuildPreviewSceneCore(bool allowOverwriteExistingPreview)
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before building the isolated UI mockup.");
        if (EditorApplication.isCompiling)
            throw new InvalidOperationException("Wait for script compilation before building the isolated UI mockup.");
        string previewBackupPath = null;
        if (File.Exists(OutputScenePath))
        {
            if (!allowOverwriteExistingPreview)
                throw new InvalidOperationException("The one-time mockup scene already exists: " + OutputScenePath);

            Scene existingPreview = SceneManager.GetSceneByPath(OutputScenePath);
            if (existingPreview.IsValid() && existingPreview.isLoaded)
                throw new InvalidOperationException("Close the existing mockup scene before rebuilding its saved preview.");

            previewBackupPath = SaveExistingPreviewCopy(OutputScenePath);
        }

        if (File.Exists(OutputScenePath) && string.IsNullOrEmpty(previewBackupPath))
            throw new InvalidOperationException("The one-time mockup scene already exists: " + OutputScenePath);

        Scene mainScene = SceneManager.GetSceneByPath(SourceScenePath);
        bool openedSourceForBuild = false;
        if (!mainScene.IsValid() || !mainScene.isLoaded)
        {
            mainScene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);
            openedSourceForBuild = true;
        }

        EnsureNoOtherDirtyScene(mainScene);
        string sourceDiskHash = File.Exists(SourceScenePath) ? HashFile(SourceScenePath) : string.Empty;
        string backupPath = null;
        string backupHash = null;
        if (mainScene.isDirty)
        {
            backupPath = SaveDirtySourceCopy(mainScene, out backupHash);
        }

        GameObject sourceCanvasObject = FindSourceCanvas(mainScene);
        Transform sourcePanel = FindLevelPanel(mainScene);
        if (sourceCanvasObject == null || sourcePanel == null)
            throw new InvalidOperationException("The source scene is missing its UI Canvas or Menus/LevelPanel subtree.");

        EventSystem sourceEventSystem = FindSourceEventSystem(mainScene);
        InputSystemUIInputModule sourceInputModule = sourceEventSystem != null
            ? sourceEventSystem.GetComponent<InputSystemUIInputModule>()
            : null;
        if (sourceEventSystem == null || sourceInputModule == null)
            throw new InvalidOperationException("The source scene needs one EventSystem with InputSystemUIInputModule and its default actions.");

        TMP_FontAsset sourcePrimaryFont = FindTitleFont(sourcePanel);
        TMP_FontAsset sourceCjkFont = FindChineseFont(sourcePanel);
        EnsureAssetFolder(FontFolder);
        TMP_FontAsset safePrimaryFont = IsDynamic(sourcePrimaryFont)
            ? CopyDynamicFont(sourcePrimaryFont, "Primary")
            : sourcePrimaryFont;
        TMP_FontAsset safeCjkFont = IsDynamic(sourceCjkFont)
            ? CopyDynamicFont(sourceCjkFont, "CJK")
            : sourceCjkFont;

        EnsureAssetFolder(OutputFolder);
        Scene mockupScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(mockupScene);

        GameObject canvasObject = CreateIsolatedCanvas(sourceCanvasObject);
        SceneManager.MoveGameObjectToScene(canvasObject, mockupScene);

        GameObject previewCameraObject = CreatePreviewCamera();
        SceneManager.MoveGameObjectToScene(previewCameraObject, mockupScene);

        GameObject clonedPanel = InstantiateVisualPanel(sourcePanel, canvasObject.transform, mockupScene);
        ConfigureClonedPanel(clonedPanel);
        RemoveNonVisualScripts(clonedPanel);
        ValidateButtonsAndSceneReferences(clonedPanel, mockupScene);

        GameObject eventSystemObject = CreateIsolatedEventSystem(sourceEventSystem, sourceInputModule);
        SceneManager.MoveGameObjectToScene(eventSystemObject, mockupScene);

        WordListHubMockupController controller = clonedPanel.AddComponent<WordListHubMockupController>();
        SetControllerVisualResources(controller, sourcePanel, safePrimaryFont, safeCjkFont);
        SetControllerAchievementIcons(controller);
        try
        {
            controller.BuildPreviewInEditor();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            throw;
        }
        clonedPanel.SetActive(true);
        EditorUtility.SetDirty(controller);

        ValidateIsolatedScene(mockupScene);
        EditorSceneManager.MarkSceneDirty(mockupScene);
        if (!EditorSceneManager.SaveScene(mockupScene, OutputScenePath))
            throw new IOException("Unity did not save the mockup scene to " + OutputScenePath);
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        if (!string.IsNullOrEmpty(sourceDiskHash) && HashFile(SourceScenePath) != sourceDiskHash)
            throw new IOException("The production mainGame scene file changed during mockup generation.");

        EditorSceneManager.OpenScene(OutputScenePath, OpenSceneMode.Single);
        Debug.Log("[WordListHubMockupBuilder] Built and opened " + OutputScenePath
            + ". Source scene disk hash unchanged: " + (!string.IsNullOrEmpty(sourceDiskHash))
            + ". Dirty source copy: " + (backupPath ?? "none")
            + (backupHash != null ? " (sha256 " + backupHash + ")" : string.Empty)
            + ". Previous preview copy: " + (previewBackupPath ?? "none")
            + ". Production Build Settings were not changed.");
    }

    private static void EnsureNoOtherDirtyScene(Scene sourceScene)
    {
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene loaded = SceneManager.GetSceneAt(index);
            if (!loaded.IsValid() || !loaded.isLoaded || !loaded.isDirty)
                continue;
            if (loaded.path != sourceScene.path)
                throw new InvalidOperationException("Another dirty scene is open. Save or preserve it before switching to the isolated mockup.");
        }
    }

    private static string SaveDirtySourceCopy(Scene sourceScene, out string hash)
    {
        string backupDirectory = Path.Combine(Path.GetTempPath(),
            "words-wordlist-hub-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDirectory);
        string backupPath = Path.Combine(backupDirectory, "mainGame-dirty-copy.unity");
        if (!EditorSceneManager.SaveScene(sourceScene, backupPath, true))
            throw new IOException("Could not preserve the dirty mainGame scene before opening the mockup.");
        hash = HashFile(backupPath);
        string manifest = "sourceScene=" + sourceScene.path + Environment.NewLine
            + "sourceDirty=true" + Environment.NewLine
            + "copy=" + backupPath + Environment.NewLine
            + "sha256=" + hash + Environment.NewLine;
        File.WriteAllText(Path.Combine(backupDirectory, "manifest.txt"), manifest, new UTF8Encoding(false));
        return backupPath;
    }

    private static string SaveExistingPreviewCopy(string previewPath)
    {
        string backupDirectory = Path.Combine(Path.GetTempPath(),
            "words-wordlist-hub-preview-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDirectory);
        string backupPath = Path.Combine(backupDirectory, "WordListHubMockup.unity");
        File.Copy(previewPath, backupPath, false);

        string metaPath = previewPath + ".meta";
        string backupMetaPath = backupPath + ".meta";
        if (File.Exists(metaPath))
            File.Copy(metaPath, backupMetaPath, false);

        string hash = HashFile(backupPath);
        string manifest = "sourceScene=" + previewPath + Environment.NewLine
            + "copy=" + backupPath + Environment.NewLine
            + "sha256=" + hash + Environment.NewLine
            + "metaCopied=" + File.Exists(backupMetaPath) + Environment.NewLine;
        File.WriteAllText(Path.Combine(backupDirectory, "manifest.txt"), manifest, new UTF8Encoding(false));
        return backupPath;
    }

    private static GameObject FindSourceCanvas(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "--- UI ---" && root.GetComponent<Canvas>() != null)
                return root;
        }
        return scene.GetRootGameObjects().FirstOrDefault(root => root.GetComponent<Canvas>() != null);
    }

    private static Transform FindLevelPanel(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform panel = root.transform.Find("Menus/LevelPanel");
            if (panel != null)
                return panel;
        }
        return null;
    }

    private static EventSystem FindSourceEventSystem(Scene scene)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<EventSystem>(true))
            .FirstOrDefault();
    }

    private static TMP_FontAsset FindTitleFont(Transform sourcePanel)
    {
        Transform title = sourcePanel.Find("Card/Title");
        TMP_Text text = title != null ? title.GetComponent<TMP_Text>() : null;
        return text != null ? text.font : null;
    }

    private static TMP_FontAsset FindChineseFont(Transform sourcePanel)
    {
        LevelPanelController sourceController = sourcePanel.GetComponent<LevelPanelController>();
        if (sourceController != null)
        {
            SerializedObject serialized = new SerializedObject(sourceController);
            SerializedProperty font = serialized.FindProperty("chineseFontAsset");
            if (font != null)
            {
                TMP_FontAsset configured = font.objectReferenceValue as TMP_FontAsset;
                if (configured != null)
                    return configured;
            }
        }

        string path = AssetDatabase.GUIDToAssetPath(ChineseFontGuid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
    }

    private static bool IsDynamic(TMP_FontAsset font)
    {
        return font != null && font.atlasPopulationMode == AtlasPopulationMode.Dynamic;
    }

    private static TMP_FontAsset CopyDynamicFont(TMP_FontAsset source, string suffix)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (string.IsNullOrEmpty(sourcePath))
            throw new InvalidOperationException("A dynamic mockup font has no copyable asset path.");

        string baseName = Path.GetFileNameWithoutExtension(sourcePath) + "_WordListHub_" + suffix;
        string destination = FontFolder + "/" + baseName + Path.GetExtension(sourcePath);
        if (File.Exists(destination))
        {
            TMP_FontAsset existingCopy = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(destination);
            if (existingCopy != null
                && existingCopy.atlasPopulationMode == AtlasPopulationMode.Dynamic
                && existingCopy.faceInfo.familyName == source.faceInfo.familyName
                && existingCopy.faceInfo.styleName == source.faceInfo.styleName)
                return existingCopy;
            throw new InvalidOperationException("A dynamic font asset already exists at the mockup path but does not match the source font.");
        }
        if (!AssetDatabase.CopyAsset(sourcePath, destination))
            throw new IOException("Could not isolate dynamic font atlas asset " + sourcePath);

        TMP_FontAsset copy = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(destination);
        if (copy == null)
            throw new IOException("Unity could not load the isolated font copy " + destination);
        return copy;
    }

    private static GameObject CreateIsolatedCanvas(GameObject sourceCanvasObject)
    {
        Canvas sourceCanvas = sourceCanvasObject.GetComponent<Canvas>();
        CanvasScaler sourceScaler = sourceCanvasObject.GetComponent<CanvasScaler>();
        GraphicRaycaster sourceRaycaster = sourceCanvasObject.GetComponent<GraphicRaycaster>();
        if (sourceCanvas == null || sourceScaler == null || sourceRaycaster == null)
            throw new InvalidOperationException("The source UI root must contain Canvas, CanvasScaler and GraphicRaycaster.");

        GameObject canvasObject = new GameObject("WordListHubMockupCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.layer = sourceCanvasObject.layer;
        EditorUtility.CopySerialized(sourceCanvas, canvasObject.GetComponent<Canvas>());
        EditorUtility.CopySerialized(sourceScaler, canvasObject.GetComponent<CanvasScaler>());
        EditorUtility.CopySerialized(sourceRaycaster, canvasObject.GetComponent<GraphicRaycaster>());

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
        }
        return canvasObject;
    }

    private static GameObject CreatePreviewCamera()
    {
        GameObject cameraObject = new GameObject("Mockup Preview Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.88f, 0.87f, 0.80f, 1f);
        camera.cullingMask = 0;
        camera.targetDisplay = 0;
        camera.useOcclusionCulling = false;
        camera.allowMSAA = false;
        return cameraObject;
    }

    private static GameObject InstantiateVisualPanel(Transform sourcePanel, Transform canvas, Scene destinationScene)
    {
        if (!destinationScene.IsValid() || !destinationScene.isLoaded || !SceneManager.GetActiveScene().Equals(destinationScene))
            throw new InvalidOperationException("The mockup scene must be active before cloning the source UI.");

        GameObject clone = UnityEngine.Object.Instantiate(sourcePanel.gameObject, canvas, false);
        clone.name = "WordListHubMockupPanel";
        clone.SetActive(false);
        return clone;
    }

    private static void ConfigureClonedPanel(GameObject clone)
    {
        Transform card = clone.transform.Find("Card");
        if (card == null)
            throw new InvalidOperationException("The cloned LevelPanel has no Card.");

        VerticalLayoutGroup layout = card.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
            layout.enabled = false;

        Transform tabRows = card.Find("TabRows");
        Transform levelContent = card.Find("LevelContent");
        Transform buttonRow = card.Find("ButtonRow");
        if (tabRows == null || levelContent == null || buttonRow == null)
            throw new InvalidOperationException("The cloned Card is missing its original tab/content/button visual subtree.");
        tabRows.gameObject.SetActive(false);
        levelContent.gameObject.SetActive(false);
        buttonRow.gameObject.SetActive(false);

        Transform title = card.Find("Title");
        if (title == null || title.GetComponent<TMP_Text>() == null)
            throw new InvalidOperationException("The cloned Card must retain its original TMP title object.");

    }

    private static void RemoveNonVisualScripts(GameObject root)
    {
        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;
            Type type = behaviour.GetType();
            string ns = type.Namespace ?? string.Empty;
            bool isUnityUi = ns == "UnityEngine.UI";
            bool isTmp = ns == "TMPro";
            if (isUnityUi || isTmp)
                continue;
            UnityEngine.Object.DestroyImmediate(behaviour);
        }
    }

    private static void ValidateButtonsAndSceneReferences(GameObject root, Scene targetScene)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            Image buttonImage = button.GetComponent<Image>();
            if (buttonImage != null)
                buttonImage.type = Image.Type.Sliced;

            Image targetImage = button.targetGraphic as Image;
            if (targetImage != null)
                targetImage.type = Image.Type.Sliced;

            int callCount = button.onClick.GetPersistentEventCount();
            if (callCount > 0)
                throw new InvalidOperationException("The cloned visual subtree contains persistent Button callbacks; source UI actions are not allowed in the mockup.");
            for (int index = 0; index < callCount; index++)
            {
                UnityEngine.Object target = button.onClick.GetPersistentTarget(index);
                if (target != null)
                    throw new InvalidOperationException("The cloned visual subtree contains a persistent Button callback target; source UI actions are not allowed in the mockup.");
            }
        }

        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null)
                continue;
            SerializedObject serialized = new SerializedObject(component);
            SerializedProperty property = serialized.GetIterator();
            bool enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = true;
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                    continue;
                UnityEngine.Object referenced = property.objectReferenceValue;
                if (referenced == null || EditorUtility.IsPersistent(referenced))
                    continue;

                GameObject referencedGameObject = referenced as GameObject;
                Component referencedComponent = referenced as Component;
                if (referencedGameObject != null && referencedGameObject.scene != targetScene)
                    throw new InvalidOperationException("The cloned visual subtree has a GameObject reference outside the isolated scene in " + component.GetType().Name + ".");
                if (referencedComponent != null && referencedComponent.gameObject.scene != targetScene)
                    throw new InvalidOperationException("The cloned visual subtree has a component reference outside the isolated scene in " + component.GetType().Name + ".");
            }
        }

        if (root.scene != targetScene)
            throw new InvalidOperationException("The cloned panel is not in the isolated destination scene.");
    }

    private static GameObject CreateIsolatedEventSystem(EventSystem source, InputSystemUIInputModule sourceModule)
    {
        GameObject root = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        EventSystem target = root.GetComponent<EventSystem>();
        target.sendNavigationEvents = source.sendNavigationEvents;
        target.pixelDragThreshold = source.pixelDragThreshold;
        target.firstSelectedGameObject = null;

        InputSystemUIInputModule targetModule = root.GetComponent<InputSystemUIInputModule>();
        EditorUtility.CopySerialized(sourceModule, targetModule);
        if (targetModule.actionsAsset == null)
            throw new InvalidOperationException("The isolated InputSystemUIInputModule did not retain the source default actions asset.");
        return root;
    }

    private static void SetControllerVisualResources(
        WordListHubMockupController controller,
        Transform sourcePanel,
        TMP_FontAsset primaryFont,
        TMP_FontAsset cjkFont)
    {
        Image cardImage = sourcePanel.Find("Card") != null
            ? sourcePanel.Find("Card").GetComponent<Image>()
            : null;
        Sprite backgroundSprite = cardImage != null ? cardImage.sprite : null;

        Sprite standardSprite = sourcePanel.GetComponentsInChildren<Button>(true)
            .Select(button => button.targetGraphic as Image)
            .Where(image => image != null && image.sprite != null)
            .Select(image => image.sprite)
            .FirstOrDefault();
        if (standardSprite == null)
            standardSprite = backgroundSprite;

        Mask sourceMask = sourcePanel.GetComponentInChildren<Mask>(true);
        Image maskImage = sourceMask != null ? sourceMask.GetComponent<Image>() : null;
        Sprite maskSprite = maskImage != null && maskImage.sprite != null
            ? maskImage.sprite
            : standardSprite;

        SerializedObject serialized = new SerializedObject(controller);
        SerializedProperty primary = serialized.FindProperty("primaryFont");
        SerializedProperty cjk = serialized.FindProperty("cjkFont");
        SerializedProperty standard = serialized.FindProperty("standardSprite");
        SerializedProperty background = serialized.FindProperty("backgroundSprite");
        SerializedProperty input = serialized.FindProperty("inputFieldSprite");
        SerializedProperty mask = serialized.FindProperty("maskSprite");
        if (primary == null || cjk == null || standard == null || background == null || input == null || mask == null)
            throw new InvalidOperationException("The mockup controller visual resource fields were not found.");
        primary.objectReferenceValue = primaryFont;
        cjk.objectReferenceValue = cjkFont;
        standard.objectReferenceValue = standardSprite;
        background.objectReferenceValue = backgroundSprite;
        input.objectReferenceValue = standardSprite;
        mask.objectReferenceValue = maskSprite;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetControllerAchievementIcons(WordListHubMockupController controller)
    {
        EnsureAssetFolder(AchievementIconFolder);
        Sprite firstWordUnlocked = EnsureAchievementIcon("COMPLETE_FIRST_WORD_achieved.png");
        Sprite firstWordLocked = EnsureAchievementIcon("COMPLETE_FIRST_WORD_unachieved.png");
        Sprite twoWordsUnlocked = EnsureAchievementIcon("COMPLETE_TWO_WORDS_achieved.png");
        Sprite twoWordsLocked = EnsureAchievementIcon("COMPLETE_TWO_WORDS_unachieved.png");
        Sprite wordWithDUnlocked = EnsureAchievementIcon("COMPLETE_WORD_WITH_D_achieved.png");
        Sprite wordWithDLocked = EnsureAchievementIcon("COMPLETE_WORD_WITH_D_unachieved.png");

        SerializedObject serialized = new SerializedObject(controller);
        SetSpriteReference(serialized, "firstWordUnlockedIcon", firstWordUnlocked);
        SetSpriteReference(serialized, "firstWordLockedIcon", firstWordLocked);
        SetSpriteReference(serialized, "twoWordsUnlockedIcon", twoWordsUnlocked);
        SetSpriteReference(serialized, "twoWordsLockedIcon", twoWordsLocked);
        SetSpriteReference(serialized, "wordWithDUnlockedIcon", wordWithDUnlocked);
        SetSpriteReference(serialized, "wordWithDLockedIcon", wordWithDLocked);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetSpriteReference(SerializedObject serialized, string fieldName, Sprite sprite)
    {
        SerializedProperty property = serialized.FindProperty(fieldName);
        if (property == null)
            throw new InvalidOperationException("The mockup controller is missing its sprite field " + fieldName);
        property.objectReferenceValue = sprite;
    }

    private static Sprite EnsureAchievementIcon(string fileName)
    {
        string assetPath = AchievementIconFolder + "/" + fileName;
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string destinationPath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(destinationPath))
        {
            string sourcePath = Path.Combine(projectRoot, "docs", "steam-achievement-placeholders", fileName);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("The existing achievement placeholder is missing.", sourcePath);
            File.Copy(sourcePath, destinationPath, false);
        }

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException("Unity could not create a texture importer for " + assetPath);
        if (importer.textureType != TextureImporterType.Sprite
            || importer.spriteImportMode != SpriteImportMode.Single
            || importer.mipmapEnabled)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sprite == null)
            throw new InvalidOperationException("Unity could not load the achievement placeholder sprite " + assetPath);
        return sprite;
    }

    private static void ValidateIsolatedScene(Scene scene)
    {
        int eventSystemCount = 0;
        int canvasCount = 0;
        int previewCameraCount = 0;
        int audioListenerCount = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            eventSystemCount += root.GetComponentsInChildren<EventSystem>(true).Length;
            canvasCount += root.GetComponentsInChildren<Canvas>(true).Length;
            audioListenerCount += root.GetComponentsInChildren<AudioListener>(true).Length;
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            {
                previewCameraCount++;
                if (camera.name != "Mockup Preview Camera"
                    || camera.cullingMask != 0
                    || camera.clearFlags != CameraClearFlags.SolidColor)
                    throw new InvalidOperationException("Only the isolated blank-background mockup preview camera is allowed.");
            }
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;
                string typeName = behaviour.GetType().Name;
                if (typeName == "GameCoordinator" || typeName == "PhaseManager"
                    || typeName == "SettingsManager" || typeName == "BuildDefaultsApplier"
                    || typeName == "LevelPanelController" || typeName == "GameConfig")
                    throw new InvalidOperationException("A gameplay/business component was found in the isolated mockup scene: " + typeName);
            }
        }
        if (eventSystemCount != 1 || canvasCount != 1 || previewCameraCount != 1 || audioListenerCount != 0)
            throw new InvalidOperationException("The mockup scene must contain one Canvas, one EventSystem, one preview Camera and no AudioListener.");
    }

    private static void EnsureAssetFolder(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder))
            return;
        string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
        string name = Path.GetFileName(assetFolder);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureAssetFolder(parent);
        if (string.IsNullOrEmpty(parent) || !AssetDatabase.IsValidFolder(parent))
            throw new InvalidOperationException("Could not create Unity asset folder " + assetFolder);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static string HashFile(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
    }
}
#endif
