using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Words.Community;
using Words.UiMockups.WordListHub;

/// <summary>
/// Projects the Community service into the Hub's UI-only read model. The UI never
/// sees Steam SDK types; Unity-facing notifications are dispatched by the bridge.
/// </summary>
public sealed class WordListHubCommunityFacade : IWordListHubCommunityAsync, IDisposable
{
    private readonly object sync = new object();
    private readonly ICommunityService service;
    private readonly Action<Action> dispatchMainThread;
    private readonly List<MockupDiscoveryItem> snapshot = new List<MockupDiscoveryItem>();
    private string continuationToken = string.Empty;
    private bool hasMore;
    private long queryGeneration;
    private bool disposed;
    private CommunityServiceState lastObservedState;
    private ulong lastObservedEffectiveAccountId;
    private bool lastObservedRemoteAuthentication;
    private MockupCommunityProgress currentProgress;

    public event Action Changed;
    public event Action BecameAvailable;
    public event Action<MockupCommunityProgress> ProgressChanged;

    public IReadOnlyList<MockupDiscoveryItem> Snapshot
    {
        get { lock (sync) return snapshot.ToArray(); }
    }

    public IReadOnlyList<MockupWordList> DownloadedItems
    {
        get
        {
            IReadOnlyList<CommunityCacheEntry> entries = service.GetCachedEntries();
            var projected = new List<MockupWordList>(entries.Count);
            ulong accountId = service.EffectiveLocalAccountId;
            foreach (CommunityCacheEntry entry in entries.OrderByDescending(candidate =>
                candidate != null ? candidate.CachedAtUtc : DateTime.MinValue))
            {
                if (entry?.Provider == null) continue;
                CommunityPersonalRecord record = null;
                if (accountId != 0)
                    service.TryGetRecord(entry.Key, entry.Version.ContentHash, accountId,
                        out record, out _);
                CommunityWordListProvider provider = entry.Provider;
                string uiId = "community:" + entry.Key.ConsumerAppId.ToString(CultureInfo.InvariantCulture)
                    + ":" + entry.Key.RemoteItemId.ToString(CultureInfo.InvariantCulture)
                    + ":" + entry.Version.ContentHash;
                projected.Add(MockupWordListProjection.CreateCommunityDownload(
                    uiId,
                    entry.Key.RemoteItemId.ToString(CultureInfo.InvariantCulture),
                    entry.Version.ContentHash,
                    entry.Title,
                    string.Empty,
                    entry.Author,
                    ToMockupLanguage(provider.LanguageMode),
                    provider.GetWords(),
                    entry.CachedAtUtc,
                    entry.Version.SchemaVersion,
                    record?.HighestStars,
                    record?.Best?.ElapsedSeconds,
                    record?.LastPlayedAtUtc,
                    record?.PlayCount ?? 0));
            }
            return projected.AsReadOnly();
        }
    }

    public string ConnectionMessage
    {
        get
        {
            CommunityServiceState state = service.State;
            if (state.Availability == CommunityAvailability.Available)
                return "Connected to Steam Community.";
            if (!string.IsNullOrWhiteSpace(state.LastError)) return state.LastError;
            if (state.Availability == CommunityAvailability.NotLoggedIn)
                return "Sign in to Steam to use Community.";
            return "Community service is unavailable. Cached downloads remain local.";
        }
    }

    public ulong EffectiveLocalAccountId => service.EffectiveLocalAccountId;
    public bool IsRemoteAuthenticated => service.IsRemoteAuthenticated;
    public bool HasHiddenItems
    {
        get
        {
            try { return service.GetHiddenItems()?.Count > 0; }
            catch { return false; }
        }
    }
    public bool IsLocalListPublicationLocked(string localListId) =>
        !string.IsNullOrWhiteSpace(localListId)
        && service.IsLocalListPublicationLocked(localListId);
    public MockupCommunityProgress CurrentProgress
    {
        get { lock (sync) return currentProgress; }
    }

    public IReadOnlyList<MockupCommunityPublicationInfo> Publications
    {
        get
        {
            IReadOnlyList<CommunityPublication> publications = service.GetPublications();
            return publications == null
                ? Array.Empty<MockupCommunityPublicationInfo>()
                : publications.Where(item => item != null)
                    .Select(ProjectPublication).ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Resolves a validated cached provider. A requested hash is exact; without one,
    /// only the newest locally cached version for this app/item is selected.
    /// </summary>
    public bool TryGetCachedProvider(string remoteItemId, string requiredContentHash,
        out CommunityWordListProvider provider, out string error)
    {
        provider = null;
        error = string.Empty;
        if (!ulong.TryParse(remoteItemId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id)
            || id == 0 || service.CurrentAppId == 0)
        {
            error = "The Community item identity is unavailable.";
            return false;
        }

        CommunityCacheEntry entry = service.GetCachedEntries()
            .Where(candidate => candidate != null
                && candidate.Key.ConsumerAppId == service.CurrentAppId
                && candidate.Key.RemoteItemId == id
                && (string.IsNullOrEmpty(requiredContentHash)
                    || string.Equals(candidate.Version.ContentHash, requiredContentHash,
                        StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(candidate => candidate.CachedAtUtc)
            .FirstOrDefault();
        if (entry == null || !(entry.Provider is CommunityWordListProvider cachedProvider))
        {
            error = string.IsNullOrEmpty(requiredContentHash)
                ? "No validated offline Community download is available."
                : "That exact Community version is no longer cached; it was not replaced with a different version.";
            return false;
        }

        provider = cachedProvider;
        return true;
    }

    public bool TryGetDownloadedContentForCopy(string communityProjectionId,
        out WordListLibraryContent content, out string title, out string error)
    {
        content = null;
        title = string.Empty;
        error = string.Empty;
        MockupWordList projected = DownloadedItems.FirstOrDefault(item => item != null
            && string.Equals(item.Id, communityProjectionId, StringComparison.Ordinal)
            && item.Ownership == MockupOwnership.CommunityDownloaded);
        if (projected == null)
        {
            error = "The selected Community download is no longer available.";
            return false;
        }
        if (!TryGetCachedProvider(projected.RemoteItemId, projected.CommunityContentHash,
            out CommunityWordListProvider provider, out error))
            return false;

        // Copy structured cached content, not the flattened card preview, so character pinyin
        // and mixed-language entries survive the move into the canonical custom-list store.
        content = new WordListLibraryContent
        {
            LanguageMode = provider.LanguageMode,
            Words = provider.GetWords(),
            ChineseWords = provider.GetChineseWords(),
            MixedWords = provider.GetMixedWords()
        };
        title = string.IsNullOrWhiteSpace(provider.DisplayName) ? projected.Title : provider.DisplayName;
        return true;
    }

    public string ContinuationToken
    {
        get { lock (sync) return continuationToken; }
    }

    public bool HasMore
    {
        get { lock (sync) return hasMore; }
    }

    public WordListHubCommunityFacade(ICommunityService service, Action<Action> dispatchMainThread)
    {
        this.service = service ?? CommunityServiceRegistry.GetShared();
        this.dispatchMainThread = dispatchMainThread;
        lastObservedState = this.service.State;
        lastObservedEffectiveAccountId = this.service.EffectiveLocalAccountId;
        lastObservedRemoteAuthentication = this.service.IsRemoteAuthenticated;
        this.service.AvailabilityChanged += HandleAvailabilityChanged;
    }

    /// <summary>Reads SDK-neutral service state only; it never starts a platform operation.</summary>
    public void PollConnection(bool force = false)
    {
        if (disposed) return;
        CommunityServiceState state = service.State;
        ulong effectiveAccountId = service.EffectiveLocalAccountId;
        bool remoteAuthenticated = service.IsRemoteAuthenticated;
        bool stateChanged;
        bool accountChanged;
        bool becameAvailable;
        lock (sync)
        {
            stateChanged = !SameState(lastObservedState, state)
                || lastObservedRemoteAuthentication != remoteAuthenticated;
            accountChanged = lastObservedEffectiveAccountId != effectiveAccountId;
            becameAvailable = !lastObservedRemoteAuthentication && remoteAuthenticated;
            lastObservedState = state;
            lastObservedEffectiveAccountId = effectiveAccountId;
            lastObservedRemoteAuthentication = remoteAuthenticated;
            if (accountChanged)
            {
                queryGeneration++;
                snapshot.Clear();
                continuationToken = string.Empty;
                hasMore = false;
            }
        }

        if (!force && !stateChanged && !accountChanged) return;
        RaiseChanged();
        if (becameAvailable) RaiseBecameAvailable();
    }

    public async Task<MockupCommunityQueryResult> QueryAsync(
        MockupDiscoveryQuery query, string pageToken, CancellationToken cancellationToken)
    {
        if (disposed)
            return new MockupCommunityQueryResult(false, false, string.Empty,
                "Community service is shutting down.");

        query = query ?? new MockupDiscoveryQuery();
        pageToken = pageToken ?? string.Empty;
        long generation;
        lock (sync)
        {
            if (string.IsNullOrEmpty(pageToken))
            {
                generation = ++queryGeneration;
                continuationToken = string.Empty;
                hasMore = false;
            }
            else
            {
                generation = queryGeneration;
            }
        }

        CommunityResult<CommunityPage> result;
        try
        {
            result = await service.QueryAsync(ToCommunityQuery(query, pageToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new MockupCommunityQueryResult(false, false, pageToken, "Community search was canceled.");
        }
        catch (Exception exception)
        {
            return new MockupCommunityQueryResult(false, false, pageToken,
                "Community search failed: " + exception.Message);
        }

        if (!result.Succeeded || result.Value == null)
            return new MockupCommunityQueryResult(false, false, pageToken, ErrorMessage(result.Error));

        lock (sync)
        {
            if (disposed || generation != queryGeneration || cancellationToken.IsCancellationRequested)
                return new MockupCommunityQueryResult(false, hasMore, continuationToken,
                    "A newer Community search replaced this result.");

            if (string.IsNullOrEmpty(pageToken)) snapshot.Clear();
            foreach (CommunitySummary item in result.Value.Items)
            {
                if (item == null || item.Key == null) continue;
                int existing = snapshot.FindIndex(candidate => candidate.RemoteItemId
                    == item.Key.RemoteItemId.ToString());
                MockupDiscoveryItem projection = Project(item);
                if (existing >= 0) snapshot[existing] = projection;
                else snapshot.Add(projection);
            }
            continuationToken = result.Value.ContinuationToken;
            hasMore = result.Value.HasMore;
        }

        RaiseChanged();
        return new MockupCommunityQueryResult(true, result.Value.HasMore,
            result.Value.ContinuationToken, string.Empty);
    }

    public async Task<MockupActionResult> LikeAsync(string remoteItemId,
        CancellationToken cancellationToken)
    {
        if (!TryParseRemoteId(remoteItemId, out ulong id))
            return Failure("The selected Community item ID is invalid.");
        MockupDiscoveryItem current = Find(remoteItemId);
        if (current != null && current.IsLiked)
            return Failure("This item has already been liked; Steam does not support removing a vote.");

        try
        {
            CommunityResult<CommunityVoteState> result = await service.VoteUpAsync(id, cancellationToken);
            if (!result.Succeeded || result.Value == null) return Failure(ErrorMessage(result.Error));
            lock (sync)
            {
                int index = snapshot.FindIndex(item => item.RemoteItemId == remoteItemId);
                if (index >= 0)
                {
                    MockupDiscoveryItem item = snapshot[index];
                    snapshot[index] = Copy(item, likeCount: result.Value.LikeCount,
                        isLiked: result.Value.IsUpvoted);
                }
            }
            RaiseChanged();
            return new MockupActionResult(true, "Liked this Community list.",
                discoveryItem: Find(remoteItemId));
        }
        catch (OperationCanceledException) { return Failure("Like was canceled."); }
        catch (Exception exception) { return Failure("Like failed: " + exception.Message); }
    }

    public async Task<MockupActionResult> DownloadAsync(string remoteItemId,
        CancellationToken cancellationToken)
    {
        if (!TryParseRemoteId(remoteItemId, out ulong id))
            return Failure("The selected Community item ID is invalid.");
        try
        {
            CommunityResult<CommunityCacheEntry> result = await service.DownloadAsync(
                id, CreateProgress(), cancellationToken);
            if (!result.Succeeded || result.Value == null) return Failure(ErrorMessage(result.Error));
            lock (sync)
            {
                int index = snapshot.FindIndex(item => item.RemoteItemId == remoteItemId);
                if (index >= 0) snapshot[index] = Copy(snapshot[index], isDownloaded: true);
            }
            RaiseChanged();
            return new MockupActionResult(true, "Community list downloaded and cached.",
                discoveryItem: Find(remoteItemId));
        }
        catch (OperationCanceledException) { return Failure("Download was canceled."); }
        catch (Exception exception) { return Failure("Download failed: " + exception.Message); }
    }

    public Task<MockupActionResult> HideLocallyAsync(string remoteItemId,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(Failure("Hide was canceled."));
        if (!TryParseRemoteId(remoteItemId, out ulong id) || service.CurrentAppId == 0)
            return Task.FromResult(Failure("The selected Community item identity is invalid."));

        try
        {
            var key = new CommunityItemKey(service.CurrentAppId, id);
            CommunityResult<bool> result = service.SetHiddenLocally(key, true);
            if (!result.Succeeded || !result.Value)
                return Task.FromResult(Failure(ErrorMessage(result.Error)));
            lock (sync)
                snapshot.RemoveAll(item => item.RemoteItemId == remoteItemId);
            RaiseChanged();
            return Task.FromResult(new MockupActionResult(true,
                "Hidden from your Community view. No report was sent to Steam."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(Failure("The item could not be hidden: " + exception.Message));
        }
    }

    public Task<MockupActionResult> RestoreHiddenItemsAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(Failure("Restoring hidden items was canceled."));

        uint appId = service.CurrentAppId;
        ulong accountId = service.EffectiveLocalAccountId;
        if (appId == 0 || accountId == 0)
            return Task.FromResult(Failure("The local Steam account is unavailable; hidden items were unchanged."));

        IReadOnlyList<CommunityItemKey> hidden;
        try { hidden = service.GetHiddenItems() ?? Array.Empty<CommunityItemKey>(); }
        catch (Exception exception)
        {
            return Task.FromResult(Failure("Hidden items could not be read: " + exception.Message));
        }

        int restored = 0;
        string failureMessage = string.Empty;
        foreach (CommunityItemKey key in hidden)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                failureMessage = "The operation was canceled after restoring "
                    + restored.ToString(CultureInfo.InvariantCulture) + " hidden items.";
                break;
            }
            if (key == null || key.ConsumerAppId != appId) continue;
            if (service.CurrentAppId != appId || service.EffectiveLocalAccountId != accountId)
            {
                failureMessage = "The Steam account changed. Remaining hidden items were left unchanged.";
                break;
            }

            CommunityResult<bool> result;
            try { result = service.SetHiddenLocally(key, false); }
            catch (Exception exception)
            {
                failureMessage = "Restored " + restored.ToString(CultureInfo.InvariantCulture)
                    + " hidden items; the rest could not be restored: " + exception.Message;
                break;
            }
            if (result == null || !result.Succeeded || !result.Value)
            {
                failureMessage = "Restored " + restored.ToString(CultureInfo.InvariantCulture)
                    + " hidden items; the rest could not be restored: " + ErrorMessage(result?.Error);
                break;
            }
            restored++;
        }

        if (restored > 0)
        {
            lock (sync)
            {
                snapshot.Clear();
                continuationToken = string.Empty;
                hasMore = false;
                queryGeneration++;
            }
            RaiseChanged();
        }
        if (!string.IsNullOrWhiteSpace(failureMessage))
            return Task.FromResult(Failure(failureMessage));
        return Task.FromResult(new MockupActionResult(true,
            restored == 0 ? "There are no hidden Community items to restore."
                : "Restored " + restored.ToString(CultureInfo.InvariantCulture)
                    + " items to your Community view. No Steam report was changed."));
    }

    public async Task<MockupCommunityPublicationResult> PublishAsync(string localListId,
        CancellationToken cancellationToken)
    {
        if (disposed || cancellationToken.IsCancellationRequested)
            return PublicationFailure("Publish was canceled.");
        if (string.IsNullOrWhiteSpace(localListId))
            return PublicationFailure("The selected My List identity is invalid.");

        SetProgress(new MockupCommunityProgress("Preparing", null, "Preparing Workshop publication…"));
        try
        {
            CommunityPublication current = service.GetPublications()
                .FirstOrDefault(item => item != null
                    && string.Equals(item.LocalListId, localListId, StringComparison.Ordinal));
            CommunityResult<CommunityPublication> result;
            bool shouldSubmit = false;

            if (current != null && current.ActiveRemoteItemId.HasValue)
            {
                if (current.Status == CommunityPublicationStatus.Published)
                    return PublicationSuccess(current, "This list is already published.");
                if (current.Status == CommunityPublicationStatus.NeedsAgreement
                    || current.Status == CommunityPublicationStatus.Created
                    || current.Status == CommunityPublicationStatus.Failed)
                    shouldSubmit = true;
                else
                    return PublicationRecovery(current,
                        string.IsNullOrWhiteSpace(current.LastError)
                            ? "This publication needs status recovery before it can be retried."
                            : current.LastError);
            }
            else
            {
                CommunityPublishRequest request = CommunityServiceRegistry.ResolvePublicationRequest(localListId);
                if (request == null)
                    return PublicationFailure("The selected canonical My List could not be read for publishing.");
                SetProgress(new MockupCommunityProgress("Creating", null, "Creating Workshop item…"));
                result = await service.CreatePublicationAsync(request, cancellationToken);
                if (result == null)
                    return PublicationFailure("Workshop item creation returned no result.");
                if (result.Outcome == CommunityOperationOutcome.NeedsAgreement)
                    return PublicationAgreement(result.Value, result.Error?.Message);
                if (result.Outcome == CommunityOperationOutcome.RecoveryRequired
                    || result.Outcome == CommunityOperationOutcome.OutcomeUnknown)
                    return PublicationRecovery(result.Value, ErrorMessage(result.Error));
                if (!result.Succeeded || result.Value == null)
                    return PublicationFailure(ErrorMessage(result.Error));
                current = result.Value;
                if (current.NeedsLegalAgreement
                    || current.Status == CommunityPublicationStatus.NeedsAgreement)
                    return PublicationAgreement(current, current.LastError);
                shouldSubmit = current.ActiveRemoteItemId.HasValue
                    && current.Status != CommunityPublicationStatus.Published;
            }

            if (shouldSubmit)
            {
                if (cancellationToken.IsCancellationRequested)
                    return PublicationFailure("Publish was canceled before upload.");
                SetProgress(new MockupCommunityProgress("Uploading", null, "Uploading word list to Workshop…"));
                result = await service.SubmitPublicationAsync(localListId,
                    CreateProgress(), cancellationToken);
                if (result == null)
                    return PublicationFailure("Workshop upload returned no result.");
                if (result.Outcome == CommunityOperationOutcome.NeedsAgreement)
                    return PublicationAgreement(result.Value, result.Error?.Message);
                if (result.Outcome == CommunityOperationOutcome.RecoveryRequired
                    || result.Outcome == CommunityOperationOutcome.OutcomeUnknown)
                    return PublicationRecovery(result.Value, ErrorMessage(result.Error));
                if (!result.Succeeded || result.Value == null)
                    return PublicationFailure(ErrorMessage(result.Error));
                RaiseChanged();
                return PublicationSuccess(result.Value, "Published to Steam Workshop.");
            }

            return current != null && current.Status == CommunityPublicationStatus.Published
                ? PublicationSuccess(current, "This list is already published.")
                : PublicationFailure("Workshop did not confirm a publishable item.");
        }
        catch (OperationCanceledException)
        {
            return PublicationFailure("Publish was canceled.");
        }
        catch (Exception exception)
        {
            return PublicationFailure("Publish failed: " + exception.Message);
        }
        finally
        {
            SetProgress(null);
            RaiseChanged();
        }
    }

    public async Task<MockupCommunityPublicationResult> UnpublishAsync(string localListId,
        CancellationToken cancellationToken)
    {
        if (disposed || cancellationToken.IsCancellationRequested)
            return PublicationFailure("Unpublish was canceled.");
        if (string.IsNullOrWhiteSpace(localListId))
            return PublicationFailure("The selected My List identity is invalid.");

        try
        {
            CommunityPublication publication = service.GetPublications()
                .FirstOrDefault(item => item != null
                    && string.Equals(item.LocalListId, localListId, StringComparison.Ordinal));
            if (publication == null || !publication.ActiveRemoteItemId.HasValue)
                return PublicationFailure("This My List has no active Workshop item to unpublish.");
            SetProgress(new MockupCommunityProgress("Unpublishing", null,
                "Making the Workshop item private…"));
            CommunityResult<CommunityPublication> result = await service.UnpublishAsync(
                publication.ActiveRemoteItemId.Value, cancellationToken);
            if (result == null)
                return PublicationFailure("Workshop visibility change returned no result.");
            if (result.Outcome == CommunityOperationOutcome.RecoveryRequired
                || result.Outcome == CommunityOperationOutcome.OutcomeUnknown)
                return PublicationRecovery(result.Value ?? publication, ErrorMessage(result.Error));
            if (!result.Succeeded || result.Value == null)
                return PublicationFailure(ErrorMessage(result.Error));
            RaiseChanged();
            return PublicationSuccess(result.Value,
                "Unpublished. The Workshop item was made private; your My List and records remain local.");
        }
        catch (OperationCanceledException)
        {
            return PublicationFailure("Unpublish was canceled.");
        }
        catch (Exception exception)
        {
            return PublicationFailure("Unpublish failed: " + exception.Message);
        }
        finally
        {
            SetProgress(null);
            RaiseChanged();
        }
    }

    public async Task<MockupCommunityPageResult> GetItemPageAsync(string remoteItemId,
        CancellationToken cancellationToken)
    {
        if (!TryParseRemoteId(remoteItemId, out ulong id))
            return new MockupCommunityPageResult(false, string.Empty, "The Workshop item ID is invalid.");
        try
        {
            CommunityResult<CommunityDetails> result = await service.GetDetailsAsync(id, cancellationToken);
            if (!result.Succeeded || result.Value == null)
                return new MockupCommunityPageResult(false, string.Empty, ErrorMessage(result.Error));
            if (result.Value.Key.RemoteItemId != id
                || !TryValidateSteamItemUri(result.Value.CommunityPageUri, id, out string uri))
                return new MockupCommunityPageResult(false, string.Empty,
                    "Steam did not return a validated Workshop item page.");
            return new MockupCommunityPageResult(true, uri, string.Empty);
        }
        catch (OperationCanceledException)
        {
            return new MockupCommunityPageResult(false, string.Empty, "Opening the Steam item page was canceled.");
        }
        catch (Exception exception)
        {
            return new MockupCommunityPageResult(false, string.Empty,
                "The Steam item page could not be retrieved: " + exception.Message);
        }
    }

    public async Task<MockupCommunityPublicationResult> ReconcilePublicationAsync(
        string localListId, CancellationToken cancellationToken)
    {
        if (disposed || cancellationToken.IsCancellationRequested)
            return PublicationFailure("Status refresh was canceled.");
        if (string.IsNullOrWhiteSpace(localListId))
            return PublicationFailure("The selected My List identity is invalid.");

        SetProgress(new MockupCommunityProgress("CheckingStatus", null,
            "Checking Workshop publication status…"));
        try
        {
            CommunityResult<CommunityPublication> result = await service.ReconcilePublicationAsync(
                localListId, cancellationToken);
            if (result == null)
                return PublicationFailure("Workshop status refresh returned no result.");
            if (result.Outcome == CommunityOperationOutcome.RecoveryRequired
                || result.Outcome == CommunityOperationOutcome.OutcomeUnknown)
                return PublicationRecovery(result.Value, ErrorMessage(result.Error));
            if (!result.Succeeded || result.Value == null)
                return PublicationFailure(ErrorMessage(result.Error));

            RaiseChanged();
            return PublicationSuccess(result.Value, "Workshop publication status refreshed.");
        }
        catch (OperationCanceledException)
        {
            return PublicationFailure("Status refresh was canceled.");
        }
        catch (Exception exception)
        {
            return PublicationFailure("Workshop status could not be refreshed: " + exception.Message);
        }
        finally
        {
            SetProgress(null);
            RaiseChanged();
        }
    }

    private void HandleAvailabilityChanged(CommunityServiceState state) => RaiseChanged();

    private void RaiseBecameAvailable()
    {
        Action notify = () =>
        {
            if (!disposed) BecameAvailable?.Invoke();
        };
        if (dispatchMainThread != null) dispatchMainThread(notify);
        else notify();
    }

    private void SetProgress(MockupCommunityProgress progress)
    {
        lock (sync) currentProgress = progress;
        Action notify = () =>
        {
            if (disposed) return;
            ProgressChanged?.Invoke(progress);
            Changed?.Invoke();
        };
        if (dispatchMainThread != null) dispatchMainThread(notify);
        else notify();
    }

    private void RaiseChanged()
    {
        if (disposed) return;
        Action notify = () =>
        {
            if (!disposed) Changed?.Invoke();
        };
        if (dispatchMainThread != null) dispatchMainThread(notify);
        else notify();
    }

    private MockupDiscoveryItem Find(string remoteItemId)
    {
        lock (sync) return snapshot.FirstOrDefault(item => item.RemoteItemId == remoteItemId);
    }

    private MockupDiscoveryItem Project(CommunitySummary item)
    {
        bool downloaded = item.IsDownloaded
            || service.TryGetCachedEntry(item.Key, out _);
        return MockupWordListProjection.CreateDiscoveryItem(
            item.Key.RemoteItemId.ToString(), item.Title, item.Description,
            ToMockupLanguage(item.Language), Array.Empty<string>(), item.Author,
            item.RemoteStatus == CommunityRemoteStatus.Available
                ? MockupDiscoveryStatus.New : MockupDiscoveryStatus.Updated,
            item.WordCount, item.LikeCount, item.IsLiked, downloaded);
    }

    private static MockupDiscoveryItem Copy(MockupDiscoveryItem item,
        int? likeCount = null, bool? isLiked = null, bool? isDownloaded = null)
    {
        return MockupWordListProjection.CreateDiscoveryItem(
            item.RemoteItemId, item.Title, item.Description, item.Language,
            item.Words, item.RemoteAuthor, item.Status, item.WordCount,
            likeCount ?? item.LikeCount, isLiked ?? item.IsLiked,
            isDownloaded ?? item.IsDownloaded, item.IsHiddenByCurrentUser);
    }

    private static CommunityQuery ToCommunityQuery(MockupDiscoveryQuery query, string token)
    {
        LanguageMode? language = null;
        if (query.LanguageFilter == MockupLanguageFilter.English) language = LanguageMode.English;
        else if (query.LanguageFilter == MockupLanguageFilter.Chinese) language = LanguageMode.Chinese;
        else if (query.LanguageFilter == MockupLanguageFilter.Mixed) language = LanguageMode.Mixed;

        return new CommunityQuery
        {
            SearchText = query.SearchText ?? string.Empty,
            Language = language,
            Sort = query.SortMode == MockupDiscoverySortMode.Likes
                ? CommunitySort.Popular : CommunitySort.Latest,
            ContinuationToken = token ?? string.Empty
        };
    }

    private static MockupLanguage ToMockupLanguage(LanguageMode language)
    {
        switch (language)
        {
            case LanguageMode.Chinese: return MockupLanguage.Chinese;
            case LanguageMode.Mixed: return MockupLanguage.Mixed;
            default: return MockupLanguage.English;
        }
    }

    private static bool TryParseRemoteId(string text, out ulong id) =>
        ulong.TryParse(text, out id) && id != 0;

    private static string ErrorMessage(CommunityError error) => error == null
        ? "The Community operation failed."
        : string.IsNullOrWhiteSpace(error.Message) ? "The Community operation failed." : error.Message;

    private IProgress<CommunityTransferProgress> CreateProgress() =>
        new Progress<CommunityTransferProgress>(progress =>
        {
            if (progress == null)
            {
                SetProgress(null);
                return;
            }
            string message = progress.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                switch (progress.Stage)
                {
                    case CommunityProgressStage.Downloading: message = "Downloading from Steam Workshop…"; break;
                    case CommunityProgressStage.Validating: message = "Validating the word list…"; break;
                    case CommunityProgressStage.Caching: message = "Saving the offline copy…"; break;
                    case CommunityProgressStage.Uploading: message = "Uploading to Steam Workshop…"; break;
                    default: message = progress.Stage.ToString(); break;
                }
            }
            SetProgress(new MockupCommunityProgress(progress.Stage.ToString(),
                progress.Fraction, message));
        });

    private static bool SameState(CommunityServiceState left, CommunityServiceState right) =>
        ReferenceEquals(left, right) || (left != null && right != null
            && left.Availability == right.Availability
            && left.CurrentAppId == right.CurrentAppId
            && left.CurrentAccountId == right.CurrentAccountId
            && left.Generation == right.Generation
            && string.Equals(left.LastError, right.LastError, StringComparison.Ordinal));

    private static bool TryValidateSteamItemUri(Uri uri, ulong expectedRemoteId, out string value)
    {
        value = string.Empty;
        if (uri == null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "steamcommunity.com", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.AbsolutePath.TrimEnd('/'), "/sharedfiles/filedetails",
                StringComparison.OrdinalIgnoreCase))
            return false;

        string query = uri.Query.TrimStart('?').Split('&')
            .FirstOrDefault(part => part.StartsWith("id=", StringComparison.OrdinalIgnoreCase));
        if (query == null || !ulong.TryParse(query.Substring(3), NumberStyles.None,
            CultureInfo.InvariantCulture, out ulong actualRemoteId) || actualRemoteId != expectedRemoteId)
            return false;

        value = uri.AbsoluteUri;
        return true;
    }

    private static MockupCommunityPublicationInfo ProjectPublication(CommunityPublication publication)
    {
        MockupCommunityPublicationState state;
        switch (publication.Status)
        {
            case CommunityPublicationStatus.Creating: state = MockupCommunityPublicationState.Creating; break;
            case CommunityPublicationStatus.NeedsAgreement: state = MockupCommunityPublicationState.NeedsAgreement; break;
            case CommunityPublicationStatus.Created: state = MockupCommunityPublicationState.Created; break;
            case CommunityPublicationStatus.Submitting: state = MockupCommunityPublicationState.Submitting; break;
            case CommunityPublicationStatus.Published: state = MockupCommunityPublicationState.Published; break;
            case CommunityPublicationStatus.Unpublishing: state = MockupCommunityPublicationState.Unpublishing; break;
            case CommunityPublicationStatus.Failed: state = MockupCommunityPublicationState.Failed; break;
            case CommunityPublicationStatus.RecoveryRequired: state = MockupCommunityPublicationState.RecoveryRequired; break;
            default: state = MockupCommunityPublicationState.Unpublished; break;
        }
        return new MockupCommunityPublicationInfo(publication.LocalListId, publication.Title,
            publication.ActiveRemoteItemId?.ToString(CultureInfo.InvariantCulture),
            publication.ContentHash, state, publication.NeedsLegalAgreement, publication.LastError);
    }

    private static MockupCommunityPublicationResult PublicationSuccess(
        CommunityPublication publication, string message) =>
        new MockupCommunityPublicationResult(true, false, false,
            publication != null ? ProjectPublication(publication) : null, message);

    private static MockupCommunityPublicationResult PublicationAgreement(
        CommunityPublication publication, string message) =>
        new MockupCommunityPublicationResult(false, true, false,
            publication != null ? ProjectPublication(publication) : null,
            string.IsNullOrWhiteSpace(message)
                ? "Accept the Steam Workshop legal agreement on the item page, then retry publishing."
                : message + " Open the Workshop item page, accept the agreement, then retry publishing.");

    private static MockupCommunityPublicationResult PublicationRecovery(
        CommunityPublication publication, string message) =>
        new MockupCommunityPublicationResult(false, false, true,
            publication != null ? ProjectPublication(publication) : null,
            string.IsNullOrWhiteSpace(message)
                ? "The publication needs status recovery before it can be retried."
                : message);

    private static MockupCommunityPublicationResult PublicationFailure(string message) =>
        new MockupCommunityPublicationResult(false, false, false, null,
            string.IsNullOrWhiteSpace(message) ? "The publication operation failed." : message);

    private static MockupActionResult Failure(string message) => new MockupActionResult(false, message);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        service.AvailabilityChanged -= HandleAvailabilityChanged;
        lock (sync)
        {
            queryGeneration++;
            snapshot.Clear();
        }
        Changed = null;
        BecameAvailable = null;
        ProgressChanged = null;
    }
}
