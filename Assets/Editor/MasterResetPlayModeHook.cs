using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Executes an explicitly armed full local reset before the next Editor Play session.
/// The profile flag is cleared only after the reset succeeds and the profile asset saves.
/// </summary>
[InitializeOnLoad]
public static class MasterResetPlayModeHook
{
    private const string GameConfigAssetPath = "Assets/-Data/GameConfig.asset";
    private const string SteamMaintenanceTypeName = "SteamAchievementMaintenance, SteamAchievementIntegration";
    private const string ArmedTokenPrefix = "Words.MasterReset.NextEditorPlay.Armed.";
    private const string ConsumedTokenPrefix = "Words.MasterReset.NextEditorPlay.Consumed.";

    static MasterResetPlayModeHook()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    /// <summary>
    /// Starts a new one-shot request after the profile flag has been enabled in the UI.
    /// Keeping the request ticket in SessionState lets a failed asset save retry only the
    /// flag write during this Editor session without repeating the successful reset.
    /// </summary>
    public static void Arm(GameConfigProfile profile)
    {
        if (profile == null)
            return;

        string key = GetProfileSessionKey(profile, ArmedTokenPrefix);
        if (string.IsNullOrEmpty(key))
            return;

        SessionState.SetString(key, Guid.NewGuid().ToString("N"));
        string consumedKey = GetProfileSessionKey(profile, ConsumedTokenPrefix);
        if (!string.IsNullOrEmpty(consumedKey))
            SessionState.EraseString(consumedKey);
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode)
            return;

        GameConfig config = AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfigAssetPath);
        GameConfigProfile profile = config != null ? config.activeProfile : null;
        if (profile == null || !profile.resetAllLocalDataOnNextEditorPlay)
            return;

        string armedKey = GetProfileSessionKey(profile, ArmedTokenPrefix);
        string consumedKey = GetProfileSessionKey(profile, ConsumedTokenPrefix);
        if (string.IsNullOrEmpty(armedKey) || string.IsNullOrEmpty(consumedKey))
        {
            BlockPlay("The active profile is not a saved asset, so its one-shot reset cannot be tracked safely.");
            return;
        }

        string ticket = SessionState.GetString(armedKey, string.Empty);
        if (string.IsNullOrEmpty(ticket))
        {
            ticket = Guid.NewGuid().ToString("N");
            SessionState.SetString(armedKey, ticket);
        }

        if (string.Equals(SessionState.GetString(consumedKey, string.Empty), ticket, StringComparison.Ordinal))
        {
            if (TrySaveDisarmedProfile(profile, out string saveError))
            {
                ClearSessionTicket(armedKey, consumedKey);
                return;
            }

            BlockPlay("The local reset already succeeded, but the profile flag could not be saved off. "
                + "Play remains blocked to avoid repeating the reset. " + saveError);
            return;
        }

        if (EditorApplication.isCompiling)
        {
            BlockPlay("The one-shot local reset was not run because scripts are compiling. Retry Play after compilation finishes.");
            return;
        }

        if (!TryReadSteamMaintenanceBusy(out bool steamBusy, out string steamError))
        {
            BlockPlay("The one-shot local reset was not run because Steam maintenance state could not be checked. " + steamError);
            return;
        }

        if (steamBusy)
        {
            BlockPlay("The one-shot local reset was not run because Steam achievement maintenance is active. Finish or cancel that operation, then retry Play.");
            return;
        }

        GameResetPlan plan = GameDataResetService.Preview(GameResetTargets.AllLocal);
        if (plan == null || !plan.IsValid)
        {
            BlockPlay("The one-shot full local reset preview failed: " + (plan != null ? plan.Error : "No reset plan was created."));
            return;
        }

        GameResetResult result = GameDataResetService.Execute(plan);
        if (result == null || !result.Succeeded)
        {
            string message = result != null ? result.Message : "The reset service returned no result.";
            if (result != null && !string.IsNullOrEmpty(result.RollbackError))
                message += " Rollback issue: " + result.RollbackError;
            BlockPlay("The one-shot full local reset failed; the profile remains armed. " + message);
            return;
        }

        SessionState.SetString(consumedKey, ticket);
        if (!TrySaveDisarmedProfile(profile, out string profileError))
        {
            BlockPlay("The full local reset succeeded, but the profile flag could not be saved off. "
                + "Play remains blocked; the reset will not be repeated in this Editor session. "
                + "Backup: " + result.BackupDirectory + ". " + profileError);
            return;
        }

        ClearSessionTicket(armedKey, consumedKey);
        Debug.Log("[MasterReset] Full local reset completed before Editor Play. Recoverable backup: " + result.BackupDirectory);
    }

    private static bool TryReadSteamMaintenanceBusy(out bool busy, out string error)
    {
        busy = false;
        error = null;
        try
        {
            Type helperType = Type.GetType(SteamMaintenanceTypeName, false);
            if (helperType == null)
                return true;

            PropertyInfo property = helperType.GetProperty("IsBusy", BindingFlags.Public | BindingFlags.Static);
            if (property == null || property.PropertyType != typeof(bool))
                throw new MissingMemberException(SteamMaintenanceTypeName, "IsBusy");

            busy = (bool)property.GetValue(null, null);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            busy = true;
            return false;
        }
    }

    private static bool TrySaveDisarmedProfile(GameConfigProfile profile, out string error)
    {
        error = null;
        try
        {
            string assetPath = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new InvalidOperationException("The active profile has no asset path.");

            profile.resetAllLocalDataOnNextEditorPlay = false;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            if (EditorUtility.IsDirty(profile))
                throw new IOException("Unity still reports the profile as unsaved after saving the reset flag.");
            if (profile.resetAllLocalDataOnNextEditorPlay)
                throw new IOException("The profile reset flag is still enabled after saving.");
            return true;
        }
        catch (Exception exception)
        {
            profile.resetAllLocalDataOnNextEditorPlay = true;
            EditorUtility.SetDirty(profile);
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    private static string GetProfileSessionKey(GameConfigProfile profile, string prefix)
    {
        if (profile == null)
            return null;

        string assetPath = AssetDatabase.GetAssetPath(profile);
        if (string.IsNullOrWhiteSpace(assetPath))
            return null;

        string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrWhiteSpace(assetGuid))
            return null;

        return prefix + assetGuid;
    }

    private static void ClearSessionTicket(string armedKey, string consumedKey)
    {
        SessionState.EraseString(armedKey);
        SessionState.EraseString(consumedKey);
    }

    private static void BlockPlay(string message)
    {
        EditorApplication.isPlaying = false;
        Debug.LogError("[MasterReset] " + message);
    }
}
