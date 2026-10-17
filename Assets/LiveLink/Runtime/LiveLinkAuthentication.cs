using System;
using System.Security.Cryptography;
using System.Text;

namespace LiveLink
{
    public static class LiveLinkAuthentication
    {
        private const string Domain = "LiveLinkTcpAuthV1|";

        public static string CreateChallenge()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes);
        }

        public static string ComputePairingId(string pairingSecret)
        {
            if (string.IsNullOrWhiteSpace(pairingSecret))
            {
                return string.Empty;
            }

            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(pairingSecret)));
            }
        }

        public static string ComputeProof(string pairingSecret, string pairingId, string challenge)
        {
            if (string.IsNullOrWhiteSpace(pairingSecret) ||
                string.IsNullOrWhiteSpace(pairingId) ||
                string.IsNullOrWhiteSpace(challenge))
            {
                return string.Empty;
            }

            var input = Domain + pairingId + "|" + challenge;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pairingSecret)))
            {
                return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(input)));
            }
        }

        public static bool VerifyProof(string pairingSecret, string pairingId, string challenge, string proof)
        {
            var expected = ComputeProof(pairingSecret, pairingId, challenge);
            if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(proof))
            {
                return false;
            }

            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var actualBytes = Encoding.UTF8.GetBytes(proof);
            if (expectedBytes.Length != actualBytes.Length)
            {
                return false;
            }

            var different = 0;
            for (var i = 0; i < expectedBytes.Length; i++)
            {
                different |= expectedBytes[i] ^ actualBytes[i];
            }

            return different == 0;
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
