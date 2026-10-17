using LiveLink;
using NUnit.Framework;

namespace LiveLink.Tests
{
    public class LiveLinkAuthenticationTests
    {
        private const string Secret = "test-only-pairing-secret";
        private const string Challenge = "fresh-challenge-1";

        [Test]
        public void CorrectProof_IsAccepted()
        {
            var pairingId = LiveLinkAuthentication.ComputePairingId(Secret);
            var proof = LiveLinkAuthentication.ComputeProof(
                Secret, pairingId, Challenge);

            Assert.That(
                LiveLinkAuthentication.VerifyProof(
                    Secret, pairingId, Challenge, proof), Is.True);
        }

        [Test]
        public void WrongSecret_IsRejected()
        {
            var pairingId = LiveLinkAuthentication.ComputePairingId(Secret);
            var proof = LiveLinkAuthentication.ComputeProof(
                Secret, pairingId, Challenge);

            Assert.That(
                LiveLinkAuthentication.VerifyProof(
                    "wrong-secret", pairingId, Challenge, proof), Is.False);
        }

        [Test]
        public void ChangedChallenge_IsRejected()
        {
            var pairingId = LiveLinkAuthentication.ComputePairingId(Secret);
            var proof = LiveLinkAuthentication.ComputeProof(
                Secret, pairingId, Challenge);

            Assert.That(
                LiveLinkAuthentication.VerifyProof(
                    Secret, pairingId, "different-challenge", proof), Is.False);
        }

        [Test]
        public void MissingInputs_AreRejected()
        {
            Assert.That(
                LiveLinkAuthentication.VerifyProof(
                    Secret, string.Empty, Challenge, "proof"), Is.False);
            Assert.That(
                LiveLinkAuthentication.VerifyProof(
                    Secret, "pairing", Challenge, string.Empty), Is.False);
        }

        [Test]
        public void Challenges_AreFresh()
        {
            Assert.That(
                LiveLinkAuthentication.CreateChallenge(),
                Is.Not.EqualTo(LiveLinkAuthentication.CreateChallenge()));
        }
    }
}
