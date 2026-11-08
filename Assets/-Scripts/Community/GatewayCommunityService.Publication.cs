using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Words.Community
{
    public sealed partial class GatewayCommunityService
    {
        public async Task<CommunityResult<CommunityPublication>> CreatePublicationAsync(
            CommunityPublishRequest request, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return CommunityResult<CommunityPublication>.Canceled();
            if (!IsRemoteAuthenticated)
                return Failure<CommunityPublication>(RemoteAuthErrorCode(), RemoteAuthErrorMessage(), retryable: true);
            if (request == null || request.Content == null)
                return Failure<CommunityPublication>(CommunityErrorCode.InvalidContent, "A publish request with word-list content is required.");

            CommunityPublicationRegistry registry = GetPublicationRegistry(CurrentScope());
            if (registry.TryGetPublication(request.LocalListId, out CommunityPublication current))
            {
                if (current.Status == CommunityPublicationStatus.Creating
                    || current.Status == CommunityPublicationStatus.Submitting
                    || current.Status == CommunityPublicationStatus.Unpublishing
                    || current.Status == CommunityPublicationStatus.RecoveryRequired)
                    return CommunityResult<CommunityPublication>.RecoveryRequired(current,
                        new CommunityError(CommunityErrorCode.RecoveryRequired,
                            "This publication has an unresolved operation. Reconcile it before creating another item."), current.LocalListId);
                if (current.ActiveRemoteItemId.HasValue)
                {
                    if (current.NeedsLegalAgreement || current.Status == CommunityPublicationStatus.NeedsAgreement)
                        return CommunityResult<CommunityPublication>.NeedsAgreement(current, current.LocalListId);
                    return CommunityResult<CommunityPublication>.Success(current, current.LocalListId);
                }
            }

            if (!CommunityContentCodec.TryEncode(request.Content, CommunityContentCodec.CurrentSchemaVersion,
                out _, out CommunityDecodedContent validated, out CommunityError contentError))
                return CommunityResult<CommunityPublication>.Failure(contentError);
            if (!CommunityWorkshopMetadata.TryCreate(request.Content, out CommunityWorkshopMetadata metadata, out contentError))
                return CommunityResult<CommunityPublication>.Failure(contentError);
            if (!CommunityWorkshopMetadata.TryBuildTags(
                ParseDetectedLanguage(metadata.DetectedLanguage), metadata.SchemaVersion,
                out IReadOnlyList<string> requiredTags, out CommunityError tagError))
                return CommunityResult<CommunityPublication>.Failure(tagError);
            if (!string.Equals(metadata.ContentHash, validated.ContentHash, StringComparison.OrdinalIgnoreCase))
                return Failure<CommunityPublication>(CommunityErrorCode.InvalidContent,
                    "The generated publication metadata does not match the canonical word-list hash.");
            List<string> normalizedTags = BuildSubmissionTags(request.Tags, requiredTags);
            string fingerprint = CommunityWorkshopMetadata.ComputePublicationFingerprint(
                request.Title, request.Description, normalizedTags, metadata);

            var opId = Guid.NewGuid().ToString("N");
            CommunityResult<CommunityPublication> intent = registry.BeginCreate(
                request.LocalListId, request.Title, validated.ContentHash, fingerprint, opId);
            if (!intent.Succeeded) return intent;
            if (cancellationToken.IsCancellationRequested)
            {
                CommunityResult<CommunityPublication> rolledBack = registry.CancelCreateBeforeCall(request.LocalListId, opId);
                return rolledBack.Succeeded ? CommunityResult<CommunityPublication>.Canceled(opId)
                    : CommunityResult<CommunityPublication>.RecoveryRequired(intent.Value, rolledBack.Error, opId);
            }

            CommunityResult<CommunityCreateReceipt> created;
            try
            {
                created = await gateway.CreatePublicationAsync(CurrentAppId, cancellationToken);
            }
            catch (Exception exception)
            {
                CommunityResult<CommunityPublication> recovery = registry.MarkRecoveryRequired(request.LocalListId,
                    opId, "Create was submitted but did not return a confirmed remote result: " + exception.Message);
                return CommunityResult<CommunityPublication>.Unknown(recovery.Error ??
                    new CommunityError(CommunityErrorCode.RecoveryRequired, "Reconcile the create operation before retrying."), opId);
            }

            if (created == null)
                return CreateUnknown(registry, request.LocalListId, opId, "The gateway returned no create result.");
            if (created.Outcome == CommunityOperationOutcome.Canceled)
            {
                CommunityResult<CommunityPublication> rolledBack = registry.CancelCreateBeforeCall(request.LocalListId, opId);
                return rolledBack.Succeeded ? CommunityResult<CommunityPublication>.Canceled(opId)
                    : CommunityResult<CommunityPublication>.RecoveryRequired(intent.Value, rolledBack.Error, opId);
            }
            if (created.Outcome == CommunityOperationOutcome.Failed)
            {
                registry.MarkFailed(request.LocalListId, opId, created.Error?.Message);
                return CommunityResult<CommunityPublication>.Failure(created.Error, opId);
            }
            if (created.Outcome == CommunityOperationOutcome.OutcomeUnknown
                || created.Outcome == CommunityOperationOutcome.RecoveryRequired)
                return CreateUnknown(registry, request.LocalListId, opId,
                    created.Error?.Message ?? "The remote create outcome is unknown.", created.Value);
            if (created.Value == null || created.Value.Key == null
                || created.Value.Key.ConsumerAppId != CurrentAppId)
                return CreateUnknown(registry, request.LocalListId, opId,
                    "Create returned an invalid remote identity; reconcile before retrying.");

            bool needsAgreement = created.Outcome == CommunityOperationOutcome.NeedsAgreement
                || created.Value.NeedsLegalAgreement;
            // The remote ID is journaled before this method reports create success.
            CommunityResult<CommunityPublication> recorded = registry.RecordCreatedRemoteId(
                request.LocalListId, opId, created.Value.Key, needsAgreement);
            if (!recorded.Succeeded && recorded.Outcome != CommunityOperationOutcome.NeedsAgreement)
                return recorded;
            return recorded;
        }

        public async Task<CommunityResult<CommunityPublication>> SubmitPublicationAsync(
            string localListId, IProgress<CommunityTransferProgress> progress,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return CommunityResult<CommunityPublication>.Canceled();
            if (!IsRemoteAuthenticated)
                return Failure<CommunityPublication>(RemoteAuthErrorCode(), RemoteAuthErrorMessage(), retryable: true);
            if (publicationRequestResolver == null)
                return Failure<CommunityPublication>(CommunityErrorCode.InvalidContent,
                    "No local word-list resolver is configured for publication.");

            CommunityPublicationRegistry registry = GetPublicationRegistry(CurrentScope());
            if (!registry.TryGetPublication(localListId, out CommunityPublication publication)
                || !publication.ActiveRemoteItemId.HasValue)
                return Failure<CommunityPublication>(CommunityErrorCode.NotFound,
                    "Create a remote item before submitting its word-list content.");
            if (publication.Status == CommunityPublicationStatus.Published)
                return CommunityResult<CommunityPublication>.Success(publication);
            if (publication.Status == CommunityPublicationStatus.Creating
                || publication.Status == CommunityPublicationStatus.Submitting
                || publication.Status == CommunityPublicationStatus.Unpublishing
                || publication.Status == CommunityPublicationStatus.RecoveryRequired)
                return CommunityResult<CommunityPublication>.RecoveryRequired(publication,
                    new CommunityError(CommunityErrorCode.RecoveryRequired, "The publication operation must be reconciled first."),
                    publication.OperationId);

            CommunityPublishRequest request;
            try { request = publicationRequestResolver(localListId); }
            catch (Exception exception)
            {
                return Failure<CommunityPublication>(CommunityErrorCode.InvalidContent,
                    "The local word list could not be read: " + exception.Message);
            }
            if (request == null || request.Content == null || request.LocalListId != localListId)
                return Failure<CommunityPublication>(CommunityErrorCode.InvalidContent,
                    "The current local word list is unavailable or has a different identity.");
            if (!CommunityContentCodec.TryEncode(request.Content, CommunityContentCodec.CurrentSchemaVersion,
                out byte[] payload, out CommunityDecodedContent validated, out CommunityError contentError))
                return CommunityResult<CommunityPublication>.Failure(contentError);
            if (!string.Equals(validated.ContentHash, publication.ContentHash, StringComparison.OrdinalIgnoreCase))
                return Failure<CommunityPublication>(CommunityErrorCode.Conflict,
                    "The local word-list content changed after the remote item was created. Create a fresh publication for the new version.");
            if (!CommunityWorkshopMetadata.TryCreate(request.Content,
                out CommunityWorkshopMetadata metadata, out contentError))
                return CommunityResult<CommunityPublication>.Failure(contentError);
            if (!CommunityWorkshopMetadata.TryBuildTags(ParseDetectedLanguage(metadata.DetectedLanguage),
                metadata.SchemaVersion, out IReadOnlyList<string> requiredTags, out CommunityError tagError))
                return CommunityResult<CommunityPublication>.Failure(tagError);

            List<string> tags = BuildSubmissionTags(request.Tags, requiredTags);
            string fingerprint = CommunityWorkshopMetadata.ComputePublicationFingerprint(
                request.Title, request.Description, tags, metadata);
            if (!string.Equals(fingerprint, publication.PublicationFingerprint, StringComparison.OrdinalIgnoreCase))
                return Failure<CommunityPublication>(CommunityErrorCode.Conflict,
                    "The local publication title, description, tags, or generated metadata changed after remote item creation.");
            var key = new CommunityItemKey(CurrentAppId, publication.ActiveRemoteItemId.Value);
            var operationId = Guid.NewGuid().ToString("N");
            CommunityResult<CommunityPublication> submitting = registry.MarkSubmitting(localListId, operationId);
            if (!submitting.Succeeded) return submitting;
            if (cancellationToken.IsCancellationRequested)
            {
                CommunityResult<CommunityPublication> rolledBack = registry.CancelSubmitBeforeCall(localListId, operationId);
                return rolledBack.Succeeded ? CommunityResult<CommunityPublication>.Canceled(operationId)
                    : CommunityResult<CommunityPublication>.RecoveryRequired(publication, rolledBack.Error, operationId);
            }

            var submitRequest = new CommunitySubmitRequest(key, request.Title, request.Description, tags,
                metadata.SchemaVersion, validated.ContentHash, payload, metadata.ToJson());
            progress?.Report(new CommunityTransferProgress(CommunityProgressStage.Uploading));
            CommunityResult<CommunityRemoteItemState> submitted;
            try
            {
                submitted = await gateway.SubmitPublicationAsync(submitRequest, progress, cancellationToken);
            }
            catch (Exception exception)
            {
                CommunityResult<CommunityPublication> recovery = registry.MarkRecoveryRequired(localListId,
                    operationId, "Submit may have reached the platform but did not return a terminal result: " + exception.Message);
                return CommunityResult<CommunityPublication>.Unknown(recovery.Error ??
                    new CommunityError(CommunityErrorCode.RecoveryRequired, "Reconcile the submit operation before retrying."), operationId);
            }

            if (submitted == null)
                return SubmitUnknown(registry, localListId, operationId, "The gateway returned no submit result.");
            if (submitted.Outcome == CommunityOperationOutcome.Canceled)
            {
                CommunityResult<CommunityPublication> rolledBack = registry.CancelSubmitBeforeCall(localListId, operationId);
                return rolledBack.Succeeded ? CommunityResult<CommunityPublication>.Canceled(operationId)
                    : CommunityResult<CommunityPublication>.RecoveryRequired(publication, rolledBack.Error, operationId);
            }
            if (submitted.Outcome == CommunityOperationOutcome.NeedsAgreement)
            {
                CommunityResult<CommunityPublication> needsAgreement = registry.MarkNeedsAgreement(localListId,
                    operationId, submitted.Error?.Message);
                return needsAgreement.Succeeded
                    ? CommunityResult<CommunityPublication>.NeedsAgreement(needsAgreement.Value, operationId)
                    : CommunityResult<CommunityPublication>.RecoveryRequired(publication, needsAgreement.Error, operationId);
            }
            if (submitted.Outcome == CommunityOperationOutcome.OutcomeUnknown
                || submitted.Outcome == CommunityOperationOutcome.RecoveryRequired)
                return SubmitUnknown(registry, localListId, operationId,
                    submitted.Error?.Message ?? "The remote submit outcome is unknown.");
            if (submitted.Outcome == CommunityOperationOutcome.Failed)
            {
                CommunityResult<CommunityPublication> failed = registry.MarkFailed(localListId, operationId,
                    submitted.Error?.Message);
                return failed.Succeeded ? CommunityResult<CommunityPublication>.Failure(submitted.Error, operationId)
                    : CommunityResult<CommunityPublication>.RecoveryRequired(publication, failed.Error, operationId);
            }

            if (submitted.Value == null || submitted.Value.Key == null || !submitted.Value.Key.Equals(key)
                || submitted.Value.Visibility != CommunityVisibility.Public)
                return SubmitUnknown(registry, localListId, operationId,
                    "Upload completed without confirming that the expected item is public.");
            CommunityResult<CommunityPublication> published = registry.MarkPublished(localListId, operationId, key);
            return published.Succeeded ? published
                : CommunityResult<CommunityPublication>.RecoveryRequired(publication, published.Error, operationId);
        }

        public async Task<CommunityResult<CommunityPublication>> UnpublishAsync(ulong remoteItemId,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return CommunityResult<CommunityPublication>.Canceled();
            if (!IsRemoteAuthenticated)
                return Failure<CommunityPublication>(RemoteAuthErrorCode(), RemoteAuthErrorMessage(), retryable: true);
            if (remoteItemId == 0) return Failure<CommunityPublication>(CommunityErrorCode.InvalidContent,
                "A remote item ID is required to unpublish.");

            CommunityAccountScope scope = CurrentScope();
            CommunityPublicationRegistry registry = GetPublicationRegistry(scope);
            CommunityPublication publication = registry.GetPublications().FirstOrDefault(item =>
                item.ActiveRemoteItemId == remoteItemId);
            if (publication == null)
                return Failure<CommunityPublication>(CommunityErrorCode.NotFound,
                    "The remote item is not an active publication for this account.");
            var key = new CommunityItemKey(CurrentAppId, remoteItemId);
            string operationId = Guid.NewGuid().ToString("N");
            CommunityResult<CommunityPublication> unpublishing = registry.MarkUnpublishing(
                publication.LocalListId, operationId, remoteItemId);
            if (!unpublishing.Succeeded) return unpublishing;
            if (cancellationToken.IsCancellationRequested)
            {
                CommunityResult<CommunityPublication> failed = registry.MarkFailed(publication.LocalListId,
                    operationId, "Unpublish was canceled before contacting the platform.");
                return failed.Succeeded ? CommunityResult<CommunityPublication>.Canceled(operationId)
                    : CommunityResult<CommunityPublication>.RecoveryRequired(publication, failed.Error, operationId);
            }

            CommunityResult<CommunityRemoteItemState> result;
            try
            {
                result = await gateway.SetVisibilityAsync(key, CommunityVisibility.Private, cancellationToken);
            }
            catch (Exception exception)
            {
                CommunityResult<CommunityPublication> recovery = registry.MarkRecoveryRequired(publication.LocalListId,
                    operationId, "Unpublish may have reached the platform but did not return a terminal result: " + exception.Message);
                return CommunityResult<CommunityPublication>.Unknown(recovery.Error ??
                    new CommunityError(CommunityErrorCode.RecoveryRequired, "Reconcile the unpublish operation before retrying."), operationId);
            }
            if (result == null)
                return UnpublishUnknown(registry, publication, operationId, "The gateway returned no visibility result.");
            if (result.Outcome == CommunityOperationOutcome.Canceled)
            {
                CommunityResult<CommunityPublication> failed = registry.MarkFailed(publication.LocalListId,
                    operationId, "Unpublish was canceled before contacting the platform.");
                return failed.Succeeded ? CommunityResult<CommunityPublication>.Canceled(operationId)
                    : CommunityResult<CommunityPublication>.RecoveryRequired(publication, failed.Error, operationId);
            }
            if (result.Outcome == CommunityOperationOutcome.Failed)
            {
                CommunityResult<CommunityPublication> failed = registry.MarkFailed(publication.LocalListId,
                    operationId, result.Error?.Message);
                return failed.Succeeded ? CommunityResult<CommunityPublication>.Failure(result.Error, operationId)
                    : CommunityResult<CommunityPublication>.RecoveryRequired(publication, failed.Error, operationId);
            }
            if (result.Outcome == CommunityOperationOutcome.OutcomeUnknown
                || result.Outcome == CommunityOperationOutcome.RecoveryRequired)
                return UnpublishUnknown(registry, publication, operationId,
                    result.Error?.Message ?? "The remote visibility outcome is unknown.");
            if (result.Value == null || result.Value.Key == null || !result.Value.Key.Equals(key)
                || result.Value.Visibility != CommunityVisibility.Private)
                return UnpublishUnknown(registry, publication, operationId,
                    "The platform did not confirm that the expected item is private.");

            // Preserve downloaded cache and records; archive only the active remote binding.
            CommunityResult<CommunityPublication> archived = registry.MarkUnpublished(
                publication.LocalListId, operationId, remoteItemId);
            return archived.Succeeded ? archived
                : CommunityResult<CommunityPublication>.RecoveryRequired(publication, archived.Error, operationId);
        }

        public async Task<CommunityResult<CommunityPublication>> ReconcilePublicationAsync(
            string localListId, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return CommunityResult<CommunityPublication>.Canceled();
            if (!IsRemoteAuthenticated)
                return Failure<CommunityPublication>(RemoteAuthErrorCode(), RemoteAuthErrorMessage(), retryable: true);

            CommunityAccountScope scope = CurrentScope();
            CommunityPublicationRegistry registry = GetPublicationRegistry(scope);
            if (!registry.TryGetPublication(localListId, out CommunityPublication publication))
                return Failure<CommunityPublication>(CommunityErrorCode.NotFound, "No publication journal entry exists.");
            if (!publication.ActiveRemoteItemId.HasValue)
                return CommunityResult<CommunityPublication>.RecoveryRequired(publication,
                    new CommunityError(CommunityErrorCode.RecoveryRequired,
                        "No remote item ID was durably recorded. Reconcile this create in Steam Workshop; the game will not guess or create another item."),
                    publication.OperationId);

            var key = new CommunityItemKey(scope.ConsumerAppId, publication.ActiveRemoteItemId.Value);
            string operationId = Guid.NewGuid().ToString("N");
            CommunityResult<CommunityDetails> response = await CallGateway(
                () => gateway.GetDetailsAsync(key, cancellationToken), cancellationToken);
            if (!response.Succeeded || response.Value == null)
                return ConvertResult<CommunityDetails, CommunityPublication>(response);

            CommunityDetails details = response.Value;
            if (details.Key == null || !details.Key.Equals(key))
                return CommunityResult<CommunityPublication>.RecoveryRequired(publication,
                    new CommunityError(CommunityErrorCode.Conflict,
                        "The details response belongs to a different remote item."), operationId);
            if (CurrentAccountId != scope.AccountId || !IsRemoteAuthenticated)
                return CommunityResult<CommunityPublication>.RecoveryRequired(publication,
                    new CommunityError(CommunityErrorCode.NotLoggedIn,
                        "The Steam account changed while this publication was being reconciled."), operationId);
            bool fullMetadataVerified = false;
            if (publicationRequestResolver != null)
            {
                CommunityPublishRequest request;
                try { request = publicationRequestResolver(localListId); }
                catch (Exception exception)
                {
                    return CommunityResult<CommunityPublication>.RecoveryRequired(publication,
                        new CommunityError(CommunityErrorCode.RecoveryRequired,
                            "The canonical local publication could not be resolved: " + exception.Message), operationId);
                }
                if (request == null || request.Content == null || request.LocalListId != localListId
                    || !string.Equals(request.Title, details.Title, StringComparison.Ordinal)
                    || !string.Equals(request.Description, details.Description, StringComparison.Ordinal))
                    return CommunityResult<CommunityPublication>.RecoveryRequired(publication,
                        new CommunityError(CommunityErrorCode.Conflict,
                            "Remote title or description does not match the canonical local publication."), operationId);
                if (!CommunityContentCodec.TryEncode(request.Content, CommunityContentCodec.CurrentSchemaVersion,
                    out _, out CommunityDecodedContent decoded, out CommunityError contentError))
                    return CommunityResult<CommunityPublication>.RecoveryRequired(publication,
                        contentError ?? new CommunityError(CommunityErrorCode.InvalidContent,
                            "The canonical local publication content is invalid."), operationId);
                if (!string.Equals(decoded.ContentHash, publication.ContentHash, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(details.ContentHash, decoded.ContentHash, StringComparison.OrdinalIgnoreCase))
                    return CommunityResult<CommunityPublication>.RecoveryRequired(publication,
                        new CommunityError(CommunityErrorCode.Conflict,
                            "Remote and local content hashes do not match the journal."), operationId);
                if (!CommunityWorkshopMetadata.TryCreate(request.Content,
                    out CommunityWorkshopMetadata metadata, out CommunityError metadataError)
                    || !CommunityWorkshopMetadata.TryBuildTags(ParseDetectedLanguage(metadata.DetectedLanguage),
                        metadata.SchemaVersion, out IReadOnlyList<string> requiredTags, out metadataError))
                    return CommunityResult<CommunityPublication>.RecoveryRequired(publication,
                        metadataError ?? new CommunityError(CommunityErrorCode.InvalidContent,
                            "The canonical publication metadata is invalid."), operationId);
                List<string> tags = BuildSubmissionTags(request.Tags, requiredTags);
                string fingerprint = CommunityWorkshopMetadata.ComputePublicationFingerprint(
                    request.Title, request.Description, tags, metadata);
                if (!string.Equals(fingerprint, publication.PublicationFingerprint, StringComparison.OrdinalIgnoreCase))
                    return CommunityResult<CommunityPublication>.RecoveryRequired(publication,
                        new CommunityError(CommunityErrorCode.Conflict,
                            "The canonical publication metadata no longer matches the saved publication."), operationId);
                fullMetadataVerified = true;
            }

            return registry.ReconcileKnownRemote(localListId, operationId, key,
                details.OwnerAccountId, details.RemoteStatus, details.ContentHash,
                details.Title, fullMetadataVerified);
        }

        private static CommunityResult<CommunityPublication> CreateUnknown(
            CommunityPublicationRegistry registry, string localListId, string operationId, string message,
            CommunityCreateReceipt receipt = null)
        {
            if (receipt != null && receipt.Key != null
                && receipt.Key.ConsumerAppId == registry.Scope.ConsumerAppId)
            {
                // Persist a known remote ID and recovery status in one atomic journal write.
                return registry.RecordCreatedRemoteId(localListId, operationId, receipt.Key,
                    receipt.NeedsLegalAgreement, recoveryRequired: true);
            }
            CommunityResult<CommunityPublication> recovery = registry.MarkRecoveryRequired(localListId, operationId, message);
            return CommunityResult<CommunityPublication>.Unknown(recovery.Error ??
                new CommunityError(CommunityErrorCode.RecoveryRequired, message), operationId);
        }

        private static CommunityResult<CommunityPublication> SubmitUnknown(
            CommunityPublicationRegistry registry, string localListId, string operationId, string message)
        {
            CommunityResult<CommunityPublication> recovery = registry.MarkRecoveryRequired(localListId, operationId, message);
            CommunityPublication state = recovery.Value;
            if (state == null) registry.TryGetPublication(localListId, out state);
            return CommunityResult<CommunityPublication>.RecoveryRequired(state,
                recovery.Error ?? new CommunityError(CommunityErrorCode.RecoveryRequired, message), operationId);
        }

        private static CommunityResult<CommunityPublication> UnpublishUnknown(
            CommunityPublicationRegistry registry, CommunityPublication publication,
            string operationId, string message)
        {
            CommunityResult<CommunityPublication> recovery = registry.MarkRecoveryRequired(
                publication.LocalListId, operationId, message);
            CommunityPublication state = recovery.Value;
            if (state == null) registry.TryGetPublication(publication.LocalListId, out state);
            return CommunityResult<CommunityPublication>.RecoveryRequired(state,
                recovery.Error ?? new CommunityError(CommunityErrorCode.RecoveryRequired, message), operationId);
        }

        private static LanguageMode ParseDetectedLanguage(string detectedLanguage)
        {
            if (detectedLanguage == "english") return LanguageMode.English;
            if (detectedLanguage == "chinese") return LanguageMode.Chinese;
            if (detectedLanguage == "mixed") return LanguageMode.Mixed;
            return (LanguageMode)(-1);
        }

        private static List<string> BuildSubmissionTags(IEnumerable<string> userTags,
            IReadOnlyList<string> requiredTags)
        {
            var tags = new List<string>();
            if (userTags != null)
            {
                foreach (string tag in userTags)
                {
                    if (string.IsNullOrWhiteSpace(tag) || tag.StartsWith("sw:", StringComparison.OrdinalIgnoreCase)) continue;
                    string trimmed = tag.Trim();
                    if (!tags.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) tags.Add(trimmed);
                }
            }
            if (requiredTags != null)
                foreach (string tag in requiredTags)
                    if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) tags.Add(tag);
            return tags;
        }
    }
}
