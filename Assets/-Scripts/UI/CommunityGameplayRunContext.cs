using System;
using Words.Community;

/// <summary>Passes the validated Community source and run-start account into gameplay.</summary>
public sealed class CommunityGameplayRunIdentity
{
    public CommunityWordListProvider Provider { get; }
    public CommunityItemKey Key { get; }
    public CommunityContentVersion Version { get; }
    public ulong AccountId { get; }

    public CommunityGameplayRunIdentity(CommunityWordListProvider provider,
        CommunityContentVersion version, ulong accountId)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Key = provider.Key ?? throw new ArgumentNullException(nameof(provider));
        Version = version ?? throw new ArgumentNullException(nameof(version));
        AccountId = accountId;
    }
}

/// <summary>
/// One-shot handoff from the Hub's validated provider start to GameCoordinator's
/// synchronous PhaseManager.OnWordListChanged callback.
/// </summary>
public static class CommunityGameplayRunContext
{
    private static CommunityGameplayRunIdentity pending;

    public static void Prepare(CommunityWordListProvider provider, ulong accountId)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        pending = new CommunityGameplayRunIdentity(provider, provider.Version, accountId);
    }

    public static bool TryConsume(CommunityWordListProvider provider,
        out CommunityGameplayRunIdentity identity)
    {
        identity = pending;
        pending = null;
        return provider != null && identity != null
            && ReferenceEquals(provider, identity.Provider)
            && identity.Key.Equals(provider.Key)
            && identity.Version.Equals(provider.Version);
    }

    public static void ClearPending()
    {
        pending = null;
    }
}
