using System;

/// <summary>
/// Platform boundary for reporting locally persisted word-achievement progress.
/// The shared Steam client session owns native initialization, callback pumping, and shutdown.
/// Implementations bind only after that session is initialized. SubmitProgress must reject a
/// profile ID that does not match the platform's current user. StatsStored reports the profile
/// ID captured when the platform write was submitted.
/// </summary>
public interface IWordAchievementPlatform
{
    bool IsInitialized { get; }
    string CurrentUserId { get; }

    event Action<string, bool> StatsStored;

    // TryInitialize only binds adapter callbacks to an already initialized session.
    bool TryInitialize();
    // Retained for compatibility; callback pumping is owned by ISteamClientSession.
    void RunCallbacks();
    bool SubmitProgress(string expectedUserId, WordAchievementProgressSnapshot snapshot);
    // Disposes adapter callbacks only; it must not shut down the shared native session.
    void Shutdown();
}

/// <summary>
/// The Steam adapter registers a factory from its optional package-backed assembly.
/// Without that assembly the game receives a local-only implementation.
/// </summary>
public static class WordAchievementPlatformRegistry
{
    private static Func<ISteamClientSession, IWordAchievementPlatform> factory;

    public static void RegisterFactory(Func<ISteamClientSession, IWordAchievementPlatform> platformFactory)
    {
        if (platformFactory != null)
            factory = platformFactory;
    }

    public static IWordAchievementPlatform CreateOrLocalOnly(ISteamClientSession session)
    {
        IWordAchievementPlatform platform = factory != null ? factory(session) : null;
        if (platform == null)
            platform = new LocalOnlyWordAchievementPlatform();
        return platform;
    }

    public static IWordAchievementPlatform CreateLocalOnly()
    {
        return new LocalOnlyWordAchievementPlatform();
    }

    public static bool TryInitialize(IWordAchievementPlatform candidate, Action<string> warning)
    {
        if (candidate == null)
            return false;

        bool initialized;
        try
        {
            initialized = candidate.IsInitialized || candidate.TryInitialize();
            if (initialized && candidate.IsInitialized)
                return true;

            if (initialized)
                warning?.Invoke("Platform initialization returned success but did not report an initialized state.");
        }
        catch (Exception initializeException)
        {
            string details = "Platform initialization failed: " + initializeException.GetType().Name
                + ": " + initializeException.Message;
            try
            {
                candidate.Shutdown();
            }
            catch (Exception cleanupException)
            {
                details += "; platform cleanup also failed: " + cleanupException.GetType().Name
                    + ": " + cleanupException.Message;
            }

            warning?.Invoke(details);
            return false;
        }

        try
        {
            candidate.Shutdown();
        }
        catch (Exception cleanupException)
        {
            warning?.Invoke("Platform initialization was unavailable and cleanup failed: "
                + cleanupException.GetType().Name + ": " + cleanupException.Message);
        }

        return false;
    }
}

internal sealed class LocalOnlyWordAchievementPlatform : IWordAchievementPlatform
{
    public bool IsInitialized => false;
    public string CurrentUserId => null;

    public event Action<string, bool> StatsStored
    {
        add { }
        remove { }
    }

    public bool TryInitialize() => false;
    public void RunCallbacks() { }
    public bool SubmitProgress(string expectedUserId, WordAchievementProgressSnapshot snapshot) => false;
    public void Shutdown() { }
}
