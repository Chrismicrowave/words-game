using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Resolves a challenge provider's progression track from its actual challenge file.
/// Custom lists are excluded even when their filename contains "_cn".
/// </summary>
public static class ChallengeTrackResolver
{
    public const string ChineseTrackPrefix = "cn_";
    public const string EnglishTrackPrefix = "";

    public static string GetTrackPrefixForChallengeProvider(LevelWordListProvider provider)
    {
        if (provider == null || string.IsNullOrWhiteSpace(provider.FilePath))
            return null;

        return GetTrackPrefixForChallengePath(provider.FilePath, LevelWordListProvider.GetChallengeDirectory());
    }

    public static string GetTrackPrefixForChallengePath(string providerPath, string challengeDirectory)
    {
        if (string.IsNullOrWhiteSpace(providerPath) || string.IsNullOrWhiteSpace(challengeDirectory))
            return null;

        try
        {
            string challengeFullPath = Path.GetFullPath(challengeDirectory);
            string providerFullPath = Path.GetFullPath(providerPath);
            string providerDirectory = Path.GetDirectoryName(providerFullPath);
            if (!string.Equals(challengeFullPath, providerDirectory, StringComparison.OrdinalIgnoreCase))
                return null;

            return GetTrackPrefixForFileName(Path.GetFileName(providerFullPath));
        }
        catch
        {
            return null;
        }
    }

    public static string GetTrackPrefixForFileName(string fileName)
    {
        string safeName = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        return safeName.IndexOf("_cn", StringComparison.OrdinalIgnoreCase) >= 0
            ? ChineseTrackPrefix
            : EnglishTrackPrefix;
    }

    public static List<LevelWordListProvider> FilterByTrack(
        IEnumerable<LevelWordListProvider> providers,
        string trackPrefix)
    {
        var result = new List<LevelWordListProvider>();
        if (providers == null)
            return result;

        string normalizedPrefix = NormalizeTrackPrefix(trackPrefix);
        foreach (var provider in providers)
        {
            string providerPrefix = GetTrackPrefixForChallengeProvider(provider);
            if (providerPrefix != null && providerPrefix == normalizedPrefix)
                result.Add(provider);
        }

        return result;
    }

    public static int FindIndex(LevelWordListProvider provider, IList<LevelWordListProvider> providers)
    {
        if (provider == null || providers == null)
            return -1;

        for (int i = 0; i < providers.Count; i++)
        {
            if (providers[i] != null && string.Equals(providers[i].FilePath, provider.FilePath, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    public static string NormalizeTrackPrefix(string trackPrefix)
    {
        return string.Equals(trackPrefix, ChineseTrackPrefix, StringComparison.Ordinal)
            ? ChineseTrackPrefix
            : EnglishTrackPrefix;
    }
}
