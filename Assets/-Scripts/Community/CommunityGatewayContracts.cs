using System;
using System.Threading;
using System.Threading.Tasks;

namespace Words.Community
{
    /// <summary>Platform-neutral result of reading a completed platform download.</summary>
    public sealed class CommunityDownloadReceipt
    {
        private readonly byte[] payload;
        public CommunityItemKey Key { get; }
        public int SchemaVersion { get; }
        public string ExpectedContentHash { get; }
        public string Title { get; }
        public string Author { get; }
        public byte[] CopyPayload() => (byte[])payload.Clone();

        public CommunityDownloadReceipt(CommunityItemKey key, int schemaVersion,
            string expectedContentHash, string title, string author, byte[] payload)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            if (schemaVersion <= 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            SchemaVersion = schemaVersion;
            ExpectedContentHash = expectedContentHash ?? string.Empty;
            Title = title ?? string.Empty;
            Author = author ?? string.Empty;
            this.payload = payload != null ? (byte[])payload.Clone() : Array.Empty<byte>();
        }
    }

    /// <summary>The remote identity returned by create; the orchestrator journals it before exposing it.</summary>
    public sealed class CommunityCreateReceipt
    {
        public CommunityItemKey Key { get; }
        public bool NeedsLegalAgreement { get; }

        public CommunityCreateReceipt(CommunityItemKey key, bool needsLegalAgreement)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            NeedsLegalAgreement = needsLegalAgreement;
        }
    }

    public enum CommunityVisibility
    {
        Public,
        Private
    }

    public sealed class CommunityRemoteItemState
    {
        public CommunityItemKey Key { get; }
        public CommunityRemoteStatus Status { get; }
        public CommunityVisibility Visibility { get; }
        public CommunityRemoteItemState(CommunityItemKey key, CommunityRemoteStatus status,
            CommunityVisibility visibility)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Status = status;
            Visibility = visibility;
        }
    }

    /// <summary>Upload data prepared by the service after canonical content validation.</summary>
    public sealed class CommunitySubmitRequest
    {
        private readonly byte[] payload;
        public CommunityItemKey Key { get; }
        public string Title { get; }
        public string Description { get; }
        public System.Collections.Generic.IReadOnlyList<string> Tags { get; }
        public int SchemaVersion { get; }
        public string ContentHash { get; }
        public string MetadataJson { get; }
        public byte[] CopyPayload() => (byte[])payload.Clone();

        public CommunitySubmitRequest(CommunityItemKey key, string title, string description,
            System.Collections.Generic.IEnumerable<string> tags, int schemaVersion,
            string contentHash, byte[] payload, string metadataJson = null)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            Tags = new System.Collections.Generic.List<string>(tags ?? Array.Empty<string>()).AsReadOnly();
            if (schemaVersion <= 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            SchemaVersion = schemaVersion;
            ContentHash = contentHash ?? string.Empty;
            MetadataJson = metadataJson ?? string.Empty;
            this.payload = payload != null ? (byte[])payload.Clone() : Array.Empty<byte>();
        }
    }

    /// <summary>
    /// Low-level platform adapter. DownloadAsync must return payload bytes, not a Steam install path.
    /// Cancellation is honored before a native call; an already-submitted native operation may finish
    /// after its caller detaches and must still report its terminal result.
    /// </summary>
    public interface ICommunityGateway
    {
        CommunityServiceState State { get; }
        event Action<CommunityServiceState> AvailabilityChanged;

        Task<CommunityResult<CommunityPage>> QueryAsync(CommunityQuery query, CancellationToken cancellationToken);
        Task<CommunityResult<CommunityDetails>> GetDetailsAsync(CommunityItemKey key, CancellationToken cancellationToken);
        Task<CommunityResult<CommunityDownloadReceipt>> DownloadAsync(CommunityItemKey key,
            IProgress<CommunityTransferProgress> progress, CancellationToken cancellationToken);
        Task<CommunityResult<CommunityVoteState>> VoteUpAsync(CommunityItemKey key, CancellationToken cancellationToken);
        Task<CommunityResult<CommunityPage>> QueryOwnedAsync(uint consumerAppId, string continuationToken,
            CancellationToken cancellationToken);
        Task<CommunityResult<CommunityCreateReceipt>> CreatePublicationAsync(uint consumerAppId,
            CancellationToken cancellationToken);
        Task<CommunityResult<CommunityRemoteItemState>> SubmitPublicationAsync(CommunitySubmitRequest request,
            IProgress<CommunityTransferProgress> progress, CancellationToken cancellationToken);
        Task<CommunityResult<CommunityRemoteItemState>> SetVisibilityAsync(CommunityItemKey key,
            CommunityVisibility visibility, CancellationToken cancellationToken);
    }
}
