using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Words.Community;
using Words.UiMockups.WordListHub;

namespace Words.UiMockups.WordListHub.Tests
{
    public sealed class CommunityProviderGameplayIsolationTests
    {
        private GameObject phaseManagerObject;
        private PhaseManager previousPhaseManager;
        private string temporaryLibraryRoot;

        [SetUp]
        public void SetUp()
        {
            previousPhaseManager = Services.Get<PhaseManager>();
            Services.Register<PhaseManager>(null);
        }

        [TearDown]
        public void TearDown()
        {
            if (phaseManagerObject != null)
                UnityEngine.Object.DestroyImmediate(phaseManagerObject);
            if (!string.IsNullOrEmpty(temporaryLibraryRoot)
                && Path.GetFileName(temporaryLibraryRoot).StartsWith("CommunityCopyTests-", StringComparison.Ordinal)
                && Directory.Exists(temporaryLibraryRoot))
                Directory.Delete(temporaryLibraryRoot, true);
            temporaryLibraryRoot = null;
            Services.Register<PhaseManager>(previousPhaseManager);
        }

        [Test]
        public void LoadingValidatedCommunityProviderDoesNotInitializeCanonicalLibrary()
        {
            phaseManagerObject = new GameObject("Community provider isolation test");
            PhaseManager phaseManager = phaseManagerObject.AddComponent<PhaseManager>();
            var content = new WordListLibraryContent
            {
                LanguageMode = LanguageMode.English,
                Words = new List<string> { "garden" }
            };
            var provider = new CommunityWordListProvider(
                new CommunityItemKey(42, 9001),
                new CommunityContentVersion(1, new string('a', 64)),
                "Garden", content);

            Assert.IsFalse(provider.IsEditable);
            Assert.IsTrue(phaseManager.LoadWordList(provider));
            Assert.AreSame(provider, phaseManager.ActiveProvider);
            Assert.AreEqual(1, phaseManager.TotalPhases);

            FieldInfo libraryField = typeof(PhaseManager).GetField(
                "wordListLibrary", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(libraryField);
            Assert.IsNull(libraryField.GetValue(phaseManager),
                "Community gameplay must not initialize or register canonical local-list storage.");
        }

        [Test]
        public void ExactCachedVersionResolvesFreshTrustedProviderWithoutReferenceIdentity()
        {
            var key = new CommunityItemKey(42, 9001);
            var version = new CommunityContentVersion(1, new string('a', 64));
            var content = new WordListLibraryContent
            {
                LanguageMode = LanguageMode.English,
                Words = new List<string> { "garden" }
            };
            var selectedProvider = new CommunityWordListProvider(key, version, "Garden", content);
            var cacheProvider = new CommunityWordListProvider(key, version, "Garden", content);
            var cacheEntry = new CommunityCacheEntry(key, version, "Garden", "Author",
                DateTime.UtcNow, cacheProvider);

            Type levelPanelType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("LevelPanelController", false))
                .FirstOrDefault(type => type != null);
            Assert.IsNotNull(levelPanelType, "The production LevelPanelController assembly should be loaded.");
            MethodInfo resolver = levelPanelType.GetMethod("TryResolveValidatedCommunityProvider",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(resolver, "The cache gate resolver must remain directly testable without a scene.");

            object[] arguments = { selectedProvider, cacheEntry, null, null };
            bool resolved = (bool)resolver.Invoke(null, arguments);
            Assert.IsTrue(resolved);
            Assert.AreSame(cacheProvider, arguments[2],
                "Gameplay must receive the provider reconstructed from the validated cache entry.");

            var wrongVersionProvider = new CommunityWordListProvider(key,
                new CommunityContentVersion(1, new string('b', 64)), "Garden", content);
            arguments = new object[] { wrongVersionProvider, cacheEntry, null, null };
            resolved = (bool)resolver.Invoke(null, arguments);
            Assert.IsFalse(resolved, "The cache gate must reject a different content hash.");
            Assert.IsNull(arguments[2]);
            Assert.IsNotEmpty((string)arguments[3]);
        }

        [Test]
        public void CommunityCopyCreatesNewCanonicalListAndPreservesStructuredPinyin()
        {
            var key = new CommunityItemKey(42, 9002);
            var sourceContent = new WordListLibraryContent
            {
                LanguageMode = LanguageMode.Chinese,
                Words = new List<string> { "你好" },
                ChineseWords = new List<ChineseWordEntry>
                {
                    new ChineseWordEntry
                    {
                        display = "你好",
                        entries = new List<ChinesePhaseEntry>
                        {
                            new ChinesePhaseEntry { character = "你", pinyin = "ni" },
                            new ChinesePhaseEntry { character = "好", pinyin = "hao" }
                        }
                    }
                }
            };
            temporaryLibraryRoot = Path.Combine(Path.GetTempPath(),
                "CommunityCopyTests-" + Guid.NewGuid().ToString("N"));
            string customDirectory = Path.Combine(temporaryLibraryRoot, "Levels", "Custom");
            string statePath = Path.Combine(temporaryLibraryRoot, "WordListLibrary.json");
            Directory.CreateDirectory(customDirectory);
            var library = new WordListLibraryService(customDirectory, statePath);

            var cacheStore = new CommunityCacheStore(Path.Combine(temporaryLibraryRoot, "CommunityCache"));
            CommunityResult<CommunityCacheEntry> cached = cacheStore.Commit(key,
                CommunityContentCodec.CurrentSchemaVersion, "Chinese garden", "Author", sourceContent);
            Assert.IsTrue(cached.Succeeded, cached.Error?.Message);
            var gateway = new CachedOnlyCommunityGateway(
                new CommunityServiceState(CommunityAvailability.Available, 42, 123, 1));
            var service = new GatewayCommunityService(gateway, cacheStore,
                Path.Combine(temporaryLibraryRoot, "CommunityState"), 42);
            object facadeInstance = null;
            try
            {
                Assert.IsTrue(service.TryRecordRun(key, cached.Value.Version.ContentHash, 123,
                    12f, 0, 3, out _, out CommunityError recordError), recordError?.Message);

                Type facadeType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("WordListHubCommunityFacade", false))
                    .FirstOrDefault(type => type != null);
                Assert.IsNotNull(facadeType, "The production Community facade assembly should be loaded.");
                ConstructorInfo facadeConstructor = facadeType.GetConstructor(
                    new[] { typeof(ICommunityService), typeof(Action<Action>) });
                Assert.IsNotNull(facadeConstructor);
                facadeInstance = facadeConstructor.Invoke(new object[] { service, null });
                var communityUi = facadeInstance as IWordListHubCommunityAsync;
                Assert.IsNotNull(communityUi);

                MockupWordList downloaded = communityUi.DownloadedItems.Single();
                Assert.IsTrue(downloaded.IsReadOnly);
                Assert.IsTrue(cached.Value.Provider.IsEditable == false);
                Assert.IsTrue(communityUi.TryGetDownloadedContentForCopy(downloaded.Id,
                    out WordListLibraryContent copyContent, out string title, out string copyError), copyError);
                Assert.AreEqual("Chinese garden", title);

                Type backendType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("WordListHubProductionBackend", false))
                    .FirstOrDefault(type => type != null);
                Assert.IsNotNull(backendType, "The production Hub backend assembly should be loaded.");
                MethodInfo createCopy = backendType.GetMethod("TryCreateCommunityCopyFromValidatedContent",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(createCopy, "The copy persistence helper must be testable without a scene.");

                object[] arguments = { library, "Chinese garden copy", copyContent, null, null };
                bool succeeded = (bool)createCopy.Invoke(null, arguments);
                Assert.IsTrue(succeeded, arguments[4] as string);
                var created = arguments[3] as WordListLibraryItem;
                Assert.IsNotNull(created);
                Assert.AreNotEqual(key.RemoteItemId.ToString(), created.LocalListId);
                Assert.IsTrue(created.LocalListId.StartsWith("cst_", StringComparison.Ordinal));
                Assert.IsTrue(Guid.TryParseExact(created.Provider.GetUUID(), "N", out _));
                Assert.IsTrue(created.IsEditable);

                var reloaded = new LevelWordListProvider(created.FilePath, true);
                Assert.IsTrue(reloaded.IsValid);
                Assert.AreEqual("ni", reloaded.GetChineseWords()[0].entries[0].pinyin);
                Assert.AreEqual("hao", reloaded.GetChineseWords()[0].entries[1].pinyin);
                Assert.IsTrue(cacheStore.Load(key, cached.Value.Version.SchemaVersion,
                    cached.Value.Version.ContentHash).Succeeded, "Copy & Edit must leave the read-only cache intact.");
                Assert.IsTrue(service.TryGetRecord(key, cached.Value.Version.ContentHash, 123,
                    out CommunityPersonalRecord record, out recordError), recordError?.Message);
                Assert.AreEqual(1, record.PlayCount, "Copy & Edit must leave Community play history in place.");
            }
            finally
            {
                if (facadeInstance is IDisposable disposableFacade) disposableFacade.Dispose();
                service.Dispose();
            }
        }

        private sealed class CachedOnlyCommunityGateway : ICommunityGateway
        {
            public CommunityServiceState State { get; }
            private Action<CommunityServiceState> availabilityChanged;
            public event Action<CommunityServiceState> AvailabilityChanged
            {
                add { availabilityChanged += value; }
                remove { availabilityChanged -= value; }
            }

            public CachedOnlyCommunityGateway(CommunityServiceState state) => State = state;

            private static Task<CommunityResult<T>> Unused<T>() => Task.FromResult(
                CommunityResult<T>.Failure(new CommunityError(CommunityErrorCode.Unknown,
                    "This SDK-free test does not use remote Community operations.")));

            public Task<CommunityResult<CommunityPage>> QueryAsync(CommunityQuery query, CancellationToken cancellationToken) => Unused<CommunityPage>();
            public Task<CommunityResult<CommunityDetails>> GetDetailsAsync(CommunityItemKey key, CancellationToken cancellationToken) => Unused<CommunityDetails>();
            public Task<CommunityResult<CommunityDownloadReceipt>> DownloadAsync(CommunityItemKey key, IProgress<CommunityTransferProgress> progress, CancellationToken cancellationToken) => Unused<CommunityDownloadReceipt>();
            public Task<CommunityResult<CommunityVoteState>> VoteUpAsync(CommunityItemKey key, CancellationToken cancellationToken) => Unused<CommunityVoteState>();
            public Task<CommunityResult<CommunityPage>> QueryOwnedAsync(uint consumerAppId, string continuationToken, CancellationToken cancellationToken) => Unused<CommunityPage>();
            public Task<CommunityResult<CommunityCreateReceipt>> CreatePublicationAsync(uint consumerAppId, CancellationToken cancellationToken) => Unused<CommunityCreateReceipt>();
            public Task<CommunityResult<CommunityRemoteItemState>> SubmitPublicationAsync(CommunitySubmitRequest request, IProgress<CommunityTransferProgress> progress, CancellationToken cancellationToken) => Unused<CommunityRemoteItemState>();
            public Task<CommunityResult<CommunityRemoteItemState>> SetVisibilityAsync(CommunityItemKey key, CommunityVisibility visibility, CancellationToken cancellationToken) => Unused<CommunityRemoteItemState>();
        }
    }
}
