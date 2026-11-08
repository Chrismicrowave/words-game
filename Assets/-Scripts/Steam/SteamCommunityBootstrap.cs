using System.IO;
using UnityEngine;
using Words.Community;

internal static class SteamCommunityBootstrap
{
    private const uint ConsumerAppId = 4966870;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterFactory()
    {
        CommunityServiceRegistry.RegisterFactory(CreateService);
    }

    private static ICommunityService CreateService()
    {
        string communityRoot = Path.Combine(Application.persistentDataPath, "Community");
        var limits = CommunityContentLimits.Default;
        var gateway = new SteamCommunityGateway(limits);
        var cacheStore = new CommunityCacheStore(Path.Combine(communityRoot, "Cache"), limits);
        string accountStateRoot = Path.Combine(communityRoot, "Accounts");
        return new GatewayCommunityService(gateway, cacheStore, accountStateRoot,
            ConsumerAppId, CommunityServiceRegistry.ResolvePublicationRequest);
    }
}
