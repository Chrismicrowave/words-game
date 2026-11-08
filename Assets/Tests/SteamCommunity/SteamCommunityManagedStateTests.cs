using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Steamworks;
using Words.Community;

namespace SteamCommunity.Tests
{
    public sealed class SteamCommunityManagedStateTests
    {
        private const uint AppId = 4966870;
        private const ulong AccountId = 76561198000000001UL;

        [Test]
        public void MutationNetworkTimeoutsAreAmbiguousButDefinitiveValidationIsNot()
        {
            Assert.That(SteamCommunityCallbackPolicy.IsAmbiguousMutationFailure(EResult.k_EResultTimeout), Is.True);
            Assert.That(SteamCommunityCallbackPolicy.IsAmbiguousMutationFailure(EResult.k_EResultNoConnection), Is.True);
            Assert.That(SteamCommunityCallbackPolicy.IsAmbiguousMutationFailure(EResult.k_EResultServiceUnavailable), Is.True);
            Assert.That(SteamCommunityCallbackPolicy.IsAmbiguousMutationFailure(EResult.k_EResultOK, ioFailure: true), Is.True);
            Assert.That(SteamCommunityCallbackPolicy.IsAmbiguousMutationFailure(EResult.k_EResultInvalidParam), Is.False);
        }

        [Test]
        public void SessionChangedMutationCanPreserveKnownRemoteValueAsUnknown()
        {
            CommunityResult<ulong> mapped = CommunityResult<ulong>.Success(44112233UL, "create-op");
            CommunityResult<ulong> result = SteamCommunityCallbackPolicy.UnknownPreservingValue(
                mapped, "The session changed after the create callback.");

            Assert.That(result.Outcome, Is.EqualTo(CommunityOperationOutcome.OutcomeUnknown));
            Assert.That(result.Value, Is.EqualTo(44112233UL));
            Assert.That(result.OperationId, Is.EqualTo("create-op"));
            Assert.That(result.Error.Code, Is.EqualTo(CommunityErrorCode.RecoveryRequired));
        }

        [Test]
        public void CompletionGateClaimsOnlyOnceAcrossConcurrentCallbackAndTimeout()
        {
            var gate = new ManagedCompletionGate();
            int claims = 0;
            Parallel.For(0, 32, _ =>
            {
                if (gate.TryClaim()) Interlocked.Increment(ref claims);
            });

            Assert.That(claims, Is.EqualTo(1));
            Assert.That(gate.TryClaim(), Is.False);
        }

        [Test]
        public void TimedOutDownloadRemainsQuarantinedUntilItsCallbackArrives()
        {
            var tracker = new ManagedDownloadFlightTracker<string>();
            object session = new object();
            Assert.That(tracker.TryBegin(AppId, 778899UL, AccountId, 7, session, "download-1"), Is.True);
            Assert.That(tracker.TryMarkTimedOut(AppId, 778899UL, AccountId, 7, session,
                out string timedOut), Is.True);
            Assert.That(timedOut, Is.EqualTo("download-1"));

            Assert.That(tracker.TryBegin(AppId, 778899UL, AccountId, 7, session, "download-2"), Is.False,
                "A timeout does not release the native flight because Steam has no request ID in the callback.");
            Assert.That(tracker.TryComplete(AppId, 778899UL, AccountId, 7, session,
                out string resolved, out bool wasTimedOut), Is.True);
            Assert.That(resolved, Is.EqualTo("download-1"));
            Assert.That(wasTimedOut, Is.True);
            Assert.That(tracker.TryComplete(AppId, 778899UL, AccountId, 7, session,
                out _, out _), Is.False, "Duplicate callback delivery must not complete the same flight twice.");
            Assert.That(tracker.TryBegin(AppId, 778899UL, AccountId, 7, session, "download-3"), Is.True);
        }

        [Test]
        public void DownloadCallbackMustMatchAppAccountGenerationAndSessionInstance()
        {
            var tracker = new ManagedDownloadFlightTracker<string>();
            object firstSession = new object();
            object secondSession = new object();
            Assert.That(tracker.TryBegin(AppId, 991100UL, AccountId, 3, firstSession, "old"), Is.True);

            Assert.That(tracker.TryComplete(AppId, 991100UL, AccountId, 4, firstSession,
                out _, out _), Is.False);
            Assert.That(tracker.TryComplete(AppId, 991100UL, AccountId, 3, secondSession,
                out _, out _), Is.False);
            List<string> stale = tracker.RemoveExcept(AppId, AccountId, 4, secondSession);
            Assert.That(stale, Is.EquivalentTo(new[] { "old" }));
            Assert.That(tracker.TryBegin(AppId, 991100UL, AccountId, 4, secondSession, "new"), Is.True);
            Assert.That(tracker.TryComplete(AppId, 991100UL, AccountId, 4, secondSession,
                out string current, out bool wasTimedOut), Is.True);
            Assert.That(current, Is.EqualTo("new"));
            Assert.That(wasTimedOut, Is.False);
        }
    }
}
