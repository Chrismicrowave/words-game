using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Words.Community
{
    [Serializable]
    internal sealed class CommunityAccountStateDocument
    {
        public int schemaVersion = 1;
        public List<string> hiddenRemoteItemIds = new List<string>();
        public List<CommunityPersonalRecordWire> records = new List<CommunityPersonalRecordWire>();
    }

    [Serializable]
    internal sealed class CommunityPersonalRecordWire
    {
        public string remoteItemId;
        public string contentHash;
        public int highestStars;
        public int playCount;
        public string lastPlayedAtUtc;
        public CommunityRunSummaryWire latest;
        public CommunityRunSummaryWire best;
        public List<CommunityRunSummaryWire> history = new List<CommunityRunSummaryWire>();
    }

    [Serializable]
    internal sealed class CommunityRunSummaryWire
    {
        public float elapsedSeconds;
        public int errors;
        public int stars;
        public string completedAtUtc;
    }

    /// <summary>Per-app/per-account local hidden items and local run records.</summary>
    public sealed class CommunityAccountStateStore
    {
        private const int DocumentSchemaVersion = 1;
        private const int MaxHistoryPerVersion = 20;
        private static readonly ConcurrentDictionary<string, object> PathLocks =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly object sync;
        private readonly string filePath;
        private CommunityAccountStateDocument document;
        private CommunityError loadError;

        public CommunityAccountScope Scope { get; }

        public CommunityAccountStateStore(string rootPath, CommunityAccountScope scope)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("An account-state root is required.", nameof(rootPath));
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            string root = Path.GetFullPath(rootPath);
            filePath = Path.GetFullPath(Path.Combine(root,
                scope.ConsumerAppId.ToString(CultureInfo.InvariantCulture),
                scope.AccountId.ToString(CultureInfo.InvariantCulture), "community-state.json"));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The account-state path escaped its configured root.", nameof(rootPath));
            sync = PathLocks.GetOrAdd(filePath, _ => new object());
            lock (sync) Reload();
        }

        public bool TrySetHidden(CommunityItemKey key, bool isHidden, out CommunityError error)
        {
            lock (sync)
            {
                if (!Reload()) { error = loadError; return false; }
                if (!IsForThisApp(key))
                {
                    error = new CommunityError(CommunityErrorCode.InvalidContent, "The hidden item belongs to a different app.");
                    return false;
                }
                CommunityAccountStateDocument next = CloneDocument();
                string id = key.RemoteItemId.ToString(CultureInfo.InvariantCulture);
                bool exists = next.hiddenRemoteItemIds.Contains(id);
                if (isHidden && !exists) next.hiddenRemoteItemIds.Add(id);
                else if (!isHidden && exists) next.hiddenRemoteItemIds.Remove(id);
                if (!TryPersist(next, out error)) return false;
                error = null;
                return true;
            }
        }

        public bool TryGetHidden(CommunityItemKey key, out bool isHidden, out CommunityError error)
        {
            lock (sync)
            {
                isHidden = false;
                if (!Reload()) { error = loadError; return false; }
                if (!IsForThisApp(key))
                {
                    error = new CommunityError(CommunityErrorCode.InvalidContent, "The hidden item belongs to a different app.");
                    return false;
                }
                isHidden = document.hiddenRemoteItemIds.Contains(key.RemoteItemId.ToString(CultureInfo.InvariantCulture));
                error = null;
                return true;
            }
        }

        public bool TryGetHiddenItems(out IReadOnlyList<CommunityItemKey> keys, out CommunityError error)
        {
            lock (sync)
            {
                keys = Array.Empty<CommunityItemKey>();
                if (!Reload()) { error = loadError; return false; }

                var result = new List<CommunityItemKey>();
                foreach (string remoteItemId in document.hiddenRemoteItemIds)
                {
                    if (!ulong.TryParse(remoteItemId, NumberStyles.None, CultureInfo.InvariantCulture,
                        out ulong parsedId) || parsedId == 0)
                    {
                        error = new CommunityError(CommunityErrorCode.CorruptCache,
                            "The hidden-item list contains an invalid remote ID.");
                        return false;
                    }
                    result.Add(new CommunityItemKey(Scope.ConsumerAppId, parsedId));
                }

                keys = result.AsReadOnly();
                error = null;
                return true;
            }
        }

        public bool TryRecordRun(CommunityItemKey key, string contentHash, float elapsedSeconds,
            int errors, int stars, out CommunityPersonalRecord record, out CommunityError error)
        {
            lock (sync)
            {
                record = null;
                if (!Reload()) { error = loadError; return false; }
                if (!IsForThisApp(key) || !IsSha256(contentHash) || float.IsNaN(elapsedSeconds)
                    || float.IsInfinity(elapsedSeconds) || elapsedSeconds < 0 || elapsedSeconds > 1000000f
                    || errors < 0 || errors > 1000000 || stars < 0 || stars > 3)
                {
                    error = new CommunityError(CommunityErrorCode.InvalidContent, "The community run record data is invalid.");
                    return false;
                }

                CommunityAccountStateDocument next = CloneDocument();
                string normalizedHash = contentHash.ToLowerInvariant();
                CommunityPersonalRecordWire wire = FindRecord(next, key.RemoteItemId, normalizedHash);
                if (wire == null)
                {
                    wire = new CommunityPersonalRecordWire
                    {
                        remoteItemId = key.RemoteItemId.ToString(CultureInfo.InvariantCulture),
                        contentHash = normalizedHash,
                        history = new List<CommunityRunSummaryWire>()
                    };
                    next.records.Add(wire);
                }
                DateTime now = DateTime.UtcNow;
                var latest = new CommunityRunSummaryWire
                {
                    elapsedSeconds = elapsedSeconds,
                    errors = errors,
                    stars = stars,
                    completedAtUtc = now.ToString("o", CultureInfo.InvariantCulture)
                };
                wire.latest = latest;
                if (wire.best == null || latest.elapsedSeconds < wire.best.elapsedSeconds)
                    wire.best = CloneSummary(latest);
                wire.highestStars = Math.Max(wire.highestStars, stars);
                wire.playCount++;
                wire.lastPlayedAtUtc = latest.completedAtUtc;
                wire.history = wire.history ?? new List<CommunityRunSummaryWire>();
                wire.history.Add(CloneSummary(latest));
                if (wire.history.Count > MaxHistoryPerVersion)
                    wire.history.RemoveRange(0, wire.history.Count - MaxHistoryPerVersion);
                if (!TryPersist(next, out error)) return false;
                record = ToRecord(key, wire);
                error = null;
                return true;
            }
        }

        public bool TryGetRecord(CommunityItemKey key, string contentHash,
            out CommunityPersonalRecord record, out CommunityError error)
        {
            lock (sync)
            {
                record = null;
                if (!Reload()) { error = loadError; return false; }
                if (!IsForThisApp(key) || !IsSha256(contentHash))
                {
                    error = new CommunityError(CommunityErrorCode.InvalidContent, "A valid item key and content hash are required.");
                    return false;
                }
                CommunityPersonalRecordWire wire = FindRecord(document, key.RemoteItemId, contentHash.ToLowerInvariant());
                record = wire != null ? ToRecord(key, wire) : null;
                error = null;
                return wire != null;
            }
        }

        private bool Reload()
        {
            if (!File.Exists(filePath))
            {
                document = NewDocument();
                loadError = null;
                return true;
            }
            try
            {
                CommunityAccountStateDocument loaded = JsonUtility.FromJson<CommunityAccountStateDocument>(File.ReadAllText(filePath));
                Validate(loaded);
                document = loaded;
                loadError = null;
                return true;
            }
            catch (Exception exception)
            {
                loadError = new CommunityError(CommunityErrorCode.CorruptCache,
                    "Community account records are corrupt and were left unchanged: " + exception.Message);
                return false;
            }
        }

        private bool TryPersist(CommunityAccountStateDocument next, out CommunityError error)
        {
            string temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(next));
                if (File.Exists(filePath)) File.Replace(temporaryPath, filePath, null);
                else File.Move(temporaryPath, filePath);
                document = next;
                error = null;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                error = new CommunityError(CommunityErrorCode.StorageFailure, exception.Message, true);
                return false;
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void Validate(CommunityAccountStateDocument loaded)
        {
            if (loaded == null || loaded.schemaVersion != DocumentSchemaVersion
                || loaded.hiddenRemoteItemIds == null || loaded.records == null)
                throw new InvalidDataException("The community account-state schema is invalid.");
            foreach (string id in loaded.hiddenRemoteItemIds)
                if (!ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed) || parsed == 0)
                    throw new InvalidDataException("The hidden-item list contains an invalid ID.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (CommunityPersonalRecordWire record in loaded.records)
            {
                if (record == null || !ulong.TryParse(record.remoteItemId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong remoteId)
                    || remoteId == 0 || !IsSha256(record.contentHash) || record.playCount < 0
                    || record.highestStars < 0 || record.highestStars > 3
                    || !seen.Add(record.remoteItemId + ":" + record.contentHash.ToLowerInvariant()))
                    throw new InvalidDataException("A community personal record is invalid.");
                record.history = record.history ?? new List<CommunityRunSummaryWire>();
                ValidateSummary(record.latest);
                ValidateSummary(record.best);
                foreach (CommunityRunSummaryWire item in record.history) ValidateSummary(item);
            }
        }

        private static void ValidateSummary(CommunityRunSummaryWire summary)
        {
            if (summary == null) return;
            if (float.IsNaN(summary.elapsedSeconds) || float.IsInfinity(summary.elapsedSeconds)
                || summary.elapsedSeconds < 0 || summary.elapsedSeconds > 1000000f
                || summary.errors < 0 || summary.errors > 1000000 || summary.stars < 0 || summary.stars > 3
                || !DateTime.TryParse(summary.completedAtUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out _))
                throw new InvalidDataException("A community run summary is invalid.");
        }

        private bool IsForThisApp(CommunityItemKey key) => key != null && key.ConsumerAppId == Scope.ConsumerAppId;

        private CommunityAccountStateDocument CloneDocument()
        {
            string json = JsonUtility.ToJson(document);
            return JsonUtility.FromJson<CommunityAccountStateDocument>(json);
        }

        private static CommunityPersonalRecordWire FindRecord(CommunityAccountStateDocument source,
            ulong remoteId, string hash) => source.records.FirstOrDefault(item =>
                item.remoteItemId == remoteId.ToString(CultureInfo.InvariantCulture)
                && string.Equals(item.contentHash, hash, StringComparison.OrdinalIgnoreCase));

        private static CommunityPersonalRecord ToRecord(CommunityItemKey key, CommunityPersonalRecordWire wire)
        {
            DateTime.TryParse(wire.lastPlayedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime lastPlayed);
            return new CommunityPersonalRecord(key, wire.contentHash, ToSummary(wire.latest), ToSummary(wire.best),
                wire.highestStars, wire.playCount, lastPlayed,
                (wire.history ?? new List<CommunityRunSummaryWire>()).Select(ToSummary));
        }

        private static CommunityRunSummary ToSummary(CommunityRunSummaryWire wire)
        {
            if (wire == null) return null;
            DateTime.TryParse(wire.completedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime completed);
            return new CommunityRunSummary(wire.elapsedSeconds, wire.errors, wire.stars, completed);
        }

        private static CommunityRunSummaryWire CloneSummary(CommunityRunSummaryWire source) =>
            new CommunityRunSummaryWire
            {
                elapsedSeconds = source.elapsedSeconds,
                errors = source.errors,
                stars = source.stars,
                completedAtUtc = source.completedAtUtc
            };

        private static CommunityAccountStateDocument NewDocument() => new CommunityAccountStateDocument();

        private static bool IsSha256(string hash)
        {
            if (string.IsNullOrEmpty(hash) || hash.Length != 64) return false;
            foreach (char value in hash)
                if (!((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F')))
                    return false;
            return true;
        }
    }
}
