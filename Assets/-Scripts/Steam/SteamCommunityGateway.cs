using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Steamworks;
using UnityEngine;
using Words.Community;

internal sealed partial class SteamCommunityGateway
{
    private const uint ExpectedAppId = 4966870;
    private readonly CommunityContentLimits limits;
    private readonly int mainThreadId;
    private SynchronizationContext unityContext;
    private ISteamClientSession observedSession;
    private CommunityServiceState state = new CommunityServiceState(
        CommunityAvailability.Offline, 0, 0, 0, "Steam is unavailable.");
    private bool disposed;

    public SteamCommunityGateway(CommunityContentLimits limits = null)
    {
        this.limits = limits ?? CommunityContentLimits.Default;
        mainThreadId = Thread.CurrentThread.ManagedThreadId;
        unityContext = SynchronizationContext.Current;
    }

    public CommunityServiceState State
    {
        get
        {
            RefreshState();
            return state;
        }
    }

    public event Action<CommunityServiceState> AvailabilityChanged;

    public Task<CommunityResult<CommunityPage>> QueryAsync(CommunityQuery query,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(CommunityResult<CommunityPage>.Canceled());
        if (!TryCaptureSession(out SessionSnapshot session, out CommunityError error))
            return Task.FromResult(CommunityResult<CommunityPage>.Failure(error));
        if (query == null)
            query = new CommunityQuery();

        return QueryPageAsync(query, session);
    }

    public Task<CommunityResult<CommunityPage>> QueryOwnedAsync(uint consumerAppId,
        string continuationToken, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(CommunityResult<CommunityPage>.Canceled());
        if (!TryCaptureSession(out SessionSnapshot session, out CommunityError error))
            return Task.FromResult(CommunityResult<CommunityPage>.Failure(error));
        if (consumerAppId != ExpectedAppId)
            return Task.FromResult(Fail<CommunityPage>(CommunityErrorCode.Incompatible,
                "The requested Workshop consumer app is unsupported."));
        if (!uint.TryParse(continuationToken, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out uint page) || page == 0)
            page = 1;
        return QueryOwnedPageAsync(page, session);
    }

    public async Task<CommunityResult<CommunityDetails>> GetDetailsAsync(CommunityItemKey key,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return CommunityResult<CommunityDetails>.Canceled();
        if (!TryCaptureSession(out SessionSnapshot session, out CommunityError error))
            return CommunityResult<CommunityDetails>.Failure(error);
        if (!IsSupportedKey(key))
            return Fail<CommunityDetails>(CommunityErrorCode.InvalidContent,
                "The Workshop item key is invalid or belongs to another app.");

        CommunityResult<NativeItem> result = await QueryItemAsync(key, session);
        if (!result.Succeeded)
            return CommunityResult<CommunityDetails>.Failure(result.Error);

        CommunityResult<bool> vote = await ReadUserVoteAsync(key, session);
        if (!vote.Succeeded)
            return ConvertResult<bool, CommunityDetails>(vote);
        return CommunityResult<CommunityDetails>.Success(ToDetails(result.Value, vote.Value));
    }

    private async Task<CommunityResult<CommunityPage>> QueryPageAsync(CommunityQuery query,
        SessionSnapshot session)
    {
        CommunityResult<NativeQueryPage> native = await QueryAllItemsAsync(query, session);
        if (!native.Succeeded)
            return CommunityResult<CommunityPage>.Failure(native.Error);

        return await BuildSummaryPageAsync(native.Value, native.Value.ContinuationToken,
            native.Value.HasMore, session);
    }

    private async Task<CommunityResult<CommunityPage>> QueryOwnedPageAsync(uint page, SessionSnapshot session)
    {
        CommunityResult<NativeQueryPage> native = await RunQueryAsync(
            BuildOwnedQuery(page, session), session);
        if (!native.Succeeded)
            return CommunityResult<CommunityPage>.Failure(native.Error);

        bool hasMore = native.Value.TotalMatchingResults > (ulong)page * 50UL;
        string next = hasMore ? (page + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
        return await BuildSummaryPageAsync(native.Value, next, hasMore, session);
    }

    private async Task<CommunityResult<CommunityPage>> BuildSummaryPageAsync(
        NativeQueryPage native, string continuationToken, bool hasMore, SessionSnapshot session)
    {
        var items = new List<CommunitySummary>(native.Items.Count);
        foreach (NativeItem item in native.Items)
        {
            CommunityResult<bool> vote = await ReadUserVoteAsync(item.Key, session);
            if (!vote.Succeeded)
                return ConvertResult<bool, CommunityPage>(vote);
            items.Add(ToSummary(item, vote.Value));
        }
        return CommunityResult<CommunityPage>.Success(new CommunityPage(
            items, continuationToken, hasMore));
    }

    private bool TryCaptureSession(out SessionSnapshot snapshot, out CommunityError error)
    {
        snapshot = default;
        error = null;
        if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
        {
            error = new CommunityError(CommunityErrorCode.Offline,
                "Steam Workshop calls must start on the Unity main thread.", true);
            return false;
        }
        if (unityContext == null)
            unityContext = SynchronizationContext.Current;
        if (unityContext == null)
        {
            error = new CommunityError(CommunityErrorCode.Offline,
                "The Unity main-thread callback context is unavailable.", true);
            return false;
        }

        RefreshState();

        ISteamClientSession session = SteamClientSessionRegistry.Current;
        if (session == null || !session.IsInitialized)
        {
            error = new CommunityError(CommunityErrorCode.Offline,
                "The shared Steam client session is unavailable.", true);
            return false;
        }
        uint appId = session.CurrentAppId;
        ulong accountId = session.CurrentAccountId;
        if (appId != ExpectedAppId)
        {
            error = new CommunityError(CommunityErrorCode.Incompatible,
                "Steam is running a different app than the Workshop integration supports.");
            return false;
        }
        if (accountId == 0)
        {
            error = new CommunityError(CommunityErrorCode.NotLoggedIn,
                "Steam has no signed-in user.", true);
            return false;
        }

        snapshot = new SessionSnapshot(session, appId, accountId, session.InitializationGeneration);
        return true;
    }

    private bool IsSupportedKey(CommunityItemKey key) => key != null
        && key.ConsumerAppId == ExpectedAppId && key.RemoteItemId != 0;

    private async Task<CommunityResult<NativeQueryPage>> QueryAllItemsAsync(
        CommunityQuery query, SessionSnapshot session) => await RunQueryAsync(
            BuildSearchQuery(query, session.AppId), session);

    private async Task<CommunityResult<NativeItem>> QueryItemAsync(
        CommunityItemKey key, SessionSnapshot session)
    {
        CommunityResult<NativeQueryPage> result = await RunQueryAsync(
            BuildDetailsQuery(key, session.AppId), session);
        return ExtractSingleItem(result);
    }
}
