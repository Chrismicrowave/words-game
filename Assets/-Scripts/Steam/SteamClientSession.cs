using System;
using Steamworks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

internal static class SteamClientSessionBootstrap
{
#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void RegisterEditorSessionFactory()
    {
        SteamClientSessionRegistry.RegisterFactory(() => new SteamClientSession());
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSessionFactory()
    {
        SteamClientSessionRegistry.RegisterFactory(() => new SteamClientSession());
    }
}

internal interface ISteamClientTransport
{
    bool CallbackDispatcherInitialized { get; }
    ESteamAPIInitResult Initialize(out string error);
    uint CurrentAppId { get; }
    ulong CurrentAccountId { get; }
    void RunCallbacks();
    void Shutdown();
}

/// <summary>Owns the single Steamworks.NET client API lifecycle for the process.</summary>
internal sealed class SteamClientSession : ISteamClientSession
{
    internal const uint ExpectedAppId = 4966870;

    private readonly ISteamClientTransport transport;
    private bool ownsInitialization;

    public SteamClientSession()
        : this(new SteamClientTransport())
    {
    }

    internal SteamClientSession(ISteamClientTransport transport)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public SteamClientSessionStatus Status { get; private set; } = SteamClientSessionStatus.NotInitialized;
    public bool IsInitialized => Status == SteamClientSessionStatus.Initialized && ownsInitialization;
    public long InitializationGeneration { get; private set; }
    public uint CurrentAppId
    {
        get
        {
            if (!IsInitialized)
                return 0;

            try
            {
                return transport.CurrentAppId;
            }
            catch (Exception exception)
            {
                Fail("Steam AppID could not be read: " + exception.GetType().Name + ": " + exception.Message);
                return 0;
            }
        }
    }

    public ulong CurrentAccountId
    {
        get
        {
            if (!IsInitialized)
                return 0;

            try
            {
                return transport.CurrentAccountId;
            }
            catch (Exception exception)
            {
                Fail("Steam account could not be read: " + exception.GetType().Name + ": " + exception.Message);
                return 0;
            }
        }
    }
    public string LastError { get; private set; }

    public bool TryInitialize()
    {
        if (IsInitialized)
            return true;

        LastError = null;
        if (transport.CallbackDispatcherInitialized)
            return Fail("Steam callbacks are already initialized by another owner; this session will not take over or shut it down.");

        try
        {
            ESteamAPIInitResult result = transport.Initialize(out string error);
            if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                return Fail("SteamAPI.InitEx failed (" + result + "): " + (error ?? string.Empty));

            ownsInitialization = true;
            if (!transport.CallbackDispatcherInitialized)
                return Fail("Steamworks.NET initialized successfully but its callback dispatcher is unavailable.");

            uint appId = transport.CurrentAppId;
            if (appId != ExpectedAppId)
            {
                return Fail("Steam AppID mismatch. Expected " + ExpectedAppId
                    + " but Steam initialized AppID " + appId + ".");
            }

            Status = SteamClientSessionStatus.Initialized;
            InitializationGeneration++;
            LastError = null;
            return true;
        }
        catch (Exception exception)
        {
            if (transport.CallbackDispatcherInitialized)
                ownsInitialization = true;
            return Fail("Steamworks initialization failed: " + exception.GetType().Name + ": " + exception.Message);
        }
    }

    public void RunCallbacks()
    {
        if (IsInitialized)
        {
            try
            {
                transport.RunCallbacks();
            }
            catch (Exception exception)
            {
                Fail("Steam callback processing failed: " + exception.GetType().Name + ": " + exception.Message);
            }
        }
    }

    public void Shutdown()
    {
        if (ownsInitialization)
        {
            ownsInitialization = false;
            transport.Shutdown();
        }

        if (Status != SteamClientSessionStatus.Failed)
            Status = SteamClientSessionStatus.NotInitialized;
    }

    private bool Fail(string message)
    {
        LastError = message;
        Status = SteamClientSessionStatus.Failed;
        if (ownsInitialization)
        {
            ownsInitialization = false;
            try
            {
                transport.Shutdown();
            }
            catch (Exception shutdownException)
            {
                LastError += " Steam shutdown also failed: " + shutdownException.GetType().Name
                    + ": " + shutdownException.Message;
            }
        }
        return false;
    }

    private sealed class SteamClientTransport : ISteamClientTransport
    {
        public bool CallbackDispatcherInitialized => CallbackDispatcher.IsInitialized;
        public ESteamAPIInitResult Initialize(out string error) => SteamAPI.InitEx(out error);
        public uint CurrentAppId => SteamUtils.GetAppID().m_AppId;
        public ulong CurrentAccountId => SteamUser.GetSteamID().m_SteamID;
        public void RunCallbacks() => SteamAPI.RunCallbacks();
        public void Shutdown() => SteamAPI.Shutdown();
    }
}
