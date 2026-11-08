using System;

/// <summary>SDK-independent state for the process-wide Steam client session.</summary>
public enum SteamClientSessionStatus
{
    Unavailable,
    NotInitialized,
    Initialized,
    Failed
}

/// <summary>
/// Minimal Steam client session boundary. Implementations own the native Steam API
/// lifecycle and callback dispatcher; feature adapters only use the initialized session.
/// </summary>
public interface ISteamClientSession
{
    SteamClientSessionStatus Status { get; }
    bool IsInitialized { get; }
    long InitializationGeneration { get; }
    uint CurrentAppId { get; }
    ulong CurrentAccountId { get; }
    string LastError { get; }

    bool TryInitialize();
    void RunCallbacks();
    void Shutdown();
}

/// <summary>SDK-independent identity check for feature adapters bound to one Steam account.</summary>
public static class SteamClientSessionIdentity
{
    public static bool IsCurrentAccount(ISteamClientSession session, uint expectedAppId,
        ulong boundAccountId, long boundGeneration)
    {
        if (session == null || !session.IsInitialized || boundAccountId == 0
            || boundGeneration == 0 || session.InitializationGeneration != boundGeneration)
            return false;

        return session.CurrentAppId == expectedAppId
            && session.CurrentAccountId == boundAccountId;
    }
}

/// <summary>A single runtime owner for the shared process Steam session.</summary>
public sealed class SteamClientSessionLease : IDisposable
{
    private bool disposed;

    internal SteamClientSessionLease(ISteamClientSession session)
    {
        Session = session;
    }

    public ISteamClientSession Session { get; }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        SteamClientSessionRegistry.ReleaseRuntime(this);
    }
}

/// <summary>
/// Resolves the optional Steam implementation without making Core depend on Steamworks.NET.
/// Tests may register a fake session factory before acquiring the runtime lease.
/// </summary>
public static class SteamClientSessionRegistry
{
    private static Func<ISteamClientSession> factory;
    private static SteamClientSessionLease runtimeLease;

    public static ISteamClientSession Current => runtimeLease?.Session;

    public static void RegisterFactory(Func<ISteamClientSession> sessionFactory)
    {
        if (sessionFactory != null)
            factory = sessionFactory;
    }

    /// <summary>Temporarily replaces the session factory for SDK-free tests.</summary>
    public static IDisposable OverrideFactoryForTests(Func<ISteamClientSession> sessionFactory)
    {
        if (runtimeLease != null)
            throw new InvalidOperationException("The session factory cannot be replaced while a runtime owner exists.");

        Func<ISteamClientSession> previousFactory = factory;
        factory = sessionFactory;
        return new FactoryOverride(previousFactory);
    }

    public static SteamClientSessionLease AcquireRuntime()
    {
        if (runtimeLease != null)
            throw new InvalidOperationException("A runtime Steam client session owner already exists.");

        ISteamClientSession session;
        try
        {
            session = factory != null ? factory() : null;
        }
        catch (Exception exception)
        {
            session = new LocalOnlySteamClientSession(
                "The optional Steam session could not be created: "
                + exception.GetType().Name + ": " + exception.Message);
        }

        if (session == null)
            session = new LocalOnlySteamClientSession("The optional Steam integration is not loaded.");

        runtimeLease = new SteamClientSessionLease(session);
        return runtimeLease;
    }

    internal static void ReleaseRuntime(SteamClientSessionLease lease)
    {
        if (!ReferenceEquals(runtimeLease, lease))
            return;

        try
        {
            runtimeLease.Session.Shutdown();
        }
        finally
        {
            runtimeLease = null;
        }
    }

    internal static ISteamClientSession CreateLocalOnly(string error = null)
    {
        return new LocalOnlySteamClientSession(error);
    }

    private sealed class FactoryOverride : IDisposable
    {
        private readonly Func<ISteamClientSession> previousFactory;
        private bool disposed;

        public FactoryOverride(Func<ISteamClientSession> previousFactory)
        {
            this.previousFactory = previousFactory;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            if (runtimeLease != null)
                throw new InvalidOperationException("The test factory cannot be restored while its runtime owner exists.");

            factory = previousFactory;
            disposed = true;
        }
    }
}

internal sealed class LocalOnlySteamClientSession : ISteamClientSession
{
    public LocalOnlySteamClientSession(string error)
    {
        LastError = string.IsNullOrWhiteSpace(error)
            ? "The optional Steam integration is unavailable."
            : error;
    }

    public SteamClientSessionStatus Status => SteamClientSessionStatus.Unavailable;
    public bool IsInitialized => false;
    public long InitializationGeneration => 0;
    public uint CurrentAppId => 0;
    public ulong CurrentAccountId => 0;
    public string LastError { get; }

    public bool TryInitialize() => false;
    public void RunCallbacks() { }
    public void Shutdown() { }
}
