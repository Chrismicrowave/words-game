using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Words.Community
{
    [Serializable]
    internal sealed class CommunityPublicationRegistryDocument
    {
        public int schemaVersion = 1;
        public List<CommunityPublicationRegistryRecord> publications = new List<CommunityPublicationRegistryRecord>();
    }

    [Serializable]
    internal sealed class CommunityPublicationRegistryRecord
    {
        public string localListId;
        public string title;
        public string contentHash;
        public string publicationFingerprint;
        public string activeRemoteItemId;
        public List<string> previousRemoteItemIds = new List<string>();
        public int status;
        public bool needsLegalAgreement;
        public int recoveryFromStatus;
        public string operationId;
        public string lastError;
    }

    /// <summary>
    /// Per-app, per-account publication journal. Create intent is persisted before remote create;
    /// a returned remote ID is persisted before the caller can report create success.
    /// </summary>
    public sealed class CommunityPublicationRegistry
    {
        private const int DocumentSchemaVersion = 1;
        private const int MaxLocalListIdLength = 256;
        private static readonly ConcurrentDictionary<string, object> PathLocks =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly object sync;
        private readonly string filePath;
        private CommunityPublicationRegistryDocument document;
        private CommunityError loadError;

        public CommunityAccountScope Scope { get; }
        public CommunityError LoadError { get { lock (sync) return loadError; } }

        public CommunityPublicationRegistry(string rootPath, CommunityAccountScope scope)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("A registry root is required.", nameof(rootPath));
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            string canonicalRoot = Path.GetFullPath(rootPath);
            filePath = Path.GetFullPath(Path.Combine(canonicalRoot,
                scope.ConsumerAppId.ToString(CultureInfo.InvariantCulture),
                scope.AccountId.ToString(CultureInfo.InvariantCulture), "publications.json"));
            string prefix = canonicalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The registry path escaped its configured root.", nameof(rootPath));
            sync = PathLocks.GetOrAdd(filePath, _ => new object());
            LoadFromDisk();
        }

        public IReadOnlyList<CommunityPublication> GetPublications()
        {
            lock (sync)
                return (document.publications ?? new List<CommunityPublicationRegistryRecord>())
                    .Select(ToPublication).ToList().AsReadOnly();
        }

        public bool TryGetPublication(string localListId, out CommunityPublication publication)
        {
            lock (sync)
            {
                CommunityPublicationRegistryRecord record = Find(localListId);
                publication = record != null ? ToPublication(record) : null;
                return record != null;
            }
        }

        public bool TryGetPublicationLock(string localListId, out bool isLocked, out CommunityError error)
        {
            lock (sync)
            {
                isLocked = true;
                error = null;
                if (!ReloadBeforeMutation())
                {
                    error = loadError ?? new CommunityError(CommunityErrorCode.CorruptCache,
                        "The publication journal could not be read.");
                    return false;
                }
                if (!IsValidLocalListId(localListId))
                {
                    error = new CommunityError(CommunityErrorCode.InvalidContent,
                        "A valid local list ID is required to check its publication lock.");
                    return false;
                }

                CommunityPublicationRegistryRecord record = Find(localListId);
                if (record == null)
                {
                    isLocked = false;
                    return true;
                }

                CommunityPublicationStatus status = (CommunityPublicationStatus)record.status;
                isLocked = IsValidRemoteId(record.activeRemoteItemId)
                    || status == CommunityPublicationStatus.Creating
                    || status == CommunityPublicationStatus.NeedsAgreement
                    || status == CommunityPublicationStatus.Created
                    || status == CommunityPublicationStatus.Submitting
                    || status == CommunityPublicationStatus.Published
                    || status == CommunityPublicationStatus.Unpublishing
                    || status == CommunityPublicationStatus.RecoveryRequired;
                return true;
            }
        }

        public CommunityResult<CommunityPublication> BeginCreate(string localListId, string title,
            string contentHash, string publicationFingerprint, string operationId)
        {
            lock (sync)
            {
                if (!ReloadBeforeMutation()) return StorageFailure<CommunityPublication>(loadError.Message);
                if (loadError != null) return StorageFailure<CommunityPublication>(loadError.Message);
                if (!IsValidLocalListId(localListId) || !IsSha256(contentHash)
                    || !IsSha256(publicationFingerprint) || string.IsNullOrWhiteSpace(operationId))
                    return Failure(CommunityErrorCode.InvalidContent,
                        "A local list ID, content hash, publication fingerprint, and operation ID are required.");
                CommunityPublicationRegistryRecord record = Find(localListId);
                if (record != null && (record.status == (int)CommunityPublicationStatus.Creating
                    || record.status == (int)CommunityPublicationStatus.Submitting
                    || record.status == (int)CommunityPublicationStatus.Unpublishing
                    || record.status == (int)CommunityPublicationStatus.RecoveryRequired))
                    return Failure(CommunityErrorCode.Conflict, "This publication has an operation that must be reconciled first.");
                if (record != null && IsValidRemoteId(record.activeRemoteItemId))
                    return Failure(CommunityErrorCode.Conflict, "This local list already has a published remote item.");

                CommunityPublicationRegistryDocument next = CloneDocument();
                CommunityPublicationRegistryRecord nextRecord = FindIn(next, localListId);
                if (nextRecord == null)
                {
                    nextRecord = new CommunityPublicationRegistryRecord
                    {
                        localListId = localListId,
                        previousRemoteItemIds = new List<string>()
                    };
                    next.publications.Add(nextRecord);
                }
                nextRecord.title = title ?? string.Empty;
                nextRecord.contentHash = contentHash.ToLowerInvariant();
                nextRecord.publicationFingerprint = publicationFingerprint.ToLowerInvariant();
                nextRecord.status = (int)CommunityPublicationStatus.Creating;
                nextRecord.recoveryFromStatus = (int)CommunityPublicationStatus.Unpublished;
                nextRecord.needsLegalAgreement = false;
                nextRecord.operationId = operationId;
                nextRecord.lastError = string.Empty;
                if (!TryPersist(next)) return StorageFailure<CommunityPublication>("The create intent could not be written to the publication journal.");
                return CommunityResult<CommunityPublication>.Success(ToPublication(nextRecord), operationId);
            }
        }

        public CommunityResult<CommunityPublication> RecordCreatedRemoteId(string localListId,
            string operationId, CommunityItemKey key, bool needsLegalAgreement, bool recoveryRequired = false)
        {
            lock (sync)
            {
                if (!ReloadBeforeMutation()) return StorageFailure<CommunityPublication>(loadError.Message, operationId);
                CommunityPublicationRegistryRecord existing = Find(localListId);
                if (existing == null || existing.operationId != operationId
                    || existing.status != (int)CommunityPublicationStatus.Creating)
                    return Failure(CommunityErrorCode.Conflict, "The create result does not match the pending journal entry.", operationId);
                if (key == null || key.ConsumerAppId != Scope.ConsumerAppId)
                    return Failure(CommunityErrorCode.InvalidContent, "The created remote item belongs to a different app.", operationId);

                CommunityPublicationRegistryDocument next = CloneDocument();
                CommunityPublicationRegistryRecord record = FindIn(next, localListId);
                record.activeRemoteItemId = key.RemoteItemId.ToString(CultureInfo.InvariantCulture);
                record.status = (int)(recoveryRequired ? CommunityPublicationStatus.RecoveryRequired
                    : needsLegalAgreement ? CommunityPublicationStatus.NeedsAgreement : CommunityPublicationStatus.Created);
                record.recoveryFromStatus = recoveryRequired
                    ? (int)CommunityPublicationStatus.Creating
                    : (int)CommunityPublicationStatus.Unpublished;
                record.needsLegalAgreement = needsLegalAgreement;
                record.lastError = recoveryRequired
                    ? "Remote create returned an ID with an unknown operation outcome; reconcile before retrying."
                    : string.Empty;
                if (!TryPersist(next))
                {
                    var recovery = new CommunityPublication(localListId, key.RemoteItemId,
                        ParseIds(existing.previousRemoteItemIds), CommunityPublicationStatus.RecoveryRequired,
                        needsLegalAgreement, "Remote item was created, but its ID could not be persisted. Reconcile before retrying.",
                        existing.contentHash, existing.title, existing.publicationFingerprint, operationId);
                    return CommunityResult<CommunityPublication>.RecoveryRequired(recovery,
                        new CommunityError(CommunityErrorCode.RecoveryRequired,
                            "Remote item was created, but its ID could not be persisted. The durable journal remains pending for reconciliation."), operationId);
                }
                if (recoveryRequired)
                    return CommunityResult<CommunityPublication>.RecoveryRequired(ToPublication(record),
                        new CommunityError(CommunityErrorCode.RecoveryRequired,
                            "Remote create returned an ID with an unknown operation outcome; reconcile before retrying."), operationId);
                return needsLegalAgreement
                    ? CommunityResult<CommunityPublication>.NeedsAgreement(ToPublication(record), operationId)
                    : CommunityResult<CommunityPublication>.Success(ToPublication(record), operationId);
            }
        }

        public CommunityResult<CommunityPublication> MarkSubmitting(string localListId, string operationId)
        {
            return BeginOperation(localListId, operationId, record =>
            {
                if (!IsValidRemoteId(record.activeRemoteItemId)) return "A created remote item is required before submit.";
                if (record.status != (int)CommunityPublicationStatus.Created
                    && record.status != (int)CommunityPublicationStatus.Failed
                    && record.status != (int)CommunityPublicationStatus.NeedsAgreement)
                    return "The publication is not in a submit-ready state.";
                record.status = (int)CommunityPublicationStatus.Submitting;
                record.needsLegalAgreement = false;
                record.lastError = string.Empty;
                return null;
            });
        }

        public CommunityResult<CommunityPublication> MarkPublished(string localListId, string operationId,
            CommunityItemKey key)
        {
            return Change(localListId, operationId, record =>
            {
                if (key == null || !string.Equals(record.activeRemoteItemId,
                    key.RemoteItemId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                    return "The submit result does not match the active remote item.";
                record.status = (int)CommunityPublicationStatus.Published;
                record.recoveryFromStatus = (int)CommunityPublicationStatus.Unpublished;
                record.needsLegalAgreement = false;
                record.lastError = string.Empty;
                return null;
            });
        }

        public CommunityResult<CommunityPublication> MarkNeedsAgreement(string localListId,
            string operationId, string message)
        {
            return Change(localListId, operationId, record =>
            {
                record.status = (int)CommunityPublicationStatus.NeedsAgreement;
                record.needsLegalAgreement = true;
                record.lastError = message ?? "Accept the Workshop legal agreement before submitting.";
                return null;
            });
        }

        public CommunityResult<CommunityPublication> MarkRecoveryRequired(string localListId,
            string operationId, string message)
        {
            return Change(localListId, operationId, record =>
            {
                if (record.status != (int)CommunityPublicationStatus.RecoveryRequired)
                    record.recoveryFromStatus = record.status;
                record.status = (int)CommunityPublicationStatus.RecoveryRequired;
                record.lastError = message ?? "The remote operation needs reconciliation before it can be retried.";
                return null;
            });
        }

        public CommunityResult<CommunityPublication> MarkFailed(string localListId,
            string operationId, string message)
        {
            return Change(localListId, operationId, record =>
            {
                record.status = (int)CommunityPublicationStatus.Failed;
                record.recoveryFromStatus = (int)CommunityPublicationStatus.Unpublished;
                record.lastError = message ?? "The community publication operation failed.";
                return null;
            });
        }

        public CommunityResult<CommunityPublication> CancelCreateBeforeCall(string localListId,
            string operationId)
        {
            return Change(localListId, operationId, record =>
            {
                if (record.status != (int)CommunityPublicationStatus.Creating
                    || IsValidRemoteId(record.activeRemoteItemId))
                    return "The create operation has already been submitted and cannot be safely rolled back.";
                record.status = (int)CommunityPublicationStatus.Failed;
                record.recoveryFromStatus = (int)CommunityPublicationStatus.Unpublished;
                record.lastError = "Create was canceled before contacting the community service.";
                return null;
            });
        }

        public CommunityResult<CommunityPublication> CancelSubmitBeforeCall(string localListId,
            string operationId)
        {
            return Change(localListId, operationId, record =>
            {
                if (record.status != (int)CommunityPublicationStatus.Submitting
                    || !IsValidRemoteId(record.activeRemoteItemId))
                    return "The submit operation has already been submitted and cannot be safely rolled back.";
                record.status = (int)CommunityPublicationStatus.Created;
                record.recoveryFromStatus = (int)CommunityPublicationStatus.Unpublished;
                record.lastError = string.Empty;
                return null;
            });
        }

        public CommunityResult<CommunityPublication> MarkUnpublishing(string localListId,
            string operationId, ulong remoteItemId)
        {
            return BeginOperation(localListId, operationId, record =>
            {
                if (!string.Equals(record.activeRemoteItemId,
                    remoteItemId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                    return "The requested remote item is not the active publication.";
                if (record.status != (int)CommunityPublicationStatus.Published
                    && record.status != (int)CommunityPublicationStatus.Failed)
                    return "The publication is not in an unpublish-ready state.";
                record.status = (int)CommunityPublicationStatus.Unpublishing;
                record.recoveryFromStatus = (int)CommunityPublicationStatus.Unpublished;
                return null;
            });
        }

        public CommunityResult<CommunityPublication> MarkUnpublished(string localListId,
            string operationId, ulong remoteItemId)
        {
            return Change(localListId, operationId, record =>
            {
                if (record.status != (int)CommunityPublicationStatus.Unpublishing
                    || !string.Equals(record.activeRemoteItemId,
                    remoteItemId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                    return "The unpublish result does not match the pending remote item.";
                record.previousRemoteItemIds = record.previousRemoteItemIds ?? new List<string>();
                if (!record.previousRemoteItemIds.Contains(record.activeRemoteItemId))
                    record.previousRemoteItemIds.Add(record.activeRemoteItemId);
                record.activeRemoteItemId = string.Empty;
                record.status = (int)CommunityPublicationStatus.Unpublished;
                record.recoveryFromStatus = (int)CommunityPublicationStatus.Unpublished;
                record.needsLegalAgreement = false;
                record.lastError = string.Empty;
                return null;
            });
        }

        public CommunityResult<CommunityPublication> ReconcileKnownRemote(string localListId,
            string operationId, CommunityItemKey key, ulong remoteOwnerAccountId,
            CommunityRemoteStatus remoteStatus, string remoteContentHash, string remoteTitle,
            bool fullMetadataVerified)
        {
            lock (sync)
            {
                if (!ReloadBeforeMutation()) return StorageFailure<CommunityPublication>(loadError.Message, operationId);
                if (string.IsNullOrWhiteSpace(operationId))
                    return Failure(CommunityErrorCode.InvalidContent, "A reconcile operation ID is required.");
                CommunityPublicationRegistryRecord current = Find(localListId);
                if (current == null) return Failure(CommunityErrorCode.NotFound, "No publication journal entry exists.", operationId);
                CommunityPublication currentView = ToPublication(current);
                if (key == null || key.ConsumerAppId != Scope.ConsumerAppId
                    || remoteOwnerAccountId == 0 || remoteOwnerAccountId != Scope.AccountId
                    || !string.Equals(current.activeRemoteItemId,
                        key.RemoteItemId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                    return CommunityResult<CommunityPublication>.RecoveryRequired(currentView,
                        new CommunityError(CommunityErrorCode.Conflict,
                            "The remote item ID, app, or owner does not match this account's journal."), operationId);
                if (!IsSha256(remoteContentHash)
                    || !string.Equals(remoteContentHash, current.contentHash, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(remoteTitle ?? string.Empty, current.title ?? string.Empty, StringComparison.Ordinal))
                    return CommunityResult<CommunityPublication>.RecoveryRequired(currentView,
                        new CommunityError(CommunityErrorCode.Conflict,
                            "The remote payload hash or title does not match the saved publication."), operationId);

                CommunityPublicationRegistryDocument next = CloneDocument();
                CommunityPublicationRegistryRecord record = FindIn(next, localListId);
                if (remoteStatus == CommunityRemoteStatus.Available)
                {
                    record.status = (int)CommunityPublicationStatus.Published;
                    record.recoveryFromStatus = (int)CommunityPublicationStatus.Unpublished;
                    record.needsLegalAgreement = false;
                    record.lastError = fullMetadataVerified ? string.Empty
                        : "Remote owner, title, and payload hash were verified; full metadata fingerprint was not rechecked.";
                }
                else if (remoteStatus == CommunityRemoteStatus.Private)
                {
                    bool unpublishWasIntent = record.status == (int)CommunityPublicationStatus.Unpublishing
                        || (record.status == (int)CommunityPublicationStatus.RecoveryRequired
                            && record.recoveryFromStatus == (int)CommunityPublicationStatus.Unpublishing);
                    if (unpublishWasIntent)
                    {
                        record.previousRemoteItemIds = record.previousRemoteItemIds ?? new List<string>();
                        if (!record.previousRemoteItemIds.Contains(record.activeRemoteItemId))
                            record.previousRemoteItemIds.Add(record.activeRemoteItemId);
                        record.activeRemoteItemId = string.Empty;
                        record.status = (int)CommunityPublicationStatus.Unpublished;
                        record.recoveryFromStatus = (int)CommunityPublicationStatus.Unpublished;
                        record.needsLegalAgreement = false;
                        record.lastError = string.Empty;
                    }
                    else if (record.status == (int)CommunityPublicationStatus.Published)
                    {
                        record.status = (int)CommunityPublicationStatus.RecoveryRequired;
                        record.recoveryFromStatus = (int)CommunityPublicationStatus.Published;
                        record.lastError = "The remote item is private, but this journal has no pending unpublish intent.";
                    }
                    else
                    {
                        record.status = record.needsLegalAgreement
                            ? (int)CommunityPublicationStatus.NeedsAgreement
                            : (int)CommunityPublicationStatus.Created;
                        record.recoveryFromStatus = (int)CommunityPublicationStatus.Unpublished;
                        record.lastError = "The owned item is confirmed private; retry Submit after resolving any legal agreement.";
                    }
                }
                else
                {
                    return CommunityResult<CommunityPublication>.RecoveryRequired(currentView,
                        new CommunityError(CommunityErrorCode.RecoveryRequired,
                            "The remote visibility is not confirmed; no journal state was changed."), operationId);
                }

                record.operationId = operationId;
                if (!TryPersist(next)) return StorageFailure<CommunityPublication>(
                    "The reconciled publication state could not be written to the journal.", operationId);
                CommunityPublication recovered = ToPublication(record);
                return recovered.Status == CommunityPublicationStatus.NeedsAgreement
                    ? CommunityResult<CommunityPublication>.NeedsAgreement(recovered, operationId)
                    : CommunityResult<CommunityPublication>.Success(recovered, operationId);
            }
        }

        private CommunityResult<CommunityPublication> Change(string localListId, string operationId,
            Func<CommunityPublicationRegistryRecord, string> update)
        {
            lock (sync)
            {
                if (!ReloadBeforeMutation()) return StorageFailure<CommunityPublication>(loadError.Message, operationId);
                if (loadError != null) return StorageFailure<CommunityPublication>(loadError.Message);
                CommunityPublicationRegistryRecord current = Find(localListId);
                if (current == null) return Failure(CommunityErrorCode.NotFound, "No publication journal entry exists.", operationId);
                if (!string.IsNullOrWhiteSpace(operationId) && current.operationId != operationId)
                    return Failure(CommunityErrorCode.Conflict, "The operation ID does not match the publication journal.", operationId);
                CommunityPublicationRegistryDocument next = CloneDocument();
                CommunityPublicationRegistryRecord record = FindIn(next, localListId);
                string error = update(record);
                if (error != null) return Failure(CommunityErrorCode.Conflict, error, operationId);
                if (!TryPersist(next)) return StorageFailure<CommunityPublication>(
                    "The publication state could not be written to the journal.", operationId);
                return CommunityResult<CommunityPublication>.Success(ToPublication(record), operationId);
            }
        }

        private CommunityResult<CommunityPublication> BeginOperation(string localListId, string operationId,
            Func<CommunityPublicationRegistryRecord, string> update)
        {
            lock (sync)
            {
                if (!ReloadBeforeMutation()) return StorageFailure<CommunityPublication>(loadError.Message, operationId);
                if (string.IsNullOrWhiteSpace(operationId))
                    return Failure(CommunityErrorCode.InvalidContent, "A new operation ID is required.");
                CommunityPublicationRegistryRecord current = Find(localListId);
                if (current == null) return Failure(CommunityErrorCode.NotFound, "No publication journal entry exists.", operationId);
                CommunityPublicationRegistryDocument next = CloneDocument();
                CommunityPublicationRegistryRecord record = FindIn(next, localListId);
                string error = update(record);
                if (error != null) return Failure(CommunityErrorCode.Conflict, error, operationId);
                record.operationId = operationId;
                if (!TryPersist(next)) return StorageFailure<CommunityPublication>(
                    "The publication state could not be written to the journal.", operationId);
                return CommunityResult<CommunityPublication>.Success(ToPublication(record), operationId);
            }
        }

        private void LoadFromDisk()
        {
            lock (sync)
            {
                ReloadBeforeMutation();
            }
        }

        // Static per-canonical-path locking protects concurrent instances in this process.
        // Cross-process writes still require an OS file lock if the game ever permits multiple instances.
        private bool ReloadBeforeMutation()
        {
            if (!File.Exists(filePath))
            {
                document = NewDocument();
                loadError = null;
                return true;
            }
            try
            {
                string json = File.ReadAllText(filePath);
                CommunityPublicationRegistryDocument loaded = JsonUtility.FromJson<CommunityPublicationRegistryDocument>(json);
                ValidateDocument(loaded);
                document = loaded;
                loadError = null;
                return true;
            }
            catch (Exception exception)
            {
                // Keep the last in-memory document and the corrupt on-disk file untouched; all writes fail closed.
                loadError = new CommunityError(CommunityErrorCode.CorruptCache,
                    "The publication journal could not be loaded: " + exception.Message);
                return false;
            }
        }

        private static void ValidateDocument(CommunityPublicationRegistryDocument loaded)
        {
            if (loaded == null || loaded.schemaVersion != DocumentSchemaVersion || loaded.publications == null)
                throw new InvalidDataException("The publication journal schema is invalid.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (CommunityPublicationRegistryRecord record in loaded.publications)
            {
                if (record == null || !IsValidLocalListId(record.localListId)
                    || !IsSha256(record.contentHash) || !IsSha256(record.publicationFingerprint)
                    || !Enum.IsDefined(typeof(CommunityPublicationStatus), record.status)
                    || !Enum.IsDefined(typeof(CommunityPublicationStatus), record.recoveryFromStatus)
                    || !seen.Add(record.localListId))
                    throw new InvalidDataException("The publication journal contains an invalid record.");
                record.previousRemoteItemIds = record.previousRemoteItemIds ?? new List<string>();
            }
        }

        private bool TryPersist(CommunityPublicationRegistryDocument next)
        {
            string directory = Path.GetDirectoryName(filePath);
            string temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(next));
                if (File.Exists(filePath)) File.Replace(temporaryPath, filePath, null);
                else File.Move(temporaryPath, filePath);
                document = next;
                loadError = null;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return false;
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private CommunityPublicationRegistryDocument CloneDocument()
        {
            var copy = new CommunityPublicationRegistryDocument { schemaVersion = document.schemaVersion };
            foreach (CommunityPublicationRegistryRecord item in document.publications)
            {
                copy.publications.Add(new CommunityPublicationRegistryRecord
                {
                    localListId = item.localListId,
                    title = item.title,
                    contentHash = item.contentHash,
                    publicationFingerprint = item.publicationFingerprint,
                    activeRemoteItemId = item.activeRemoteItemId,
                    previousRemoteItemIds = item.previousRemoteItemIds != null
                        ? new List<string>(item.previousRemoteItemIds) : new List<string>(),
                    status = item.status,
                    needsLegalAgreement = item.needsLegalAgreement,
                    recoveryFromStatus = item.recoveryFromStatus,
                    operationId = item.operationId,
                    lastError = item.lastError
                });
            }
            return copy;
        }

        private CommunityPublicationRegistryRecord Find(string localListId) => FindIn(document, localListId);

        private static CommunityPublicationRegistryRecord FindIn(CommunityPublicationRegistryDocument source,
            string localListId) => source.publications.FirstOrDefault(item =>
                string.Equals(item.localListId, localListId, StringComparison.Ordinal));

        private static CommunityPublication ToPublication(CommunityPublicationRegistryRecord record)
        {
            ulong? active = ulong.TryParse(record.activeRemoteItemId, NumberStyles.None,
                CultureInfo.InvariantCulture, out ulong remoteId) && remoteId != 0 ? remoteId : (ulong?)null;
            return new CommunityPublication(record.localListId, active, ParseIds(record.previousRemoteItemIds),
                (CommunityPublicationStatus)record.status, record.needsLegalAgreement, record.lastError,
                record.contentHash, record.title, record.publicationFingerprint, record.operationId,
                (CommunityPublicationStatus)record.recoveryFromStatus);
        }

        private static List<ulong> ParseIds(IEnumerable<string> ids)
        {
            var result = new List<ulong>();
            if (ids == null) return result;
            foreach (string id in ids)
                if (ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed) && parsed != 0)
                    result.Add(parsed);
            return result;
        }

        private static bool IsValidRemoteId(string id) =>
            ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed) && parsed != 0;

        private static bool IsValidLocalListId(string id) => !string.IsNullOrWhiteSpace(id)
            && id.Length <= MaxLocalListIdLength && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

        private static bool IsSha256(string hash)
        {
            if (string.IsNullOrEmpty(hash) || hash.Length != 64) return false;
            foreach (char value in hash)
                if (!((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F')))
                    return false;
            return true;
        }

        private static CommunityPublicationRegistryDocument NewDocument() => new CommunityPublicationRegistryDocument();

        private static CommunityResult<CommunityPublication> Failure(CommunityErrorCode code,
            string message, string operationId = null) => CommunityResult<CommunityPublication>.Failure(
                new CommunityError(code, message), operationId);

        private static CommunityResult<CommunityPublication> StorageFailure<T>(string message, string operationId = null) =>
            CommunityResult<CommunityPublication>.Failure(new CommunityError(CommunityErrorCode.StorageFailure, message, true), operationId);
    }
}
