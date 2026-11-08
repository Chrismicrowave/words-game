using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Steamworks;
using UnityEngine;
using Words.Community;

internal sealed partial class SteamCommunityGateway
{
    private readonly ManagedDownloadFlightTracker<PendingDownload> downloadFlights =
        new ManagedDownloadFlightTracker<PendingDownload>();
    private Callback<DownloadItemResult_t> downloadItemCallback;
    private SessionSnapshot downloadCallbackSession;

    public async Task<CommunityResult<CommunityDownloadReceipt>> DownloadAsync(CommunityItemKey key,
        IProgress<CommunityTransferProgress> progress, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return CommunityResult<CommunityDownloadReceipt>.Canceled();
        if (!TryCaptureSession(out SessionSnapshot session, out CommunityError error))
            return CommunityResult<CommunityDownloadReceipt>.Failure(error);
        if (!IsSupportedKey(key))
            return Fail<CommunityDownloadReceipt>(CommunityErrorCode.InvalidContent,
                "The Workshop item key is invalid or belongs to another app.");

        CommunityResult<NativeItem> itemResult = await QueryItemAsync(key, session);
        if (!itemResult.Succeeded)
            return CommunityResult<CommunityDownloadReceipt>.Failure(itemResult.Error);
        NativeItem item = itemResult.Value;
        if (item.RemoteStatus != CommunityRemoteStatus.Available || item.Metadata == null)
            return Fail<CommunityDownloadReceipt>(MapRemoteError(item.RemoteStatus),
                "The Workshop item is not available for download.");
        if (cancellationToken.IsCancellationRequested)
            return CommunityResult<CommunityDownloadReceipt>.Canceled();

        PendingDownload pending = null;
        bool mayBeInFlight = false;
        try
        {
            EItemState itemState = (EItemState)SteamUGC.GetItemState(new PublishedFileId_t(key.RemoteItemId));
            if ((itemState & EItemState.k_EItemStateInstalled) != 0
                && (itemState & EItemState.k_EItemStateNeedsUpdate) == 0)
                return ReadInstalledReceipt(item, session);

            if (downloadFlights.Contains(ExpectedAppId, key.RemoteItemId, session.AccountId,
                session.Generation, session.Session))
                return Unknown<CommunityDownloadReceipt>(
                    "A previous download for this item has no terminal callback yet; retry after it resolves.");

            EnsureDownloadCallback(session);
            pending = new PendingDownload(this, item, session, progress, unityContext);
            if (!downloadFlights.TryBegin(ExpectedAppId, key.RemoteItemId, session.AccountId,
                session.Generation, session.Session, pending))
            {
                pending.DisposeTimer();
                return Unknown<CommunityDownloadReceipt>(
                    "A previous download for this item has no terminal callback yet; retry after it resolves.");
            }
            mayBeInFlight = true;
            bool started = SteamUGC.DownloadItem(new PublishedFileId_t(key.RemoteItemId), false);
            if (!started)
            {
                mayBeInFlight = false;
                downloadFlights.TryRemove(ExpectedAppId, key.RemoteItemId, session.AccountId,
                    session.Generation, session.Session, out _);
                pending.DisposeTimer();
                return Fail<CommunityDownloadReceipt>(CommunityErrorCode.Offline,
                    "Steam could not start the item download. The item was not automatically subscribed.");
            }

            Report(progress, CommunityProgressStage.Downloading, null, "Downloading from Steam Workshop.");
            return await pending.Task;
        }
        catch (Exception exception)
        {
            if (pending != null)
            {
                if (mayBeInFlight)
                {
                    downloadFlights.TryMarkTimedOut(ExpectedAppId, key.RemoteItemId, session.AccountId,
                        session.Generation, session.Session, out _);
                    pending.Complete(Unknown<CommunityDownloadReceipt>(
                        "Steam may have started the download but its result was lost; wait for the callback before retrying."));
                    return Unknown<CommunityDownloadReceipt>(
                        "Steam may have started the download but its result was lost; wait for the callback before retrying.");
                }
                downloadFlights.TryRemove(ExpectedAppId, key.RemoteItemId, session.AccountId,
                    session.Generation, session.Session, out _);
                pending.Complete(Fail<CommunityDownloadReceipt>(CommunityErrorCode.Offline,
                    "Steam could not continue the Workshop download."));
            }
            return Fail<CommunityDownloadReceipt>(CommunityErrorCode.Offline,
                "Steam could not start or read the Workshop download: " + exception.Message);
        }
    }

    public async Task<CommunityResult<CommunityVoteState>> VoteUpAsync(CommunityItemKey key,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return CommunityResult<CommunityVoteState>.Canceled();
        if (!TryCaptureSession(out SessionSnapshot session, out CommunityError error))
            return CommunityResult<CommunityVoteState>.Failure(error);
        if (!IsSupportedKey(key))
            return Fail<CommunityVoteState>(CommunityErrorCode.InvalidContent,
                "The Workshop item key is invalid or belongs to another app.");

        SteamAPICall_t handle = SteamUGC.SetUserItemVote(new PublishedFileId_t(key.RemoteItemId), true);
        CommunityResult<bool> vote = await StartCall<SetUserItemVoteResult_t, bool>(handle,
            session, true, callback =>
            {
                if (callback.m_nPublishedFileId.m_PublishedFileId != key.RemoteItemId)
                    return Fail<bool>(CommunityErrorCode.Unknown, "Steam returned a vote for a different item.");
                if (callback.m_eResult != EResult.k_EResultOK)
                    return FailureFromSteam<bool>(callback.m_eResult, remoteMutation: true);
                if (!callback.m_bVoteUp)
                    return Fail<bool>(CommunityErrorCode.Unknown, "Steam did not confirm the requested upvote.");
                return CommunityResult<bool>.Success(true);
            }, null);
        if (vote.Outcome != CommunityOperationOutcome.Succeeded)
            return ConvertResult<bool, CommunityVoteState>(vote);

        CommunityResult<NativeItem> details = await QueryItemAsync(key, session);
        if (!details.Succeeded)
            return CommunityResult<CommunityVoteState>.Failure(details.Error);
        return CommunityResult<CommunityVoteState>.Success(
            new CommunityVoteState(true, details.Value.LikeCount));
    }

    private Task<CommunityResult<bool>> ReadUserVoteAsync(CommunityItemKey key, SessionSnapshot session)
    {
        SteamAPICall_t handle = SteamUGC.GetUserItemVote(new PublishedFileId_t(key.RemoteItemId));
        return StartCall<GetUserItemVoteResult_t, bool>(handle,
            session, false, callback =>
            {
                if (callback.m_nPublishedFileId.m_PublishedFileId != key.RemoteItemId)
                    return Fail<bool>(CommunityErrorCode.Unknown, "Steam returned vote state for a different item.");
                if (callback.m_eResult != EResult.k_EResultOK)
                    return FailureFromSteam<bool>(callback.m_eResult);
                return CommunityResult<bool>.Success(callback.m_bVotedUp);
            }, null);
    }

    private void EnsureDownloadCallback(SessionSnapshot session)
    {
        if (downloadItemCallback != null && SameSession(downloadCallbackSession, session))
            return;
        downloadItemCallback?.Dispose();
        downloadCallbackSession = session;
        downloadItemCallback = Callback<DownloadItemResult_t>.Create(
            callback => OnDownloadItemResult(session, callback));
    }

    private void OnDownloadItemResult(SessionSnapshot callbackSession, DownloadItemResult_t callback)
    {
        ulong remoteId = callback.m_nPublishedFileId.m_PublishedFileId;
        if (!downloadFlights.TryComplete(ExpectedAppId, remoteId, callbackSession.AccountId,
            callbackSession.Generation, callbackSession.Session, out PendingDownload pending,
            out bool wasTimedOut))
            return;
        if (wasTimedOut)
            return;
        if (callback.m_unAppID.m_AppId != ExpectedAppId)
        {
            pending.Complete(Fail<CommunityDownloadReceipt>(CommunityErrorCode.Incompatible,
                "Steam completed a download under a different app ID."));
            return;
        }
        if (!IsSessionCurrent(callbackSession) || !SameSession(pending.Session, callbackSession))
        {
            pending.Complete(Fail<CommunityDownloadReceipt>(CommunityErrorCode.Offline,
                "The Steam account or session changed during the download."));
            return;
        }
        if (callback.m_eResult != EResult.k_EResultOK)
        {
            pending.Complete(FailureFromSteam<CommunityDownloadReceipt>(callback.m_eResult));
            return;
        }

        pending.Complete(ReadInstalledReceipt(pending.Item, pending.Session));
    }

    private void FailStaleDownloads()
    {
        ISteamClientSession session = SteamClientSessionRegistry.Current;
        uint appId = session != null && session.IsInitialized ? session.CurrentAppId : 0;
        ulong accountId = session != null && session.IsInitialized ? session.CurrentAccountId : 0;
        long generation = session != null ? session.InitializationGeneration : 0;
        List<PendingDownload> stale = downloadFlights.RemoveExcept(
            appId, accountId, generation, session);
        foreach (PendingDownload pending in stale)
            pending.Complete(Fail<CommunityDownloadReceipt>(CommunityErrorCode.Offline,
                "The Steam account or session changed during the download."));

        if (downloadItemCallback != null && !IsSessionCurrent(downloadCallbackSession))
        {
            downloadItemCallback.Dispose();
            downloadItemCallback = null;
            downloadCallbackSession = default;
        }
    }

    private void FinishPendingDownloadTimeout(PendingDownload pending)
    {
        if (downloadFlights.TryMarkTimedOut(ExpectedAppId, pending.Item.Key.RemoteItemId,
            pending.Session.AccountId, pending.Session.Generation, pending.Session.Session,
            out PendingDownload current) && ReferenceEquals(current, pending))
            pending.Complete(Unknown<CommunityDownloadReceipt>(
                "Steam Workshop download did not finish before timeout; its callback is still pending, so retry is blocked."));
    }

    private static bool SameSession(SessionSnapshot left, SessionSnapshot right) =>
        ReferenceEquals(left.Session, right.Session) && left.AppId == right.AppId
        && left.AccountId == right.AccountId && left.Generation == right.Generation;

    private CommunityResult<CommunityDownloadReceipt> ReadInstalledReceipt(
        NativeItem item, SessionSnapshot session)
    {
        if (!IsSessionCurrent(session))
            return Fail<CommunityDownloadReceipt>(CommunityErrorCode.Offline,
                "The Steam account or session changed before the download could be read.");
        if (item.Metadata == null)
            return Fail<CommunityDownloadReceipt>(CommunityErrorCode.Incompatible,
                "The Workshop item has no supported metadata.");

        if (!SteamUGC.GetItemInstallInfo(new PublishedFileId_t(item.Key.RemoteItemId),
            out _, out string folder, 4096, out _))
            return Fail<CommunityDownloadReceipt>(CommunityErrorCode.NotFound,
                "Steam did not provide an installed content folder.");

        try
        {
            string fullFolder = Path.GetFullPath(folder);
            string payloadPath = Path.GetFullPath(Path.Combine(fullFolder, CommunityContentCodec.PayloadFileName));
            string folderPrefix = fullFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!payloadPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                return Fail<CommunityDownloadReceipt>(CommunityErrorCode.InvalidContent,
                    "The Workshop payload path escaped Steam's install folder.");

            var folderInfo = new DirectoryInfo(fullFolder);
            var payloadInfo = new FileInfo(payloadPath);
            if (!folderInfo.Exists || (folderInfo.Attributes & FileAttributes.ReparsePoint) != 0
                || !payloadInfo.Exists || (payloadInfo.Attributes & FileAttributes.ReparsePoint) != 0
                || payloadInfo.Length <= 0 || payloadInfo.Length > limits.MaxPayloadBytes)
                return Fail<CommunityDownloadReceipt>(CommunityErrorCode.InvalidContent,
                    "The installed Workshop payload is missing or outside its size limits.");

            byte[] payload = File.ReadAllBytes(payloadPath);
            if (payload.Length == 0 || payload.Length > limits.MaxPayloadBytes)
                return Fail<CommunityDownloadReceipt>(CommunityErrorCode.InvalidContent,
                    "The downloaded payload is outside its size limits.");
            if (!CommunityContentCodec.TryDecodeAndValidate(payload, item.Metadata.SchemaVersion,
                item.Metadata.ContentHash, out _, out CommunityError validationError, limits))
                return Fail<CommunityDownloadReceipt>(CommunityErrorCode.InvalidContent,
                    validationError != null ? validationError.Message : "The downloaded payload is invalid.");

            return CommunityResult<CommunityDownloadReceipt>.Success(new CommunityDownloadReceipt(
                item.Key, item.Metadata.SchemaVersion, item.Metadata.ContentHash, item.Title,
                item.OwnerId, payload));
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
            || exception is ArgumentException || exception is NotSupportedException)
        {
            return Fail<CommunityDownloadReceipt>(CommunityErrorCode.StorageFailure,
                "The installed Workshop payload could not be read: " + exception.Message);
        }
    }

    private void DisposeDownloadCallbacks()
    {
        downloadItemCallback?.Dispose();
        downloadItemCallback = null;
        downloadCallbackSession = default;
        foreach (PendingDownload pending in downloadFlights.Clear())
            pending.Complete(Fail<CommunityDownloadReceipt>(CommunityErrorCode.Offline,
                "The Steam Workshop gateway stopped during download."));
    }

    private static CommunityErrorCode MapRemoteError(CommunityRemoteStatus status)
    {
        switch (status)
        {
            case CommunityRemoteStatus.Private: return CommunityErrorCode.Private;
            case CommunityRemoteStatus.Banned: return CommunityErrorCode.Banned;
            case CommunityRemoteStatus.Deleted: return CommunityErrorCode.NotFound;
            case CommunityRemoteStatus.Incompatible: return CommunityErrorCode.Incompatible;
            default: return CommunityErrorCode.Unknown;
        }
    }

    private static void Report(IProgress<CommunityTransferProgress> progress,
        CommunityProgressStage stage, float? fraction, string message)
    {
        try { progress?.Report(new CommunityTransferProgress(stage, fraction, message)); }
        catch { }
    }

    private static CommunityResult<TOut> ConvertResult<TIn, TOut>(CommunityResult<TIn> result)
    {
        switch (result.Outcome)
        {
            case CommunityOperationOutcome.Canceled: return CommunityResult<TOut>.Canceled(result.OperationId);
            case CommunityOperationOutcome.OutcomeUnknown: return CommunityResult<TOut>.Unknown(result.Error, result.OperationId);
            case CommunityOperationOutcome.NeedsAgreement: return CommunityResult<TOut>.NeedsAgreement(default(TOut), result.OperationId);
            case CommunityOperationOutcome.RecoveryRequired: return CommunityResult<TOut>.RecoveryRequired(default(TOut), result.Error, result.OperationId);
            default: return CommunityResult<TOut>.Failure(result.Error, result.OperationId);
        }
    }

    private sealed class PendingDownload
    {
        private readonly SteamCommunityGateway owner;
        private readonly IProgress<CommunityTransferProgress> progress;
        private readonly TaskCompletionSource<CommunityResult<CommunityDownloadReceipt>> completion =
            new TaskCompletionSource<CommunityResult<CommunityDownloadReceipt>>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SynchronizationContext context;
        private readonly Timer timer;
        private readonly ManagedCompletionGate completionGate = new ManagedCompletionGate();

        public readonly NativeItem Item;
        public readonly SessionSnapshot Session;
        public Task<CommunityResult<CommunityDownloadReceipt>> Task => completion.Task;

        public PendingDownload(SteamCommunityGateway owner, NativeItem item, SessionSnapshot session,
            IProgress<CommunityTransferProgress> progress, SynchronizationContext context)
        {
            this.owner = owner;
            Item = item;
            Session = session;
            this.progress = progress;
            this.context = context;
            timer = new Timer(_ => context.Post(__ => Timeout(), null), null,
                TimeSpan.FromSeconds(NativeOperationTimeoutSeconds), System.Threading.Timeout.InfiniteTimeSpan);
        }

        public void DisposeTimer() => timer.Dispose();

        public void Complete(CommunityResult<CommunityDownloadReceipt> result)
        {
            if (!completionGate.TryClaim())
                return;
            timer.Dispose();
            completion.TrySetResult(result);
            Report(progress, CommunityProgressStage.Downloading,
                result.Succeeded ? 1f : (float?)null,
                result.Succeeded ? "Workshop download completed." : result.Error?.Message);
        }

        private void Timeout() => owner.PostToUnity(() => owner.FinishPendingDownloadTimeout(this));
    }
}
