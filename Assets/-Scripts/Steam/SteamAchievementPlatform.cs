using System;
using System.Collections.Generic;
using System.Globalization;
using Steamworks;
using UnityEngine;

internal static class SteamAchievementPlatformBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterPlatformFactory()
    {
        WordAchievementPlatformRegistry.RegisterFactory(() => new SteamAchievementPlatform());
    }
}

/// <summary>
/// Owns the Steamworks.NET client lifecycle and retries locally saved, monotonic achievement state.
/// GameCoordinator is the sole callback pump and shutdown owner for this instance.
/// </summary>
internal sealed class SteamAchievementPlatform : IWordAchievementPlatform
{
    private const uint ExpectedAppId = 4966870;
    private const float StoreRetryIntervalSeconds = 10f;

    private readonly Dictionary<string, WordAchievementProgressSnapshot> pendingSnapshots =
        new Dictionary<string, WordAchievementProgressSnapshot>();
    private readonly Dictionary<string, HashSet<string>> dirtyAchievementNames =
        new Dictionary<string, HashSet<string>>();

    private Callback<UserStatsStored_t> userStatsStoredCallback;
    private List<string> pendingStoreAchievementNames;
    private string pendingStoreSteamId;
    private float retryStoreAt;
    private bool initialized;
    private string lastInitializationWarning;

    public bool IsInitialized => initialized;

    public string CurrentUserId
    {
        get
        {
            if (!initialized)
                return null;

            ulong steamId = SteamUser.GetSteamID().m_SteamID;
            return steamId == 0 ? null : steamId.ToString(CultureInfo.InvariantCulture);
        }
    }

    public event Action<string, bool> StatsStored;

    public bool TryInitialize()
    {
        if (initialized)
            return true;

        try
        {
            ESteamAPIInitResult initResult = SteamAPI.InitEx(out string initError);
            if (initResult != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                bool appIdFilePresent = System.IO.File.Exists("steam_appid.txt");
                WarnInitializationIfChanged("SteamAPI.InitEx failed (" + initResult + "): " + initError
                    + ". steam_appid.txt in the process current working directory exists: " + appIdFilePresent
                    + "; local achievements remain available.");
                return false;
            }

            initialized = true;
            uint actualAppId = SteamUtils.GetAppID().m_AppId;
            if (actualAppId != ExpectedAppId)
            {
                WarnInitializationIfChanged("Steam AppID mismatch; Steam achievements are disabled for this process.");
                Shutdown();
                return false;
            }

            userStatsStoredCallback = Callback<UserStatsStored_t>.Create(OnUserStatsStored);
            lastInitializationWarning = null;
            return true;
        }
        catch (Exception exception)
        {
            WarnInitializationIfChanged("Steamworks initialization failed; local achievements remain available. "
                + exception.GetType().Name + ": " + exception.Message);
            Shutdown();
            return false;
        }
    }

    public void RunCallbacks()
    {
        if (!initialized)
            return;

        SteamAPI.RunCallbacks();
        ProcessCurrentUserSnapshot();
    }

    public bool SubmitProgress(string expectedUserId, WordAchievementProgressSnapshot snapshot)
    {
        string currentUserId = CurrentUserId;
        if (string.IsNullOrEmpty(expectedUserId)
            || !string.Equals(expectedUserId, currentUserId, StringComparison.Ordinal))
            return false;

        pendingSnapshots[expectedUserId] = snapshot;
        if (pendingStoreSteamId != null)
            return true;

        return ProcessSnapshot(expectedUserId, snapshot);
    }

    public void Shutdown()
    {
        userStatsStoredCallback?.Dispose();
        userStatsStoredCallback = null;
        pendingStoreSteamId = null;
        pendingStoreAchievementNames = null;

        if (initialized)
        {
            initialized = false;
            SteamAPI.Shutdown();
        }
    }

    private void ProcessCurrentUserSnapshot()
    {
        if (pendingStoreSteamId != null || Time.unscaledTime < retryStoreAt)
            return;

        string currentUserId = CurrentUserId;
        if (string.IsNullOrEmpty(currentUserId)
            || !pendingSnapshots.TryGetValue(currentUserId, out WordAchievementProgressSnapshot snapshot))
            return;

        ProcessSnapshot(currentUserId, snapshot);
    }

    private bool ProcessSnapshot(string steamId, WordAchievementProgressSnapshot snapshot)
    {
        if (!initialized || !string.Equals(CurrentUserId, steamId, StringComparison.Ordinal))
            return false;

        if (!dirtyAchievementNames.TryGetValue(steamId, out HashSet<string> dirtyNames))
        {
            dirtyNames = new HashSet<string>();
            dirtyAchievementNames.Add(steamId, dirtyNames);
        }

        bool needsStore = dirtyNames.Count > 0;
        foreach (string achievementName in GetUnlockedAchievementNames(snapshot))
        {
            // A previous SetAchievement may already be true in Steam's memory even when its
            // StoreStats call/callback failed. Keep dirty names scheduled for another StoreStats.
            if (dirtyNames.Contains(achievementName))
            {
                needsStore = true;
                continue;
            }

            if (!SteamUserStats.GetAchievement(achievementName, out bool achieved))
            {
                retryStoreAt = Time.unscaledTime + StoreRetryIntervalSeconds;
                return false;
            }

            if (achieved)
                continue;

            if (!SteamUserStats.SetAchievement(achievementName))
            {
                retryStoreAt = Time.unscaledTime + StoreRetryIntervalSeconds;
                return false;
            }

            dirtyNames.Add(achievementName);
            needsStore = true;
        }

        if (!needsStore)
        {
            pendingSnapshots.Remove(steamId);
            retryStoreAt = 0f;
            return true;
        }

        if (!SteamUserStats.StoreStats())
        {
            retryStoreAt = Time.unscaledTime + StoreRetryIntervalSeconds;
            return false;
        }

        pendingStoreSteamId = steamId;
        pendingStoreAchievementNames = new List<string>(dirtyNames);
        retryStoreAt = float.PositiveInfinity;
        return true;
    }

    private void OnUserStatsStored(UserStatsStored_t callback)
    {
        if (!initialized || pendingStoreSteamId == null
            || (uint)(callback.m_nGameID & 0x00FFFFFFUL) != ExpectedAppId)
            return;

        string storedSteamId = pendingStoreSteamId;
        bool succeeded = callback.m_eResult == EResult.k_EResultOK;

        if (succeeded && dirtyAchievementNames.TryGetValue(storedSteamId, out HashSet<string> dirtyNames))
        {
            foreach (string storedAchievement in pendingStoreAchievementNames)
                dirtyNames.Remove(storedAchievement);

            if (dirtyNames.Count == 0)
                dirtyAchievementNames.Remove(storedSteamId);
        }

        pendingStoreSteamId = null;
        pendingStoreAchievementNames = null;
        retryStoreAt = succeeded ? 0f : Time.unscaledTime + StoreRetryIntervalSeconds;

        try
        {
            StatsStored?.Invoke(storedSteamId, succeeded);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Achievements] StoreStats callback observer failed: " + exception.Message);
        }

        if (succeeded && string.Equals(CurrentUserId, storedSteamId, StringComparison.Ordinal))
            ProcessCurrentUserSnapshot();
    }

    private static IEnumerable<string> GetUnlockedAchievementNames(WordAchievementProgressSnapshot snapshot)
    {
        if (snapshot.CompleteFirstWordUnlocked)
            yield return WordAchievementApiNames.CompleteFirstWord;
        if (snapshot.CompleteTwoWordsUnlocked)
            yield return WordAchievementApiNames.CompleteTwoWords;
        if (snapshot.CompleteWordWithDUnlocked)
            yield return WordAchievementApiNames.CompleteWordWithD;
    }

    private void WarnInitializationIfChanged(string message)
    {
        if (string.Equals(lastInitializationWarning, message, StringComparison.Ordinal))
            return;

        lastInitializationWarning = message;
        Debug.LogWarning("[Achievements] " + message);
    }
}
