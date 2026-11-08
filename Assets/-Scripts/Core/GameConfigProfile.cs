using System;
using UnityEngine;

/// <summary>
/// The first challenge tab to restore on a fresh local setup.
/// Community is deliberately excluded because its panel is not implemented yet.
/// </summary>
public enum InitialChallengeTab
{
    Challenges,
    Custom
}

public enum BuildPurpose
{
    LocalTesting,
    PlayerRelease
}

[Serializable]
public sealed class GameDefaultSettings
{
    [Header("Window")]
    public bool fullscreen;
    [Range(0, 2)] public int resolutionIndex = 1;
    public bool resizableWindow;

    [Header("Visuals and Input")]
    public bool crt = true;
    public bool screenShake = true;
    public bool actionPrompts = true;

    [Header("Audio")]
    [Range(0f, 1f)] public float masterVolume = 0.7f;
    [Range(0f, 1f)] public float sfxVolume = 0.7f;
    [Range(0f, 1f)] public float bgmVolume = 0.7f;

    [Header("Language and Identity")]
    public bool showPinyin = true;
    public string uiLanguage = "en";
    public string playerName = "Unnamed User #1234";

    [Header("Panels and Navigation")]
    public bool wordsPanel;
    public bool timerPanel;
    public bool infoPanel = true;
    public InitialChallengeTab initialChallengeTab = InitialChallengeTab.Challenges;
}

[CreateAssetMenu(fileName = "GameConfigProfile", menuName = "Words/Game Config Profile")]
public sealed class GameConfigProfile : ScriptableObject
{
    [Header("Profile")]
    [Tooltip("Stable ID used to scope an optional first-run reset marker. Each profile must have a unique ID.")]
    public string profileId;

    [Header("Build Purpose")]
    [Tooltip("Select LocalTesting for developer/test builds or PlayerRelease for a player-facing build.")]
    public BuildPurpose buildPurpose = BuildPurpose.LocalTesting;

    [Header("Steam Debug")]
    [Tooltip("Show read-only runtime Steam and Community diagnostics in LocalTesting builds. Does not change which Steam account is used.")]
    public bool enableSteamDiagnostics;
    [Tooltip("Allow the Editor-only Steam achievement maintenance controls. This is a tooling opt-in, not an account permission or identity setting.")]
    public bool allowEditorSteamTestTools;

    [Tooltip("Targets for this profile's optional, one-time local reset. Steam's remote achievements are never reset here.")]
    public GameResetTargets resetTargets = GameResetTargets.None;
    [Tooltip("When enabled, apply the selected local reset once per profileId and resetRevision. Leave disabled to preserve existing saves.")]
    public bool resetLocalDataOnFirstRun;
    public string resetRevision = "1";

    [Header("Master Reset")]
    [Tooltip("Arm one full local reset for the next Editor Play. The request is cleared only after the reset succeeds.")]
    public bool resetAllLocalDataOnNextEditorPlay;
    [Tooltip("When enabled, each build made with the Master Config build button receives a new reset token and resets local data once on that build's first run. Building does not reset the Editor data.")]
    public bool resetAllLocalDataOnFirstBuildRun;

    [Header("Feature Access and Caps")]
    [Tooltip("Maximum challenge levels available. 0 means unlimited. Unlock All Features bypasses this challenge cap.")]
    [Min(0)] public int maxChallengeLevels = 5;
    [Tooltip("Maximum number of custom lists. 0 means unlimited.")]
    [Min(0)] public int maxCustomLists = 1;
    [Tooltip("Maximum number of words per custom list. 0 means unlimited.")]
    [Min(0)] public int maxWordsPerList = 5;

    [Header("Language Content")]
    [Tooltip("Allow Chinese and mixed Chinese-English gameplay content. This does not change the UI language.")]
    public bool allowChineseAndMixed = true;

    [Header("Progression")]
    [Tooltip("Developer master unlock: bypass all progression thresholds, challenge-level caps, and the Community enabled switch. Custom-list and word-count caps still apply.")]
    public bool unlockAllFeatures;
    // Retained only so existing serialized profiles deserialize without losing data.
    [HideInInspector]
    [Tooltip("Legacy serialized compatibility field. No longer affects access; use Unlock All Features for the developer bypass.")]
    public bool ignoreProgressionLocks;
    [Min(0)] public int customUnlockLevels = 5;
    [Tooltip("Enable or disable Community independently of its progression threshold.")]
    public bool communityEnabled = true;
    [Min(0)] public int communityUnlockLevels = 10;

    [Header("Debug")]
    [Tooltip("Allow one key press to complete the current word normally and advance automatically. Scores, achievements, and progression are recorded as usual.")]
    public bool debugAnyKeyCompletesWord;

    [Header("Default Settings")]
    public GameDefaultSettings defaults = new GameDefaultSettings();

    /// <summary>Configured challenge cap, or zero when there is no active cap.</summary>
    public int EffectiveChallengeCap
    {
        get => unlockAllFeatures ? 0 : Mathf.Max(0, maxChallengeLevels);
    }

    public bool IsCommunityAvailable => communityEnabled || unlockAllFeatures;

    /// <summary>Returns whether Custom can be opened without changing saved progression.</summary>
    public bool CanOpenCustomLists(int completedChallenges)
    {
        int completed = Mathf.Max(0, completedChallenges);
        return unlockAllFeatures || completed >= Mathf.Max(0, customUnlockLevels);
    }

    /// <summary>Returns whether Community can be opened for the completed challenge count.</summary>
    public bool CanOpenCommunityLists(int completedChallenges)
    {
        int completed = Mathf.Max(0, completedChallenges);
        return unlockAllFeatures || (communityEnabled
            && completed >= Mathf.Max(0, communityUnlockLevels));
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(profileId))
            profileId = Guid.NewGuid().ToString("N");
    }
}
