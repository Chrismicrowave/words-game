using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Words.Community
{
    public enum CommunityAvailability
    {
        Unknown,
        Available,
        Offline,
        NotLoggedIn
    }

    public enum CommunityRemoteStatus
    {
        Unknown,
        Available,
        Private,
        Deleted,
        Banned,
        Incompatible
    }

    public enum CommunityCacheStatus
    {
        Missing,
        Cached,
        Corrupt
    }

    public enum CommunitySort
    {
        Latest,
        Popular
    }

    public enum CommunityOperationOutcome
    {
        Succeeded,
        Failed,
        Canceled,
        OutcomeUnknown,
        NeedsAgreement,
        RecoveryRequired
    }

    public enum CommunityErrorCode
    {
        None,
        Offline,
        NotLoggedIn,
        NotFound,
        Private,
        Deleted,
        Banned,
        Incompatible,
        RateLimited,
        QuotaExceeded,
        LegalAgreementRequired,
        InvalidContent,
        CorruptCache,
        Conflict,
        StorageFailure,
        RecoveryRequired,
        Unknown
    }

    public enum CommunityProgressStage
    {
        Querying,
        Downloading,
        Validating,
        Caching,
        CreatingItem,
        Uploading,
        SettingVisibility
    }

    /// <summary>Stable remote identity. Account identity is deliberately stored separately.</summary>
    [Serializable]
    public sealed class CommunityItemKey : IEquatable<CommunityItemKey>
    {
        public uint ConsumerAppId { get; }
        public ulong RemoteItemId { get; }

        public CommunityItemKey(uint consumerAppId, ulong remoteItemId)
        {
            if (consumerAppId == 0) throw new ArgumentOutOfRangeException(nameof(consumerAppId));
            if (remoteItemId == 0) throw new ArgumentOutOfRangeException(nameof(remoteItemId));
            ConsumerAppId = consumerAppId;
            RemoteItemId = remoteItemId;
        }

        public bool Equals(CommunityItemKey other) => other != null
            && ConsumerAppId == other.ConsumerAppId && RemoteItemId == other.RemoteItemId;
        public override bool Equals(object obj) => Equals(obj as CommunityItemKey);
        public override int GetHashCode() => (ConsumerAppId.GetHashCode() * 397) ^ RemoteItemId.GetHashCode();
        public override string ToString() => ConsumerAppId + ":" + RemoteItemId;
    }

    [Serializable]
    public sealed class CommunityContentVersion : IEquatable<CommunityContentVersion>
    {
        public int SchemaVersion { get; }
        public string ContentHash { get; }

        public CommunityContentVersion(int schemaVersion, string contentHash)
        {
            if (schemaVersion <= 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            if (string.IsNullOrWhiteSpace(contentHash)) throw new ArgumentException("A content hash is required.", nameof(contentHash));
            SchemaVersion = schemaVersion;
            ContentHash = contentHash.ToLowerInvariant();
        }

        public bool Equals(CommunityContentVersion other) => other != null
            && SchemaVersion == other.SchemaVersion
            && string.Equals(ContentHash, other.ContentHash, StringComparison.Ordinal);
        public override bool Equals(object obj) => Equals(obj as CommunityContentVersion);
        public override int GetHashCode() => (SchemaVersion * 397) ^ StringComparer.Ordinal.GetHashCode(ContentHash);
    }

    [Serializable]
    public sealed class CommunityAccountScope : IEquatable<CommunityAccountScope>
    {
        public uint ConsumerAppId { get; }
        public ulong AccountId { get; }

        public CommunityAccountScope(uint consumerAppId, ulong accountId)
        {
            if (consumerAppId == 0) throw new ArgumentOutOfRangeException(nameof(consumerAppId));
            if (accountId == 0) throw new ArgumentOutOfRangeException(nameof(accountId));
            ConsumerAppId = consumerAppId;
            AccountId = accountId;
        }

        public bool Equals(CommunityAccountScope other) => other != null
            && ConsumerAppId == other.ConsumerAppId && AccountId == other.AccountId;
        public override bool Equals(object obj) => Equals(obj as CommunityAccountScope);
        public override int GetHashCode() => (ConsumerAppId.GetHashCode() * 397) ^ AccountId.GetHashCode();
    }

    public sealed class CommunityServiceState
    {
        public CommunityAvailability Availability { get; }
        public uint CurrentAppId { get; }
        public ulong CurrentAccountId { get; }
        public long Generation { get; }
        public string LastError { get; }

        public CommunityServiceState(CommunityAvailability availability, uint currentAppId,
            ulong currentAccountId, long generation, string lastError = null)
        {
            Availability = availability;
            CurrentAppId = currentAppId;
            CurrentAccountId = currentAccountId;
            Generation = generation;
            LastError = lastError ?? string.Empty;
        }
    }

    public sealed class CommunityError
    {
        public CommunityErrorCode Code { get; }
        public bool IsRetryable { get; }
        public string Message { get; }

        public CommunityError(CommunityErrorCode code, string message, bool isRetryable = false)
        {
            Code = code;
            Message = message ?? string.Empty;
            IsRetryable = isRetryable;
        }
    }

    /// <summary>Result distinguishes an unknown remote outcome from a safe retry or cancellation.</summary>
    public sealed class CommunityResult<T>
    {
        public CommunityOperationOutcome Outcome { get; }
        public T Value { get; }
        public CommunityError Error { get; }
        public string OperationId { get; }
        public bool Succeeded => Outcome == CommunityOperationOutcome.Succeeded;

        private CommunityResult(CommunityOperationOutcome outcome, T value, CommunityError error, string operationId)
        {
            Outcome = outcome;
            Value = value;
            Error = error;
            OperationId = operationId ?? string.Empty;
        }

        public static CommunityResult<T> Success(T value, string operationId = null) =>
            new CommunityResult<T>(CommunityOperationOutcome.Succeeded, value, null, operationId);

        public static CommunityResult<T> Failure(CommunityError error, string operationId = null) =>
            new CommunityResult<T>(CommunityOperationOutcome.Failed, default(T),
                error ?? new CommunityError(CommunityErrorCode.Unknown, "The operation failed."), operationId);

        public static CommunityResult<T> Canceled(string operationId = null) =>
            new CommunityResult<T>(CommunityOperationOutcome.Canceled, default(T), null, operationId);

        public static CommunityResult<T> Unknown(CommunityError error, string operationId) =>
            new CommunityResult<T>(CommunityOperationOutcome.OutcomeUnknown, default(T), error, operationId);

        public static CommunityResult<T> Unknown(T value, CommunityError error, string operationId) =>
            new CommunityResult<T>(CommunityOperationOutcome.OutcomeUnknown, value, error, operationId);

        public static CommunityResult<T> NeedsAgreement(T value, string operationId = null) =>
            new CommunityResult<T>(CommunityOperationOutcome.NeedsAgreement, value,
                new CommunityError(CommunityErrorCode.LegalAgreementRequired,
                    "Accept the Workshop legal agreement before submitting this item."), operationId);

        public static CommunityResult<T> RecoveryRequired(T value, CommunityError error, string operationId) =>
            new CommunityResult<T>(CommunityOperationOutcome.RecoveryRequired, value,
                error ?? new CommunityError(CommunityErrorCode.RecoveryRequired,
                    "The remote operation needs reconciliation before it can be retried."), operationId);
    }

    public sealed class CommunityTransferProgress
    {
        public CommunityProgressStage Stage { get; }
        public float? Fraction { get; }
        public string Message { get; }

        public CommunityTransferProgress(CommunityProgressStage stage, float? fraction = null, string message = null)
        {
            Stage = stage;
            Fraction = fraction.HasValue ? (float?)Math.Max(0f, Math.Min(1f, fraction.Value)) : null;
            Message = message ?? string.Empty;
        }
    }

    public sealed class CommunityQuery
    {
        public string SearchText { get; set; } = string.Empty;
        public LanguageMode? Language { get; set; }
        public CommunitySort Sort { get; set; } = CommunitySort.Latest;
        public string ContinuationToken { get; set; } = string.Empty;
    }

    public sealed class CommunityPage
    {
        public IReadOnlyList<CommunitySummary> Items { get; }
        public string ContinuationToken { get; }
        public bool HasMore { get; }

        public CommunityPage(IEnumerable<CommunitySummary> items, string continuationToken, bool hasMore)
        {
            Items = new List<CommunitySummary>(items ?? Array.Empty<CommunitySummary>()).AsReadOnly();
            ContinuationToken = continuationToken ?? string.Empty;
            HasMore = hasMore;
        }
    }

    public class CommunitySummary
    {
        public CommunityItemKey Key { get; }
        public string Title { get; }
        public string Author { get; }
        public string Description { get; }
        public LanguageMode Language { get; }
        public int WordCount { get; }
        public int LikeCount { get; }
        public bool IsLiked { get; }
        public bool IsDownloaded { get; }
        public CommunityRemoteStatus RemoteStatus { get; }

        public CommunitySummary(CommunityItemKey key, string title, string author, string description,
            LanguageMode language, int wordCount, int likeCount, bool isLiked, bool isDownloaded,
            CommunityRemoteStatus remoteStatus)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Title = title ?? string.Empty;
            Author = author ?? string.Empty;
            Description = description ?? string.Empty;
            Language = language;
            WordCount = Math.Max(0, wordCount);
            LikeCount = Math.Max(0, likeCount);
            IsLiked = isLiked;
            IsDownloaded = isDownloaded;
            RemoteStatus = remoteStatus;
        }
    }

    public sealed class CommunityDetails : CommunitySummary
    {
        public Uri CommunityPageUri { get; }
        public ulong OwnerAccountId { get; }
        public string ContentHash { get; }

        public CommunityDetails(CommunityItemKey key, string title, string author, string description,
            LanguageMode language, int wordCount, int likeCount, bool isLiked, bool isDownloaded,
            CommunityRemoteStatus remoteStatus, Uri communityPageUri, ulong ownerAccountId = 0,
            string contentHash = null)
            : base(key, title, author, description, language, wordCount, likeCount, isLiked,
                isDownloaded, remoteStatus)
        {
            CommunityPageUri = communityPageUri;
            OwnerAccountId = ownerAccountId;
            ContentHash = contentHash ?? string.Empty;
        }
    }

    public sealed class CommunityVoteState
    {
        public bool IsUpvoted { get; }
        public int LikeCount { get; }
        public CommunityVoteState(bool isUpvoted, int likeCount)
        {
            IsUpvoted = isUpvoted;
            LikeCount = Math.Max(0, likeCount);
        }
    }

    public sealed class CommunityRunSummary
    {
        public float ElapsedSeconds { get; }
        public int Errors { get; }
        public int Stars { get; }
        public DateTime CompletedAtUtc { get; }

        public CommunityRunSummary(float elapsedSeconds, int errors, int stars, DateTime completedAtUtc)
        {
            ElapsedSeconds = elapsedSeconds;
            Errors = errors;
            Stars = stars;
            CompletedAtUtc = DateTime.SpecifyKind(completedAtUtc, DateTimeKind.Utc);
        }
    }

    public sealed class CommunityPersonalRecord
    {
        public CommunityItemKey Key { get; }
        public string ContentHash { get; }
        public CommunityRunSummary Latest { get; }
        public CommunityRunSummary Best { get; }
        public int HighestStars { get; }
        public int PlayCount { get; }
        public DateTime LastPlayedAtUtc { get; }
        public IReadOnlyList<CommunityRunSummary> History { get; }

        public CommunityPersonalRecord(CommunityItemKey key, string contentHash,
            CommunityRunSummary latest, CommunityRunSummary best, int highestStars,
            int playCount, DateTime lastPlayedAtUtc, IEnumerable<CommunityRunSummary> history)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            ContentHash = contentHash ?? string.Empty;
            Latest = latest;
            Best = best;
            HighestStars = Math.Max(0, Math.Min(3, highestStars));
            PlayCount = Math.Max(0, playCount);
            LastPlayedAtUtc = DateTime.SpecifyKind(lastPlayedAtUtc, DateTimeKind.Utc);
            History = new List<CommunityRunSummary>(history ?? Array.Empty<CommunityRunSummary>()).AsReadOnly();
        }
    }

    /// <summary>Validated, app-owned cached snapshot; its provider never exposes editable storage.</summary>
    public sealed class CommunityCacheEntry
    {
        public CommunityItemKey Key { get; }
        public CommunityContentVersion Version { get; }
        public string Title { get; }
        public string Author { get; }
        public DateTime CachedAtUtc { get; }
        public CommunityWordListProvider Provider { get; }

        public CommunityCacheEntry(CommunityItemKey key, CommunityContentVersion version,
            string title, string author, DateTime cachedAtUtc, CommunityWordListProvider provider)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Title = title ?? string.Empty;
            Author = author ?? string.Empty;
            CachedAtUtc = DateTime.SpecifyKind(cachedAtUtc, DateTimeKind.Utc);
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }
    }

    public sealed class CommunityPublishRequest
    {
        public string LocalListId { get; }
        public string Title { get; }
        public string Description { get; }
        public IReadOnlyList<string> Tags { get; }
        public WordListLibraryContent Content { get; }

        public CommunityPublishRequest(string localListId, string title, string description,
            IEnumerable<string> tags, WordListLibraryContent content)
        {
            if (string.IsNullOrWhiteSpace(localListId)) throw new ArgumentException("A local list ID is required.", nameof(localListId));
            LocalListId = localListId;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            Tags = new List<string>(tags ?? Array.Empty<string>()).AsReadOnly();
            Content = content == null ? null : content.Clone();
        }
    }

    public sealed class CommunityPublication
    {
        public string LocalListId { get; }
        public string Title { get; }
        public string ContentHash { get; }
        public string PublicationFingerprint { get; }
        public string OperationId { get; }
        public ulong? ActiveRemoteItemId { get; }
        public IReadOnlyList<ulong> PreviousRemoteItemIds { get; }
        public CommunityPublicationStatus Status { get; }
        public CommunityPublicationStatus RecoveryFromStatus { get; }
        public bool NeedsLegalAgreement { get; }
        public string LastError { get; }

        public CommunityPublication(string localListId, ulong? activeRemoteItemId,
            IEnumerable<ulong> previousRemoteItemIds, CommunityPublicationStatus status,
            bool needsLegalAgreement = false, string lastError = null, string contentHash = null,
            string title = null, string publicationFingerprint = null, string operationId = null,
            CommunityPublicationStatus recoveryFromStatus = CommunityPublicationStatus.Unpublished)
        {
            LocalListId = localListId ?? string.Empty;
            Title = title ?? string.Empty;
            ContentHash = contentHash ?? string.Empty;
            PublicationFingerprint = publicationFingerprint ?? string.Empty;
            OperationId = operationId ?? string.Empty;
            ActiveRemoteItemId = activeRemoteItemId;
            PreviousRemoteItemIds = new List<ulong>(previousRemoteItemIds ?? Array.Empty<ulong>()).AsReadOnly();
            Status = status;
            RecoveryFromStatus = recoveryFromStatus;
            NeedsLegalAgreement = needsLegalAgreement;
            LastError = lastError ?? string.Empty;
        }
    }

    public enum CommunityPublicationStatus
    {
        Unpublished,
        Creating,
        NeedsAgreement,
        Created,
        Submitting,
        Published,
        Unpublishing,
        Failed,
        RecoveryRequired
    }

    /// <summary>High-level service owns Steam intake, content validation, and cache commit.</summary>
    public interface ICommunityService
    {
        CommunityServiceState State { get; }
        uint CurrentAppId { get; }
        ulong CurrentAccountId { get; }
        ulong EffectiveLocalAccountId { get; }
        bool IsRemoteAuthenticated { get; }
        event Action<CommunityServiceState> AvailabilityChanged;

        IReadOnlyList<CommunityCacheEntry> GetCachedEntries();
        bool TryGetCachedEntry(CommunityItemKey key, out CommunityCacheEntry entry);
        IReadOnlyList<CommunityItemKey> GetHiddenItems();
        IReadOnlyList<CommunityPublication> GetPublications();
        bool TryGetPublication(string localListId, out CommunityPublication publication);
        bool IsLocalListPublicationLocked(string localListId);
        CommunityResult<bool> SetHiddenLocally(CommunityItemKey key, bool isHidden);
        bool TryGetHiddenLocally(CommunityItemKey key, out bool isHidden, out CommunityError error);
        bool TryRecordRun(CommunityItemKey key, string contentHash, ulong accountId,
            float elapsedSeconds, int errors, int stars, out CommunityPersonalRecord record, out CommunityError error);
        bool TryGetRecord(CommunityItemKey key, string contentHash, ulong accountId,
            out CommunityPersonalRecord record, out CommunityError error);

        Task<CommunityResult<CommunityPage>> QueryAsync(CommunityQuery query, CancellationToken cancellationToken);
        Task<CommunityResult<CommunityDetails>> GetDetailsAsync(ulong remoteItemId, CancellationToken cancellationToken);
        Task<CommunityResult<CommunityCacheEntry>> DownloadAsync(ulong remoteItemId,
            IProgress<CommunityTransferProgress> progress, CancellationToken cancellationToken);
        Task<CommunityResult<CommunityVoteState>> VoteUpAsync(ulong remoteItemId, CancellationToken cancellationToken);
        Task<CommunityResult<CommunityPage>> QueryOwnedAsync(string continuationToken, CancellationToken cancellationToken);
        Task<CommunityResult<CommunityPublication>> CreatePublicationAsync(CommunityPublishRequest request,
            CancellationToken cancellationToken);
        Task<CommunityResult<CommunityPublication>> SubmitPublicationAsync(string localListId,
            IProgress<CommunityTransferProgress> progress, CancellationToken cancellationToken);
        Task<CommunityResult<CommunityPublication>> UnpublishAsync(ulong remoteItemId,
            CancellationToken cancellationToken);
        Task<CommunityResult<CommunityPublication>> ReconcilePublicationAsync(string localListId,
            CancellationToken cancellationToken);
    }
}
