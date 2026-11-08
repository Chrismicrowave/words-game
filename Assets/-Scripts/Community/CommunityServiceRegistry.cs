using System;
using System.Threading;

namespace Words.Community
{
    /// <summary>Process-wide service composition point; the optional platform assembly registers its factory.</summary>
    public static class CommunityServiceRegistry
    {
        private static readonly object Sync = new object();
        private static Func<ICommunityService> factory;
        private static Func<string, CommunityPublishRequest> publicationRequestResolver;
        private static long publicationResolverGeneration;
        private static long activePublicationResolverId;
        private static ICommunityService shared;

        /// <summary>Registered by the canonical library bridge; the Community assembly never calls the library directly.</summary>
        public static IDisposable RegisterPublicationRequestResolver(Func<string, CommunityPublishRequest> resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            lock (Sync)
            {
                long leaseId = ++publicationResolverGeneration;
                if (leaseId == 0) leaseId = ++publicationResolverGeneration;
                activePublicationResolverId = leaseId;
                publicationRequestResolver = resolver;
                return new PublicationResolverLease(leaseId);
            }
        }

        public static CommunityPublishRequest ResolvePublicationRequest(string localListId)
        {
            Func<string, CommunityPublishRequest> resolver;
            long leaseId;
            lock (Sync)
            {
                resolver = publicationRequestResolver;
                leaseId = activePublicationResolverId;
            }
            if (resolver == null || leaseId == 0) return null;
            CommunityPublishRequest request;
            try { request = resolver(localListId); }
            catch (Exception) { return null; }
            lock (Sync)
                return activePublicationResolverId == leaseId ? request : null;
        }

        private static void ReleasePublicationResolver(long leaseId)
        {
            lock (Sync)
            {
                if (activePublicationResolverId != leaseId) return;
                activePublicationResolverId = 0;
                publicationRequestResolver = null;
            }
        }

        private sealed class PublicationResolverLease : IDisposable
        {
            private long leaseId;
            public PublicationResolverLease(long leaseId) { this.leaseId = leaseId; }
            public void Dispose()
            {
                long id = Interlocked.Exchange(ref leaseId, 0);
                if (id != 0) ReleasePublicationResolver(id);
            }
        }

        public static void RegisterFactory(Func<ICommunityService> serviceFactory)
        {
            if (serviceFactory == null) throw new ArgumentNullException(nameof(serviceFactory));
            lock (Sync)
            {
                if (shared is IDisposable disposable) disposable.Dispose();
                factory = serviceFactory;
                shared = null;
            }
        }

        /// <summary>Always returns a safe service. Without a platform factory, operations report Offline.</summary>
        public static ICommunityService GetShared()
        {
            lock (Sync) return GetOrCreate();
        }

        /// <summary>Returns the already-created service without invoking the registered factory.</summary>
        public static bool TryGetExisting(out ICommunityService service)
        {
            lock (Sync)
            {
                service = shared;
                return service != null;
            }
        }

        /// <summary>Returns false when only the unavailable fallback is available; service is never null.</summary>
        public static bool TryGetShared(out ICommunityService service)
        {
            lock (Sync)
            {
                service = GetOrCreate();
                return factory != null;
            }
        }

        /// <summary>Ends the shared service instance while leaving the optional factory registered.</summary>
        public static void Shutdown()
        {
            lock (Sync)
            {
                if (shared is IDisposable disposable) disposable.Dispose();
                shared = null;
            }
        }

        private static ICommunityService GetOrCreate()
        {
            if (shared != null) return shared;
            try
            {
                shared = factory != null ? factory() : new UnavailableCommunityService();
            }
            catch (Exception exception)
            {
                shared = new UnavailableCommunityService("Community service initialization failed: " + exception.Message);
            }
            if (shared == null) shared = new UnavailableCommunityService("Community service factory returned no service.");
            return shared;
        }
    }
}
