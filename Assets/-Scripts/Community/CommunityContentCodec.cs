using System;
using System.Text;
using UnityEngine;

namespace Words.Community
{
    [Serializable]
    internal sealed class CommunityPayloadEnvelope
    {
        public int schemaVersion;
        public string contentHash;
        public WordListLibraryContent content;
    }

    public sealed class CommunityContentLimits
    {
        public int MaxPayloadBytes { get; }
        public int MaxWords { get; }
        public int MaxCharactersPerWord { get; }
        public int MaxTotalCharacters { get; }

        public CommunityContentLimits(int maxPayloadBytes = 2 * 1024 * 1024, int maxWords = 2000,
            int maxCharactersPerWord = 256, int maxTotalCharacters = 128 * 1024)
        {
            if (maxPayloadBytes <= 0 || maxWords <= 0 || maxCharactersPerWord <= 0 || maxTotalCharacters <= 0)
                throw new ArgumentOutOfRangeException("Community content limits must be positive.");
            MaxPayloadBytes = maxPayloadBytes;
            MaxWords = maxWords;
            MaxCharactersPerWord = maxCharactersPerWord;
            MaxTotalCharacters = maxTotalCharacters;
        }

        public static CommunityContentLimits Default => new CommunityContentLimits();
    }

    public sealed class CommunityDecodedContent
    {
        public int SchemaVersion { get; }
        public string ContentHash { get; }
        public WordListLibraryContent Content { get; }

        internal CommunityDecodedContent(int schemaVersion, string contentHash, WordListLibraryContent content)
        {
            SchemaVersion = schemaVersion;
            ContentHash = contentHash;
            Content = content.Clone();
        }
    }

    /// <summary>Uses the canonical library auto codec and content hash; never flattens structured pinyin.</summary>
    public static class CommunityContentCodec
    {
        public const int CurrentSchemaVersion = 1;
        public const string PayloadFileName = "community-wordlist.json";
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static bool TryDecodeAndValidate(byte[] payload, int expectedSchemaVersion,
            string expectedContentHash, out CommunityDecodedContent decoded, out CommunityError error,
            CommunityContentLimits limits = null)
        {
            decoded = null;
            limits = limits ?? CommunityContentLimits.Default;
            if (payload == null || payload.Length == 0 || payload.Length > limits.MaxPayloadBytes)
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent,
                    "The community word-list payload is empty or exceeds the size limit.");
                return false;
            }
            if (expectedSchemaVersion <= 0)
            {
                error = new CommunityError(CommunityErrorCode.Incompatible, "The community schema version is unsupported.");
                return false;
            }

            CommunityPayloadEnvelope envelope;
            try
            {
                envelope = JsonUtility.FromJson<CommunityPayloadEnvelope>(StrictUtf8.GetString(payload));
            }
            catch (Exception exception)
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent,
                    "The community word-list payload could not be decoded: " + exception.Message);
                return false;
            }

            if (envelope == null || envelope.content == null || envelope.schemaVersion != expectedSchemaVersion)
            {
                error = new CommunityError(CommunityErrorCode.Incompatible,
                    "The community word-list schema is missing or unsupported.");
                return false;
            }

            if (!IsSha256(envelope.contentHash))
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent,
                    "The community word-list payload is missing its SHA-256 content hash.");
                return false;
            }

            if (!WordListLibraryContentCodec.TryNormalizeAutoContent(envelope.content,
                envelope.content.LanguageMode, out _, out _, out WordListLibraryContent normalized, out string validationError))
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent,
                    string.IsNullOrWhiteSpace(validationError) ? "The community word list is invalid." : validationError);
                return false;
            }

            if (normalized.Words == null || normalized.Words.Count == 0 || normalized.Words.Count > limits.MaxWords)
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent,
                    "The community word-list count is outside the supported limits.");
                return false;
            }

            int totalCharacters = 0;
            foreach (string word in normalized.Words)
            {
                int length = word == null ? 0 : word.Length;
                totalCharacters += length;
                if (length == 0 || length > limits.MaxCharactersPerWord || totalCharacters > limits.MaxTotalCharacters)
                {
                    error = new CommunityError(CommunityErrorCode.InvalidContent,
                        "The community word list contains an empty or oversized word.");
                    return false;
                }
            }

            string calculatedHash = WordListLibraryService.ComputeContentHash(normalized);
            if (!string.Equals(calculatedHash, envelope.contentHash, StringComparison.OrdinalIgnoreCase))
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent,
                    "The declared community content hash does not match the word-list content.");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedContentHash)
                && (!IsSha256(expectedContentHash)
                    || !string.Equals(calculatedHash, expectedContentHash, StringComparison.OrdinalIgnoreCase)))
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent,
                    "The community content hash does not match the downloaded words.");
                return false;
            }

            decoded = new CommunityDecodedContent(expectedSchemaVersion, calculatedHash, normalized);
            error = null;
            return true;
        }

        public static bool TryEncode(WordListLibraryContent content, int schemaVersion,
            out byte[] payload, out CommunityDecodedContent validated, out CommunityError error,
            CommunityContentLimits limits = null)
        {
            payload = null;
            validated = null;
            if (content == null || schemaVersion <= 0)
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent, "Valid community content and schema are required.");
                return false;
            }

            if (!WordListLibraryContentCodec.TryNormalizeAutoContent(content, content.LanguageMode,
                out _, out _, out WordListLibraryContent normalized, out string validationError))
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent,
                    string.IsNullOrWhiteSpace(validationError) ? "The word-list content is invalid." : validationError);
                return false;
            }

            string hash = WordListLibraryService.ComputeContentHash(normalized);
            string json = JsonUtility.ToJson(new CommunityPayloadEnvelope
            {
                schemaVersion = schemaVersion,
                contentHash = hash,
                content = normalized
            });
            try
            {
                payload = StrictUtf8.GetBytes(json);
            }
            catch (Exception exception)
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent, exception.Message);
                return false;
            }

            if (payload.Length > (limits ?? CommunityContentLimits.Default).MaxPayloadBytes)
            {
                payload = null;
                error = new CommunityError(CommunityErrorCode.InvalidContent,
                    "The community word-list payload exceeds the size limit.");
                return false;
            }

            if (!TryDecodeAndValidate(payload, schemaVersion, hash, out validated, out error, limits))
            {
                payload = null;
                return false;
            }
            error = null;
            return true;
        }

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
