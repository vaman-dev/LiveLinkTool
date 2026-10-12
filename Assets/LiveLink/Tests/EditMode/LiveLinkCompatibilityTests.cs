using NUnit.Framework;
using LiveLink.Editor.Connection;

namespace LiveLink.Tests
{
    public sealed class LiveLinkCompatibilityTests
    {
        private const string Fingerprint =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        [Test]
        public void MatchingManifest_IsCompatible()
        {
            Assert.That(
                LiveLinkCompatibilityEvaluator.Evaluate(
                    1, Fingerprint, Fingerprint),
                Is.EqualTo(LiveLinkCompatibilityState.Compatible));
        }

        [Test]
        public void DifferentFingerprint_IsMismatch()
        {
            Assert.That(
                LiveLinkCompatibilityEvaluator.Evaluate(
                    1, new string('a', 64), Fingerprint),
                Is.EqualTo(LiveLinkCompatibilityState.Mismatch));
        }

        [Test]
        public void UnsupportedProtocol_IsIncompatible()
        {
            Assert.That(
                LiveLinkCompatibilityEvaluator.Evaluate(
                    99, Fingerprint, Fingerprint),
                Is.EqualTo(LiveLinkCompatibilityState.Incompatible));
        }

        [Test]
        public void EmptyFingerprint_FailsVerification()
        {
            Assert.That(
                LiveLinkCompatibilityEvaluator.Evaluate(
                    1, string.Empty, Fingerprint),
                Is.EqualTo(LiveLinkCompatibilityState.VerificationFailed));
        }

        [Test]
        public void MalformedFingerprint_FailsVerification()
        {
            Assert.That(
                LiveLinkCompatibilityEvaluator.Evaluate(
                    1, "not-a-hash", Fingerprint),
                Is.EqualTo(LiveLinkCompatibilityState.VerificationFailed));
        }

        [Test]
        public void TransformOnlyChange_DoesNotAffectComparison()
        {
            Assert.That(
                LiveLinkCompatibilityEvaluator.Evaluate(
                    1, Fingerprint, Fingerprint),
                Is.EqualTo(LiveLinkCompatibilityState.Compatible));
        }

        [Test]
        public void PlayerConnectionSessions_AreDistinctFromDiscoverySessions()
        {
            var player = new LiveLinkSessionKey(
                LiveLinkTransportKind.PlayerConnection, "42", 1);
            var discovery = new LiveLinkSessionKey(
                LiveLinkTransportKind.DiscoveryTcp, "42", 1);

            Assert.That(player, Is.Not.EqualTo(discovery));
        }

        [Test]
        public void ReconnectGeneration_CreatesNewSessionKey()
        {
            var first = new LiveLinkSessionKey(
                LiveLinkTransportKind.DiscoveryTcp, "session", 1);
            var second = new LiveLinkSessionKey(
                LiveLinkTransportKind.DiscoveryTcp, "session", 2);

            Assert.That(first, Is.Not.EqualTo(second));
        }

        [TestCase(LiveLinkCompatibilityState.NotVerified)]
        [TestCase(LiveLinkCompatibilityState.Checking)]
        [TestCase(LiveLinkCompatibilityState.Mismatch)]
        [TestCase(LiveLinkCompatibilityState.Incompatible)]
        [TestCase(LiveLinkCompatibilityState.VerificationFailed)]
        [TestCase(LiveLinkCompatibilityState.Disconnected)]
        public void NonCompatibleSessions_CannotSynchronize(
            LiveLinkCompatibilityState state)
        {
            Assert.That(
                LiveLinkCompatibilityEvaluator.CanSynchronize(state),
                Is.False);
        }

        [Test]
        public void CompatibleSession_CanSynchronize()
        {
            Assert.That(
                LiveLinkCompatibilityEvaluator.CanSynchronize(
                    LiveLinkCompatibilityState.Compatible),
                Is.True);
        }

        [Test]
        public void AuthorizationCommand_CarriesEditorFingerprint()
        {
            var command = new LiveLinkCommand
            {
                type = "SYNC_AUTHORIZATION",
                manifestFingerprint = Fingerprint
            };

            Assert.That(command.type, Is.EqualTo("SYNC_AUTHORIZATION"));
            Assert.That(command.manifestFingerprint, Is.EqualTo(Fingerprint));
        }
    }
}
