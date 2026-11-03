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

    [Header("Demo and Capacity")]
    public bool isDemo;
    [Min(0)] public int maxChallengeLevels = 5;
    [Min(0)] public int maxCustomLists = 1;
    [Min(0)] public int maxWordsPerList = 5;
    [Min(0)] public int customUnlockLevels = 5;

    [Header("Language Content")]
    [Tooltip("Allow Chinese and mixed Chinese-English gameplay content. This does not change the UI language.")]
    public bool allowChineseAndMixed = true;

    [Header("Feature Unlocks")]
    [Tooltip("Bypass implemented feature gates and demo caps. This never changes saved challenge progress or Steam achievements.")]
    public bool unlockAllFeatures;
    [Tooltip("Unlock challenge levels and disable the demo level-count cap without recording progress.")]
    public bool unlockAllChallenges;
    [Tooltip("Open Custom before its challenge-clear threshold; this does not unlock list actions.")]
    public bool unlockCustomLists;
    [Tooltip("In Demo profiles, allow creating custom lists; the demo list-count cap still applies.")]
    public bool unlockCreateLists;
    [Tooltip("In Demo profiles, allow moving or deleting words in a list.")]
    public bool unlockEditLists;
    [Tooltip("In Demo profiles, allow deleting custom lists.")]
    public bool unlockDeleteLists;
    [Tooltip("In Demo profiles, allow duplicating custom lists; the demo list-count cap still applies.")]
    public bool unlockDuplicateLists;
    [Tooltip("In Demo profiles, allow importing custom lists; the demo list-count cap still applies.")]
    public bool unlockImportLists;
    [Tooltip("In Demo profiles, allow exporting lists.")]
    public bool unlockExportLists;
    [Tooltip("In Demo profiles, allow adding words; the demo per-list word cap still applies.")]
    public bool unlockAddWords;

    [Header("Default Settings")]
    public GameDefaultSettings defaults = new GameDefaultSettings();

    /// <summary>True when a feature can be used under this profile's demo policy.</summary>
    public bool IsFeatureAvailable(bool explicitlyUnlocked)
    {
        return !isDemo || unlockAllFeatures || explicitlyUnlocked;
    }

    /// <summary>Demo challenge cap, or zero when there is no active cap.</summary>
    public int EffectiveChallengeCap
    {
        get => isDemo && !unlockAllFeatures && !unlockAllChallenges
            ? Mathf.Max(0, maxChallengeLevels)
            : 0;
    }

    /// <summary>Returns whether Custom can be opened without changing saved progression.</summary>
    public bool CanOpenCustomLists(int completedChallenges)
    {
        return unlockAllFeatures || unlockCustomLists || completedChallenges >= Mathf.Max(0, customUnlockLevels);
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(profileId))
            profileId = Guid.NewGuid().ToString("N");
    }
}
