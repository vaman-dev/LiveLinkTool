using LiveLink;
using NUnit.Framework;
using UnityEngine;

namespace LiveLink.Tests
{
    public sealed class ManifestHandshakeTests
    {
        [Test]
        public void HandshakeResponse_SerializesCorrectly()
        {
            var response = new LiveLinkHandshakeResponse
            {
                protocolVersion = 1,
                fingerprint = new string('a', 64),
                buildName = "LiveLink"
            };

            string json = JsonUtility.ToJson(response);

            var restored =
                JsonUtility.FromJson<LiveLinkHandshakeResponse>(json);

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.protocolVersion, Is.EqualTo(1));
            Assert.That(
                restored.fingerprint,
                Is.EqualTo(response.fingerprint));
            Assert.That(
                restored.buildName,
                Is.EqualTo(response.buildName));
        }

        [Test]
        public void HandshakeResponse_MissingFingerprintIsInvalid()
        {
            var response = new LiveLinkHandshakeResponse
            {
                protocolVersion = 1,
                fingerprint = ""
            };

            Assert.That(
                string.IsNullOrWhiteSpace(response.fingerprint),
                Is.True);
        }
    }
}
