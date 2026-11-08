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
    public Task<CommunityResult<CommunityCreateReceipt>> CreatePublicationAsync(
        uint consumerAppId, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(CommunityResult<CommunityCreateReceipt>.Canceled());
        if (!TryCaptureSession(out SessionSnapshot session, out CommunityError error))
            return Task.FromResult(CommunityResult<CommunityCreateReceipt>.Failure(error));
        if (consumerAppId != ExpectedAppId)
            return Task.FromResult(Fail<CommunityCreateReceipt>(CommunityErrorCode.Incompatible,
                "The requested Workshop consumer app is unsupported."));

        SteamAPICall_t handle = SteamUGC.CreateItem(new AppId_t(consumerAppId), EWorkshopFileType.k_EWorkshopFileTypeCommunity);
        return StartCall<CreateItemResult_t, CommunityCreateReceipt>(handle, session, true,
            callback =>
            {
                if (callback.m_eResult != EResult.k_EResultOK)
                {
                    ulong returnedId = callback.m_nPublishedFileId.m_PublishedFileId;
                    if (returnedId != 0)
                    {
                        var knownReceipt = new CommunityCreateReceipt(
                            new CommunityItemKey(consumerAppId, returnedId),
                            callback.m_bUserNeedsToAcceptWorkshopLegalAgreement);
                        return CommunityResult<CommunityCreateReceipt>.Unknown(knownReceipt,
                            new CommunityError(CommunityErrorCode.RecoveryRequired,
                                "Steam returned an item ID with a non-success create result; reconcile before retrying.", true),
                            string.Empty);
                    }
                    return FailureFromSteam<CommunityCreateReceipt>(callback.m_eResult, remoteMutation: true);
                }
                ulong remoteId = callback.m_nPublishedFileId.m_PublishedFileId;
                if (remoteId == 0)
                    return Unknown<CommunityCreateReceipt>("Steam accepted item creation but returned no item ID. Reconcile before retrying.");
                var key = new CommunityItemKey(consumerAppId, remoteId);
                return CommunityResult<CommunityCreateReceipt>.Success(new CommunityCreateReceipt(
                    key, callback.m_bUserNeedsToAcceptWorkshopLegalAgreement));
            }, null);
    }

    public async Task<CommunityResult<CommunityRemoteItemState>> SubmitPublicationAsync(
        CommunitySubmitRequest request, IProgress<CommunityTransferProgress> progress,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return CommunityResult<CommunityRemoteItemState>.Canceled();
        if (!TryCaptureSession(out SessionSnapshot session, out CommunityError error))
            return CommunityResult<CommunityRemoteItemState>.Failure(error);
        if (request == null || !IsSupportedKey(request.Key))
            return Fail<CommunityRemoteItemState>(CommunityErrorCode.InvalidContent,
                "A valid Workshop publication request is required.");

        byte[] payload = request.CopyPayload();
        if (!CommunityContentCodec.TryDecodeAndValidate(payload, request.SchemaVersion,
            request.ContentHash, out CommunityDecodedContent decoded, out CommunityError validationError, limits))
            return CommunityResult<CommunityRemoteItemState>.Failure(validationError);
        if (!CommunityWorkshopMetadata.TryCreate(decoded.Content, out CommunityWorkshopMetadata metadata,
            out CommunityError metadataError, limits))
            return CommunityResult<CommunityRemoteItemState>.Failure(metadataError);
        if (!CommunityWorkshopMetadata.TryBuildTags(decoded.Content.LanguageMode,
            request.SchemaVersion, out IReadOnlyList<string> tags, out CommunityError tagError))
            return CommunityResult<CommunityRemoteItemState>.Failure(tagError);
        if (cancellationToken.IsCancellationRequested)
            return CommunityResult<CommunityRemoteItemState>.Canceled();

        string uploadDirectory = Path.Combine(Application.temporaryCachePath,
            "SteamWorkshopUpload", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(uploadDirectory);
            string payloadPath = Path.Combine(uploadDirectory, CommunityContentCodec.PayloadFileName);
            File.WriteAllBytes(payloadPath, payload);

            UGCUpdateHandle_t update = SteamUGC.StartItemUpdate(new AppId_t(request.Key.ConsumerAppId),
                new PublishedFileId_t(request.Key.RemoteItemId));
            if (update == UGCUpdateHandle_t.Invalid)
                return Fail<CommunityRemoteItemState>(CommunityErrorCode.Offline,
                    "Steam could not start the Workshop item update.");

            bool configured = SteamUGC.SetItemTitle(update, request.Title)
                && SteamUGC.SetItemDescription(update, request.Description)
                && SteamUGC.SetItemMetadata(update, metadata.ToJson())
                && SteamUGC.SetItemTags(update, new List<string>(tags), false)
                && SteamUGC.SetItemContent(update, uploadDirectory)
                && SteamUGC.SetItemVisibility(update,
                    ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic);
            if (!configured)
                return Fail<CommunityRemoteItemState>(CommunityErrorCode.InvalidContent,
                    "Steam rejected one of the local Workshop item settings; no update was submitted.");

            Report(progress, CommunityProgressStage.Uploading, null, "Submitting the Workshop item.");
            SteamAPICall_t handle = SteamUGC.SubmitItemUpdate(update, string.Empty);
            return await SubmitStartedUpdateAsync(handle, request.Key,
                CommunityVisibility.Public, session, uploadDirectory);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
            || exception is ArgumentException || exception is NotSupportedException)
        {
            SafeDeleteDirectory(uploadDirectory);
            return Fail<CommunityRemoteItemState>(CommunityErrorCode.StorageFailure,
                "The Workshop upload payload could not be prepared: " + exception.Message);
        }
    }

    public async Task<CommunityResult<CommunityRemoteItemState>> SetVisibilityAsync(
        CommunityItemKey key, CommunityVisibility visibility, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return CommunityResult<CommunityRemoteItemState>.Canceled();
        if (!TryCaptureSession(out SessionSnapshot session, out CommunityError error))
            return CommunityResult<CommunityRemoteItemState>.Failure(error);
        if (!IsSupportedKey(key))
            return Fail<CommunityRemoteItemState>(CommunityErrorCode.InvalidContent,
                "The Workshop item key is invalid or belongs to another app.");

        UGCUpdateHandle_t update = SteamUGC.StartItemUpdate(new AppId_t(key.ConsumerAppId),
            new PublishedFileId_t(key.RemoteItemId));
        if (update == UGCUpdateHandle_t.Invalid)
            return Fail<CommunityRemoteItemState>(CommunityErrorCode.Offline,
                "Steam could not start the Workshop visibility update.");

        ERemoteStoragePublishedFileVisibility nativeVisibility = visibility == CommunityVisibility.Private
            ? ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate
            : ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic;
        if (!SteamUGC.SetItemVisibility(update, nativeVisibility))
            return Fail<CommunityRemoteItemState>(CommunityErrorCode.InvalidContent,
                "Steam rejected the requested visibility; no update was submitted.");

        return await SubmitStartedUpdateAsync(SteamUGC.SubmitItemUpdate(update, string.Empty),
            key, visibility, session, null);
    }

    private Task<CommunityResult<CommunityRemoteItemState>> SubmitStartedUpdateAsync(
        SteamAPICall_t handle, CommunityItemKey key, CommunityVisibility visibility,
        SessionSnapshot session, string temporaryDirectory)
    {
        return StartCall<SubmitItemUpdateResult_t, CommunityRemoteItemState>(handle, session,
            true, callback =>
            {
                if (callback.m_eResult != EResult.k_EResultOK)
                    return FailureFromSteam<CommunityRemoteItemState>(callback.m_eResult, remoteMutation: true);
                if (callback.m_nPublishedFileId.m_PublishedFileId != key.RemoteItemId)
                    return Unknown<CommunityRemoteItemState>("Steam returned an update for a different item. Reconcile before retrying.");

                CommunityRemoteStatus status = visibility == CommunityVisibility.Private
                    ? CommunityRemoteStatus.Private : CommunityRemoteStatus.Available;
                var state = new CommunityRemoteItemState(key, status, visibility);
                return callback.m_bUserNeedsToAcceptWorkshopLegalAgreement
                    ? CommunityResult<CommunityRemoteItemState>.NeedsAgreement(state)
                    : CommunityResult<CommunityRemoteItemState>.Success(state);
            }, string.IsNullOrEmpty(temporaryDirectory)
                ? null : () => SafeDeleteDirectory(temporaryDirectory));
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
