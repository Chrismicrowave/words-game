using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Words.UiMockups.WordListHub
{
    /// <summary>
    /// Optional async Community capability. The UI assembly owns this contract so
    /// it does not depend on Steamworks or the Community service assembly.
    /// </summary>
    public interface IWordListHubCommunityAsync
    {
        event Action Changed;
        event Action BecameAvailable;
        event Action<MockupCommunityProgress> ProgressChanged;
        IReadOnlyList<MockupDiscoveryItem> Snapshot { get; }
        IReadOnlyList<MockupWordList> DownloadedItems { get; }
        IReadOnlyList<MockupCommunityPublicationInfo> Publications { get; }
        string ConnectionMessage { get; }
        string ContinuationToken { get; }
        bool HasMore { get; }
        bool IsRemoteAuthenticated { get; }
        bool HasHiddenItems { get; }
        bool IsLocalListPublicationLocked(string localListId);
        MockupCommunityProgress CurrentProgress { get; }
        bool TryGetDownloadedContentForCopy(string communityProjectionId,
            out WordListLibraryContent content, out string title, out string error);

        Task<MockupCommunityQueryResult> QueryAsync(
            MockupDiscoveryQuery query, string continuationToken,
            CancellationToken cancellationToken);
        Task<MockupActionResult> LikeAsync(string remoteItemId, CancellationToken cancellationToken);
        Task<MockupActionResult> DownloadAsync(string remoteItemId, CancellationToken cancellationToken);
        Task<MockupActionResult> HideLocallyAsync(string remoteItemId, CancellationToken cancellationToken);
        Task<MockupActionResult> RestoreHiddenItemsAsync(CancellationToken cancellationToken);
        Task<MockupCommunityPublicationResult> PublishAsync(string localListId,
            CancellationToken cancellationToken);
        Task<MockupCommunityPublicationResult> UnpublishAsync(string localListId,
            CancellationToken cancellationToken);
        Task<MockupCommunityPublicationResult> ReconcilePublicationAsync(string localListId,
            CancellationToken cancellationToken);
        Task<MockupCommunityPageResult> GetItemPageAsync(string remoteItemId,
            CancellationToken cancellationToken);
    }

    public enum MockupCommunityPublicationState
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

    public sealed class MockupCommunityPublicationInfo
    {
        public string LocalListId { get; }
        public string Title { get; }
        public string RemoteItemId { get; }
        public string ContentHash { get; }
        public MockupCommunityPublicationState State { get; }
        public bool NeedsLegalAgreement { get; }
        public string LastError { get; }

        public MockupCommunityPublicationInfo(string localListId, string title,
            string remoteItemId, string contentHash, MockupCommunityPublicationState state,
            bool needsLegalAgreement, string lastError)
        {
            LocalListId = localListId ?? string.Empty;
            Title = title ?? string.Empty;
            RemoteItemId = remoteItemId ?? string.Empty;
            ContentHash = contentHash ?? string.Empty;
            State = state;
            NeedsLegalAgreement = needsLegalAgreement;
            LastError = lastError ?? string.Empty;
        }
    }

    public sealed class MockupCommunityPublicationResult
    {
        public bool Succeeded { get; }
        public bool NeedsLegalAgreement { get; }
        public bool NeedsRecovery { get; }
        public MockupCommunityPublicationInfo Publication { get; }
        public string Message { get; }

        public MockupCommunityPublicationResult(bool succeeded, bool needsLegalAgreement,
            bool needsRecovery, MockupCommunityPublicationInfo publication, string message)
        {
            Succeeded = succeeded;
            NeedsLegalAgreement = needsLegalAgreement;
            NeedsRecovery = needsRecovery;
            Publication = publication;
            Message = message ?? string.Empty;
        }
    }

    public sealed class MockupCommunityPageResult
    {
        public bool Succeeded { get; }
        public string Uri { get; }
        public string Message { get; }

        public MockupCommunityPageResult(bool succeeded, string uri, string message)
        {
            Succeeded = succeeded;
            Uri = uri ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }

    public sealed class MockupCommunityProgress
    {
        public string Stage { get; }
        public float? Fraction { get; }
        public string Message { get; }

        public MockupCommunityProgress(string stage, float? fraction, string message)
        {
            Stage = stage ?? string.Empty;
            Fraction = fraction;
            Message = message ?? string.Empty;
        }
    }

    /// <summary>Result for a page request, projected without service/SDK types.</summary>
    public sealed class MockupCommunityQueryResult
    {
        public bool Succeeded { get; }
        public bool HasMore { get; }
        public string ContinuationToken { get; }
        public string Message { get; }

        public MockupCommunityQueryResult(bool succeeded, bool hasMore,
            string continuationToken, string message)
        {
            Succeeded = succeeded;
            HasMore = hasMore;
            ContinuationToken = continuationToken ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }

    /// <summary>Implemented by production backends that supply live Community data.</summary>
    public interface IWordListHubCommunityBackend
    {
        IWordListHubCommunityAsync CommunityAsync { get; }
    }
}
