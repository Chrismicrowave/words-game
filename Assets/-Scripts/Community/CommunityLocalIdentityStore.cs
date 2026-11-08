using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Words.Community
{
    [Serializable]
    internal sealed class CommunityLocalIdentityDocument
    {
        public int schemaVersion = 1;
        public string consumerAppId;
        public string lastKnownAccountId;
    }

    /// <summary>App-scoped last-known identity for local-only offline records; never authorizes remote calls.</summary>
    public sealed class CommunityLocalIdentityStore
    {
        private const int DocumentSchemaVersion = 1;
        private static readonly ConcurrentDictionary<string, object> PathLocks =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly object sync;
        private readonly string filePath;
        private readonly uint consumerAppId;
        private ulong lastKnownAccountId;
        private CommunityError loadError;

        public CommunityLocalIdentityStore(string rootPath, uint consumerAppId)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("An identity root is required.", nameof(rootPath));
            if (consumerAppId == 0) throw new ArgumentOutOfRangeException(nameof(consumerAppId));
            this.consumerAppId = consumerAppId;
            string root = Path.GetFullPath(rootPath);
            filePath = Path.GetFullPath(Path.Combine(root, consumerAppId.ToString(CultureInfo.InvariantCulture), "last-known-account.json"));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The identity path escaped its configured root.", nameof(rootPath));
            sync = PathLocks.GetOrAdd(filePath, _ => new object());
            lock (sync) Reload();
        }

        public ulong GetLastKnownAccountId()
        {
            lock (sync)
            {
                Reload();
                return lastKnownAccountId;
            }
        }

        public bool TryRemember(ulong accountId, out CommunityError error)
        {
            lock (sync)
            {
                if (accountId == 0)
                {
                    error = new CommunityError(CommunityErrorCode.InvalidContent, "A nonzero account ID is required.");
                    return false;
                }
                if (!Reload()) { error = loadError; return false; }
                if (lastKnownAccountId == accountId)
                {
                    error = null;
                    return true;
                }
                var document = new CommunityLocalIdentityDocument
                {
                    schemaVersion = DocumentSchemaVersion,
                    consumerAppId = consumerAppId.ToString(CultureInfo.InvariantCulture),
                    lastKnownAccountId = accountId.ToString(CultureInfo.InvariantCulture)
                };
                string temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    File.WriteAllText(temporaryPath, JsonUtility.ToJson(document));
                    if (File.Exists(filePath)) File.Replace(temporaryPath, filePath, null);
                    else File.Move(temporaryPath, filePath);
                    lastKnownAccountId = accountId;
                    loadError = null;
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
        }

        private bool Reload()
        {
            if (!File.Exists(filePath))
            {
                lastKnownAccountId = 0;
                loadError = null;
                return true;
            }
            try
            {
                CommunityLocalIdentityDocument document = JsonUtility.FromJson<CommunityLocalIdentityDocument>(File.ReadAllText(filePath));
                if (document == null || document.schemaVersion != DocumentSchemaVersion
                    || !uint.TryParse(document.consumerAppId, NumberStyles.None, CultureInfo.InvariantCulture, out uint appId)
                    || appId != consumerAppId
                    || !ulong.TryParse(document.lastKnownAccountId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong accountId)
                    || accountId == 0)
                    throw new InvalidDataException("The last-known account identity is invalid.");
                lastKnownAccountId = accountId;
                loadError = null;
                return true;
            }
            catch (Exception exception)
            {
                loadError = new CommunityError(CommunityErrorCode.CorruptCache,
                    "The last-known account identity is corrupt and was left unchanged: " + exception.Message);
                return false;
            }
        }
    }
}
