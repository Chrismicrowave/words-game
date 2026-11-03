#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Steamworks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only, explicit maintenance for this game's three Steam achievements.
/// This class owns Steamworks.NET initialization and callback pumping while active.
/// </summary>
public static class SteamAchievementMaintenance
{
    private const uint ExpectedAppId = 4966870;
    private const double OperationTimeoutSeconds = 15.0;
    private const string ZeroProgressJson = "{\n  \"schemaVersion\": 1,\n  \"completedWordCount\": 0,\n  \"hasCompletedWordWithD\": false\n}\n";

    private static readonly string[] AchievementNames =
    {
        WordAchievementApiNames.CompleteFirstWord,
        WordAchievementApiNames.CompleteTwoWords,
        WordAchievementApiNames.CompleteWordWithD
    };

    private static Operation activeOperation;
    private static bool editorHooksInstalled;
    private static string lastResultMessage;
    private static bool? lastOperationSucceeded;
    private static string lastInspectedSteamId;

    public static bool IsBusy => activeOperation != null;
    public static string LastResultMessage => lastResultMessage;
    public static bool? LastOperationSucceeded => lastOperationSucceeded;
    public static string LastInspectedSteamId => lastInspectedSteamId;

    static SteamAchievementMaintenance()
    {
        InstallEditorHooks();
    }

    /// <summary>Reads the current account and the three achievement states without changing them.</summary>
    public static void InspectAccount(Action<bool, string, string> completion)
    {
        Start(Operation.CreateInspect(completion));
    }

    /// <summary>
    /// Resets only this account's three word achievements after verifying its ID against the
    /// value explicitly confirmed by the Editor UI.
    /// </summary>
    public static void ResetCurrentAccount(string expectedSteamId, Action<bool, string> completion)
    {
        Start(Operation.CreateReset(expectedSteamId, completion));
    }

    private static void Start(Operation operation)
    {
        if (operation == null)
            return;

        if (operation.Kind == OperationKind.Reset && string.IsNullOrEmpty(operation.ExpectedSteamId))
        {
            operation.CompleteImmediately(false, "A valid Steam account must be confirmed by the Editor UI before resetting achievements.");
            return;
        }

        if (activeOperation != null)
        {
            operation.CompleteImmediately(false, "Steam achievement maintenance is already running.");
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode
            || EditorApplication.isPlaying)
        {
            operation.CompleteImmediately(false, "Steam achievement maintenance is available only while the Editor is idle outside Play mode.");
            return;
        }

        if (CallbackDispatcher.IsInitialized)
        {
            operation.CompleteImmediately(false,
                "Steam callbacks are already initialized by another owner. Maintenance cannot take over or shut down that Steam session.");
            return;
        }

        activeOperation = operation;
        operation.Deadline = EditorApplication.timeSinceStartup + OperationTimeoutSeconds;

        try
        {
            ESteamAPIInitResult initResult = SteamAPI.InitEx(out string initError);
            if (initResult != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                Finish(operation, false, "Steam could not be initialized: " + SafeText(initError));
                return;
            }

            if (!CallbackDispatcher.IsInitialized)
            {
                Finish(operation, false,
                    "Steamworks.NET reported successful initialization, but its callback dispatcher is not initialized. Reload the Editor scripts before retrying Steam maintenance.");
                return;
            }

            operation.OwnsSteamApiInitialization = true;

            uint appId = SteamUtils.GetAppID().m_AppId;
            if (appId != ExpectedAppId)
            {
                Finish(operation, false, "Steam is connected to AppID " + appId.ToString(CultureInfo.InvariantCulture)
                    + "; this tool only supports AppID " + ExpectedAppId.ToString(CultureInfo.InvariantCulture) + ".");
                return;
            }

            string steamId = GetCurrentSteamId();
            if (string.IsNullOrEmpty(steamId))
            {
                Finish(operation, false, "Steam did not provide a valid current account.");
                return;
            }

            operation.SteamId = steamId;
            operation.AccountName = GetAccountName();
            if (operation.Kind == OperationKind.Reset
                && !string.Equals(operation.ExpectedSteamId, steamId, StringComparison.Ordinal))
            {
                Finish(operation, false, "The connected Steam account does not match the account confirmed in the Editor UI.");
                return;
            }

            if (operation.Kind == OperationKind.Reset)
                operation.UserStatsStoredCallback = Callback<UserStatsStored_t>.Create(OnUserStatsStored);
        }
        catch (Exception exception)
        {
            if (CallbackDispatcher.IsInitialized)
                operation.OwnsSteamApiInitialization = true;
            Finish(operation, false, "Steam initialization failed: " + DescribeException(exception));
        }
    }

    private static void OnEditorUpdate()
    {
        Operation operation = activeOperation;
        if (operation == null)
            return;

        if (EditorApplication.isCompiling)
        {
            Finish(operation, false, "Steam maintenance was stopped because script compilation began.");
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            Finish(operation, false, "Steam maintenance was stopped because the Editor is entering Play mode.");
            return;
        }

        try
        {
            if (!VerifyCurrentAccount(operation))
                return;

            SteamAPI.RunCallbacks();
            if (!IsCurrentOperation(operation))
                return;

            if (!VerifyCurrentAccount(operation))
                return;

            if (EditorApplication.timeSinceStartup >= operation.Deadline)
            {
                Finish(operation, false, "Steam did not complete the requested operation within 15 seconds.");
                return;
            }

            if (operation.Phase == OperationPhase.ReadInitialState)
            {
                if (!TryReadAchievementStates(operation.InitialStates))
                    return;

                if (operation.Kind == OperationKind.Inspect)
                {
                    Finish(operation, true, BuildStatusDescription(operation, operation.InitialStates));
                    return;
                }

                BeginReset(operation);
                return;
            }

            if (operation.Phase == OperationPhase.WaitForStore)
            {
                if (!operation.StoreCallbackReceived)
                    return;

                if (operation.StoreResult != EResult.k_EResultOK)
                {
                    Finish(operation, false, "Steam rejected the achievement reset (" + operation.StoreResult
                        + "). The remote achievement state may have changed partially.");
                    return;
                }

                operation.Phase = OperationPhase.VerifyReset;
            }

            if (operation.Phase == OperationPhase.VerifyReset)
            {
                bool[] finalStates = new bool[AchievementNames.Length];
                if (!TryReadAchievementStates(finalStates))
                    return;

                for (int i = 0; i < finalStates.Length; i++)
                {
                    if (finalStates[i])
                    {
                        Finish(operation, false, "Steam reported the reset request as stored, but at least one achievement is still unlocked. The remote achievement state may have changed partially.");
                        return;
                    }
                }

                operation.ResetVerified = true;
                Finish(operation, true, BuildResetSuccessMessage(operation));
            }
        }
        catch (Exception exception)
        {
            string message = "Steam maintenance failed: " + DescribeException(exception);
            if (operation.StoreRequested)
                message += " The remote achievement state may have changed partially.";
            Finish(operation, false, message);
        }
    }

    private static void BeginReset(Operation operation)
    {
        if (!string.Equals(GetCurrentSteamId(), operation.ExpectedSteamId, StringComparison.Ordinal))
        {
            Finish(operation, false, "The connected Steam account no longer matches the account confirmed in the Editor UI.");
            return;
        }

        try
        {
            BackupAndWriteZeroProfile(operation);
        }
        catch (Exception exception)
        {
            Finish(operation, false, "The local achievement profile could not be backed up and reset: " + DescribeException(exception));
            return;
        }

        for (int i = 0; i < AchievementNames.Length; i++)
        {
            if (SteamUserStats.ClearAchievement(AchievementNames[i]))
            {
                operation.AnyAchievementCleared = true;
                continue;
            }

            Finish(operation, false, "Steam could not clear one of the three word achievements.");
            return;
        }

        if (!SteamUserStats.StoreStats())
        {
            Finish(operation, false, "Steam rejected the StoreStats request.");
            return;
        }

        operation.StoreRequested = true;
        operation.Phase = OperationPhase.WaitForStore;
    }

    private static void OnUserStatsStored(UserStatsStored_t callback)
    {
        Operation operation = activeOperation;
        if (operation == null || operation.Kind != OperationKind.Reset || !operation.StoreRequested)
            return;

        if (!string.Equals(GetCurrentSteamId(), operation.SteamId, StringComparison.Ordinal))
            return;

        uint callbackAppId = (uint)(callback.m_nGameID & 0x00FFFFFFUL);
        if (callbackAppId != ExpectedAppId)
            return;

        operation.StoreResult = callback.m_eResult;
        operation.StoreCallbackReceived = true;
    }

    private static bool TryReadAchievementStates(bool[] states)
    {
        bool allReadable = true;
        for (int i = 0; i < AchievementNames.Length; i++)
        {
            bool achieved;
            bool readable = SteamUserStats.GetAchievement(AchievementNames[i], out achieved);
            states[i] = achieved;
            if (!readable)
                allReadable = false;
        }

        return allReadable;
    }

    private static string BuildStatusDescription(Operation operation, bool[] states)
    {
        string account = string.IsNullOrWhiteSpace(operation.AccountName) ? "(name unavailable)" : operation.AccountName;
        return "Account: " + account + "; AppID: " + ExpectedAppId.ToString(CultureInfo.InvariantCulture)
            + "; " + AchievementNames[0] + ": " + FormatState(states[0])
            + "; " + AchievementNames[1] + ": " + FormatState(states[1])
            + "; " + AchievementNames[2] + ": " + FormatState(states[2]) + ".";
    }

    private static string BuildResetSuccessMessage(Operation operation)
    {
        return "Reset verified for " + (string.IsNullOrWhiteSpace(operation.AccountName) ? "the current Steam account" : operation.AccountName)
            + " (AppID " + ExpectedAppId.ToString(CultureInfo.InvariantCulture) + "). All three word achievements are locked."
            + " The local profile was reset. Backup retained at: " + operation.BackupDirectory;
    }

    private static string FormatState(bool achieved)
    {
        return achieved ? "unlocked" : "locked";
    }

    private static string GetCurrentSteamId()
    {
        ulong steamId = SteamUser.GetSteamID().m_SteamID;
        return steamId == 0 ? null : steamId.ToString(CultureInfo.InvariantCulture);
    }

    private static bool VerifyCurrentAccount(Operation operation)
    {
        string currentSteamId = GetCurrentSteamId();
        if (string.Equals(operation.SteamId, currentSteamId, StringComparison.Ordinal))
            return true;

        Finish(operation, false, "The connected Steam account changed during maintenance.");
        return false;
    }

    private static string GetAccountName()
    {
        string personaName = SteamFriends.GetPersonaName();
        return string.IsNullOrWhiteSpace(personaName) ? "(name unavailable)" : personaName.Trim();
    }

    private static void BackupAndWriteZeroProfile(Operation operation)
    {
        string persistentRoot = Path.GetFullPath(Application.persistentDataPath);
        string profileDirectory = Path.GetFullPath(Path.Combine(persistentRoot, "WordAchievementProfiles"));
        string profilePath = Path.GetFullPath(Path.Combine(profileDirectory, "steam-" + operation.SteamId + ".json"));
        EnsurePathIsUnderRoot(persistentRoot, profilePath);
        EnsureNoReparsePoints(persistentRoot, profilePath);

        string backupRoot = Path.GetFullPath(Path.Combine(persistentRoot, "MasterConfigBackups"));
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
        string backupDirectory = Path.GetFullPath(Path.Combine(backupRoot, "steam-" + timestamp));
        EnsurePathIsUnderRoot(persistentRoot, backupDirectory);
        EnsureNoReparsePoints(persistentRoot, backupDirectory);
        if (Directory.Exists(backupDirectory))
            backupDirectory = Path.Combine(backupRoot, "steam-" + timestamp + "-" + Guid.NewGuid().ToString("N"));

        EnsurePathIsUnderRoot(persistentRoot, backupDirectory);

        Directory.CreateDirectory(profileDirectory);
        Directory.CreateDirectory(backupDirectory);
        EnsureNoReparsePoints(persistentRoot, profilePath);
        EnsureNoReparsePoints(persistentRoot, backupDirectory);

        bool hadOriginalProfile = File.Exists(profilePath);
        string backupFilePath;
        if (hadOriginalProfile)
        {
            backupFilePath = Path.Combine(backupDirectory, "steam-" + operation.SteamId + ".json");
            EnsurePathIsUnderRoot(persistentRoot, backupFilePath);
            EnsureNoReparsePoints(persistentRoot, backupFilePath);
            File.Copy(profilePath, backupFilePath, false);
        }
        else
        {
            backupFilePath = Path.Combine(backupDirectory, "profile-was-absent.txt");
            File.WriteAllText(backupFilePath, "The Steam achievement profile file did not exist before this reset.\n", new UTF8Encoding(false));
        }

        operation.ProfilePath = profilePath;
        operation.BackupFilePath = backupFilePath;
        operation.BackupDirectory = backupDirectory;
        operation.HadOriginalProfile = hadOriginalProfile;
        operation.LocalMutationStarted = true;
        AtomicWrite(profilePath, new UTF8Encoding(false).GetBytes(ZeroProgressJson));
        operation.LocalMutationCompleted = true;
        try
        {
            operation.ZeroProfileLastWriteTimeUtc = File.GetLastWriteTimeUtc(profilePath);
            operation.HasZeroProfileLastWriteTimeUtc = true;
        }
        catch
        {
            // Exact zero-file content remains a fallback guard if timestamp metadata is unavailable.
        }
    }

    private static void RestoreLocalProfile(Operation operation)
    {
        if (!operation.LocalMutationStarted || operation.LocalMutationCompleted == false)
            return;

        string root = Path.GetFullPath(Application.persistentDataPath);
        EnsurePathIsUnderRoot(root, operation.ProfilePath);
        EnsureNoReparsePoints(root, operation.ProfilePath);

        if (string.IsNullOrEmpty(operation.BackupFilePath))
            throw new IOException("The profile backup path is missing.");
        EnsurePathIsUnderRoot(root, operation.BackupFilePath);
        EnsureNoReparsePoints(root, operation.BackupFilePath);

        if (!File.Exists(operation.ProfilePath))
        {
            if (operation.HadOriginalProfile)
                throw new IOException("The profile file disappeared during maintenance; it was left missing to protect newer file changes.");
            return;
        }

        byte[] currentBytes = File.ReadAllBytes(operation.ProfilePath);
        byte[] zeroBytes = new UTF8Encoding(false).GetBytes(ZeroProgressJson);
        if (!ByteArraysEqual(currentBytes, zeroBytes))
            throw new IOException("The profile file changed after the maintenance tool wrote it; newer data was left in place.");
        if (operation.HasZeroProfileLastWriteTimeUtc
            && File.GetLastWriteTimeUtc(operation.ProfilePath) != operation.ZeroProfileLastWriteTimeUtc)
        {
            throw new IOException("The profile file timestamp changed after the maintenance tool wrote it; newer data was left in place.");
        }

        if (operation.HadOriginalProfile)
        {
            if (!File.Exists(operation.BackupFilePath))
                throw new IOException("The profile backup is missing.");

            byte[] originalBytes = File.ReadAllBytes(operation.BackupFilePath);
            AtomicWrite(operation.ProfilePath, originalBytes);
            return;
        }

        File.Delete(operation.ProfilePath);
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        string directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
            throw new IOException("The profile file has no parent directory.");

        Directory.CreateDirectory(directory);
        string temporaryPath = path + ".maintenance-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void EnsurePathIsUnderRoot(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullPath = Path.GetFullPath(path);
        string prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The maintenance file path is outside the persistent data directory.");
    }

    private static void EnsureNoReparsePoints(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullPath = Path.GetFullPath(path);
        EnsurePathIsUnderRoot(fullRoot, fullPath);

        string relative = fullPath.Substring(fullRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string current = fullRoot;
        RejectReparsePointIfPresent(current);

        string[] segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);
            RejectReparsePointIfPresent(current);
        }
    }

    private static void RejectReparsePointIfPresent(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return;

        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A maintenance path component is a symbolic link or reparse point.");
    }

    private static bool ByteArraysEqual(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
            return false;

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return false;
        }

        return true;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        Operation operation = activeOperation;
        if (operation == null || state == PlayModeStateChange.EnteredEditMode)
            return;

        Finish(operation, false, "Steam maintenance was stopped because the Editor play state changed.");
    }

    private static void OnBeforeAssemblyReload()
    {
        Operation operation = activeOperation;
        if (operation != null)
            Finish(operation, false, "Steam maintenance was stopped before the Editor reloaded scripts.");
    }

    private static void OnEditorQuitting()
    {
        Operation operation = activeOperation;
        if (operation != null)
            Finish(operation, false, "Steam maintenance was stopped because the Editor is closing.");
    }

    private static void InstallEditorHooks()
    {
        if (editorHooksInstalled)
            return;

        editorHooksInstalled = true;
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        EditorApplication.quitting += OnEditorQuitting;
    }

    private static void Finish(Operation operation, bool success, string message)
    {
        if (operation == null || operation.Finished)
            return;

        operation.Finished = true;
        string finalMessage = message ?? "Steam achievement maintenance ended.";

        if (!success && (operation.StoreRequested || operation.AnyAchievementCleared)
            && finalMessage.IndexOf("remote achievement state may have changed partially", StringComparison.OrdinalIgnoreCase) < 0)
        {
            finalMessage += " The remote achievement state may have changed partially.";
        }

        if (!success && operation.LocalMutationCompleted && !operation.ResetVerified)
        {
            try
            {
                RestoreLocalProfile(operation);
                finalMessage += " The local profile was restored from its backup.";
            }
            catch (Exception rollbackException)
            {
                finalMessage += " Local profile rollback failed: " + DescribeException(rollbackException)
                    + ". Backup location: " + operation.BackupDirectory;
            }
        }

        if (ReferenceEquals(activeOperation, operation))
            activeOperation = null;

        if (operation.UserStatsStoredCallback != null)
        {
            try
            {
                operation.UserStatsStoredCallback.Dispose();
            }
            catch (Exception exception)
            {
                finalMessage += " Callback cleanup reported: " + DescribeException(exception) + ".";
                success = false;
            }
            operation.UserStatsStoredCallback = null;
        }

        if (operation.OwnsSteamApiInitialization)
        {
            operation.OwnsSteamApiInitialization = false;
            try
            {
                SteamAPI.Shutdown();
            }
            catch (Exception exception)
            {
                finalMessage += " Steam API shutdown reported: " + DescribeException(exception) + ".";
                success = false;
            }
        }

        CompleteOperation(operation, success, finalMessage);
    }

    private static void CompleteOperation(Operation operation, bool success, string message)
    {
        string safeMessage = RedactSteamIds(message ?? "Steam achievement maintenance ended.");
        lastResultMessage = safeMessage;
        lastOperationSucceeded = success;
        if (!success)
            lastInspectedSteamId = null;
        else if (!string.IsNullOrEmpty(operation.SteamId))
            lastInspectedSteamId = operation.SteamId;

        try
        {
            operation.InvokeCompletion(success, safeMessage);
        }
        catch (Exception exception)
        {
            LogCompletionCallbackFailure(exception);
        }
    }

    private static string RedactSteamIds(string text)
    {
        // SteamID64 values are never written to the Editor log or diagnostic message text.
        return Regex.Replace(text ?? string.Empty, @"(?<!\d)\d{17}(?!\d)", "[SteamID]");
    }

    private static void LogCompletionCallbackFailure(Exception exception)
    {
        try
        {
            string exceptionType = exception == null ? "UnknownException" : exception.GetType().FullName;
            string exceptionMessage = exception == null ? "No exception details were available." : exception.Message;
            Debug.LogWarning("Steam achievement maintenance completion callback failed (" + exceptionType
                + "): " + RedactSteamIds(exceptionMessage));
        }
        catch
        {
            // Logging must not interfere with cleanup or completion state.
        }
    }

    private static bool IsCurrentOperation(Operation operation)
    {
        return ReferenceEquals(activeOperation, operation) && !operation.Finished;
    }

    private static string SafeText(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? "no additional details were provided" : text.Trim();
    }

    private static string DescribeException(Exception exception)
    {
        return exception.GetType().Name + ": " + exception.Message;
    }

    private enum OperationKind
    {
        Inspect,
        Reset
    }

    private enum OperationPhase
    {
        ReadInitialState,
        WaitForStore,
        VerifyReset
    }

    private sealed class Operation
    {
        public readonly OperationKind Kind;
        public readonly Action<bool, string, string> InspectCompletion;
        public readonly Action<bool, string> ResetCompletion;
        public readonly string ExpectedSteamId;
        public readonly bool[] InitialStates = new bool[AchievementNames.Length];

        public double Deadline;
        public string SteamId;
        public string AccountName;
        public string ProfilePath;
        public string BackupFilePath;
        public string BackupDirectory;
        public DateTime ZeroProfileLastWriteTimeUtc;
        public bool OwnsSteamApiInitialization;
        public bool Finished;
        public bool LocalMutationStarted;
        public bool LocalMutationCompleted;
        public bool HasZeroProfileLastWriteTimeUtc;
        public bool HadOriginalProfile;
        public bool AnyAchievementCleared;
        public bool StoreRequested;
        public bool StoreCallbackReceived;
        public bool ResetVerified;
        public EResult StoreResult;
        public OperationPhase Phase = OperationPhase.ReadInitialState;
        public Callback<UserStatsStored_t> UserStatsStoredCallback;

        private Operation(OperationKind kind, string expectedSteamId,
            Action<bool, string, string> inspectCompletion, Action<bool, string> resetCompletion)
        {
            Kind = kind;
            ExpectedSteamId = expectedSteamId;
            InspectCompletion = inspectCompletion;
            ResetCompletion = resetCompletion;
        }

        public static Operation CreateInspect(Action<bool, string, string> completion)
        {
            return new Operation(OperationKind.Inspect, null, completion, null);
        }

        public static Operation CreateReset(string expectedSteamId, Action<bool, string> completion)
        {
            string normalizedId = null;
            if (!string.IsNullOrEmpty(expectedSteamId)
                && ulong.TryParse(expectedSteamId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)
                && parsed != 0)
            {
                normalizedId = parsed.ToString(CultureInfo.InvariantCulture);
            }

            return new Operation(OperationKind.Reset, normalizedId, null, completion);
        }

        public void CompleteImmediately(bool success, string message)
        {
            CompleteOperation(this, success, message);
        }

        public void InvokeCompletion(bool success, string message)
        {
            if (Kind == OperationKind.Inspect)
                InspectCompletion?.Invoke(success, success ? SteamId : SteamId ?? string.Empty, message);
            else
                ResetCompletion?.Invoke(success, message);
        }
    }
}
#endif
