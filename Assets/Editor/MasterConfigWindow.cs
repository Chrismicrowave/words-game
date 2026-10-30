#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(GameConfig))]
public sealed class GameConfigInspector : Editor
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        SerializedProperty isDemo = serializedObject.FindProperty("isDemo");
        SerializedProperty activeProfile = serializedObject.FindProperty("activeProfile");
        root.Add(new PropertyField(isDemo, "Legacy demo flag"));
        root.Add(new PropertyField(activeProfile, "Active profile"));

        var note = new HelpBox("Profiles configure defaults and feature access. Switching profiles does not isolate or reset saves.", HelpBoxMessageType.Info);
        root.Add(note);

        var openButton = new Button(() => MasterConfigWindow.OpenForConfig((GameConfig)target))
        {
            text = "Open Master Config"
        };
        root.Add(openButton);
        root.Bind(serializedObject);
        return root;
    }
}

[CustomEditor(typeof(GameConfigProfile))]
public sealed class GameConfigProfileInspector : Editor
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        root.Add(new HelpBox("Selecting this profile does not create a separate save. First-run local resets are opt-in; Steam achievements are managed separately.", HelpBoxMessageType.Info));
        MasterConfigWindow.AddProfilePropertyGroups(root, serializedObject, includeResetRecipe: true);
        root.Bind(serializedObject);
        return root;
    }
}

public sealed class MasterConfigWindow : EditorWindow
{
    public const string CanonicalGameConfigAssetPath = "Assets/-Data/GameConfig.asset";

    private static readonly GameResetTargets[] IndividualTargets =
    {
        GameResetTargets.AudioSettings,
        GameResetTargets.DisplaySettings,
        GameResetTargets.VisualEffects,
        GameResetTargets.InputHints,
        GameResetTargets.UiLanguage,
        GameResetTargets.Pinyin,
        GameResetTargets.PlayerName,
        GameResetTargets.PanelVisibility,
        GameResetTargets.NavigationMemory,
        GameResetTargets.EnglishChallengeProgress,
        GameResetTargets.ChineseChallengeProgress,
        GameResetTargets.TimeRecords,
        GameResetTargets.MyLists,
        GameResetTargets.CustomLists,
        GameResetTargets.LocalAchievements
    };

    private static readonly string[] TargetNames =
    {
        "Audio settings", "Display settings", "Visual effects", "Input hints", "UI language", "Pinyin",
        "Player name", "Panel visibility", "Navigation memory", "English challenge progress",
        "Chinese challenge progress", "Time records", "My lists", "Custom lists", "Local achievements"
    };

    private static readonly string[] TargetDescriptions =
    {
        "Removes master, SFX, and BGM volume preferences.",
        "Removes fullscreen and resolution preferences.",
        "Removes CRT and screen-shake preferences.",
        "Removes the action-prompt preference.",
        "Removes the UI language preference.",
        "Removes the Show Pinyin preference.",
        "Removes the leaderboard display name preference.",
        "Removes Words, Timer, and Info panel visibility preferences.",
        "Removes remembered active tab, selected level path and tab, and selected user-list path.",
        "Removes English challenge unlock count and star keys for indices 0–99, plus any higher current challenge indices.",
        "Removes Chinese challenge unlock count and star keys for indices 0–99, plus any higher current challenge indices.",
        "Moves ListTimeRecords.json into the backup.",
        "Moves the WordLists folder into the backup and clears its selected-path preference.",
        "Moves Levels/Custom into the backup. The bundled hello-world seed remains in StreamingAssets and can be reseeded by the game.",
        "Moves all local word-achievement profiles into the backup. Steam account achievements are not changed."
    };

    private GameConfig activeConfig;
    private GameConfigProfile activeProfile;
    private GameResetTargets manualTargets;
    private GameResetPlan manualPlan;
    private GameResetTargets standaloneTargets;
    private StandalonePreferencesPreview standalonePreview;
    private Type steamHelperType;
    private string inspectedSteamId;
    private SteamInspectCoordinator steamInspectCoordinator;
    private bool waitingForSteamHelperResult;

    private ObjectField configField;
    private ObjectField profileField;
    private EnumFlagsField manualTargetField;
    private EnumFlagsField standaloneTargetField;
    private Button executeResetButton;
    private Button executeStandaloneButton;
    private Button buildProfileButton;
    private Toggle developmentBuildToggle;
    private Button inspectSteamButton;
    private Button resetSteamButton;
    private Label planOutput;
    private Label standaloneOutput;
    private Label steamOutput;
    private Label masterResetOutput;
    private Button masterResetNowButton;
    private Button armBothResetsButton;
    private ScrollView profileFields;
    private VisualElement scopeButtons;

    [MenuItem("Tools/Words/Master Config")]
    public static void Open()
    {
        GameConfig config = FindOneConfig();
        OpenForConfig(config);
    }

    public static void OpenForConfig(GameConfig config)
    {
        GameConfig canonicalConfig = FindOneConfig();
        if (config == null || config != canonicalConfig)
        {
            if (config != null)
                EditorUtility.DisplayDialog("Master Config", "Only " + CanonicalGameConfigAssetPath + " is wired to the runtime scenes and reset hooks. Opening the canonical GameConfig instead.", "OK");
            config = canonicalConfig;
        }
        MasterConfigWindow window = GetWindow<MasterConfigWindow>("Master Config");
        window.activeConfig = config;
        window.activeProfile = config != null ? config.activeProfile : null;
        window.manualTargets = window.activeProfile != null ? window.activeProfile.resetTargets : GameResetTargets.None;
        window.manualPlan = null;
        window.Rebuild();
        window.Show();
        window.Focus();
    }

    public static void OpenForReset(GameResetTargets initialTargets)
    {
        MasterConfigWindow window = GetWindow<MasterConfigWindow>("Master Config");
        if (window.activeConfig == null)
            window.activeConfig = FindOneConfig();
        if (window.activeConfig != null)
            window.activeProfile = window.activeConfig.activeProfile;
        window.manualTargets = initialTargets;
        window.manualPlan = null;
        window.Rebuild();
        window.Show();
        window.Focus();
    }

    public void CreateGUI()
    {
        if (activeConfig == null)
            activeConfig = FindOneConfig();
        if (activeConfig != null && activeProfile == null)
            activeProfile = activeConfig.activeProfile;
        Rebuild();
    }

    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        steamHelperType = Type.GetType("SteamAchievementMaintenance, SteamAchievementIntegration", false);
        EnsureSteamInspectCoordinator();
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        if (steamInspectCoordinator != null)
        {
            steamInspectCoordinator.StateChanged -= OnSteamInspectStateChanged;
            steamInspectCoordinator.Cancel();
            steamInspectCoordinator = null;
        }
    }

    private void Update()
    {
        SyncResetRecipeFromActiveProfile();
        if (waitingForSteamHelperResult)
            RestoreSteamHelperResultIfAvailable();
        RefreshActionState();
    }

    private void SyncResetRecipeFromActiveProfile()
    {
        if (activeProfile == null || manualTargetField == null || manualTargets == activeProfile.resetTargets)
            return;
        manualTargets = activeProfile.resetTargets;
        manualTargetField.SetValueWithoutNotify(manualTargets);
        manualPlan = null;
        UpdatePlanOutput();
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        RefreshActionState();
    }

    private void Rebuild()
    {
        if (rootVisualElement == null)
            return;

        rootVisualElement.Clear();
        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1;
        rootVisualElement.Add(scroll);

        scroll.Add(new Label("Master Config") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 18 } });
        scroll.Add(new HelpBox("Profiles group runtime defaults, demo limits, and independent unlocks. Profiles share the same local saves and Steam account.", HelpBoxMessageType.Info));

        var configRow = new VisualElement();
        configRow.style.flexDirection = FlexDirection.Row;
        configRow.style.alignItems = Align.Center;
        configField = new ObjectField("Canonical Game Config") { objectType = typeof(GameConfig), allowSceneObjects = false, value = activeConfig };
        configField.style.flexGrow = 1;
        configField.SetEnabled(false);
        configRow.Add(configField);
        scroll.Add(configRow);

        profileField = new ObjectField("Active profile") { objectType = typeof(GameConfigProfile), allowSceneObjects = false, value = activeProfile };
        profileField.RegisterValueChangedCallback(evt => SetActiveProfile(evt.newValue as GameConfigProfile));
        scroll.Add(profileField);

        var profileActions = new VisualElement();
        profileActions.style.flexDirection = FlexDirection.Row;
        profileActions.Add(new Button(CreateProfile) { text = "Create profile" });
        profileActions.Add(new Button(DuplicateProfile) { text = "Duplicate active profile" });
        profileActions.Add(new Button(OpenProfileAsset) { text = "Open profile asset" });
        scroll.Add(profileActions);

        profileFields = new ScrollView(ScrollViewMode.Vertical);
        profileFields.style.maxHeight = 520;
        profileFields.style.marginTop = 6;
        scroll.Add(profileFields);
        BuildProfileView();

        scroll.Add(new Label("Build with active profile") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 12 } });
        scroll.Add(new HelpBox("This wrapper temporarily applies the active profile's window mode, startup resolution, and resizable-window setting to the current Standalone target. Original PlayerSettings are restored after success, failure, or exception.", HelpBoxMessageType.Info));
        developmentBuildToggle = new Toggle("Development build") { value = false };
        scroll.Add(developmentBuildToggle);
        buildProfileButton = new Button(BuildWithActiveProfile) { text = "Choose output and build" };
        scroll.Add(buildProfileButton);

        scroll.Add(new Label("Master Reset") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 12 } });
        scroll.Add(new HelpBox("Reset all local settings, progress, lists, records, and local achievement profiles now, or arm this profile to reset on its next Editor Play and/or each profile build's first run. A build's first run clears that player's local data; on this machine it shares the game's save. Editor Play clears Editor local data and does not also clear the Standalone app's separate PlayerPrefs. Steam account achievements remain separate.", HelpBoxMessageType.Warning));
        var masterResetRow = new VisualElement();
        masterResetRow.style.flexDirection = FlexDirection.Row;
        masterResetNowButton = new Button(ResetAllLocalNow) { text = "Reset all local data now" };
        armBothResetsButton = new Button(ArmBothResets) { text = "Arm both one-time resets" };
        masterResetRow.Add(masterResetNowButton);
        masterResetRow.Add(armBothResetsButton);
        scroll.Add(masterResetRow);
        masterResetOutput = new Label("Master Reset does not change the advanced reset recipe or Steam achievements.");
        masterResetOutput.style.whiteSpace = WhiteSpace.Normal;
        scroll.Add(masterResetOutput);

        scroll.Add(new Label("Manual local reset") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 12 } });
        scroll.Add(new HelpBox("Manual reset affects only this project's local PlayerPrefs and persistent-data files. Each operation creates a recoverable backup first. It never resets Steam.", HelpBoxMessageType.Info));
        manualTargetField = new EnumFlagsField("Reset scopes", manualTargets);
        manualTargetField.RegisterValueChangedCallback(evt =>
        {
            manualTargets = (GameResetTargets)evt.newValue;
            manualPlan = null;
            SaveResetRecipeToActiveProfile(manualTargets);
            UpdateManualPlanControls();
        });
        scroll.Add(manualTargetField);
        scroll.Add(new HelpBox("With an active profile, this selection is saved as that profile's local reset recipe and also feeds its optional first-run reset. It never executes by itself. Without a profile it is only a manual selection.", HelpBoxMessageType.Info));

        var previewRow = new VisualElement();
        previewRow.style.flexDirection = FlexDirection.Row;
        previewRow.Add(new Button(() => PreviewLocalReset(manualTargets)) { text = "Preview selected" });
        executeResetButton = new Button(ExecuteLocalReset) { text = "Execute previewed reset" };
        previewRow.Add(executeResetButton);
        scroll.Add(previewRow);

        scopeButtons = new VisualElement();
        scopeButtons.style.flexDirection = FlexDirection.Row;
        scopeButtons.style.flexWrap = Wrap.Wrap;
        for (int i = 0; i < IndividualTargets.Length; i++)
        {
            int index = i;
            var button = new Button(() => PreviewLocalReset(IndividualTargets[index])) { text = "Preview " + TargetNames[index] };
            button.tooltip = TargetDescriptions[index];
            scopeButtons.Add(button);
        }
        scroll.Add(scopeButtons);

        planOutput = new Label("Choose reset scopes and preview their exact keys and paths.");
        planOutput.style.whiteSpace = WhiteSpace.Normal;
        planOutput.style.marginTop = 6;
        scroll.Add(planOutput);

        scroll.Add(new Label("Windows Standalone PlayerPrefs") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 12 } });
        scroll.Add(new HelpBox("This is a separate target from Editor preferences. It only removes this project's owned registry values whose Unity hashed names match selected keys; unknown values and subkeys are left alone.", HelpBoxMessageType.Info));
        standaloneTargetField = new EnumFlagsField("Standalone preference scopes", standaloneTargets);
        standaloneTargetField.RegisterValueChangedCallback(evt =>
        {
            standaloneTargets = (GameResetTargets)evt.newValue;
            standalonePreview = null;
            UpdateStandaloneControls();
        });
        scroll.Add(standaloneTargetField);
        var standaloneRow = new VisualElement();
        standaloneRow.style.flexDirection = FlexDirection.Row;
        standaloneRow.Add(new Button(PreviewStandaloneReset) { text = "Preview standalone preferences" });
        executeStandaloneButton = new Button(ExecuteStandaloneReset) { text = "Execute standalone reset" };
        standaloneRow.Add(executeStandaloneButton);
        scroll.Add(standaloneRow);
        standaloneOutput = new Label("Standalone preferences are not included in local reset scopes.");
        standaloneOutput.style.whiteSpace = WhiteSpace.Normal;
        scroll.Add(standaloneOutput);

        scroll.Add(new Label("Steam achievements (account-wide)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 12 } });
        scroll.Add(new HelpBox("Steam achievements are shared by this signed-in Steam account across all profiles. This action is separate from every local reset and is never run by startup or build.", HelpBoxMessageType.Warning));
        var steamRow = new VisualElement();
        steamRow.style.flexDirection = FlexDirection.Row;
        inspectSteamButton = new Button(InspectSteamAccount) { text = "Inspect Steam account" };
        resetSteamButton = new Button(ConfirmAndResetSteamAccount) { text = "Reset this account's 3 achievements" };
        steamRow.Add(inspectSteamButton);
        steamRow.Add(resetSteamButton);
        scroll.Add(steamRow);
        steamOutput = new Label("Steam status has not been checked. Use the inspect button before the reset button.");
        steamOutput.style.whiteSpace = WhiteSpace.Normal;
        scroll.Add(steamOutput);

        RestoreSteamHelperResultIfAvailable();
        RefreshActionState();
    }

    private void BuildProfileView()
    {
        if (profileFields == null)
            return;
        profileFields.Clear();
        if (activeProfile == null)
        {
            profileFields.Add(new HelpBox("No active profile is assigned. Create or select a profile to edit defaults and unlocks.", HelpBoxMessageType.Warning));
            return;
        }

        var serializedProfile = new SerializedObject(activeProfile);
        AddProfilePropertyGroups(profileFields, serializedProfile, includeResetRecipe: false);
        profileFields.Bind(serializedProfile);
    }

    public static void AddProfilePropertyGroups(VisualElement root, SerializedObject profileObject, bool includeResetRecipe = true)
    {
        AddFoldout(root, profileObject, "Profile identity", "profileId");
        AddFoldout(root, profileObject, "Default settings", "defaults");
        AddFoldout(root, profileObject, "Demo and capacity", "isDemo", "maxChallengeLevels", "maxCustomLists", "maxWordsPerList", "customUnlockLevels", "allowChineseAndMixed");
        AddFoldout(root, profileObject, "Feature unlocks", "unlockAllFeatures", "unlockAllChallenges", "unlockCustomLists", "unlockCreateLists", "unlockEditLists", "unlockDeleteLists", "unlockDuplicateLists", "unlockImportLists", "unlockExportLists", "unlockAddWords");
        AddMasterResetScheduleFoldout(root, profileObject);
        string[] resetProperties = includeResetRecipe
            ? new[] { "resetLocalDataOnFirstRun", "resetRevision", "resetTargets" }
            : new[] { "resetLocalDataOnFirstRun", "resetRevision" };
        var resetFoldout = AddFoldout(root, profileObject, "Advanced first-run local reset recipe", resetProperties);
        resetFoldout.Add(new HelpBox("Disabled by default. When enabled, this profile's saved local reset recipe runs once per profileId + revision on the next Editor Play and when a player build using this profile starts. It resets the same shared local save; profiles are not separate saves. Increase resetRevision to run the recipe again. If a Master Reset flag is also armed, both paths can run. Steam achievements are never part of this option.", HelpBoxMessageType.Warning));
    }

    private static void AddMasterResetScheduleFoldout(VisualElement root, SerializedObject profileObject)
    {
        var foldout = new Foldout { text = "Master Reset scheduling", value = true };
        AddMasterResetScheduleField(foldout, profileObject, "resetAllLocalDataOnNextEditorPlay", "Reset all local data on next Editor Play");
        AddMasterResetScheduleField(foldout, profileObject, "resetAllLocalDataOnFirstBuildRun", "Reset all local data on each profile build's first run");
        foldout.Add(new HelpBox("Both options are off by default. The Editor Play option is consumed after a successful reset in the Editor environment; it does not also scan the Standalone app's separate PlayerPrefs. The build option gives each profile build a fresh one-time AllLocal recipe. Building does not clear the Editor's data; that build's first run clears the player's local data, sharing the game's save on this machine. Steam achievements remain separate.", HelpBoxMessageType.Warning));
        root.Add(foldout);
    }

    private static void AddMasterResetScheduleField(VisualElement root, SerializedObject profileObject, string propertyName, string label)
    {
        SerializedProperty property = profileObject.FindProperty(propertyName);
        if (property == null)
            return;

        bool previousValue = property.boolValue;
        var field = new PropertyField(property, label);
        field.RegisterValueChangeCallback(_ =>
        {
            profileObject.ApplyModifiedProperties();
            var profile = profileObject.targetObject as GameConfigProfile;
            if (profile == null)
                return;
            bool isEditorPlayFlag = propertyName == "resetAllLocalDataOnNextEditorPlay";
            bool currentValue = isEditorPlayFlag
                ? profile.resetAllLocalDataOnNextEditorPlay
                : profile.resetAllLocalDataOnFirstBuildRun;
            if (isEditorPlayFlag && !previousValue && currentValue)
                MasterResetPlayModeHook.Arm(profile);
            previousValue = currentValue;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        });
        root.Add(field);
    }

    private static Foldout AddFoldout(VisualElement root, SerializedObject serialized, string title, params string[] names)
    {
        var foldout = new Foldout { text = title, value = true };
        foreach (string name in names)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null)
                foldout.Add(new PropertyField(property));
        }
        root.Add(foldout);
        return foldout;
    }

    private void SetConfig(GameConfig config)
    {
        GameConfig canonicalConfig = FindOneConfig();
        if (config != null && config != canonicalConfig)
        {
            if (configField != null)
                configField.SetValueWithoutNotify(canonicalConfig);
            EditorUtility.DisplayDialog("Master Config", "Only " + CanonicalGameConfigAssetPath + " is wired to the runtime scenes and reset hooks. The selected asset was not changed.", "OK");
            return;
        }

        activeConfig = config;
        activeProfile = config != null ? config.activeProfile : null;
        manualTargets = activeProfile != null ? activeProfile.resetTargets : GameResetTargets.None;
        manualPlan = null;
        if (profileField != null)
            profileField.SetValueWithoutNotify(activeProfile);
        BuildProfileView();
        UpdateManualPlanControls();
    }

    private void SetActiveProfile(GameConfigProfile profile)
    {
        activeProfile = profile;
        manualTargets = profile != null ? profile.resetTargets : GameResetTargets.None;
        manualPlan = null;
        if (activeConfig != null)
        {
            Undo.RecordObject(activeConfig, "Set Active Game Config Profile");
            activeConfig.activeProfile = profile;
            EditorUtility.SetDirty(activeConfig);
            AssetDatabase.SaveAssetIfDirty(activeConfig);
        }
        BuildProfileView();
        UpdateManualPlanControls();
    }

    private void CreateProfile()
    {
        string path = EditorUtility.SaveFilePanelInProject("Create Game Config Profile", "PlaytestProfile", "asset", "Choose where to save the new profile.", "Assets/-Data");
        if (string.IsNullOrEmpty(path))
            return;

        var profile = CreateInstance<GameConfigProfile>();
        if (string.IsNullOrWhiteSpace(profile.profileId))
            profile.profileId = Guid.NewGuid().ToString("N");
        AssetDatabase.CreateAsset(profile, path);
        AssetDatabase.SaveAssetIfDirty(profile);
        SetActiveProfile(profile);
        EditorGUIUtility.PingObject(profile);
    }

    private void DuplicateProfile()
    {
        if (activeProfile == null)
        {
            EditorUtility.DisplayDialog("Duplicate profile", "Select an active profile first.", "OK");
            return;
        }

        string sourcePath = AssetDatabase.GetAssetPath(activeProfile);
        string suggestedName = Path.GetFileNameWithoutExtension(sourcePath) + " Copy";
        string targetPath = EditorUtility.SaveFilePanelInProject("Duplicate Game Config Profile", suggestedName, "asset", "Choose where to save the duplicate.", Path.GetDirectoryName(sourcePath));
        if (string.IsNullOrEmpty(targetPath))
            return;
        if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
        {
            EditorUtility.DisplayDialog("Duplicate profile", "Unity could not copy the profile asset.", "OK");
            return;
        }

        AssetDatabase.ImportAsset(targetPath);
        var copy = AssetDatabase.LoadAssetAtPath<GameConfigProfile>(targetPath);
        if (copy == null)
        {
            EditorUtility.DisplayDialog("Duplicate profile", "The copied asset could not be loaded.", "OK");
            return;
        }
        copy.profileId = Guid.NewGuid().ToString("N");
        EditorUtility.SetDirty(copy);
        AssetDatabase.SaveAssetIfDirty(copy);
        SetActiveProfile(copy);
        EditorGUIUtility.PingObject(copy);
    }

    private void OpenProfileAsset()
    {
        if (activeProfile == null)
        {
            EditorUtility.DisplayDialog("Open profile", "Select an active profile first.", "OK");
            return;
        }
        Selection.activeObject = activeProfile;
        EditorGUIUtility.PingObject(activeProfile);
        AssetDatabase.OpenAsset(activeProfile);
    }

    private void BuildWithActiveProfile()
    {
        if (activeProfile == null || !CanRunEditorMaintenance())
            return;

        bool buildWithMasterReset = activeProfile.resetAllLocalDataOnFirstBuildRun;
        if (buildWithMasterReset && activeProfile.resetLocalDataOnFirstRun
            && !EditorUtility.DisplayDialog("Both startup reset paths are armed",
                "This build will use Master Reset to clear the player's local data on first run; building itself does not clear Editor data. The advanced per-profile reset recipe is also enabled and may run in addition. Both paths affect the same local save. Continue with the full Master Reset build?",
                "Build with Master Reset", "Cancel"))
            return;

        BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
        if (target != BuildTarget.StandaloneWindows64
            && target != BuildTarget.StandaloneWindows
            && target != BuildTarget.StandaloneLinux64
            && target != BuildTarget.StandaloneOSX)
        {
            EditorUtility.DisplayDialog("Build with active profile", "Select a Windows, Linux, or macOS Standalone target in Build Settings first. The target is not changed by this tool.", "OK");
            return;
        }

        string location = ChooseBuildLocation(target);
        if (string.IsNullOrEmpty(location))
            return;

        string[] scenes = GetEnabledBuildScenes();
        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog("Build with active profile", "No enabled scenes are listed in Build Settings.", "OK");
            return;
        }

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = location,
            target = target,
            options = developmentBuildToggle != null && developmentBuildToggle.value
                ? BuildOptions.Development
                : BuildOptions.None
        };

        FullScreenMode originalMode = PlayerSettings.fullScreenMode;
        int originalWidth = PlayerSettings.defaultScreenWidth;
        int originalHeight = PlayerSettings.defaultScreenHeight;
        bool originalResizable = PlayerSettings.resizableWindow;
        string buildSummary = "";
        bool playerSettingsRestored = false;
        bool profileResetFieldsNeedRestore = false;
        bool profileResetFieldsRestored = !buildWithMasterReset;
        bool originalFirstRunReset = activeProfile.resetLocalDataOnFirstRun;
        GameResetTargets originalResetTargets = activeProfile.resetTargets;
        string originalResetRevision = activeProfile.resetRevision;
        try
        {
            int resolutionIndex = Mathf.Clamp(activeProfile.defaults.resolutionIndex, 0, 2);
            int[] widths = { 1280, 1920, 2560 };
            int[] heights = { 720, 1080, 1440 };
            PlayerSettings.defaultScreenWidth = widths[resolutionIndex];
            PlayerSettings.defaultScreenHeight = heights[resolutionIndex];
            PlayerSettings.fullScreenMode = activeProfile.defaults.fullscreen
                ? FullScreenMode.FullScreenWindow
                : FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = activeProfile.defaults.resizableWindow;

            if (buildWithMasterReset)
            {
                profileResetFieldsNeedRestore = true;
                activeProfile.resetLocalDataOnFirstRun = true;
                activeProfile.resetTargets = GameResetTargets.AllLocal;
                activeProfile.resetRevision = "master-build-" + Guid.NewGuid().ToString("N");
                EditorUtility.SetDirty(activeProfile);
                AssetDatabase.SaveAssetIfDirty(activeProfile);
                if (EditorUtility.IsDirty(activeProfile))
                    throw new IOException("The active profile could not be saved with the build reset token.");
            }

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            buildSummary = "Build result: " + summary.result + "\nOutput: " + summary.outputPath
                + "\nErrors: " + summary.totalErrors + "\nWarnings: " + summary.totalWarnings;
        }
        catch (Exception exception)
        {
            buildSummary = "Build failed: " + exception.GetType().Name + ": " + exception.Message;
        }
        finally
        {
            var restoreErrors = new List<string>();
            if (profileResetFieldsNeedRestore)
            {
                RestoreSetting(() =>
                {
                    activeProfile.resetLocalDataOnFirstRun = originalFirstRunReset;
                    activeProfile.resetTargets = originalResetTargets;
                    activeProfile.resetRevision = originalResetRevision;
                    EditorUtility.SetDirty(activeProfile);
                    AssetDatabase.SaveAssetIfDirty(activeProfile);
                    if (EditorUtility.IsDirty(activeProfile))
                        throw new IOException("Unity still reports the active profile as unsaved.");
                }, "active profile reset fields", restoreErrors);
                profileResetFieldsRestored = restoreErrors.Count == 0
                    && activeProfile.resetLocalDataOnFirstRun == originalFirstRunReset
                    && activeProfile.resetTargets == originalResetTargets
                    && string.Equals(activeProfile.resetRevision, originalResetRevision, StringComparison.Ordinal)
                    && !EditorUtility.IsDirty(activeProfile);
                if (!profileResetFieldsRestored && restoreErrors.Count == 0)
                    restoreErrors.Add("active profile reset fields did not return to their original values");
            }
            RestoreSetting(() => PlayerSettings.fullScreenMode = originalMode, "fullscreen mode", restoreErrors);
            RestoreSetting(() => PlayerSettings.defaultScreenWidth = originalWidth, "default screen width", restoreErrors);
            RestoreSetting(() => PlayerSettings.defaultScreenHeight = originalHeight, "default screen height", restoreErrors);
            RestoreSetting(() => PlayerSettings.resizableWindow = originalResizable, "resizable window", restoreErrors);
            playerSettingsRestored = restoreErrors.Count == 0
                && PlayerSettings.fullScreenMode == originalMode
                && PlayerSettings.defaultScreenWidth == originalWidth
                && PlayerSettings.defaultScreenHeight == originalHeight
                && PlayerSettings.resizableWindow == originalResizable;
            if (restoreErrors.Count > 0)
                buildSummary += "\nRestore errors: " + string.Join("; ", restoreErrors);
            if (!playerSettingsRestored && restoreErrors.Count == 0)
                buildSummary += "\nPlayerSettings restore verification did not match the original values.";
        }

        bool allTemporarySettingsRestored = playerSettingsRestored && profileResetFieldsRestored;
        buildSummary += allTemporarySettingsRestored
            ? "\n\nOriginal PlayerSettings and profile reset fields were restored."
            : "\n\nTemporary settings were not fully restored; review the errors above.";
        EditorUtility.DisplayDialog("Build with active profile", buildSummary, "OK");
    }

    private static string ChooseBuildLocation(BuildTarget target)
    {
        string product = string.IsNullOrWhiteSpace(PlayerSettings.productName) ? "StickyWords" : PlayerSettings.productName;
        switch (target)
        {
            case BuildTarget.StandaloneWindows64:
            case BuildTarget.StandaloneWindows:
                return EditorUtility.SaveFilePanel("Choose Windows player output", "", product + ".exe", "exe");
            case BuildTarget.StandaloneLinux64:
                return EditorUtility.SaveFilePanel("Choose Linux player output", "", product, "");
            case BuildTarget.StandaloneOSX:
                return EditorUtility.SaveFolderPanel("Choose macOS app bundle output", "", product + ".app");
            default:
                return "";
        }
    }

    private static string[] GetEnabledBuildScenes()
    {
        var scenes = new List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            if (scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                scenes.Add(scene.path);
        return scenes.ToArray();
    }

    private static void RestoreSetting(Action restore, string name, List<string> errors)
    {
        try { restore(); }
        catch (Exception exception) { errors.Add(name + ": " + exception.Message); }
    }

    private void PreviewLocalReset(GameResetTargets targets)
    {
        manualPlan = GameDataResetService.Preview(targets);
        UpdatePlanOutput();
        UpdateManualPlanControls();
    }

    private void ResetAllLocalNow()
    {
        if (!CanRunEditorMaintenance())
            return;

        GameResetPlan plan = GameDataResetService.Preview(GameResetTargets.AllLocal);
        if (plan == null || !plan.IsValid)
        {
            if (masterResetOutput != null)
                masterResetOutput.text = "Master Reset preview failed: " + (plan == null ? "no plan was returned." : plan.Error);
            return;
        }

        string confirmation = FormatPlan(plan, includeBackup: true)
            + "\n\nThis clears all local settings and progress for the Editor environment after creating a recoverable backup. It does not clear Steam achievements or separately scan the Standalone app's PlayerPrefs. Continue?";
        if (!EditorUtility.DisplayDialog("Master Reset: all local data", confirmation, "Reset all local data", "Cancel"))
            return;

        GameResetResult result = GameDataResetService.Execute(plan);
        if (masterResetOutput != null)
            masterResetOutput.text = FormatResult(result);
        UpdateMasterResetControls();
    }

    private void ArmBothResets()
    {
        if (!CanRunEditorMaintenance())
            return;
        if (activeProfile == null)
        {
            if (masterResetOutput != null)
                masterResetOutput.text = "Select an active profile before arming Master Reset.";
            return;
        }

        try
        {
            Undo.RecordObject(activeProfile, "Arm both Master Reset options");
            activeProfile.resetAllLocalDataOnNextEditorPlay = true;
            activeProfile.resetAllLocalDataOnFirstBuildRun = true;
            MasterResetPlayModeHook.Arm(activeProfile);
            EditorUtility.SetDirty(activeProfile);
            AssetDatabase.SaveAssetIfDirty(activeProfile);
            if (EditorUtility.IsDirty(activeProfile))
                throw new IOException("Unity still reports the active profile as unsaved.");
            if (masterResetOutput != null)
                masterResetOutput.text = "Armed for the next Editor Play and each profile build's first run. The build run shares this game's local save on the same machine.";
            BuildProfileView();
        }
        catch (Exception exception)
        {
            if (masterResetOutput != null)
                masterResetOutput.text = "Could not save Master Reset flags: " + exception.GetType().Name + ": " + exception.Message;
        }
        UpdateMasterResetControls();
    }

    private void SaveResetRecipeToActiveProfile(GameResetTargets targets)
    {
        if (activeProfile == null || activeProfile.resetTargets == targets)
            return;

        var profileObject = new SerializedObject(activeProfile);
        profileObject.Update();
        SerializedProperty resetTargets = profileObject.FindProperty("resetTargets");
        if (resetTargets == null)
            return;
        resetTargets.intValue = (int)targets;
        profileObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(activeProfile);
        AssetDatabase.SaveAssetIfDirty(activeProfile);
    }

    private void ExecuteLocalReset()
    {
        if (manualPlan == null || !manualPlan.IsValid || !CanRunEditorMaintenance())
            return;
        string description = FormatPlan(manualPlan, includeBackup: true);
        if (!EditorUtility.DisplayDialog("Confirm local reset", description + "\n\nA backup will be kept in this path.", "Reset selected data", "Cancel"))
            return;

        GameResetResult result = GameDataResetService.Execute(manualPlan);
        manualPlan = null;
        planOutput.text = FormatResult(result);
        UpdateManualPlanControls();
    }

    private void UpdatePlanOutput()
    {
        if (planOutput == null)
            return;
        planOutput.text = manualPlan == null
            ? "Choose reset scopes and preview their exact keys and paths."
            : FormatPlan(manualPlan, includeBackup: true);
    }

    private void PreviewStandaloneReset()
    {
        standalonePreview = WindowsStandalonePreferencesReset.Preview(standaloneTargets);
        standaloneOutput.text = standalonePreview.Description;
        UpdateStandaloneControls();
    }

    private void ExecuteStandaloneReset()
    {
        if (standalonePreview == null || !standalonePreview.CanExecute || !CanRunEditorMaintenance())
            return;
        if (!EditorUtility.DisplayDialog("Confirm standalone PlayerPrefs reset", standalonePreview.Description + "\n\nA typed registry backup will be saved before any values are removed.", "Remove listed values", "Cancel"))
            return;

        StandalonePreferencesResult result = WindowsStandalonePreferencesReset.Execute(standalonePreview);
        standalonePreview = null;
        standaloneOutput.text = result.Description;
        UpdateStandaloneControls();
    }

    private void InspectSteamAccount()
    {
        EnsureSteamInspectCoordinator();
        steamHelperType = Type.GetType("SteamAchievementMaintenance, SteamAchievementIntegration", false);
        if (steamHelperType == null)
        {
            steamInspectCoordinator.Fail("Steam maintenance is unavailable. The optional Steamworks integration assembly is not loaded.");
            return;
        }

        MethodInfo inspect = steamHelperType.GetMethod("InspectAccount", BindingFlags.Public | BindingFlags.Static);
        if (inspect == null)
        {
            steamInspectCoordinator.Fail("Steam maintenance helper is present but does not expose InspectAccount.");
            return;
        }

        waitingForSteamHelperResult = false;
        steamInspectCoordinator.Inspect(completion =>
        {
            try
            {
                inspect.Invoke(null, new object[] { completion });
            }
            catch (Exception exception)
            {
                throw UnwrapException(exception);
            }
        });
    }

    private void EnsureSteamInspectCoordinator()
    {
        if (steamInspectCoordinator != null)
            return;
        steamInspectCoordinator = new SteamInspectCoordinator();
        steamInspectCoordinator.StateChanged += OnSteamInspectStateChanged;
    }

    private void OnSteamInspectStateChanged()
    {
        if (this == null || steamInspectCoordinator == null)
            return;
        inspectedSteamId = steamInspectCoordinator.InspectedSteamId;
        if (steamOutput != null)
            steamOutput.text = steamInspectCoordinator.Message;
        UpdateSteamControls();
    }

    private void RestoreSteamHelperResultIfAvailable()
    {
        if (steamOutput == null)
            return;
        if (steamHelperType == null)
            steamHelperType = Type.GetType("SteamAchievementMaintenance, SteamAchievementIntegration", false);
        if (steamHelperType == null)
            return;

        EnsureSteamInspectCoordinator();
        if (ReadSteamBusy())
        {
            waitingForSteamHelperResult = true;
            steamInspectCoordinator.RestoreDiagnostic("Steam maintenance is still running; waiting for Steam's result…");
            return;
        }
        if (!waitingForSteamHelperResult && steamOutput.text != SteamInspectCoordinator.NotCheckedMessage)
            return;

        PropertyInfo messageProperty = steamHelperType.GetProperty("LastResultMessage", BindingFlags.Public | BindingFlags.Static);
        PropertyInfo successProperty = steamHelperType.GetProperty("LastOperationSucceeded", BindingFlags.Public | BindingFlags.Static);
        PropertyInfo steamIdProperty = steamHelperType.GetProperty("LastInspectedSteamId", BindingFlags.Public | BindingFlags.Static);
        try
        {
            string message = messageProperty?.GetValue(null) as string;
            bool? success = successProperty?.GetValue(null) as bool?;
            string steamId = steamIdProperty?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(message))
                return;

            waitingForSteamHelperResult = false;
            if (success == true && !string.IsNullOrWhiteSpace(steamId)
                && message.StartsWith("Account:", StringComparison.Ordinal))
                steamInspectCoordinator.RestoreSuccessfulInspect(steamId, message);
            else
                steamInspectCoordinator.RestoreDiagnostic(message);
        }
        catch (Exception exception)
        {
            waitingForSteamHelperResult = false;
            steamInspectCoordinator.RestoreDiagnostic("Could not read the last Steam maintenance result: " + UnwrapException(exception).Message);
        }
    }

    private void ConfirmAndResetSteamAccount()
    {
        if (string.IsNullOrWhiteSpace(inspectedSteamId) || steamHelperType == null || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        string confirmation = "This resets only the three StickyWords achievements for SteamID " + inspectedSteamId
            + " (AppID 4966870). The action affects this Steam account across all profiles and cannot be undone on Steam. Continue?";
        if (!EditorUtility.DisplayDialog("Confirm Steam achievement reset", confirmation, "Reset these achievements", "Cancel"))
            return;

        MethodInfo reset = steamHelperType.GetMethod("ResetCurrentAccount", BindingFlags.Public | BindingFlags.Static);
        if (reset == null)
        {
            steamOutput.text = "Steam maintenance helper is present but does not expose ResetCurrentAccount.";
            return;
        }

        string expectedSteamId = inspectedSteamId;
        resetSteamButton?.SetEnabled(false);
        steamOutput.text = "Reset request started for SteamID " + expectedSteamId + ". Waiting for Steam's result…";
        try
        {
            Action<bool, string> completion = (success, description) =>
            {
                steamOutput.text = description;
                if (!success)
                    inspectedSteamId = null;
                UpdateSteamControls();
            };
            reset.Invoke(null, new object[] { expectedSteamId, completion });
        }
        catch (Exception exception)
        {
            inspectedSteamId = null;
            steamOutput.text = "Steam reset could not start: " + UnwrapException(exception).Message;
            UpdateSteamControls();
        }
    }

    private static Exception UnwrapException(Exception exception)
    {
        return exception is TargetInvocationException invocation && invocation.InnerException != null
            ? invocation.InnerException
            : exception;
    }

    private void RefreshActionState()
    {
        UpdateManualPlanControls();
        UpdateStandaloneControls();
        UpdateMasterResetControls();
        UpdateSteamControls();
    }

    private bool CanRunEditorMaintenance()
    {
        return !EditorApplication.isPlaying
            && !EditorApplication.isPlayingOrWillChangePlaymode
            && !ReadSteamBusy()
            && (steamInspectCoordinator == null || !steamInspectCoordinator.IsChecking);
    }

    private void UpdateMasterResetControls()
    {
        bool allowed = CanRunEditorMaintenance();
        if (masterResetNowButton != null)
            masterResetNowButton.SetEnabled(allowed);
        if (armBothResetsButton != null)
            armBothResetsButton.SetEnabled(allowed && activeProfile != null);
    }

    private void UpdateManualPlanControls()
    {
        bool allowed = CanRunEditorMaintenance();
        if (manualTargetField != null) manualTargetField.SetEnabled(allowed);
        if (executeResetButton != null) executeResetButton.SetEnabled(allowed && manualPlan != null && manualPlan.IsValid);
        if (scopeButtons != null)
            foreach (VisualElement child in scopeButtons.Children()) child.SetEnabled(allowed);
    }

    private void UpdateStandaloneControls()
    {
        bool allowed = CanRunEditorMaintenance();
        if (standaloneTargetField != null) standaloneTargetField.SetEnabled(allowed && Application.platform == RuntimePlatform.WindowsEditor);
        if (executeStandaloneButton != null) executeStandaloneButton.SetEnabled(allowed && standalonePreview != null && standalonePreview.CanExecute);
        if (buildProfileButton != null) buildProfileButton.SetEnabled(allowed && activeProfile != null);
    }

    private void UpdateSteamControls()
    {
        if (steamHelperType == null)
            steamHelperType = Type.GetType("SteamAchievementMaintenance, SteamAchievementIntegration", false);
        bool playAllowed = !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode;
        bool busy = ReadSteamBusy() || (steamInspectCoordinator != null && steamInspectCoordinator.IsChecking);
        if (inspectSteamButton != null) inspectSteamButton.SetEnabled(playAllowed && !busy && steamHelperType != null);
        if (resetSteamButton != null) resetSteamButton.SetEnabled(playAllowed && !busy && steamHelperType != null && !string.IsNullOrWhiteSpace(inspectedSteamId));
    }

    private bool ReadSteamBusy()
    {
        PropertyInfo property = steamHelperType?.GetProperty("IsBusy", BindingFlags.Public | BindingFlags.Static);
        try { return property != null && (bool)property.GetValue(null); }
        catch { return true; }
    }

    private static string FormatPlan(GameResetPlan plan, bool includeBackup)
    {
        if (!plan.IsValid)
            return "Preview failed: " + plan.Error;
        var lines = new List<string>
        {
            "Selected: " + DescribeTargets(plan.Targets),
            "PlayerPrefs keys (present keys will be deleted):"
        };
        foreach (GameResetPreferenceSnapshot pref in plan.Preferences)
            lines.Add("  • " + pref.key + (pref.existed ? " [present]" : " [not present]"));
        lines.Add("Persistent-data paths:");
        foreach (GameResetPathSnapshot path in plan.Paths)
            lines.Add("  • " + path.originalPath + (path.existed ? " [will be backed up]" : " [not present]"));
        if (includeBackup)
            lines.Add("Backup: " + plan.BackupDirectory);
        return string.Join("\n", lines);
    }

    private static string FormatResult(GameResetResult result)
    {
        if (result == null)
            return "Reset returned no result.";
        string text = result.Message;
        if (!string.IsNullOrEmpty(result.BackupDirectory))
            text += "\nBackup: " + result.BackupDirectory;
        if (!result.RollbackSucceeded)
            text += "\nRollback was incomplete: " + result.RollbackError;
        return text;
    }

    private static string DescribeTargets(GameResetTargets targets)
    {
        if (targets == GameResetTargets.AllLocal)
            return "All local targets (Steam is excluded)";
        var selected = new List<string>();
        for (int i = 0; i < IndividualTargets.Length; i++)
            if ((targets & IndividualTargets[i]) != 0)
                selected.Add(TargetNames[i]);
        return selected.Count == 0 ? "None" : string.Join(", ", selected);
    }

    private static GameConfig FindOneConfig()
    {
        return AssetDatabase.LoadAssetAtPath<GameConfig>(CanonicalGameConfigAssetPath);
    }
}

public sealed class StandalonePreferencesPreview
{
    public bool CanExecute { get; internal set; }
    public string Description { get; internal set; }
    internal GameResetTargets targets;
    internal string registryPath;
    internal List<WindowsStandalonePreferencesReset.Entry> entries;
    internal string backupDirectory;
}

public sealed class StandalonePreferencesResult
{
    public bool Succeeded { get; internal set; }
    public string Description { get; internal set; }
}

internal static class WindowsStandalonePreferencesReset
{
    private const string BackupFolderName = ".GameDataResetBackups";

    internal static StandalonePreferencesPreview Preview(GameResetTargets targets)
    {
        if (Application.platform != RuntimePlatform.WindowsEditor)
            return new StandalonePreferencesPreview { Description = "Standalone PlayerPrefs cleanup is available only in the Windows Editor.", CanExecute = false };
        if (targets == GameResetTargets.None)
            return new StandalonePreferencesPreview { Description = "Select one or more preference scopes first.", CanExecute = false };

        string company = PlayerSettings.companyName;
        string product = PlayerSettings.productName;
        if (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(product)
            || company.Contains("\\") || company.Contains("/") || product.Contains("\\") || product.Contains("/"))
            return new StandalonePreferencesPreview { Description = "Company or product name cannot be mapped safely to a Windows registry path.", CanExecute = false };

        string registryPath = @"Software\" + company + "\\" + product;
        GameResetPlan keyPlan = GameDataResetService.Preview(
            targets,
            Application.persistentDataPath,
            new EmptyPreferences(),
            GameDataResetService.GetKnownChallengeCounts(Application.streamingAssetsPath));
        if (!keyPlan.IsValid)
            return new StandalonePreferencesPreview { Description = "Could not enumerate owned keys: " + keyPlan.Error, CanExecute = false };

        var ownedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameResetPreferenceSnapshot preference in keyPlan.Preferences)
            ownedKeys.Add(preference.key);

        try
        {
            var entries = new List<Entry>();
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registryPath, writable: false))
            {
                if (key != null)
                {
                    foreach (string valueName in key.GetValueNames())
                    {
                        if (!MatchesOwnedKey(valueName, ownedKeys))
                            continue;
                        RegistryValueKind kind = key.GetValueKind(valueName);
                        object value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (!IsSupported(kind, value))
                            return new StandalonePreferencesPreview { Description = "An owned value has an unsupported registry type; no values can be reset safely.", CanExecute = false };
                        entries.Add(new Entry(valueName, kind, CloneValue(value)));
                    }
                }
            }

            string backupDirectory = Path.Combine(Application.persistentDataPath, BackupFolderName, "StandalonePrefs-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N"));
            ValidateBackupDirectory(backupDirectory);
            var lines = new List<string>
            {
                "Registry: HKCU\\" + registryPath,
                "Scopes: " + targets,
                "Matched owned values: " + entries.Count
            };
            foreach (Entry entry in entries)
                lines.Add("  • " + entry.Name + " (" + entry.Kind + ")");
            lines.Add("Backup manifest: " + Path.Combine(backupDirectory, "manifest.json"));
            if (entries.Count == 0)
                lines.Add("No matching values were found; no registry values will be changed.");

            return new StandalonePreferencesPreview
            {
                CanExecute = entries.Count > 0,
                Description = string.Join("\n", lines),
                targets = targets,
                registryPath = registryPath,
                entries = entries,
                backupDirectory = backupDirectory
            };
        }
        catch (Exception exception)
        {
            return new StandalonePreferencesPreview { Description = "Could not inspect standalone preferences: " + exception.Message, CanExecute = false };
        }
    }

    internal static StandalonePreferencesResult Execute(StandalonePreferencesPreview preview)
    {
        if (Application.platform != RuntimePlatform.WindowsEditor)
            return new StandalonePreferencesResult { Succeeded = false, Description = "Standalone PlayerPrefs cleanup is available only in the Windows Editor." };
        if (preview == null || !preview.CanExecute || preview.entries == null)
            return new StandalonePreferencesResult { Succeeded = false, Description = "Standalone preference preview is invalid." };

        string currentRegistryPath;
        try
        {
            currentRegistryPath = BuildRegistryPath(PlayerSettings.companyName, PlayerSettings.productName);
            ValidateBackupDirectory(preview.backupDirectory);
        }
        catch (Exception exception)
        {
            return new StandalonePreferencesResult { Succeeded = false, Description = "Standalone reset scope is no longer safe: " + exception.Message };
        }
        if (!string.Equals(currentRegistryPath, preview.registryPath, StringComparison.OrdinalIgnoreCase))
            return new StandalonePreferencesResult { Succeeded = false, Description = "Company/product settings changed after preview. Preview the standalone preferences again." };

        var deleted = new List<Entry>();
        bool backupCreated = false;
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(preview.registryPath, writable: true))
            {
                if (key == null)
                    return new StandalonePreferencesResult { Succeeded = false, Description = "The Standalone PlayerPrefs key no longer exists. Preview again." };

                foreach (Entry entry in preview.entries)
                {
                    if (!key.GetValueNames().Contains(entry.Name, StringComparer.Ordinal))
                        return new StandalonePreferencesResult { Succeeded = false, Description = "A listed registry value changed after preview. Preview again before resetting." };
                    RegistryValueKind currentKind = key.GetValueKind(entry.Name);
                    if (currentKind != entry.Kind)
                        return new StandalonePreferencesResult { Succeeded = false, Description = "A listed registry value changed type after preview. Preview again before resetting." };
                    object currentValue = key.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (!ValuesEqual(currentValue, entry.Value))
                        return new StandalonePreferencesResult { Succeeded = false, Description = "A listed registry value changed after preview. Preview again before resetting." };
                }

                // Re-evaluate the canonical destination and project route immediately before
                // creating the backup: both can change between preview and this transaction.
                currentRegistryPath = BuildRegistryPath(PlayerSettings.companyName, PlayerSettings.productName);
                if (!string.Equals(currentRegistryPath, preview.registryPath, StringComparison.OrdinalIgnoreCase))
                    return new StandalonePreferencesResult { Succeeded = false, Description = "Company/product settings changed after preview. Preview the standalone preferences again." };
                string canonicalBackup = ValidateBackupDirectory(preview.backupDirectory);
                if (Directory.Exists(canonicalBackup) || File.Exists(canonicalBackup))
                    return new StandalonePreferencesResult { Succeeded = false, Description = "The previewed backup destination already exists. Preview again." };
                Directory.CreateDirectory(Path.GetDirectoryName(canonicalBackup));
                ValidateBackupDirectory(canonicalBackup);
                if (Directory.Exists(canonicalBackup) || File.Exists(canonicalBackup))
                    return new StandalonePreferencesResult { Succeeded = false, Description = "The previewed backup destination was occupied. Preview again." };
                Directory.CreateDirectory(canonicalBackup);
                ValidateBackupDirectory(canonicalBackup);
                backupCreated = true;
                var manifest = new RegistryManifest
                {
                    createdUtc = DateTime.UtcNow.ToString("o"),
                    registryPath = @"HKCU\" + preview.registryPath,
                    values = new List<RegistryManifestEntry>()
                };
                foreach (Entry entry in preview.entries)
                    manifest.values.Add(ToManifestEntry(entry));
                File.WriteAllText(Path.Combine(canonicalBackup, "manifest.json"), JsonUtility.ToJson(manifest, true));

                foreach (Entry entry in preview.entries)
                {
                    deleted.Add(entry);
                    key.DeleteValue(entry.Name, throwOnMissingValue: true);
                }
                key.Flush();
            }

            return new StandalonePreferencesResult { Succeeded = true, Description = "Standalone preferences reset. Typed registry backup: " + preview.backupDirectory };
        }
        catch (Exception exception)
        {
            string rollbackError = RestoreRegistryValues(preview.registryPath, deleted);
            string message = "Standalone preference reset failed: " + exception.GetType().Name + ": " + exception.Message;
            if (!string.IsNullOrEmpty(rollbackError))
                message += "\nRegistry rollback was incomplete: " + rollbackError;
            if (backupCreated)
                message += "\nBackup retained: " + preview.backupDirectory;
            return new StandalonePreferencesResult { Succeeded = false, Description = message };
        }
    }

    private static string RestoreRegistryValues(string registryPath, List<Entry> entries)
    {
        if (entries.Count == 0)
            return "";
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true))
            {
                if (key == null)
                    return "Could not reopen the Standalone PlayerPrefs registry key.";
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    Entry entry = entries[i];
                    key.SetValue(entry.Name, CloneValue(entry.Value), entry.Kind);
                }
                key.Flush();
            }
            return "";
        }
        catch (Exception exception)
        {
            return exception.GetType().Name + ": " + exception.Message;
        }
    }

    private static bool MatchesOwnedKey(string registryName, HashSet<string> ownedKeys)
    {
        if (ownedKeys.Contains(registryName))
            return true;
        foreach (string key in ownedKeys)
        {
            string prefix = key + "_h";
            if (!registryName.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            string suffix = registryName.Substring(prefix.Length);
            if (suffix.Length > 0 && IsDecimal(suffix))
                return true;
        }
        return false;
    }

    private static string BuildRegistryPath(string company, string product)
    {
        if (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(product)
            || company.Contains("\\") || company.Contains("/") || product.Contains("\\") || product.Contains("/"))
            throw new InvalidOperationException("Company or product name cannot be mapped safely to a Windows registry path.");
        return @"Software\" + company + "\\" + product;
    }

    private static string ValidateBackupDirectory(string backupDirectory)
    {
        string root = Path.GetFullPath(Application.persistentDataPath);
        string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(backupDirectory);
        StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(rootPrefix, comparison))
            throw new UnauthorizedAccessException("Backup must stay inside Application.persistentDataPath.");

        string current = candidate;
        string rootWithoutSeparator = rootPrefix.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (current.Length >= rootWithoutSeparator.Length)
        {
            if (Directory.Exists(current) || File.Exists(current))
            {
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Backup path contains a reparse point: " + current);
            }
            if (string.Equals(current, rootWithoutSeparator, comparison))
                break;
            current = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(current))
                break;
        }
        return candidate;
    }

    private static bool IsDecimal(string value)
    {
        foreach (char character in value)
            if (character < '0' || character > '9')
                return false;
        return true;
    }

    private static bool IsSupported(RegistryValueKind kind, object value)
    {
        switch (kind)
        {
            case RegistryValueKind.String:
            case RegistryValueKind.ExpandString: return value is string;
            case RegistryValueKind.DWord: return value is int;
            case RegistryValueKind.QWord: return value is long;
            case RegistryValueKind.Binary:
            case RegistryValueKind.None: return value is byte[];
            case RegistryValueKind.MultiString: return value is string[];
            default: return false;
        }
    }

    private static object CloneValue(object value)
    {
        if (value is byte[] bytes)
            return (byte[])bytes.Clone();
        if (value is string[] strings)
            return (string[])strings.Clone();
        return value;
    }

    private static RegistryManifestEntry ToManifestEntry(Entry entry)
    {
        var manifest = new RegistryManifestEntry { name = entry.Name, kind = entry.Kind.ToString() };
        if (entry.Value is string text)
            manifest.textValue = text;
        else if (entry.Value is int intValue)
            manifest.numericValue = intValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        else if (entry.Value is long longValue)
            manifest.numericValue = longValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        else if (entry.Value is byte[] binary)
            manifest.base64Value = Convert.ToBase64String(binary);
        else if (entry.Value is string[] multi)
            manifest.multiStringValues = multi;
        return manifest;
    }

    private static bool ValuesEqual(object left, object right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.GetType() != right.GetType())
            return false;
        if (left is byte[] leftBytes && right is byte[] rightBytes)
            return leftBytes.SequenceEqual(rightBytes);
        if (left is string[] leftStrings && right is string[] rightStrings)
            return leftStrings.SequenceEqual(rightStrings, StringComparer.Ordinal);
        return left.Equals(right);
    }

    private sealed class EmptyPreferences : IGameResetPreferenceStore
    {
        public bool HasKey(string key) => false;
        public int GetInt(string key) => 0;
        public float GetFloat(string key) => 0;
        public string GetString(string key) => "";
        public void SetInt(string key, int value) { }
        public void SetFloat(string key, float value) { }
        public void SetString(string key, string value) { }
        public void DeleteKey(string key) { }
        public void Save() { }
    }

    internal sealed class Entry
    {
        public readonly string Name;
        public readonly RegistryValueKind Kind;
        public readonly object Value;
        public Entry(string name, RegistryValueKind kind, object value) { Name = name; Kind = kind; Value = value; }
    }

    [Serializable]
    private sealed class RegistryManifest
    {
        public string createdUtc;
        public string registryPath;
        public List<RegistryManifestEntry> values;
    }

    [Serializable]
    private sealed class RegistryManifestEntry
    {
        public string name;
        public string kind;
        public string textValue;
        public string numericValue;
        public string base64Value;
        public string[] multiStringValues;
    }
}
#endif
