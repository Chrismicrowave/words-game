using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Steamworks;
using Words.Community;

internal sealed partial class SteamCommunityGateway : ICommunityGateway, IDisposable
{
    private const int NativeOperationTimeoutSeconds = 120;
    private readonly object pendingSync = new object();
    private readonly HashSet<IPendingCall> pendingCalls = new HashSet<IPendingCall>();

    private void RefreshState()
    {
        if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            return;

        ISteamClientSession session = SteamClientSessionRegistry.Current;
        CommunityServiceState next;
        if (session == null || !session.IsInitialized)
        {
            next = new CommunityServiceState(CommunityAvailability.Offline, 0, 0,
                session != null ? session.InitializationGeneration : 0,
                session != null ? session.LastError : "The shared Steam client session is unavailable.");
        }
        else
        {
            uint appId = session.CurrentAppId;
            ulong accountId = session.CurrentAccountId;
            CommunityAvailability availability = appId != ExpectedAppId
                ? CommunityAvailability.Offline
                : accountId == 0 ? CommunityAvailability.NotLoggedIn : CommunityAvailability.Available;
            next = new CommunityServiceState(availability, appId, accountId,
                session.InitializationGeneration, session.LastError);
        }

        CommunityServiceState previous = state;
        bool changed = !ReferenceEquals(observedSession, session)
            || previous.Availability != next.Availability
            || previous.CurrentAppId != next.CurrentAppId
            || previous.CurrentAccountId != next.CurrentAccountId
            || previous.Generation != next.Generation
            || !string.Equals(previous.LastError, next.LastError, StringComparison.Ordinal);
        if (!changed)
            return;

        observedSession = session;
        state = next;
        IPendingCall[] pending;
        lock (pendingSync) pending = new List<IPendingCall>(pendingCalls).ToArray();
        foreach (IPendingCall call in pending)
            if (!IsSessionCurrent(call.Session))
                call.FailSessionChanged();
        FailStaleDownloads();

        try { AvailabilityChanged?.Invoke(next); }
        catch { }
    }

    private bool IsSessionCurrent(SessionSnapshot snapshot) => !disposed
        && ReferenceEquals(SteamClientSessionRegistry.Current, snapshot.Session)
        && SteamClientSessionIdentity.IsCurrentAccount(snapshot.Session,
            ExpectedAppId, snapshot.AccountId, snapshot.Generation);

    private Task<CommunityResult<TValue>> StartCall<TCallback, TValue>(SteamAPICall_t apiCall,
        SessionSnapshot session, bool remoteMutation,
        Func<TCallback, CommunityResult<TValue>> mapResult, Action cleanup)
    {
        if (apiCall == SteamAPICall_t.Invalid)
        {
            SafeCleanup(cleanup);
            return Task.FromResult(remoteMutation
                ? Unknown<TValue>("Steam did not return a valid callback handle; the remote outcome is unknown.")
                : Fail<TValue>(CommunityErrorCode.Offline, "Steam did not return a valid callback handle."));
        }
        if (disposed || unityContext == null)
        {
            SafeCleanup(cleanup);
            return Task.FromResult(remoteMutation
                ? Unknown<TValue>("The Workshop adapter stopped after submission; the remote outcome is unknown.")
                : Fail<TValue>(CommunityErrorCode.Offline, "The Unity callback context is unavailable."));
        }

        var pending = new PendingCall<TCallback, TValue>(this, session,
            remoteMutation, mapResult, cleanup, unityContext);
        lock (pendingSync) pendingCalls.Add(pending);
        pending.Start(apiCall);
        return pending.Task;
    }

    private void RemovePendingCall(IPendingCall call)
    {
        lock (pendingSync) pendingCalls.Remove(call);
    }

    private void PostToUnity(Action action)
    {
        SynchronizationContext context = unityContext;
        if (context == null)
            return;
        if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
            action();
        else
            context.Post(_ => action(), null);
    }

    private static CommunityResult<T> FailureFromSteam<T>(EResult result, bool remoteMutation = false)
    {
        if (remoteMutation && SteamCommunityCallbackPolicy.IsAmbiguousMutationFailure(result))
            return Unknown<T>("Steam returned an ambiguous result for a submitted Workshop mutation: " + result + ".");

        CommunityErrorCode code;
        bool retryable = false;
        switch (result)
        {
            case EResult.k_EResultFileNotFound:
                code = CommunityErrorCode.NotFound;
                break;
            case EResult.k_EResultNoConnection:
            case EResult.k_EResultServiceUnavailable:
            case EResult.k_EResultTimeout:
                code = CommunityErrorCode.Offline;
                retryable = true;
                break;
            case EResult.k_EResultNotLoggedOn:
                code = CommunityErrorCode.NotLoggedIn;
                retryable = true;
                break;
            case EResult.k_EResultLimitExceeded:
                code = CommunityErrorCode.QuotaExceeded;
                break;
            case EResult.k_EResultRateLimitExceeded:
                code = CommunityErrorCode.RateLimited;
                retryable = true;
                break;
            case EResult.k_EResultInvalidParam:
                code = CommunityErrorCode.InvalidContent;
                break;
            default:
                code = CommunityErrorCode.Unknown;
                break;
        }
        return CommunityResult<T>.Failure(new CommunityError(code,
            "Steam Workshop returned " + result + ".", retryable));
    }

    private static CommunityResult<T> Fail<T>(CommunityErrorCode code, string message) =>
        CommunityResult<T>.Failure(new CommunityError(code, message));

    private static CommunityResult<T> Unknown<T>(string message) =>
        CommunityResult<T>.Unknown(new CommunityError(CommunityErrorCode.RecoveryRequired,
            message, true), string.Empty);

    private static void SafeCleanup(Action cleanup)
    {
        try { cleanup?.Invoke(); }
        catch { }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        IPendingCall[] pending;
        lock (pendingSync) pending = new List<IPendingCall>(pendingCalls).ToArray();
        foreach (IPendingCall call in pending)
            call.FailDisposed();
        DisposeDownloadCallbacks();
    }

    private interface IPendingCall
    {
        SessionSnapshot Session { get; }
        void FailSessionChanged();
        void FailDisposed();
    }

    private sealed class PendingCall<TCallback, TValue> : IPendingCall
    {
        private readonly SteamCommunityGateway owner;
        private readonly bool remoteMutation;
        private readonly Func<TCallback, CommunityResult<TValue>> mapResult;
        private readonly Action cleanup;
        private readonly SynchronizationContext context;
        private readonly TaskCompletionSource<CommunityResult<TValue>> completion =
            new TaskCompletionSource<CommunityResult<TValue>>(TaskCreationOptions.RunContinuationsAsynchronously);
        private CallResult<TCallback> callResult;
        private Timer timer;
        private readonly ManagedCompletionGate completionGate = new ManagedCompletionGate();

        public SessionSnapshot Session { get; }
        public Task<CommunityResult<TValue>> Task => completion.Task;

        public PendingCall(SteamCommunityGateway owner, SessionSnapshot session, bool remoteMutation,
            Func<TCallback, CommunityResult<TValue>> mapResult, Action cleanup,
            SynchronizationContext context)
        {
            this.owner = owner;
            Session = session;
            this.remoteMutation = remoteMutation;
            this.mapResult = mapResult;
            this.cleanup = cleanup;
            this.context = context;
        }

        public void Start(SteamAPICall_t handle)
        {
            try
            {
                callResult = CallResult<TCallback>.Create(OnResult);
                callResult.Set(handle);
                timer = new Timer(_ => context.Post(__ => FinishTimeout(), null), null,
                    TimeSpan.FromSeconds(NativeOperationTimeoutSeconds), Timeout.InfiniteTimeSpan);
            }
            catch (Exception exception)
            {
                CommunityResult<TValue> result = remoteMutation
                    ? Unknown<TValue>("The Workshop request was submitted but its callback could not be tracked: " + exception.Message)
                    : Fail<TValue>(CommunityErrorCode.Offline,
                        "Steam could not track the Workshop callback: " + exception.Message);
                Finish(result);
            }
        }

        private void OnResult(TCallback callback, bool ioFailure)
        {
            CommunityResult<TValue> result;
            if (!owner.IsSessionCurrent(Session))
                result = remoteMutation && !ioFailure
                    ? MapStaleMutationCallback(callback)
                    : SessionChanged();
            else if (ioFailure)
                result = remoteMutation && SteamCommunityCallbackPolicy.IsAmbiguousMutationFailure(
                    default(EResult), ioFailure: true)
                    ? Unknown<TValue>("Steam lost the callback after submission; the remote outcome is unknown.")
                    : Fail<TValue>(CommunityErrorCode.Offline, "Steam could not complete the Workshop request.");
            else
            {
                try { result = mapResult(callback); }
                catch (Exception exception)
                {
                    result = remoteMutation
                        ? Unknown<TValue>("The Workshop callback failed after submission: " + exception.Message)
                        : Fail<TValue>(CommunityErrorCode.Unknown, exception.Message);
                }
            }
            Finish(result);
        }

        public void FailSessionChanged() => owner.PostToUnity(() => Finish(SessionChanged()));
        public void FailDisposed() => owner.PostToUnity(() => Finish(remoteMutation
            ? Unknown<TValue>("The Workshop adapter stopped after submission; the remote outcome is unknown.")
            : Fail<TValue>(CommunityErrorCode.Offline, "The Workshop adapter stopped.")));

        private CommunityResult<TValue> SessionChanged() => remoteMutation
            ? Unknown<TValue>("The Steam account or session changed after submission; reconcile the remote operation.")
            : Fail<TValue>(CommunityErrorCode.Offline, "The Steam account or session changed during the request.");

        private CommunityResult<TValue> MapStaleMutationCallback(TCallback callback)
        {
            try
            {
                CommunityResult<TValue> mapped = mapResult(callback);
                return SteamCommunityCallbackPolicy.UnknownPreservingValue(mapped,
                    "The Steam account or session changed after submission; reconcile the remote operation.");
            }
            catch
            {
                return SessionChanged();
            }
        }

        private void FinishTimeout() => Finish(remoteMutation
            ? Unknown<TValue>("Steam did not confirm the submitted operation before timeout; reconcile before retrying.")
            : Fail<TValue>(CommunityErrorCode.Offline, "Steam Workshop did not respond before timeout."));

        private void Finish(CommunityResult<TValue> result)
        {
            if (!completionGate.TryClaim())
                return;
            timer?.Dispose();
            try { completion.TrySetResult(result); }
            finally
            {
                SafeCleanup(cleanup);
                try { callResult?.Dispose(); }
                catch { }
                owner.RemovePendingCall(this);
            }
        }
    }

    private readonly struct SessionSnapshot
    {
        public readonly ISteamClientSession Session;
        public readonly uint AppId;
        public readonly ulong AccountId;
        public readonly long Generation;

        public SessionSnapshot(ISteamClientSession session, uint appId, ulong accountId, long generation)
        {
            Session = session;
            AppId = appId;
            AccountId = accountId;
            Generation = generation;
        }
    }
}
