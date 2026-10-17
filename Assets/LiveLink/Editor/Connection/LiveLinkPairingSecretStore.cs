#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace LiveLink
{
    internal static class LiveLinkPairingSecretStore
    {
        private const int SecretLength = 32;

        private static string SecretPath => Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "Library",
            "LiveLink",
            "pairing-secret.bin");

        public static string GetOrCreateSecret()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SecretPath));
            if (File.Exists(SecretPath))
            {
                var existing = File.ReadAllBytes(SecretPath);
                if (existing.Length == SecretLength)
                {
                    return Convert.ToBase64String(existing);
                }
            }

            var secret = new byte[SecretLength];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(secret);
            }

            File.WriteAllBytes(SecretPath, secret);
            return Convert.ToBase64String(secret);
        }
    }
}
#endif
