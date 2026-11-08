using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Words.Community
{
    [Serializable]
    internal sealed class CommunityWorkshopMetadataWire
    {
        public int schemaVersion;
        public string contentHash;
        public string detectedLanguage;
        public int wordCount;
        public string[] previewWords;
    }

    [Serializable]
    internal sealed class CommunityPublicationFingerprintWire
    {
        public string contentHash;
        public string title;
        public string description;
        public string[] tags;
        public string generatedMetadata;
    }

    /// <summary>Small, untrusted Workshop listing metadata. The downloaded payload remains authoritative.</summary>
    public sealed class CommunityWorkshopMetadata
    {
        public const int MaximumPreviewWords = 8;
        public const int MaximumMetadataCharacters = 8192;

        public int SchemaVersion { get; }
        public string ContentHash { get; }
        public string DetectedLanguage { get; }
        public int WordCount { get; }
        public IReadOnlyList<string> PreviewWords { get; }

        private CommunityWorkshopMetadata(int schemaVersion, string contentHash,
            string detectedLanguage, int wordCount, IEnumerable<string> previewWords)
        {
            SchemaVersion = schemaVersion;
            ContentHash = contentHash;
            DetectedLanguage = detectedLanguage;
            WordCount = wordCount;
            PreviewWords = new List<string>(previewWords ?? Array.Empty<string>()).AsReadOnly();
        }

        public static bool TryCreate(WordListLibraryContent candidate, out CommunityWorkshopMetadata metadata,
            out CommunityError error, CommunityContentLimits limits = null)
        {
            metadata = null;
            if (!WordListLibraryContentCodec.TryNormalizeAutoContent(candidate,
                candidate != null ? candidate.LanguageMode : LanguageMode.English,
                out LanguageMode detectedMode, out _, out WordListLibraryContent normalized, out string validationError))
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent,
                    string.IsNullOrWhiteSpace(validationError) ? "The word-list content is invalid." : validationError);
                return false;
            }

            if (!CommunityContentCodec.TryEncode(normalized, CommunityContentCodec.CurrentSchemaVersion,
                out _, out CommunityDecodedContent validated, out error, limits))
                return false;

            var preview = new List<string>(MaximumPreviewWords);
            for (int index = 0; index < normalized.Words.Count && preview.Count < MaximumPreviewWords; index++)
                preview.Add(normalized.Words[index]);
            metadata = new CommunityWorkshopMetadata(CommunityContentCodec.CurrentSchemaVersion,
                validated.ContentHash, ToLanguageTag(detectedMode), normalized.Words.Count, preview);
            error = null;
            return true;
        }

        public static bool TryParse(string json, out CommunityWorkshopMetadata metadata, out CommunityError error)
        {
            metadata = null;
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumMetadataCharacters)
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent, "Workshop metadata is empty or exceeds its size limit.");
                return false;
            }

            CommunityWorkshopMetadataWire wire;
            try { wire = JsonUtility.FromJson<CommunityWorkshopMetadataWire>(json); }
            catch (Exception exception)
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent, "Workshop metadata could not be decoded: " + exception.Message);
                return false;
            }

            if (wire == null || wire.schemaVersion != CommunityContentCodec.CurrentSchemaVersion
                || !IsSha256(wire.contentHash) || !IsLanguageTag(wire.detectedLanguage)
                || wire.wordCount < 1 || wire.wordCount > (CommunityContentLimits.Default.MaxWords)
                || wire.previewWords == null || wire.previewWords.Length > MaximumPreviewWords)
            {
                error = new CommunityError(CommunityErrorCode.InvalidContent, "Workshop metadata fields are invalid or unsupported.");
                return false;
            }

            foreach (string word in wire.previewWords)
            {
                if (string.IsNullOrWhiteSpace(word) || word.Length > CommunityContentLimits.Default.MaxCharactersPerWord)
                {
                    error = new CommunityError(CommunityErrorCode.InvalidContent, "Workshop preview words are invalid.");
                    return false;
                }
            }

            metadata = new CommunityWorkshopMetadata(wire.schemaVersion, wire.contentHash.ToLowerInvariant(),
                wire.detectedLanguage, wire.wordCount, wire.previewWords);
            error = null;
            return true;
        }

        public string ToJson()
        {
            return JsonUtility.ToJson(new CommunityWorkshopMetadataWire
            {
                schemaVersion = SchemaVersion,
                contentHash = ContentHash,
                detectedLanguage = DetectedLanguage,
                wordCount = WordCount,
                previewWords = new List<string>(PreviewWords).ToArray()
            });
        }

        public static string ComputePublicationFingerprint(string title, string description,
            IEnumerable<string> normalizedTags, CommunityWorkshopMetadata metadata)
        {
            if (metadata == null) return string.Empty;
            var tags = new List<string>();
            if (normalizedTags != null)
            {
                foreach (string tag in normalizedTags)
                {
                    if (string.IsNullOrWhiteSpace(tag)) continue;
                    string canonical = tag.Trim().ToLowerInvariant();
                    if (!tags.Exists(existing => string.Equals(existing, canonical, StringComparison.Ordinal)))
                        tags.Add(canonical);
                }
            }
            tags.Sort(StringComparer.Ordinal);
            string canonicalJson = JsonUtility.ToJson(new CommunityPublicationFingerprintWire
            {
                contentHash = metadata.ContentHash,
                title = title ?? string.Empty,
                description = description ?? string.Empty,
                tags = tags.ToArray(),
                generatedMetadata = metadata.ToJson()
            });
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(canonicalJson));
                var output = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest) output.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return output.ToString();
            }
        }

        public static bool TryBuildTags(LanguageMode detectedMode, int schemaVersion,
            out IReadOnlyList<string> tags, out CommunityError error)
        {
            tags = null;
            if (schemaVersion != CommunityContentCodec.CurrentSchemaVersion || !IsSupportedLanguage(detectedMode))
            {
                error = new CommunityError(CommunityErrorCode.Incompatible, "The Workshop language or schema is unsupported.");
                return false;
            }
            tags = new[]
            {
                CommunityWorkshopTags.WordList,
                CommunityWorkshopTags.SchemaV1,
                CommunityWorkshopTags.ForLanguage(detectedMode)
            };
            error = null;
            return true;
        }

        private static string ToLanguageTag(LanguageMode mode) =>
            mode == LanguageMode.English ? "english" : mode == LanguageMode.Chinese ? "chinese" : mode == LanguageMode.Mixed ? "mixed" : string.Empty;

        private static bool IsLanguageTag(string tag) => tag == "english" || tag == "chinese" || tag == "mixed";
        private static bool IsSupportedLanguage(LanguageMode mode) => mode == LanguageMode.English
            || mode == LanguageMode.Chinese || mode == LanguageMode.Mixed;

        private static bool IsSha256(string hash)
        {
            if (string.IsNullOrEmpty(hash) || hash.Length != 64) return false;
            foreach (char value in hash)
                if (!((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F')))
                    return false;
            return true;
        }
    }

    public static class CommunityWorkshopTags
    {
        public const string WordList = "sw:wordlist";
        public const string SchemaV1 = "sw:schema:1";
        public const string LanguageEnglish = "sw:language:english";
        public const string LanguageChinese = "sw:language:chinese";
        public const string LanguageMixed = "sw:language:mixed";

        public static string ForLanguage(LanguageMode detectedMode)
        {
            switch (detectedMode)
            {
                case LanguageMode.English: return LanguageEnglish;
                case LanguageMode.Chinese: return LanguageChinese;
                case LanguageMode.Mixed: return LanguageMixed;
                default: return string.Empty;
            }
        }
    }
}
