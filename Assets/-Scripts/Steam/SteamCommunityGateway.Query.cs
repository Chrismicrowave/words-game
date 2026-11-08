using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Steamworks;
using Words.Community;

internal sealed partial class SteamCommunityGateway
{
    private const uint MetadataBufferSize = CommunityWorkshopMetadata.MaximumMetadataCharacters + 1;
    private const uint TagBufferSize = 512;
    private const uint MaximumReturnedTags = 64;

    private UGCQueryHandle_t BuildSearchQuery(CommunityQuery query, uint appId)
    {
        EUGCQuery sort = !string.IsNullOrWhiteSpace(query.SearchText)
            ? EUGCQuery.k_EUGCQuery_RankedByTextSearch
            : query.Sort == CommunitySort.Popular
                ? EUGCQuery.k_EUGCQuery_RankedByVote
                : EUGCQuery.k_EUGCQuery_RankedByPublicationDate;
        string cursor = string.IsNullOrWhiteSpace(query.ContinuationToken)
            ? "*" : query.ContinuationToken;
        UGCQueryHandle_t handle = SteamUGC.CreateQueryAllUGCRequest(sort,
            EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items_ReadyToUse,
            new AppId_t(appId), new AppId_t(ExpectedAppId), cursor);
        if (handle == UGCQueryHandle_t.Invalid)
            return handle;

        bool configured = SteamUGC.AddRequiredTag(handle, CommunityWorkshopTags.WordList)
            && SteamUGC.AddRequiredTag(handle, CommunityWorkshopTags.SchemaV1)
            && SteamUGC.SetReturnMetadata(handle, true)
            && SteamUGC.SetReturnKeyValueTags(handle, true)
            && SteamUGC.SetReturnLongDescription(handle, true);
        if (configured && query.Language.HasValue)
            configured = SteamUGC.AddRequiredTag(handle, CommunityWorkshopTags.ForLanguage(query.Language.Value));
        if (configured && !string.IsNullOrWhiteSpace(query.SearchText))
            configured = SteamUGC.SetSearchText(handle, query.SearchText.Trim());

        if (configured)
            return handle;

        SteamUGC.ReleaseQueryUGCRequest(handle);
        return UGCQueryHandle_t.Invalid;
    }

    private UGCQueryHandle_t BuildDetailsQuery(CommunityItemKey key, uint appId)
    {
        var ids = new[] { new PublishedFileId_t(key.RemoteItemId) };
        UGCQueryHandle_t handle = SteamUGC.CreateQueryUGCDetailsRequest(ids, 1);
        if (handle == UGCQueryHandle_t.Invalid)
            return handle;

        bool configured = SteamUGC.SetReturnMetadata(handle, true)
            && SteamUGC.SetReturnKeyValueTags(handle, true)
            && SteamUGC.SetReturnLongDescription(handle, true);
        if (configured)
            return handle;

        SteamUGC.ReleaseQueryUGCRequest(handle);
        return UGCQueryHandle_t.Invalid;
    }

    private UGCQueryHandle_t BuildOwnedQuery(uint page, SessionSnapshot session)
    {
        var account = new AccountID_t((uint)(session.AccountId & 0xFFFFFFFFUL));
        UGCQueryHandle_t handle = SteamUGC.CreateQueryUserUGCRequest(account,
            EUserUGCList.k_EUserUGCList_Published,
            EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items_ReadyToUse,
            EUserUGCListSortOrder.k_EUserUGCListSortOrder_LastUpdatedDesc,
            new AppId_t(session.AppId), new AppId_t(ExpectedAppId), page);
        if (handle == UGCQueryHandle_t.Invalid)
            return handle;

        bool configured = SteamUGC.AddRequiredTag(handle, CommunityWorkshopTags.WordList)
            && SteamUGC.AddRequiredTag(handle, CommunityWorkshopTags.SchemaV1)
            && SteamUGC.SetReturnMetadata(handle, true)
            && SteamUGC.SetReturnKeyValueTags(handle, true)
            && SteamUGC.SetReturnLongDescription(handle, true);
        if (configured)
            return handle;

        SteamUGC.ReleaseQueryUGCRequest(handle);
        return UGCQueryHandle_t.Invalid;
    }

    private Task<CommunityResult<NativeQueryPage>> RunQueryAsync(
        UGCQueryHandle_t handle, SessionSnapshot session)
    {
        if (handle == UGCQueryHandle_t.Invalid)
            return Task.FromResult(Fail<NativeQueryPage>(CommunityErrorCode.Unknown,
                "Steam could not create the Workshop query."));

        SteamAPICall_t apiCall;
        try
        {
            apiCall = SteamUGC.SendQueryUGCRequest(handle);
        }
        catch (Exception exception)
        {
            SteamUGC.ReleaseQueryUGCRequest(handle);
            return Task.FromResult(Fail<NativeQueryPage>(CommunityErrorCode.Offline,
                "Steam could not start the Workshop query: " + exception.Message));
        }

        if (apiCall == SteamAPICall_t.Invalid)
        {
            SteamUGC.ReleaseQueryUGCRequest(handle);
            return Task.FromResult(Fail<NativeQueryPage>(CommunityErrorCode.Offline,
                "Steam rejected the Workshop query request."));
        }

        return StartCall<SteamUGCQueryCompleted_t, NativeQueryPage>(apiCall, session, false,
            callback =>
            {
                if (callback.m_handle != handle)
                    return Fail<NativeQueryPage>(CommunityErrorCode.Unknown,
                        "Steam returned a different Workshop query handle.");
                if (callback.m_eResult != EResult.k_EResultOK)
                    return FailureFromSteam<NativeQueryPage>(callback.m_eResult);

                var items = new List<NativeItem>((int)callback.m_unNumResultsReturned);
                for (uint index = 0; index < callback.m_unNumResultsReturned; index++)
                {
                    if (!SteamUGC.GetQueryUGCResult(handle, index, out SteamUGCDetails_t details))
                        continue;
                    items.Add(ReadNativeItem(handle, index, details));
                }
                string next = callback.m_rgchNextCursor ?? string.Empty;
                bool hasMore = !string.IsNullOrWhiteSpace(next) && next != "*";
                return CommunityResult<NativeQueryPage>.Success(new NativeQueryPage(items, next,
                    hasMore, callback.m_unTotalMatchingResults, callback.m_unNumResultsReturned));
            }, () => SteamUGC.ReleaseQueryUGCRequest(handle));
    }

    private NativeItem ReadNativeItem(UGCQueryHandle_t handle, uint index, SteamUGCDetails_t details)
    {
        var tags = new List<string>();
        uint tagCount = SteamUGC.GetQueryUGCNumTags(handle, index);
        bool tagsReadable = tagCount <= MaximumReturnedTags;
        if (tagsReadable)
        {
            for (uint tagIndex = 0; tagIndex < tagCount; tagIndex++)
            {
                if (!SteamUGC.GetQueryUGCTag(handle, index, tagIndex, out string tag, TagBufferSize))
                {
                    tagsReadable = false;
                    break;
                }
                if (!string.IsNullOrWhiteSpace(tag))
                    tags.Add(tag);
            }
        }

        CommunityWorkshopMetadata metadata = null;
        bool metadataReadable = SteamUGC.GetQueryUGCMetadata(handle, index,
            out string metadataJson, MetadataBufferSize)
            && CommunityWorkshopMetadata.TryParse(metadataJson, out metadata, out _);
        string[] languageTags = tags.FindAll(tag => tag.StartsWith("sw:language:", StringComparison.Ordinal)).ToArray();
        string expectedLanguageTag = metadata != null ? "sw:language:" + metadata.DetectedLanguage : string.Empty;
        bool contentTagsValid = tagsReadable
            && tags.Contains(CommunityWorkshopTags.WordList)
            && tags.Contains(CommunityWorkshopTags.SchemaV1)
            && languageTags.Length == 1
            && string.Equals(languageTags[0], expectedLanguageTag, StringComparison.Ordinal);

        CommunityRemoteStatus remoteStatus;
        if (details.m_eResult == EResult.k_EResultFileNotFound)
            remoteStatus = CommunityRemoteStatus.Unknown;
        else if (details.m_bBanned || details.m_eResult == EResult.k_EResultBanned)
            remoteStatus = CommunityRemoteStatus.Banned;
        else if (details.m_eVisibility != ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic)
            remoteStatus = CommunityRemoteStatus.Private;
        else if (details.m_eResult != EResult.k_EResultOK || !metadataReadable || !contentTagsValid
            || details.m_nCreatorAppID.m_AppId != ExpectedAppId
            || details.m_nConsumerAppID.m_AppId != ExpectedAppId
            || details.m_bTagsTruncated)
            remoteStatus = CommunityRemoteStatus.Incompatible;
        else
            remoteStatus = CommunityRemoteStatus.Available;

        ulong remoteId = details.m_nPublishedFileId.m_PublishedFileId;
        CommunityItemKey key = remoteId == 0 ? null : new CommunityItemKey(ExpectedAppId, remoteId);
        uint votesUp = details.m_unVotesUp;
        int likeCount = votesUp > int.MaxValue ? int.MaxValue : (int)votesUp;
        bool isInstalled = remoteId != 0
            && (((EItemState)SteamUGC.GetItemState(new PublishedFileId_t(remoteId)))
                & EItemState.k_EItemStateInstalled) != 0;
        return new NativeItem(key, details.m_rgchTitle, details.m_rgchDescription,
            details.m_ulSteamIDOwner == 0 ? string.Empty : details.m_ulSteamIDOwner.ToString(CultureInfo.InvariantCulture),
            details.m_ulSteamIDOwner, metadata, likeCount, isInstalled, remoteStatus);
    }

    private CommunityResult<NativeItem> ExtractSingleItem(CommunityResult<NativeQueryPage> result)
    {
        if (result == null || !result.Succeeded)
            return CommunityResult<NativeItem>.Failure(result?.Error
                ?? new CommunityError(CommunityErrorCode.Unknown, "The Workshop details query failed."));
        if (result.Value.Items.Count == 0 || result.Value.Items[0].Key == null)
            return Fail<NativeItem>(CommunityErrorCode.NotFound, "The Workshop item was not found.");
        return CommunityResult<NativeItem>.Success(result.Value.Items[0]);
    }

    private CommunitySummary ToSummary(NativeItem item, bool isLiked) => new CommunitySummary(
        item.Key, item.Title, item.OwnerId, item.Description, GetLanguage(item.Metadata),
        item.Metadata != null ? item.Metadata.WordCount : 0, item.LikeCount, isLiked,
        false, item.RemoteStatus);

    private CommunityDetails ToDetails(NativeItem item, bool isLiked) => new CommunityDetails(
        item.Key, item.Title, item.OwnerId, item.Description, GetLanguage(item.Metadata),
        item.Metadata != null ? item.Metadata.WordCount : 0, item.LikeCount, isLiked,
        false, item.RemoteStatus, new Uri("https://steamcommunity.com/sharedfiles/filedetails/?id="
            + item.Key.RemoteItemId.ToString(CultureInfo.InvariantCulture)), item.OwnerAccountId,
        item.Metadata != null ? item.Metadata.ContentHash : string.Empty);

    private static LanguageMode GetLanguage(CommunityWorkshopMetadata metadata)
    {
        if (metadata == null) return LanguageMode.English;
        switch (metadata.DetectedLanguage)
        {
            case "chinese": return LanguageMode.Chinese;
            case "mixed": return LanguageMode.Mixed;
            default: return LanguageMode.English;
        }
    }

    private sealed class NativeItem
    {
        public readonly CommunityItemKey Key;
        public readonly string Title;
        public readonly string Description;
        public readonly string OwnerId;
        public readonly ulong OwnerAccountId;
        public readonly CommunityWorkshopMetadata Metadata;
        public readonly int LikeCount;
        public readonly bool IsInstalled;
        public readonly CommunityRemoteStatus RemoteStatus;

        public NativeItem(CommunityItemKey key, string title, string description, string ownerId,
            ulong ownerAccountId, CommunityWorkshopMetadata metadata, int likeCount, bool isInstalled,
            CommunityRemoteStatus remoteStatus)
        {
            Key = key;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            OwnerId = ownerId ?? string.Empty;
            OwnerAccountId = ownerAccountId;
            Metadata = metadata;
            LikeCount = likeCount;
            IsInstalled = isInstalled;
            RemoteStatus = remoteStatus;
        }
    }

    private sealed class NativeQueryPage
    {
        public readonly IReadOnlyList<NativeItem> Items;
        public readonly string ContinuationToken;
        public readonly bool HasMore;
        public readonly uint TotalMatchingResults;
        public readonly uint NumResultsReturned;

        public NativeQueryPage(IEnumerable<NativeItem> items, string continuationToken, bool hasMore,
            uint totalMatchingResults, uint numResultsReturned)
        {
            Items = new List<NativeItem>(items ?? Array.Empty<NativeItem>()).AsReadOnly();
            ContinuationToken = continuationToken ?? string.Empty;
            HasMore = hasMore;
            TotalMatchingResults = totalMatchingResults;
            NumResultsReturned = numResultsReturned;
        }
    }
}
