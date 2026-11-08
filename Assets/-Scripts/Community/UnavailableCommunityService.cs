using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Words.Community
{
    /// <summary>Typed, non-throwing fallback used when no platform gateway is registered.</summary>
    public sealed class UnavailableCommunityService : ICommunityService
    {
        private readonly CommunityServiceState state;
        private readonly string message;
        public CommunityServiceState State => state;
        public uint CurrentAppId => state.CurrentAppId;
        public ulong CurrentAccountId => state.CurrentAccountId;
        public ulong EffectiveLocalAccountId => 0;
        public bool IsRemoteAuthenticated => false;
        public event Action<CommunityServiceState> AvailabilityChanged { add { } remove { } }

        public UnavailableCommunityService(string message = null, uint appId = 0, ulong accountId = 0)
        {
            this.message = string.IsNullOrWhiteSpace(message)
                ? "Community features are unavailable because no platform service is connected."
                : message;
            state = new CommunityServiceState(CommunityAvailability.Offline, appId, accountId, 0, this.message);
        }

        public IReadOnlyList<CommunityCacheEntry> GetCachedEntries() => Array.Empty<CommunityCacheEntry>();
        public bool TryGetCachedEntry(CommunityItemKey key, out CommunityCacheEntry entry)
        { entry = null; return false; }
        public IReadOnlyList<CommunityItemKey> GetHiddenItems() => Array.Empty<CommunityItemKey>();
        public IReadOnlyList<CommunityPublication> GetPublications() => Array.Empty<CommunityPublication>();
        public bool TryGetPublication(string localListId, out CommunityPublication publication)
        { publication = null; return false; }
        public bool IsLocalListPublicationLocked(string localListId) => true;
        public CommunityResult<bool> SetHiddenLocally(CommunityItemKey key, bool isHidden) =>
            CommunityResult<bool>.Failure(new CommunityError(CommunityErrorCode.Offline, message, true));
        public bool TryGetHiddenLocally(CommunityItemKey key, out bool isHidden, out CommunityError error)
        { isHidden = false; error = new CommunityError(CommunityErrorCode.Offline, message, true); return false; }
        public bool TryRecordRun(CommunityItemKey key, string contentHash, ulong accountId,
            float elapsedSeconds, int errors, int stars, out CommunityPersonalRecord record, out CommunityError error)
        { record = null; error = new CommunityError(CommunityErrorCode.Offline, message, true); return false; }
        public bool TryGetRecord(CommunityItemKey key, string contentHash, ulong accountId,
            out CommunityPersonalRecord record, out CommunityError error)
        { record = null; error = new CommunityError(CommunityErrorCode.Offline, message, true); return false; }

        public Task<CommunityResult<CommunityPage>> QueryAsync(CommunityQuery query, CancellationToken cancellationToken) =>
            Result<CommunityPage>(cancellationToken);
        public Task<CommunityResult<CommunityDetails>> GetDetailsAsync(ulong remoteItemId, CancellationToken cancellationToken) =>
            Result<CommunityDetails>(cancellationToken);
        public Task<CommunityResult<CommunityCacheEntry>> DownloadAsync(ulong remoteItemId,
            IProgress<CommunityTransferProgress> progress, CancellationToken cancellationToken) =>
            Result<CommunityCacheEntry>(cancellationToken);
        public Task<CommunityResult<CommunityVoteState>> VoteUpAsync(ulong remoteItemId, CancellationToken cancellationToken) =>
            Result<CommunityVoteState>(cancellationToken);
        public Task<CommunityResult<CommunityPage>> QueryOwnedAsync(string continuationToken, CancellationToken cancellationToken) =>
            Result<CommunityPage>(cancellationToken);
        public Task<CommunityResult<CommunityPublication>> CreatePublicationAsync(CommunityPublishRequest request,
            CancellationToken cancellationToken) => Result<CommunityPublication>(cancellationToken);
        public Task<CommunityResult<CommunityPublication>> SubmitPublicationAsync(string localListId,
            IProgress<CommunityTransferProgress> progress, CancellationToken cancellationToken) =>
            Result<CommunityPublication>(cancellationToken);
        public Task<CommunityResult<CommunityPublication>> UnpublishAsync(ulong remoteItemId,
            CancellationToken cancellationToken) => Result<CommunityPublication>(cancellationToken);
        public Task<CommunityResult<CommunityPublication>> ReconcilePublicationAsync(string localListId,
            CancellationToken cancellationToken) => Result<CommunityPublication>(cancellationToken);

        private Task<CommunityResult<T>> Result<T>(CancellationToken cancellationToken)
        {
            CommunityResult<T> result = cancellationToken.IsCancellationRequested
                ? CommunityResult<T>.Canceled()
                : CommunityResult<T>.Failure(new CommunityError(CommunityErrorCode.Offline, message, true));
            return Task.FromResult(result);
        }
    }
}
