using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Words.Community;

namespace Words.Community.Tests
{
    public sealed class CommunityFoundationTests
    {
        private const uint AppId = 4966870;
        private const ulong AccountId = 76561198000000001;
        private string tempRoot;
        private string cacheRoot;
        private string stateRoot;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "words-community-tests-" + Guid.NewGuid().ToString("N"));
            cacheRoot = Path.Combine(tempRoot, "cache");
            stateRoot = Path.Combine(tempRoot, "state");
            Directory.CreateDirectory(tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }

        [Test]
        public void CodecRejectsMissingOrMismatchedDeclaredHash()
        {
            Assert.That(CommunityContentCodec.TryEncode(EnglishContent("alpha"),
                CommunityContentCodec.CurrentSchemaVersion, out byte[] payload,
                out CommunityDecodedContent decoded, out CommunityError encodeError), Is.True, encodeError?.Message);
            string json = Encoding.UTF8.GetString(payload);
            string declared = "\"contentHash\":\"" + decoded.ContentHash + "\"";

            byte[] mismatched = Encoding.UTF8.GetBytes(json.Replace(declared,
                "\"contentHash\":\"" + new string('0', 64) + "\""));
            Assert.That(CommunityContentCodec.TryDecodeAndValidate(mismatched, 1, null,
                out _, out CommunityError mismatchError), Is.False);
            Assert.That(mismatchError.Code, Is.EqualTo(CommunityErrorCode.InvalidContent));

            byte[] missing = Encoding.UTF8.GetBytes(json.Replace(declared, "\"contentHash\":\"\""));
            Assert.That(CommunityContentCodec.TryDecodeAndValidate(missing, 1, null,
                out _, out CommunityError missingError), Is.False);
            Assert.That(missingError.Code, Is.EqualTo(CommunityErrorCode.InvalidContent));

            Assert.That(CommunityWorkshopMetadata.TryCreate(EnglishContent("one", "two", "three", "four",
                "five", "six", "seven", "eight", "nine"), out CommunityWorkshopMetadata metadata,
                out CommunityError metadataError), Is.True, metadataError?.Message);
            Assert.That(metadata.PreviewWords.Count, Is.EqualTo(8));
            Assert.That(metadata.DetectedLanguage, Is.EqualTo("english"));
            Assert.That(CommunityWorkshopMetadata.TryBuildTags(LanguageMode.English, 1,
                out System.Collections.Generic.IReadOnlyList<string> tags, out CommunityError tagError), Is.True, tagError?.Message);
            Assert.That(tags, Does.Contain("sw:wordlist"));
            Assert.That(tags, Does.Contain("sw:schema:1"));
            Assert.That(tags, Does.Contain("sw:language:english"));
        }

        [Test]
        public void CacheCommitAndReloadPreserveImmutableProviderAndVersion()
        {
            var store = new CommunityCacheStore(cacheRoot);
            var key = new CommunityItemKey(AppId, 1099);
            CommunityResult<CommunityCacheEntry> committed = store.Commit(key, 1, "Cached title", "author",
                EnglishContent("alpha", "beta"));
            Assert.That(committed.Succeeded, Is.True, committed.Error?.Message);
            Assert.That(committed.Value.Provider.IsEditable, Is.False);

            var words = committed.Value.Provider.GetWords();
            words[0] = "mutated outside";
            CommunityResult<CommunityCacheEntry> loaded = store.Load(key, committed.Value.Version.SchemaVersion,
                committed.Value.Version.ContentHash);
            Assert.That(loaded.Succeeded, Is.True, loaded.Error?.Message);
            Assert.That(loaded.Value.Provider.GetWords(), Is.EqualTo(new[] { "alpha", "beta" }));
            Assert.That(loaded.Value.Version.ContentHash, Is.EqualTo(committed.Value.Version.ContentHash));
        }

        [Test]
        public void CacheRejectsJunctionInConfiguredPathWithoutFollowingIt()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                Assert.Ignore("Windows junction coverage is only available on Windows.");

            string outside = Path.Combine(tempRoot, "outside");
            string appLink = Path.Combine(cacheRoot, AppId.ToString());
            Directory.CreateDirectory(outside);
            string marker = Path.Combine(outside, "keep.txt");
            File.WriteAllText(marker, "keep");
            Directory.CreateDirectory(cacheRoot);
            CreateJunctionOrIgnore(appLink, outside);
            try
            {
                CommunityResult<System.Collections.Generic.IReadOnlyList<CommunityCacheEntry>> result =
                    new CommunityCacheStore(cacheRoot).GetEntries(AppId);
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Error.Code, Is.EqualTo(CommunityErrorCode.StorageFailure));
                Assert.That(File.ReadAllText(marker), Is.EqualTo("keep"));
            }
            finally
            {
                if (Directory.Exists(appLink)) Directory.Delete(appLink);
            }
        }

        [Test]
        public void RegistryReloadsAcrossInstancesAndDoesNotCreateDuplicateRemoteItems()
        {
            var scope = new CommunityAccountScope(AppId, AccountId);
            var first = new CommunityPublicationRegistry(stateRoot, scope);
            var second = new CommunityPublicationRegistry(stateRoot, scope);
            string hash = new string('a', 64);
            CommunityResult<CommunityPublication> intent = first.BeginCreate("local-list-1", "Title", hash,
                new string('f', 64), "op-1");
            Assert.That(intent.Succeeded, Is.True, intent.Error?.Message);
            CommunityResult<CommunityPublication> duplicatePending = second.BeginCreate("local-list-1", "Title", hash,
                new string('f', 64), "op-2");
            Assert.That(duplicatePending.Succeeded, Is.False);
            Assert.That(duplicatePending.Error.Code, Is.EqualTo(CommunityErrorCode.Conflict));

            var key = new CommunityItemKey(AppId, 77001);
            CommunityResult<CommunityPublication> recorded = first.RecordCreatedRemoteId(
                "local-list-1", "op-1", key, false);
            Assert.That(recorded.Succeeded, Is.True, recorded.Error?.Message);
            CommunityResult<CommunityPublication> duplicateKnown = second.BeginCreate("local-list-1", "Title", hash,
                new string('f', 64), "op-3");
            Assert.That(duplicateKnown.Succeeded, Is.False);
            Assert.That(duplicateKnown.Error.Code, Is.EqualTo(CommunityErrorCode.Conflict));
            Assert.That(duplicateKnown.Value, Is.Null);
            Assert.That(second.TryGetPublication("local-list-1", out CommunityPublication refreshed), Is.True);
            Assert.That(refreshed.ActiveRemoteItemId, Is.EqualTo(key.RemoteItemId));
        }

        [Test]
        public void CorruptPublicationJournalFailsClosedAndRemainsUntouched()
        {
            string journal = Path.Combine(stateRoot, AppId.ToString(), AccountId.ToString(), "publications.json");
            Directory.CreateDirectory(Path.GetDirectoryName(journal));
            const string corrupt = "{not valid json";
            File.WriteAllText(journal, corrupt);
            var registry = new CommunityPublicationRegistry(stateRoot, new CommunityAccountScope(AppId, AccountId));
            CommunityResult<CommunityPublication> result = registry.BeginCreate(
                "local-list-2", "Title", new string('b', 64), new string('f', 64), "op");
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(CommunityErrorCode.StorageFailure));
            Assert.That(File.ReadAllText(journal), Is.EqualTo(corrupt));
        }

        [Test]
        public async Task ServiceQueryAndDownloadDecorateCacheAndFilterLocalHide()
        {
            var gateway = new FakeCommunityGateway(AppId, AccountId, CommunityAvailability.Available);
            var itemKey = new CommunityItemKey(AppId, 88001);
            gateway.QueryPage = new CommunityPage(new[]
            {
                new CommunitySummary(itemKey, "List", "author", "description", LanguageMode.English,
                    1, 5, false, false, CommunityRemoteStatus.Available)
            }, "next", true);
            Assert.That(CommunityContentCodec.TryEncode(EnglishContent("hello"), 1,
                out byte[] payload, out CommunityDecodedContent content, out CommunityError codecError), Is.True, codecError?.Message);
            gateway.DownloadReceipt = new CommunityDownloadReceipt(itemKey, 1, content.ContentHash,
                "List", "author", payload);
            var service = CreateService(gateway);

            CommunityResult<CommunityCacheEntry> downloaded = await service.DownloadAsync(itemKey.RemoteItemId,
                null, CancellationToken.None);
            Assert.That(downloaded.Succeeded, Is.True, downloaded.Error?.Message);
            Assert.That(service.TryRecordRun(itemKey, content.ContentHash, AccountId, 12f, 1, 2,
                out CommunityPersonalRecord beforeHide, out CommunityError recordError), Is.True, recordError?.Message);
            CommunityResult<CommunityPage> page = await service.QueryAsync(new CommunityQuery(), CancellationToken.None);
            Assert.That(page.Succeeded, Is.True, page.Error?.Message);
            Assert.That(page.Value.Items[0].IsDownloaded, Is.True);

            CommunityResult<bool> hidden = service.SetHiddenLocally(itemKey, true);
            Assert.That(hidden.Succeeded, Is.True, hidden.Error?.Message);
            Assert.That(service.GetHiddenItems(), Is.EquivalentTo(new[] { itemKey }));
            CommunityResult<CommunityPage> filtered = await service.QueryAsync(new CommunityQuery(), CancellationToken.None);
            Assert.That(filtered.Value.Items, Is.Empty);

            CommunityResult<bool> restored = service.SetHiddenLocally(itemKey, false);
            Assert.That(restored.Succeeded, Is.True, restored.Error?.Message);
            Assert.That(service.GetHiddenItems(), Is.Empty);
            Assert.That(service.TryGetCachedEntry(itemKey, out _), Is.True);
            Assert.That(service.TryGetRecord(itemKey, content.ContentHash, AccountId,
                out CommunityPersonalRecord afterRestore, out CommunityError afterRestoreError), Is.True,
                afterRestoreError?.Message);
            Assert.That(afterRestore.PlayCount, Is.EqualTo(beforeHide.PlayCount));
            Assert.That(gateway.QueryCalls, Is.EqualTo(2));
            service.Dispose();
            service.Dispose();
            Assert.That(gateway.DisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task CreatePersistsRemoteIdBeforeSuccessAndReusesItOnRetry()
        {
            var gateway = new FakeCommunityGateway(AppId, AccountId, CommunityAvailability.Available)
            {
                CreateResult = CommunityResult<CommunityCreateReceipt>.Success(
                    new CommunityCreateReceipt(new CommunityItemKey(AppId, 99001), false))
            };
            var service = CreateService(gateway, _ => new CommunityPublishRequest(
                "local-list-3", "Title", "Description", Array.Empty<string>(), EnglishContent("publish me")));
            var request = new CommunityPublishRequest("local-list-3", "Title", "Description",
                Array.Empty<string>(), EnglishContent("publish me"));

            CommunityResult<CommunityPublication> created = await service.CreatePublicationAsync(request, CancellationToken.None);
            Assert.That(created.Succeeded, Is.True, created.Error?.Message);
            Assert.That(gateway.CreateCalls, Is.EqualTo(1));
            CommunityResult<CommunityPublication> retry = await service.CreatePublicationAsync(request, CancellationToken.None);
            Assert.That(retry.Succeeded, Is.True, retry.Error?.Message);
            Assert.That(retry.Value.ActiveRemoteItemId, Is.EqualTo(99001UL));
            Assert.That(gateway.CreateCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task UnknownCreateOutcomeBlocksBlindCreateRetry()
        {
            var gateway = new FakeCommunityGateway(AppId, AccountId, CommunityAvailability.Available)
            {
                CreateResult = CommunityResult<CommunityCreateReceipt>.Unknown(
                    new CommunityCreateReceipt(new CommunityItemKey(AppId, 99002), false),
                    new CommunityError(CommunityErrorCode.Unknown, "timeout"), "fake-op")
            };
            var service = CreateService(gateway);
            var request = new CommunityPublishRequest("local-list-4", "Title", "Description",
                Array.Empty<string>(), EnglishContent("publish me"));

            CommunityResult<CommunityPublication> first = await service.CreatePublicationAsync(request, CancellationToken.None);
            Assert.That(first.Outcome, Is.EqualTo(CommunityOperationOutcome.RecoveryRequired));
            Assert.That(first.Value.ActiveRemoteItemId, Is.EqualTo(99002UL));
            CommunityResult<CommunityPublication> retry = await service.CreatePublicationAsync(request, CancellationToken.None);
            Assert.That(retry.Outcome, Is.EqualTo(CommunityOperationOutcome.RecoveryRequired));
            Assert.That(gateway.CreateCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task SubmitRejectsMetadataChangedAfterCreate()
        {
            var gateway = new FakeCommunityGateway(AppId, AccountId, CommunityAvailability.Available)
            {
                CreateResult = CommunityResult<CommunityCreateReceipt>.Success(
                    new CommunityCreateReceipt(new CommunityItemKey(AppId, 100002), false))
            };
            var service = CreateService(gateway, _ => new CommunityPublishRequest(
                "local-list-6", "Changed title", "Description", new[] { "tag" }, EnglishContent("same words")));
            var original = new CommunityPublishRequest("local-list-6", "Original title", "Description",
                new[] { "tag" }, EnglishContent("same words"));
            CommunityResult<CommunityPublication> created = await service.CreatePublicationAsync(original, CancellationToken.None);
            Assert.That(created.Succeeded, Is.True, created.Error?.Message);

            CommunityResult<CommunityPublication> submit = await service.SubmitPublicationAsync(
                "local-list-6", null, CancellationToken.None);
            Assert.That(submit.Succeeded, Is.False);
            Assert.That(submit.Error.Code, Is.EqualTo(CommunityErrorCode.Conflict));
            Assert.That(gateway.SubmitCalls, Is.EqualTo(0));
        }

        [Test]
        public async Task ReconcileKnownRemoteStatesAndUnknownCreateWithoutIdSafely()
        {
            var gateway = new FakeCommunityGateway(AppId, AccountId, CommunityAvailability.Available);
            var requests = new System.Collections.Generic.Dictionary<string, CommunityPublishRequest>();
            var service = CreateService(gateway, id => requests.TryGetValue(id, out CommunityPublishRequest request) ? request : null);
            var registry = new CommunityPublicationRegistry(stateRoot, new CommunityAccountScope(AppId, AccountId));

            CommunityPublishRequest publicRequest = PublishRequest("reconcile-public", "public words");
            requests[publicRequest.LocalListId] = publicRequest;
            CommunityItemKey publicKey = CreateJournaledItem(service, gateway, publicRequest, 210001);
            Assert.That(registry.MarkSubmitting(publicRequest.LocalListId, "crashed-submit").Succeeded, Is.True);
            gateway.DetailsResult = CommunityResult<CommunityDetails>.Success(
                Details(publicKey, publicRequest, CommunityRemoteStatus.Available, AccountId), "reconcile-public-details");
            CommunityResult<CommunityPublication> published = await service.ReconcilePublicationAsync(
                publicRequest.LocalListId, CancellationToken.None);
            Assert.That(published.Succeeded, Is.True, published.Error?.Message);
            Assert.That(published.Value.Status, Is.EqualTo(CommunityPublicationStatus.Published));

            CommunityPublishRequest privateRequest = PublishRequest("reconcile-private", "private words");
            requests[privateRequest.LocalListId] = privateRequest;
            CommunityItemKey privateKey = CreateJournaledItem(service, gateway, privateRequest, 210002);
            Assert.That(registry.MarkSubmitting(privateRequest.LocalListId, "submit-private").Succeeded, Is.True);
            Assert.That(registry.MarkPublished(privateRequest.LocalListId, "submit-private", privateKey).Succeeded, Is.True);
            Assert.That(registry.MarkUnpublishing(privateRequest.LocalListId, "crashed-unpublish",
                privateKey.RemoteItemId).Succeeded, Is.True);
            gateway.DetailsResult = CommunityResult<CommunityDetails>.Success(
                Details(privateKey, privateRequest, CommunityRemoteStatus.Private, AccountId), "reconcile-private-details");
            CommunityResult<CommunityPublication> unpublished = await service.ReconcilePublicationAsync(
                privateRequest.LocalListId, CancellationToken.None);
            Assert.That(unpublished.Succeeded, Is.True, unpublished.Error?.Message);
            Assert.That(unpublished.Value.Status, Is.EqualTo(CommunityPublicationStatus.Unpublished));
            Assert.That(unpublished.Value.ActiveRemoteItemId, Is.Null);
            Assert.That(unpublished.Value.PreviousRemoteItemIds, Does.Contain(privateKey.RemoteItemId));

            CommunityPublishRequest ownerMismatchRequest = PublishRequest("reconcile-owner", "owner words");
            requests[ownerMismatchRequest.LocalListId] = ownerMismatchRequest;
            CommunityItemKey ownerMismatchKey = CreateJournaledItem(service, gateway, ownerMismatchRequest, 210003);
            gateway.DetailsResult = CommunityResult<CommunityDetails>.Success(Details(ownerMismatchKey,
                ownerMismatchRequest, CommunityRemoteStatus.Available, AccountId + 1), "reconcile-owner-details");
            CommunityResult<CommunityPublication> ownerMismatch = await service.ReconcilePublicationAsync(
                ownerMismatchRequest.LocalListId, CancellationToken.None);
            Assert.That(ownerMismatch.Outcome, Is.EqualTo(CommunityOperationOutcome.RecoveryRequired));
            Assert.That(service.TryGetPublication(ownerMismatchRequest.LocalListId, out CommunityPublication stillCreated), Is.True);
            Assert.That(stillCreated.Status, Is.EqualTo(CommunityPublicationStatus.Created));

            CommunityPublishRequest unknownRequest = PublishRequest("reconcile-no-id", "unknown create");
            gateway.CreateResult = CommunityResult<CommunityCreateReceipt>.Unknown(
                new CommunityError(CommunityErrorCode.Unknown, "timeout"), "no-id-op");
            CommunityResult<CommunityPublication> unknown = await service.CreatePublicationAsync(
                unknownRequest, CancellationToken.None);
            Assert.That(unknown.Outcome, Is.EqualTo(CommunityOperationOutcome.OutcomeUnknown));
            int detailsBefore = gateway.DetailsCalls;
            CommunityResult<CommunityPublication> blocked = await service.ReconcilePublicationAsync(
                unknownRequest.LocalListId, CancellationToken.None);
            Assert.That(blocked.Outcome, Is.EqualTo(CommunityOperationOutcome.RecoveryRequired));
            Assert.That(blocked.Value.ActiveRemoteItemId, Is.Null);
            Assert.That(gateway.DetailsCalls, Is.EqualTo(detailsBefore));
            int createsBefore = gateway.CreateCalls;
            CommunityResult<CommunityPublication> retry = await service.CreatePublicationAsync(
                unknownRequest, CancellationToken.None);
            Assert.That(retry.Outcome, Is.EqualTo(CommunityOperationOutcome.RecoveryRequired));
            Assert.That(gateway.CreateCalls, Is.EqualTo(createsBefore));
        }

        [Test]
        public void PublicationEditLockSurvivesAccountSwitchAndReleasesAfterConfirmedUnpublish()
        {
            var accountA = new CommunityAccountScope(AppId, AccountId);
            var registry = new CommunityPublicationRegistry(stateRoot, accountA);
            string localListId = "cross-account-lock";
            string contentHash = new string('a', 64);
            string fingerprint = new string('b', 64);
            Assert.That(registry.BeginCreate(localListId, "Locked", contentHash, fingerprint, "create-a").Succeeded, Is.True);

            var gateway = new FakeCommunityGateway(AppId, AccountId + 1, CommunityAvailability.Available);
            GatewayCommunityService switchedAccountService = CreateService(gateway);
            Assert.That(switchedAccountService.IsLocalListPublicationLocked(localListId), Is.True,
                "An in-flight create in Account A must remain locked after switching to Account B.");

            var key = new CommunityItemKey(AppId, 310001);
            Assert.That(registry.RecordCreatedRemoteId(localListId, "create-a", key, false).Succeeded, Is.True);
            Assert.That(switchedAccountService.IsLocalListPublicationLocked(localListId), Is.True,
                "A known remote binding remains locked across account changes.");
            Assert.That(registry.MarkSubmitting(localListId, "submit-a").Succeeded, Is.True);
            Assert.That(registry.MarkPublished(localListId, "submit-a", key).Succeeded, Is.True);
            Assert.That(registry.MarkUnpublishing(localListId, "unpublish-a", key.RemoteItemId).Succeeded, Is.True);
            Assert.That(switchedAccountService.IsLocalListPublicationLocked(localListId), Is.True);
            Assert.That(registry.MarkUnpublished(localListId, "unpublish-a", key.RemoteItemId).Succeeded, Is.True);
            Assert.That(switchedAccountService.IsLocalListPublicationLocked(localListId), Is.False,
                "Only confirmed unpublish releases the binding lock.");

            Assert.That(registry.BeginCreate("failed-before-create", "Failed", contentHash,
                fingerprint, "create-failed").Succeeded, Is.True);
            Assert.That(registry.MarkFailed("failed-before-create", "create-failed", "failed before remote create").Succeeded, Is.True);
            Assert.That(switchedAccountService.IsLocalListPublicationLocked("failed-before-create"), Is.False,
                "A failed pre-create journal with no remote ID does not lock editing.");

            var otherApp = new CommunityPublicationRegistry(stateRoot,
                new CommunityAccountScope(AppId + 1, AccountId + 2));
            Assert.That(otherApp.BeginCreate(localListId, "Other app", contentHash, fingerprint, "other-app-create").Succeeded, Is.True);
            Assert.That(switchedAccountService.IsLocalListPublicationLocked(localListId), Is.False,
                "A journal for a different consumer app does not lock this app's local list.");
        }

        [Test]
        public void OfflineRecordsUseOnlyKnownAccountAndRejectAccountSwitch()
        {
            var identity = new CommunityLocalIdentityStore(stateRoot, AppId);
            Assert.That(identity.TryRemember(AccountId, out CommunityError rememberError), Is.True, rememberError?.Message);
            var gateway = new FakeCommunityGateway(AppId, 0, CommunityAvailability.Offline);
            var service = CreateService(gateway);
            var key = new CommunityItemKey(AppId, 100001);
            string hash = new string('c', 64);

            Assert.That(service.EffectiveLocalAccountId, Is.EqualTo(AccountId));
            Assert.That(service.TryRecordRun(key, hash, AccountId, 10f, 2, 2,
                out CommunityPersonalRecord offlineRecord, out CommunityError offlineError), Is.True, offlineError?.Message);
            Assert.That(offlineRecord.PlayCount, Is.EqualTo(1));

            gateway.SetState(new CommunityServiceState(CommunityAvailability.Available, AppId,
                AccountId + 1, 2));
            Assert.That(service.TryRecordRun(key, hash, AccountId, 9f, 1, 3,
                out _, out CommunityError switchedError), Is.False);
            Assert.That(switchedError.Code, Is.EqualTo(CommunityErrorCode.NotLoggedIn));
            Assert.That(service.TryRecordRun(key, hash, AccountId + 1, 9f, 1, 3,
                out CommunityPersonalRecord switchedRecord, out CommunityError newAccountError), Is.True, newAccountError?.Message);
            Assert.That(switchedRecord.PlayCount, Is.EqualTo(1));

            var noIdentityService = CreateService(new FakeCommunityGateway(AppId, 0, CommunityAvailability.Offline),
                stateSuffix: "unknown");
            Assert.That(noIdentityService.EffectiveLocalAccountId, Is.EqualTo(0));
            Assert.That(noIdentityService.TryRecordRun(key, hash, 0, 8f, 0, 3,
                out _, out CommunityError noIdentityError), Is.False);
            Assert.That(noIdentityError.Code, Is.EqualTo(CommunityErrorCode.NotLoggedIn));
        }

        [Test]
        public async Task AcceptedWorkshopAgreementAndUnpublishKeepOldCacheIdentity()
        {
            var gateway = new FakeCommunityGateway(AppId, AccountId, CommunityAvailability.Available)
            {
                CreateResult = CommunityResult<CommunityCreateReceipt>.NeedsAgreement(
                    new CommunityCreateReceipt(new CommunityItemKey(AppId, 110001), true)),
                SubmitResult = CommunityResult<CommunityRemoteItemState>.Success(
                    new CommunityRemoteItemState(new CommunityItemKey(AppId, 110001),
                        CommunityRemoteStatus.Available, CommunityVisibility.Public)),
                VisibilityResult = CommunityResult<CommunityRemoteItemState>.Success(
                    new CommunityRemoteItemState(new CommunityItemKey(AppId, 110001),
                        CommunityRemoteStatus.Private, CommunityVisibility.Private))
            };
            var request = new CommunityPublishRequest("local-list-5", "Title", "Description",
                Array.Empty<string>(), EnglishContent("publish me"));
            var cacheStore = new CommunityCacheStore(cacheRoot);
            var cached = cacheStore.Commit(new CommunityItemKey(AppId, 110001), 1,
                "Title", "author", EnglishContent("publish me"));
            Assert.That(cached.Succeeded, Is.True, cached.Error?.Message);
            var service = CreateService(gateway, _ => request);
            CommunityResult<CommunityPublication> created = await service.CreatePublicationAsync(request, CancellationToken.None);
            Assert.That(created.Outcome, Is.EqualTo(CommunityOperationOutcome.NeedsAgreement));

            // Simulate the user accepting Steam's agreement between the create and submit attempts.
            gateway.CreateResult = CommunityResult<CommunityCreateReceipt>.Success(
                new CommunityCreateReceipt(new CommunityItemKey(AppId, 120001), false));
            // The existing remote ID is reused; no second create should be issued.
            CommunityResult<CommunityPublication> submitted = await service.SubmitPublicationAsync(
                "local-list-5", null, CancellationToken.None);
            Assert.That(submitted.Succeeded, Is.True, submitted.Error?.Message);
            Assert.That(gateway.SubmitCalls, Is.EqualTo(1));
            CommunityResult<CommunityPublication> unpublished = await service.UnpublishAsync(110001, CancellationToken.None);
            Assert.That(unpublished.Succeeded, Is.True, unpublished.Error?.Message);
            Assert.That(unpublished.Value.ActiveRemoteItemId, Is.Null);
            Assert.That(unpublished.Value.PreviousRemoteItemIds, Does.Contain(110001UL));
            Assert.That(cacheStore.Load(cached.Value.Key, cached.Value.Version.SchemaVersion,
                cached.Value.Version.ContentHash).Succeeded, Is.True);
            Assert.That(gateway.CreateCalls, Is.EqualTo(1));
        }

        private GatewayCommunityService CreateService(FakeCommunityGateway gateway,
            Func<string, CommunityPublishRequest> resolver = null, string stateSuffix = null)
        {
            string root = string.IsNullOrEmpty(stateSuffix) ? stateRoot : Path.Combine(stateRoot, stateSuffix);
            return new GatewayCommunityService(gateway, new CommunityCacheStore(cacheRoot), root, AppId, resolver);
        }

        private static WordListLibraryContent EnglishContent(params string[] words) => new WordListLibraryContent
        {
            LanguageMode = LanguageMode.English,
            Words = new System.Collections.Generic.List<string>(words)
        };

        private static CommunityPublishRequest PublishRequest(string localListId, string word) =>
            new CommunityPublishRequest(localListId, localListId, "Description", new[] { "sample" }, EnglishContent(word));

        private static CommunityItemKey CreateJournaledItem(GatewayCommunityService service,
            FakeCommunityGateway gateway, CommunityPublishRequest request, ulong remoteItemId)
        {
            gateway.CreateResult = CommunityResult<CommunityCreateReceipt>.Success(
                new CommunityCreateReceipt(new CommunityItemKey(AppId, remoteItemId), false));
            CommunityResult<CommunityPublication> created = service.CreatePublicationAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(created.Succeeded, Is.True, created.Error?.Message);
            return new CommunityItemKey(AppId, remoteItemId);
        }

        private static CommunityDetails Details(CommunityItemKey key, CommunityPublishRequest request,
            CommunityRemoteStatus status, ulong ownerAccountId)
        {
            Assert.That(CommunityContentCodec.TryEncode(request.Content, 1, out _,
                out CommunityDecodedContent decoded, out CommunityError error), Is.True, error?.Message);
            return new CommunityDetails(key, request.Title, "author", request.Description,
                LanguageMode.English, request.Content.Words.Count, 0, false, false, status,
                new Uri("https://steamcommunity.com/sharedfiles/filedetails/?id=" + key.RemoteItemId),
                ownerAccountId, decoded.ContentHash);
        }

        private static void CreateJunctionOrIgnore(string linkPath, string targetPath)
        {
            var start = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c mklink /J \"" + linkPath + "\" \"" + targetPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using (Process process = Process.Start(start))
            {
                process.WaitForExit(5000);
                if (!Directory.Exists(linkPath)) Assert.Ignore("This Windows runtime cannot create a junction without elevation.");
            }
        }

        private sealed class FakeCommunityGateway : ICommunityGateway, IDisposable
        {
            public CommunityServiceState State { get; private set; }
            public event Action<CommunityServiceState> AvailabilityChanged;
            public CommunityPage QueryPage = new CommunityPage(Array.Empty<CommunitySummary>(), string.Empty, false);
            public CommunityDownloadReceipt DownloadReceipt;
            public CommunityResult<CommunityDetails> DetailsResult;
            public CommunityResult<CommunityCreateReceipt> CreateResult;
            public CommunityResult<CommunityRemoteItemState> SubmitResult;
            public CommunityResult<CommunityRemoteItemState> VisibilityResult;
            public int QueryCalls;
            public int CreateCalls;
            public int SubmitCalls;
            public int DetailsCalls;
            public int DisposeCalls;

            public FakeCommunityGateway(uint appId, ulong accountId, CommunityAvailability availability)
            {
                State = new CommunityServiceState(availability, appId, accountId, 1);
            }

            public void SetState(CommunityServiceState state)
            {
                State = state;
                AvailabilityChanged?.Invoke(state);
            }

            public Task<CommunityResult<CommunityPage>> QueryAsync(CommunityQuery query, CancellationToken cancellationToken)
            {
                QueryCalls++;
                return Task.FromResult(CommunityResult<CommunityPage>.Success(QueryPage));
            }

            public Task<CommunityResult<CommunityDetails>> GetDetailsAsync(CommunityItemKey key, CancellationToken cancellationToken)
            {
                DetailsCalls++;
                return Task.FromResult(DetailsResult ?? CommunityResult<CommunityDetails>.Failure(
                    new CommunityError(CommunityErrorCode.NotFound, "unused")));
            }

            public Task<CommunityResult<CommunityDownloadReceipt>> DownloadAsync(CommunityItemKey key,
                IProgress<CommunityTransferProgress> progress, CancellationToken cancellationToken) =>
                Task.FromResult(CommunityResult<CommunityDownloadReceipt>.Success(DownloadReceipt));

            public Task<CommunityResult<CommunityVoteState>> VoteUpAsync(CommunityItemKey key, CancellationToken cancellationToken) =>
                Task.FromResult(CommunityResult<CommunityVoteState>.Success(new CommunityVoteState(true, 1)));

            public Task<CommunityResult<CommunityPage>> QueryOwnedAsync(uint consumerAppId, string continuationToken,
                CancellationToken cancellationToken) => Task.FromResult(CommunityResult<CommunityPage>.Success(QueryPage));

            public Task<CommunityResult<CommunityCreateReceipt>> CreatePublicationAsync(uint consumerAppId,
                CancellationToken cancellationToken)
            {
                CreateCalls++;
                return Task.FromResult(CreateResult);
            }

            public Task<CommunityResult<CommunityRemoteItemState>> SubmitPublicationAsync(CommunitySubmitRequest request,
                IProgress<CommunityTransferProgress> progress, CancellationToken cancellationToken)
            {
                SubmitCalls++;
                return Task.FromResult(SubmitResult);
            }

            public Task<CommunityResult<CommunityRemoteItemState>> SetVisibilityAsync(CommunityItemKey key,
                CommunityVisibility visibility, CancellationToken cancellationToken) => Task.FromResult(VisibilityResult);

            public void Dispose() { DisposeCalls++; }
        }
    }
}
