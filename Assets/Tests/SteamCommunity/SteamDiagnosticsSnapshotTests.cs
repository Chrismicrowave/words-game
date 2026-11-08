using NUnit.Framework;
using UnityEngine;
using Words.Community;

namespace SteamCommunity.Tests
{
    public sealed class SteamDiagnosticsSnapshotTests
    {
        private sealed class FakeSteamSession : ISteamClientSession
        {
            public SteamClientSessionStatus Status { get; set; } = SteamClientSessionStatus.Initialized;
            public bool IsInitialized { get; set; } = true;
            public long InitializationGeneration { get; set; } = 1;
            public uint CurrentAppId { get; set; } = SteamDiagnosticsSnapshotProvider.ExpectedAppId;
            public ulong CurrentAccountId { get; set; } = 12345678901234567UL;
            public string LastError { get; set; } = string.Empty;
            public int InitializeCalls { get; private set; }
            public int CallbackCalls { get; private set; }
            public int ShutdownCalls { get; private set; }

            public bool TryInitialize()
            {
                InitializeCalls++;
                return IsInitialized;
            }

            public void RunCallbacks() => CallbackCalls++;
            public void Shutdown() => ShutdownCalls++;
        }

        [Test]
        public void SnapshotProjectsLiveSessionAndCommunityStateWithoutSteamOperations()
        {
            var session = new FakeSteamSession();
            var communityState = new CommunityServiceState(
                CommunityAvailability.Available,
                SteamDiagnosticsSnapshotProvider.ExpectedAppId,
                12345678901234567UL,
                generation: 3,
                lastError: string.Empty);

            SteamDiagnosticsSnapshot snapshot = SteamDiagnosticsSnapshotProvider.Capture(
                session, communityState, remoteAuthenticated: true);

            Assert.AreEqual("4966870", snapshot.ExpectedAppId);
            Assert.AreEqual("Initialized", snapshot.SessionStatus);
            Assert.AreEqual("4966870", snapshot.CurrentAppId);
            Assert.AreEqual("12345678901234567", snapshot.CurrentAccountId);
            Assert.AreEqual("Available", snapshot.CommunityAvailability);
            Assert.AreEqual("4966870", snapshot.CommunityAppId);
            Assert.AreEqual("Yes", snapshot.CommunityAuthenticated);
            Assert.AreEqual(0, session.InitializeCalls);
            Assert.AreEqual(0, session.CallbackCalls);
            Assert.AreEqual(0, session.ShutdownCalls);
        }

        [Test]
        public void MissingExistingServicesProjectAsUnavailableWithoutInitializingAnything()
        {
            SteamDiagnosticsSnapshot snapshot = SteamDiagnosticsSnapshotProvider.Capture(
                session: null, communityState: null, remoteAuthenticated: false,
                communityReadError: "Community service has not been initialized by the game.");

            Assert.AreEqual("Unavailable", snapshot.SessionStatus);
            Assert.AreEqual("Not available", snapshot.CurrentAccountId);
            Assert.AreEqual("Unknown", snapshot.CommunityAvailability);
            Assert.AreEqual("No", snapshot.CommunityAuthenticated);
            Assert.That(snapshot.CommunityError, Does.Contain("has not been initialized"));
        }

        [Test]
        public void OverlayAttachIsDisabledForDisabledDiagnosticsAndPlayerReleaseProfiles()
        {
            GameObject owner = new GameObject("SteamDiagnosticsOverlayTest");
            GameConfigProfile profile = ScriptableObject.CreateInstance<GameConfigProfile>();
            try
            {
                profile.buildPurpose = BuildPurpose.LocalTesting;
                profile.enableSteamDiagnostics = false;
                Assert.IsNull(SteamDiagnosticsOverlay.Attach(owner, profile));
                Assert.IsNull(owner.GetComponent<SteamDiagnosticsOverlay>());

                profile.buildPurpose = BuildPurpose.PlayerRelease;
                profile.enableSteamDiagnostics = true;
                Assert.IsNull(SteamDiagnosticsOverlay.Attach(owner, profile));
                Assert.IsNull(owner.GetComponent<SteamDiagnosticsOverlay>());
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(owner);
            }
        }
    }
}
