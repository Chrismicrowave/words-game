using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Words.Community
{
    /// <summary>Platform-neutral orchestration over a community gateway and app-owned stores.</summary>
    public sealed partial class GatewayCommunityService : ICommunityService, IDisposable
    {
        private readonly ICommunityGateway gateway;
        private readonly CommunityCacheStore cacheStore;
        private readonly string accountStateRoot;
        private readonly uint configuredConsumerAppId;
        private readonly Func<string, CommunityPublishRequest> publicationRequestResolver;
        private readonly object storesLock = new object();
        private readonly Dictionary<CommunityAccountScope, CommunityPublicationRegistry> publicationRegistries =
            new Dictionary<CommunityAccountScope, CommunityPublicationRegistry>();
        private readonly Dictionary<CommunityAccountScope, CommunityAccountStateStore> accountStores =
            new Dictionary<CommunityAccountScope, CommunityAccountStateStore>();
        private readonly Dictionary<uint, CommunityLocalIdentityStore> identityStores =
            new Dictionary<uint, CommunityLocalIdentityStore>();
        private event Action<CommunityServiceState> availabilityChanged;
        private bool disposed;

        public CommunityServiceState State
        {
            get
            {
                CommunityServiceState state = gateway.State ??
                    new CommunityServiceState(CommunityAvailability.Unknown, 0, 0, 0, "Community gateway has no state.");
                if (state.CurrentAppId == 0 && configuredConsumerAppId != 0)
                    return new CommunityServiceState(state.Availability, configuredConsumerAppId,
                        state.CurrentAccountId, state.Generation, state.LastError);
                return state;
            }
        }
        public uint CurrentAppId => configuredConsumerAppId != 0 ? configuredConsumerAppId : State.CurrentAppId;
        public ulong CurrentAccountId
        {
            get
            {
                CommunityServiceState state = gateway.State;
                if (state == null || (state.CurrentAppId != 0 && state.CurrentAppId != CurrentAppId)) return 0;
                return state.CurrentAccountId;
            }
        }
        public ulong EffectiveLocalAccountId
        {
            get
            {
                ulong currentAccountId = CurrentAccountId;
                if (currentAccountId != 0)
                {
                    GetIdentityStore(CurrentAppId)?.TryRemember(currentAccountId, out _);
                    return currentAccountId;
                }
                return GetIdentityStore(CurrentAppId)?.GetLastKnownAccountId() ?? 0;
            }
        }
        public bool IsRemoteAuthenticated => CurrentAppId != 0 && CurrentAccountId != 0
            && State.Availability == CommunityAvailability.Available
            && (gateway.State == null || gateway.State.CurrentAppId == 0 || gateway.State.CurrentAppId == CurrentAppId);
        public event Action<CommunityServiceState> AvailabilityChanged
        {
            add { availabilityChanged += value; }
            remove { availabilityChanged -= value; }
        }

        public GatewayCommunityService(ICommunityGateway gateway, CommunityCacheStore cacheStore,
            string accountStateRoot, uint consumerAppId,
            Func<string, CommunityPublishRequest> publicationRequestResolver = null)
        {
            this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            this.cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
            if (string.IsNullOrWhiteSpace(accountStateRoot))
                throw new ArgumentException("An app/account state root is required.", nameof(accountStateRoot));
            if (consumerAppId == 0) throw new ArgumentOutOfRangeException(nameof(consumerAppId));
            this.accountStateRoot = accountStateRoot;
            configuredConsumerAppId = consumerAppId;
            this.publicationRequestResolver = publicationRequestResolver;
            this.gateway.AvailabilityChanged += OnGatewayAvailabilityChanged;
        }

        public async Task<CommunityResult<CommunityPage>> QueryAsync(CommunityQuery query,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return CommunityResult<CommunityPage>.Canceled();
            if (query == null || !Enum.IsDefined(typeof(CommunitySort), query.Sort)
                || (query.Language.HasValue && !IsSupportedLanguage(query.Language.Value))
                || (query.SearchText ?? string.Empty).Length > 256
                || (query.ContinuationToken ?? string.Empty).Length > 2048)
                return Failure<CommunityPage>(CommunityErrorCode.InvalidContent, "The community query is invalid.");

            CommunityQuery stableQuery = new CommunityQuery
            {
                SearchText = (query.SearchText ?? string.Empty).Trim(),
                Language = query.Language,
                Sort = query.Sort,
                ContinuationToken = query.ContinuationToken ?? string.Empty
            };
            CommunityResult<CommunityPage> response = await CallGateway(
                () => gateway.QueryAsync(stableQuery, cancellationToken), cancellationToken);
            if (!response.Succeeded || response.Value == null) return response;
            return CommunityResult<CommunityPage>.Success(DecoratePage(response.Value), response.OperationId);
        }

        public async Task<CommunityResult<CommunityDetails>> GetDetailsAsync(ulong remoteItemId,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return CommunityResult<CommunityDetails>.Canceled();
            if (remoteItemId == 0 || CurrentAppId == 0)
                return Failure<CommunityDetails>(CommunityErrorCode.InvalidContent, "A current app and remote item ID are required.");
            var key = new CommunityItemKey(CurrentAppId, remoteItemId);
            CommunityResult<CommunityDetails> response = await CallGateway(
                () => gateway.GetDetailsAsync(key, cancellationToken), cancellationToken);
            if (!response.Succeeded || response.Value == null) return response;
            if (!response.Value.Key.Equals(key))
                return Failure<CommunityDetails>(CommunityErrorCode.Incompatible, "The details response has a different community identity.", response.OperationId);
            return CommunityResult<CommunityDetails>.Success(DecorateDetails(response.Value), response.OperationId);
        }

        public async Task<CommunityResult<CommunityCacheEntry>> DownloadAsync(ulong remoteItemId,
            IProgress<CommunityTransferProgress> progress, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return CommunityResult<CommunityCacheEntry>.Canceled();
            if (remoteItemId == 0 || CurrentAppId == 0)
                return Failure<CommunityCacheEntry>(CommunityErrorCode.InvalidContent, "A current app and remote item ID are required.");

            var key = new CommunityItemKey(CurrentAppId, remoteItemId);
            progress?.Report(new CommunityTransferProgress(CommunityProgressStage.Downloading));
            CommunityResult<CommunityDownloadReceipt> response = await CallGateway(
                () => gateway.DownloadAsync(key, progress, cancellationToken), cancellationToken);
            if (!response.Succeeded) return ConvertResult<CommunityDownloadReceipt, CommunityCacheEntry>(response);
            CommunityDownloadReceipt receipt = response.Value;
            if (receipt == null || receipt.Key == null || !receipt.Key.Equals(key))
                return Failure<CommunityCacheEntry>(CommunityErrorCode.Incompatible,
                    "The download payload has a different community identity.", response.OperationId);

            progress?.Report(new CommunityTransferProgress(CommunityProgressStage.Validating));
            CommunityResult<CommunityCacheEntry> committed = cacheStore.Commit(receipt);
            if (!committed.Succeeded) return WithOperationId(committed, response.OperationId);
            progress?.Report(new CommunityTransferProgress(CommunityProgressStage.Caching, 1f));
            return CommunityResult<CommunityCacheEntry>.Success(committed.Value, response.OperationId);
        }

        public async Task<CommunityResult<CommunityVoteState>> VoteUpAsync(ulong remoteItemId,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return CommunityResult<CommunityVoteState>.Canceled();
            if (!IsRemoteAuthenticated) return Failure<CommunityVoteState>(RemoteAuthErrorCode(), RemoteAuthErrorMessage(), retryable: true);
            if (remoteItemId == 0 || CurrentAppId == 0)
                return Failure<CommunityVoteState>(CommunityErrorCode.InvalidContent, "A current app and remote item ID are required.");
            return await CallGateway(() => gateway.VoteUpAsync(
                new CommunityItemKey(CurrentAppId, remoteItemId), cancellationToken), cancellationToken);
        }

        public async Task<CommunityResult<CommunityPage>> QueryOwnedAsync(string continuationToken,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return CommunityResult<CommunityPage>.Canceled();
            if (CurrentAppId == 0) return Failure<CommunityPage>(CommunityErrorCode.Offline,
                "The community platform session is not initialized.", retryable: true);
            if (!IsRemoteAuthenticated) return Failure<CommunityPage>(RemoteAuthErrorCode(), RemoteAuthErrorMessage(), retryable: true);
            CommunityResult<CommunityPage> response = await CallGateway(
                () => gateway.QueryOwnedAsync(CurrentAppId, continuationToken ?? string.Empty, cancellationToken),
                cancellationToken);
            if (!response.Succeeded || response.Value == null) return response;
            return CommunityResult<CommunityPage>.Success(DecoratePage(response.Value), response.OperationId);
        }

        public IReadOnlyList<CommunityCacheEntry> GetCachedEntries()
        {
            uint appId = CurrentAppId;
            if (appId == 0) return Array.Empty<CommunityCacheEntry>();
            CommunityResult<IReadOnlyList<CommunityCacheEntry>> result = cacheStore.GetEntries(appId);
            return result.Succeeded && result.Value != null ? result.Value : Array.Empty<CommunityCacheEntry>();
        }

        public bool TryGetCachedEntry(CommunityItemKey key, out CommunityCacheEntry entry)
        {
            entry = null;
            if (key == null || key.ConsumerAppId != CurrentAppId) return false;
            CommunityResult<IReadOnlyList<CommunityCacheEntry>> entries = cacheStore.GetEntries(key.ConsumerAppId);
            if (!entries.Succeeded || entries.Value == null) return false;
            foreach (CommunityCacheEntry candidate in entries.Value)
            {
                if (candidate.Key.Equals(key) && (entry == null || candidate.CachedAtUtc > entry.CachedAtUtc))
                    entry = candidate;
            }
            return entry != null;
        }

        public IReadOnlyList<CommunityItemKey> GetHiddenItems()
        {
            CommunityAccountScope scope = CurrentScope();
            if (scope == null) return Array.Empty<CommunityItemKey>();
            CommunityAccountStateStore store = GetAccountStore(scope);
            if (!store.TryGetHiddenItems(out IReadOnlyList<CommunityItemKey> hidden, out _)
                || !scope.Equals(CurrentScope()))
                return Array.Empty<CommunityItemKey>();
            return hidden.Where(key => key != null && key.ConsumerAppId == CurrentAppId).ToList().AsReadOnly();
        }

        public IReadOnlyList<CommunityPublication> GetPublications()
        {
            CommunityAccountScope scope = CurrentScope();
            return scope != null ? GetPublicationRegistry(scope).GetPublications() : Array.Empty<CommunityPublication>();
        }

        public bool TryGetPublication(string localListId, out CommunityPublication publication)
        {
            publication = null;
            CommunityAccountScope scope = CurrentScope();
            return scope != null && GetPublicationRegistry(scope).TryGetPublication(localListId, out publication);
        }

        public bool IsLocalListPublicationLocked(string localListId)
        {
            if (string.IsNullOrWhiteSpace(localListId) || localListId.Length > 256 || CurrentAppId == 0)
                return true;

            try
            {
                string root = Path.GetFullPath(accountStateRoot);
                string appPath = Path.GetFullPath(Path.Combine(root,
                    CurrentAppId.ToString(CultureInfo.InvariantCulture)));
                string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (!appPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                    || !TryValidateNoReparsePath(appPath))
                    return true;
                if (!Directory.Exists(appPath)) return false;

                foreach (string accountPath in Directory.GetDirectories(appPath))
                {
                    if (!TryValidateNoReparsePath(accountPath)) return true;
                    string accountSegment = Path.GetFileName(accountPath);
                    if (!ulong.TryParse(accountSegment, NumberStyles.None, CultureInfo.InvariantCulture,
                        out ulong accountId) || accountId == 0)
                        return true;

                    string journalPath = Path.Combine(accountPath, "publications.json");
                    if (!TryValidateNoReparsePath(journalPath)) return true;
                    var registry = new CommunityPublicationRegistry(root,
                        new CommunityAccountScope(CurrentAppId, accountId));
                    if (!registry.TryGetPublicationLock(localListId, out bool isLocked, out _)) return true;
                    if (isLocked) return true;
                }
                return false;
            }
            catch (Exception)
            {
                // An unreadable or malformed account journal must not make an existing binding editable.
                return true;
            }
        }

        private static bool TryValidateNoReparsePath(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                string volumeRoot = Path.GetPathRoot(fullPath);
                string relative = fullPath.Substring(volumeRoot.Length);
                string current = volumeRoot;
                string[] segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);
                for (int index = 0; index < segments.Length; index++)
                {
                    current = Path.Combine(current, segments[index]);
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(current); }
                    catch (FileNotFoundException) { break; }
                    catch (DirectoryNotFoundException) { break; }
                    if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                    if (index < segments.Length - 1 && (attributes & FileAttributes.Directory) == 0)
                        return false;
                }
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is ArgumentException || exception is NotSupportedException)
            {
                return false;
            }
        }

        public CommunityResult<bool> SetHiddenLocally(CommunityItemKey key, bool isHidden)
        {
            if (key == null || key.ConsumerAppId != CurrentAppId)
                return CommunityResult<bool>.Failure(new CommunityError(CommunityErrorCode.InvalidContent,
                    "The hidden item must belong to the current app."));
            CommunityAccountScope scope = CurrentScope();
            if (scope == null) return CommunityResult<bool>.Failure(new CommunityError(
                CommunityErrorCode.NotLoggedIn, "Sign in to save a local hidden-item preference."));
            bool success = GetAccountStore(scope).TrySetHidden(key, isHidden, out CommunityError error);
            return success ? CommunityResult<bool>.Success(isHidden)
                : CommunityResult<bool>.Failure(error);
        }

        public bool TryGetHiddenLocally(CommunityItemKey key, out bool isHidden, out CommunityError error)
        {
            isHidden = false;
            if (key == null || key.ConsumerAppId != CurrentAppId)
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent, "The hidden item must belong to the current app.");
                return false;
            }
            CommunityAccountScope scope = CurrentScope();
            if (scope == null)
            {
                error = new CommunityError(CommunityErrorCode.NotLoggedIn, "Sign in to read local hidden-item preferences.");
                return false;
            }
            return GetAccountStore(scope).TryGetHidden(key, out isHidden, out error);
        }

        public bool TryRecordRun(CommunityItemKey key, string contentHash, ulong accountId,
            float elapsedSeconds, int errors, int stars, out CommunityPersonalRecord record, out CommunityError error)
        {
            record = null;
            ulong currentAccountId = CurrentAccountId;
            ulong effectiveLocalAccountId = EffectiveLocalAccountId;
            bool accountMatches = currentAccountId != 0
                ? accountId == currentAccountId
                : accountId != 0 && accountId == effectiveLocalAccountId;
            if (key == null || accountId == 0 || key.ConsumerAppId != CurrentAppId || !accountMatches)
            {
                error = new CommunityError(CommunityErrorCode.NotLoggedIn,
                    "The run account is not the current or last-known local account; no record was written.");
                return false;
            }
            var scope = new CommunityAccountScope(key.ConsumerAppId, accountId);
            return GetAccountStore(scope).TryRecordRun(key, contentHash, elapsedSeconds,
                errors, stars, out record, out error);
        }

        public bool TryGetRecord(CommunityItemKey key, string contentHash, ulong accountId,
            out CommunityPersonalRecord record, out CommunityError error)
        {
            record = null;
            if (key == null || accountId == 0 || key.ConsumerAppId != CurrentAppId
                || accountId != EffectiveLocalAccountId)
            {
                error = new CommunityError(CommunityErrorCode.NotLoggedIn, "The requested records do not match the current or last-known local account.");
                return false;
            }
            var scope = new CommunityAccountScope(key.ConsumerAppId, accountId);
            return GetAccountStore(scope).TryGetRecord(key, contentHash, out record, out error);
        }

        private CommunityPage DecoratePage(CommunityPage page)
        {
            IReadOnlyList<CommunityCacheEntry> cached = GetCachedEntries();
            var cachedKeys = new HashSet<CommunityItemKey>(cached.Select(item => item.Key));
            var items = new List<CommunitySummary>();
            foreach (CommunitySummary item in page.Items)
            {
                if (item == null) continue;
                if (TryGetHiddenLocally(item.Key, out bool hidden, out _) && hidden) continue;
                items.Add(new CommunitySummary(item.Key, item.Title, item.Author, item.Description,
                    item.Language, item.WordCount, item.LikeCount, item.IsLiked,
                    cachedKeys.Contains(item.Key), item.RemoteStatus));
            }
            return new CommunityPage(items, page.ContinuationToken, page.HasMore);
        }

        private CommunityDetails DecorateDetails(CommunityDetails details)
        {
            bool downloaded = TryGetCachedEntry(details.Key, out _);
            return new CommunityDetails(details.Key, details.Title, details.Author, details.Description,
                details.Language, details.WordCount, details.LikeCount, details.IsLiked,
                downloaded, details.RemoteStatus, details.CommunityPageUri,
                details.OwnerAccountId, details.ContentHash);
        }

        private CommunityAccountScope CurrentScope()
        {
            uint appId = CurrentAppId;
            ulong accountId = EffectiveLocalAccountId;
            return appId != 0 && accountId != 0 ? new CommunityAccountScope(appId, accountId) : null;
        }

        private CommunityLocalIdentityStore GetIdentityStore(uint appId)
        {
            if (appId == 0) return null;
            lock (storesLock)
            {
                if (!identityStores.TryGetValue(appId, out CommunityLocalIdentityStore store))
                {
                    store = new CommunityLocalIdentityStore(accountStateRoot, appId);
                    identityStores[appId] = store;
                }
                return store;
            }
        }

        private CommunityPublicationRegistry GetPublicationRegistry(CommunityAccountScope scope)
        {
            lock (storesLock)
            {
                if (!publicationRegistries.TryGetValue(scope, out CommunityPublicationRegistry registry))
                {
                    registry = new CommunityPublicationRegistry(accountStateRoot, scope);
                    publicationRegistries[scope] = registry;
                }
                return registry;
            }
        }

        private CommunityAccountStateStore GetAccountStore(CommunityAccountScope scope)
        {
            lock (storesLock)
            {
                if (!accountStores.TryGetValue(scope, out CommunityAccountStateStore store))
                {
                    store = new CommunityAccountStateStore(accountStateRoot, scope);
                    accountStores[scope] = store;
                }
                return store;
            }
        }

        private async Task<CommunityResult<T>> CallGateway<T>(Func<Task<CommunityResult<T>>> operation,
            CancellationToken cancellationToken)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested) return CommunityResult<T>.Canceled();
                // Preserve Unity's captured synchronization context for adapter callbacks and JsonUtility/file-store work.
                CommunityResult<T> response = await operation();
                return response ?? CommunityResult<T>.Failure(new CommunityError(
                    CommunityErrorCode.Unknown, "The community gateway returned no result."));
            }
            catch (OperationCanceledException)
            {
                return cancellationToken.IsCancellationRequested ? CommunityResult<T>.Canceled()
                    : CommunityResult<T>.Unknown(new CommunityError(CommunityErrorCode.Unknown,
                        "The community operation ended without a confirmed result."), Guid.NewGuid().ToString("N"));
            }
            catch (Exception exception)
            {
                return CommunityResult<T>.Failure(new CommunityError(CommunityErrorCode.Unknown,
                    "The community operation failed: " + exception.Message, true));
            }
        }

        private void OnGatewayAvailabilityChanged(CommunityServiceState state)
        {
            if (state != null && state.CurrentAccountId != 0
                && (state.CurrentAppId == 0 || state.CurrentAppId == configuredConsumerAppId))
                GetIdentityStore(configuredConsumerAppId)
                    ?.TryRemember(state.CurrentAccountId, out _);
            availabilityChanged?.Invoke(State);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            gateway.AvailabilityChanged -= OnGatewayAvailabilityChanged;
            if (gateway is IDisposable disposable) disposable.Dispose();
        }

        private static bool IsSupportedLanguage(LanguageMode mode) => mode == LanguageMode.English
            || mode == LanguageMode.Chinese || mode == LanguageMode.Mixed;

        private CommunityErrorCode RemoteAuthErrorCode() => CurrentAccountId == 0
            ? CommunityErrorCode.NotLoggedIn : CommunityErrorCode.Offline;

        private string RemoteAuthErrorMessage() => CurrentAccountId == 0
            ? "Sign in to perform this community action."
            : "Reconnect the community platform to perform this action.";

        private static CommunityResult<T> Failure<T>(CommunityErrorCode code, string message,
            string operationId = null, bool retryable = false) => CommunityResult<T>.Failure(
                new CommunityError(code, message, retryable), operationId);

        private static CommunityResult<TOut> ConvertResult<TIn, TOut>(CommunityResult<TIn> source)
        {
            if (source == null) return CommunityResult<TOut>.Failure(new CommunityError(CommunityErrorCode.Unknown, "The community gateway returned no result."));
            switch (source.Outcome)
            {
                case CommunityOperationOutcome.Canceled: return CommunityResult<TOut>.Canceled(source.OperationId);
                case CommunityOperationOutcome.OutcomeUnknown: return CommunityResult<TOut>.Unknown(source.Error, source.OperationId);
                case CommunityOperationOutcome.NeedsAgreement: return CommunityResult<TOut>.NeedsAgreement(default(TOut), source.OperationId);
                case CommunityOperationOutcome.RecoveryRequired: return CommunityResult<TOut>.RecoveryRequired(default(TOut), source.Error, source.OperationId);
                default: return CommunityResult<TOut>.Failure(source.Error, source.OperationId);
            }
        }

        private static CommunityResult<T> WithOperationId<T>(CommunityResult<T> result, string operationId)
        {
            if (result == null) return CommunityResult<T>.Failure(new CommunityError(CommunityErrorCode.Unknown, "No result was returned."), operationId);
            if (result.Succeeded) return CommunityResult<T>.Success(result.Value, operationId);
            return ConvertResult<T, T>(result);
        }
    }
}
