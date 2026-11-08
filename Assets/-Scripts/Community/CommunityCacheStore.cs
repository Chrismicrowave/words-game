using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Words.Community
{
    [Serializable]
    internal sealed class CommunityCacheManifest
    {
        public int schemaVersion;
        public string consumerAppId;
        public string remoteItemId;
        public string contentHash;
        public string title;
        public string author;
        public string cachedAtUtc;
    }

    /// <summary>
    /// App-owned, version-addressed community cache. Account identity is intentionally absent so
    /// downloaded content remains playable after sign-out or account changes.
    /// </summary>
    public sealed class CommunityCacheStore
    {
        private const string PayloadFileName = CommunityContentCodec.PayloadFileName;
        private const string ManifestFileName = "manifest.json";
        private readonly string rootPath;
        private readonly CommunityContentLimits limits;

        public CommunityCacheStore(string rootPath, CommunityContentLimits limits = null)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("A cache root is required.", nameof(rootPath));
            this.rootPath = Path.GetFullPath(rootPath);
            this.limits = limits ?? CommunityContentLimits.Default;
        }

        public CommunityResult<CommunityCacheEntry> Commit(CommunityDownloadReceipt receipt)
        {
            if (receipt == null)
                return Fail("A download receipt is required.");

            byte[] payload = receipt.CopyPayload();
            if (!CommunityContentCodec.TryDecodeAndValidate(payload, receipt.SchemaVersion,
                receipt.ExpectedContentHash, out CommunityDecodedContent decoded, out CommunityError validationError, limits))
                return CommunityResult<CommunityCacheEntry>.Failure(validationError);

            return CommitValidated(receipt.Key, receipt.SchemaVersion, decoded.ContentHash,
                receipt.Title, receipt.Author, payload, decoded.Content);
        }

        public CommunityResult<CommunityCacheEntry> Commit(CommunityItemKey key, int schemaVersion,
            string title, string author, WordListLibraryContent content)
        {
            if (key == null) return Fail("A community item key is required.");
            if (!CommunityContentCodec.TryEncode(content, schemaVersion, out byte[] payload,
                out CommunityDecodedContent decoded, out CommunityError validationError, limits))
                return CommunityResult<CommunityCacheEntry>.Failure(validationError);
            return CommitValidated(key, schemaVersion, decoded.ContentHash, title, author, payload, decoded.Content);
        }

        public CommunityResult<CommunityCacheEntry> Load(CommunityItemKey key, int schemaVersion, string contentHash)
        {
            if (!TryBuildEntryPath(key, schemaVersion, contentHash, out string entryPath, out CommunityError pathError))
                return CommunityResult<CommunityCacheEntry>.Failure(pathError);
            return LoadAtPath(key, schemaVersion, contentHash, entryPath);
        }

        public CommunityCacheStatus GetStatus(CommunityItemKey key, int schemaVersion, string contentHash)
        {
            CommunityResult<CommunityCacheEntry> result = Load(key, schemaVersion, contentHash);
            if (result.Succeeded) return CommunityCacheStatus.Cached;
            return result.Error != null && result.Error.Code == CommunityErrorCode.CorruptCache
                ? CommunityCacheStatus.Corrupt : CommunityCacheStatus.Missing;
        }

        public CommunityResult<IReadOnlyList<CommunityCacheEntry>> GetEntries(uint consumerAppId)
        {
            var entries = new List<CommunityCacheEntry>();
            if (consumerAppId == 0)
                return CommunityResult<IReadOnlyList<CommunityCacheEntry>>.Failure(
                    new CommunityError(CommunityErrorCode.InvalidContent, "A consumer app ID is required."));
            string appPath = Path.Combine(rootPath, consumerAppId.ToString(CultureInfo.InvariantCulture));
            if (!TryValidateNoReparsePath(appPath, out CommunityError pathError))
                return CommunityResult<IReadOnlyList<CommunityCacheEntry>>.Failure(pathError);
            if (!Directory.Exists(appPath))
                return CommunityResult<IReadOnlyList<CommunityCacheEntry>>.Success(entries.AsReadOnly());

            try
            {
                foreach (string remotePath in Directory.GetDirectories(appPath))
                {
                    if (!TryValidateNoReparsePath(remotePath, out pathError))
                        return CommunityResult<IReadOnlyList<CommunityCacheEntry>>.Failure(pathError);
                    string remoteName = Path.GetFileName(remotePath);
                    if (!ulong.TryParse(remoteName, NumberStyles.None, CultureInfo.InvariantCulture, out ulong remoteId)
                        || remoteId == 0) continue;
                    foreach (string versionPath in Directory.GetDirectories(remotePath))
                    {
                        if (!TryValidateNoReparsePath(versionPath, out pathError)
                            || !TryValidateNoReparsePath(Path.Combine(versionPath, ManifestFileName), out pathError)
                            || !TryValidateNoReparsePath(Path.Combine(versionPath, PayloadFileName), out pathError))
                            return CommunityResult<IReadOnlyList<CommunityCacheEntry>>.Failure(pathError);
                        CommunityCacheManifest manifest = TryReadManifest(versionPath);
                        if (!TryReadIdentity(manifest, out uint appId, out ulong manifestRemoteId,
                            out int schemaVersion, out string hash) || appId != consumerAppId || manifestRemoteId != remoteId)
                            continue;
                        CommunityResult<CommunityCacheEntry> loaded = Load(new CommunityItemKey(appId, remoteId),
                            schemaVersion, hash);
                        if (loaded.Succeeded) entries.Add(loaded.Value);
                        else if (loaded.Error != null && loaded.Error.Code == CommunityErrorCode.StorageFailure)
                            return CommunityResult<IReadOnlyList<CommunityCacheEntry>>.Failure(loaded.Error);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return CommunityResult<IReadOnlyList<CommunityCacheEntry>>.Failure(
                    new CommunityError(CommunityErrorCode.StorageFailure, exception.Message, true));
            }

            return CommunityResult<IReadOnlyList<CommunityCacheEntry>>.Success(entries.AsReadOnly());
        }

        private CommunityResult<CommunityCacheEntry> CommitValidated(CommunityItemKey key, int schemaVersion,
            string contentHash, string title, string author, byte[] payload, WordListLibraryContent content)
        {
            if (key == null) return Fail("A community item key is required.");
            if (!TryBuildEntryPath(key, schemaVersion, contentHash, out string destination, out CommunityError pathError))
                return CommunityResult<CommunityCacheEntry>.Failure(pathError);

            if (!TryValidateNoReparsePath(destination, out pathError))
                return CommunityResult<CommunityCacheEntry>.Failure(pathError);
            if (Directory.Exists(destination))
                return LoadAtPath(key, schemaVersion, contentHash, destination);

            string parent = Path.GetDirectoryName(destination);
            string staging = Path.Combine(parent, ".staging-" + Guid.NewGuid().ToString("N"));
            DateTime now = DateTime.UtcNow;
            try
            {
                if (!TryValidateNoReparsePath(parent, out pathError))
                    return CommunityResult<CommunityCacheEntry>.Failure(pathError);
                Directory.CreateDirectory(parent);
                if (!TryValidateNoReparsePath(parent, out pathError))
                    return CommunityResult<CommunityCacheEntry>.Failure(pathError);
                Directory.CreateDirectory(staging);
                if (!TryValidateNoReparsePath(staging, out pathError))
                    return CommunityResult<CommunityCacheEntry>.Failure(pathError);
                string payloadPath = Path.Combine(staging, PayloadFileName);
                if (!TryValidateNoReparsePath(payloadPath, out pathError))
                    return CommunityResult<CommunityCacheEntry>.Failure(pathError);
                File.WriteAllBytes(payloadPath, payload);
                var manifest = new CommunityCacheManifest
                {
                    schemaVersion = schemaVersion,
                    consumerAppId = key.ConsumerAppId.ToString(CultureInfo.InvariantCulture),
                    remoteItemId = key.RemoteItemId.ToString(CultureInfo.InvariantCulture),
                    contentHash = contentHash,
                    title = title ?? string.Empty,
                    author = author ?? string.Empty,
                    cachedAtUtc = now.ToString("o", CultureInfo.InvariantCulture)
                };
                string manifestPath = Path.Combine(staging, ManifestFileName);
                if (!TryValidateNoReparsePath(manifestPath, out pathError))
                    return CommunityResult<CommunityCacheEntry>.Failure(pathError);
                File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest));
                if (!TryValidateNoReparsePath(staging, out pathError))
                    return CommunityResult<CommunityCacheEntry>.Failure(pathError);
                Directory.Move(staging, destination);
                return CommunityResult<CommunityCacheEntry>.Success(CreateEntry(key, schemaVersion,
                    contentHash, manifest.title, manifest.author, now, content));
            }
            catch (IOException exception)
            {
                if (Directory.Exists(destination))
                    return LoadAtPath(key, schemaVersion, contentHash, destination);
                return CommunityResult<CommunityCacheEntry>.Failure(
                    new CommunityError(CommunityErrorCode.StorageFailure, exception.Message, true));
            }
            catch (UnauthorizedAccessException exception)
            {
                return CommunityResult<CommunityCacheEntry>.Failure(
                    new CommunityError(CommunityErrorCode.StorageFailure, exception.Message));
            }
            finally
            {
                TryDeleteStaging(staging);
            }
        }

        private CommunityResult<CommunityCacheEntry> LoadAtPath(CommunityItemKey key, int schemaVersion,
            string contentHash, string entryPath)
        {
            string manifestPath = Path.Combine(entryPath, ManifestFileName);
            string payloadPath = Path.Combine(entryPath, PayloadFileName);
            if (!TryValidateNoReparsePath(entryPath, out CommunityError pathError)
                || !TryValidateNoReparsePath(manifestPath, out pathError)
                || !TryValidateNoReparsePath(payloadPath, out pathError))
                return CommunityResult<CommunityCacheEntry>.Failure(pathError);
            if (!File.Exists(manifestPath) || !File.Exists(payloadPath))
                return CommunityResult<CommunityCacheEntry>.Failure(
                    new CommunityError(CommunityErrorCode.CorruptCache, "The cached community item is incomplete."));

            try
            {
                CommunityCacheManifest manifest = TryReadManifest(entryPath);
                if (!TryReadIdentity(manifest, out uint appId, out ulong remoteId, out int storedSchema,
                    out string storedHash) || appId != key.ConsumerAppId || remoteId != key.RemoteItemId
                    || storedSchema != schemaVersion || !string.Equals(storedHash, contentHash, StringComparison.OrdinalIgnoreCase))
                    return Corrupt("The cached community manifest does not match its identity.");

                var info = new FileInfo(payloadPath);
                if (info.Length <= 0 || info.Length > limits.MaxPayloadBytes)
                    return Corrupt("The cached community payload has an invalid size.");
                byte[] payload = File.ReadAllBytes(payloadPath);
                if (!CommunityContentCodec.TryDecodeAndValidate(payload, schemaVersion, contentHash,
                    out CommunityDecodedContent decoded, out CommunityError validationError, limits))
                    return CommunityResult<CommunityCacheEntry>.Failure(new CommunityError(
                        CommunityErrorCode.CorruptCache, validationError != null ? validationError.Message : "The cached word list is invalid."));
                if (!DateTime.TryParse(manifest.cachedAtUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTime cachedAt))
                    return Corrupt("The cached community timestamp is invalid.");
                return CommunityResult<CommunityCacheEntry>.Success(CreateEntry(key, schemaVersion,
                    contentHash, manifest.title, manifest.author, cachedAt, decoded.Content));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return CommunityResult<CommunityCacheEntry>.Failure(
                    new CommunityError(CommunityErrorCode.StorageFailure, exception.Message, true));
            }
            catch (Exception exception)
            {
                return Corrupt("The cached community item could not be read: " + exception.Message);
            }
        }

        private bool TryBuildEntryPath(CommunityItemKey key, int schemaVersion, string hash,
            out string entryPath, out CommunityError error)
        {
            entryPath = null;
            error = null;
            if (key == null || schemaVersion <= 0 || !IsSha256(hash))
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent, "A valid item identity and SHA-256 content hash are required.");
                return false;
            }

            string versionSegment = schemaVersion.ToString(CultureInfo.InvariantCulture) + "-" + hash.ToLowerInvariant();
            entryPath = Path.GetFullPath(Path.Combine(rootPath,
                key.ConsumerAppId.ToString(CultureInfo.InvariantCulture),
                key.RemoteItemId.ToString(CultureInfo.InvariantCulture), versionSegment));
            string rootPrefix = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!entryPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                entryPath = null;
                error = new CommunityError(CommunityErrorCode.InvalidContent, "The cache path is outside its configured root.");
                return false;
            }
            if (!TryValidateNoReparsePath(entryPath, out error))
            {
                entryPath = null;
                return false;
            }
            return true;
        }

        private static bool TryReadIdentity(CommunityCacheManifest manifest, out uint appId,
            out ulong remoteId, out int schemaVersion, out string hash)
        {
            appId = 0;
            remoteId = 0;
            schemaVersion = 0;
            hash = string.Empty;
            return manifest != null
                && uint.TryParse(manifest.consumerAppId, NumberStyles.None, CultureInfo.InvariantCulture, out appId)
                && ulong.TryParse(manifest.remoteItemId, NumberStyles.None, CultureInfo.InvariantCulture, out remoteId)
                && int.TryParse(manifest.schemaVersion.ToString(CultureInfo.InvariantCulture), NumberStyles.None,
                    CultureInfo.InvariantCulture, out schemaVersion)
                && appId != 0 && remoteId != 0 && schemaVersion > 0 && IsSha256(manifest.contentHash)
                && (hash = manifest.contentHash.ToLowerInvariant()) != null;
        }

        private static CommunityCacheManifest TryReadManifest(string entryPath)
        {
            string path = Path.Combine(entryPath, ManifestFileName);
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<CommunityCacheManifest>(json);
        }

        private static CommunityCacheEntry CreateEntry(CommunityItemKey key, int schemaVersion,
            string hash, string title, string author, DateTime cachedAt, WordListLibraryContent content)
        {
            var version = new CommunityContentVersion(schemaVersion, hash);
            var provider = new CommunityWordListProvider(key, version, title, content);
            return new CommunityCacheEntry(key, version, title, author, cachedAt, provider);
        }

        private static bool IsSha256(string hash)
        {
            if (string.IsNullOrEmpty(hash) || hash.Length != 64) return false;
            foreach (char value in hash)
                if (!((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F')))
                    return false;
            return true;
        }

        private static void TryDeleteStaging(string staging)
        {
            try
            {
                if (Directory.Exists(staging)
                    && TryValidateNoReparsePath(staging, out _))
                    Directory.Delete(staging, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static bool TryValidateNoReparsePath(string path, out CommunityError error)
        {
            error = null;
            try
            {
                string fullPath = Path.GetFullPath(path);
                string volumeRoot = Path.GetPathRoot(fullPath);
                string relative = fullPath.Substring(volumeRoot.Length);
                string current = volumeRoot;
                string[] segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);
                for (int index = 0; index < segments.Length; index++)
                {
                    current = Path.Combine(current, segments[index]);
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(current); }
                    catch (FileNotFoundException) { break; }
                    catch (DirectoryNotFoundException) { break; }
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        error = new CommunityError(CommunityErrorCode.StorageFailure,
                            "The community cache path contains a reparse point and was not followed.");
                        return false;
                    }
                    bool isLast = index == segments.Length - 1;
                    if (!isLast && (attributes & FileAttributes.Directory) == 0)
                    {
                        error = new CommunityError(CommunityErrorCode.StorageFailure,
                            "A cache path segment is not a directory.");
                        return false;
                    }
                }
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is ArgumentException || exception is NotSupportedException)
            {
                error = new CommunityError(CommunityErrorCode.StorageFailure,
                    "The community cache path could not be validated: " + exception.Message, true);
                return false;
            }
        }

        private static CommunityResult<CommunityCacheEntry> Corrupt(string message) =>
            CommunityResult<CommunityCacheEntry>.Failure(new CommunityError(CommunityErrorCode.CorruptCache, message));

        private static CommunityResult<CommunityCacheEntry> Fail(string message) =>
            CommunityResult<CommunityCacheEntry>.Failure(new CommunityError(CommunityErrorCode.StorageFailure, message));
    }
}
