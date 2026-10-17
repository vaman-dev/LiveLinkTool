using LiveLink;
using UnityEditor;
using UnityEngine;

namespace LiveLink.Editor.Build
{
    internal static class LiveLinkManifestBaker
    {
        internal const string ResourcePath =
            "Assets/Resources/LiveLinkBuildIdentity.asset";

        internal const string ResourceName =
            "LiveLinkBuildIdentity";

        internal static bool Bake(string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                Debug.LogError(
                    "[LiveLink] Cannot bake an empty manifest fingerprint.");
                return false;
            }

            EnsureResourcesFolder();
            Cleanup();

            var asset =
                ScriptableObject.CreateInstance<LiveLinkBuildIdentity>();

            var serialized = new SerializedObject(asset);
            serialized.Update();
            serialized.FindProperty("protocolVersion").intValue = 1;
            serialized.FindProperty("manifestFingerprint")
                .stringValue = fingerprint;
            var pairingSecret = LiveLinkPairingSecretStore.GetOrCreateSecret();
            var pairingSecretProperty = serialized.FindProperty("pairingSecret");
            var pairingIdProperty = serialized.FindProperty("pairingId");
            if (pairingSecretProperty == null || pairingIdProperty == null)
            {
                Object.DestroyImmediate(asset);
                Debug.LogError(
                    "[LiveLink] Build identity schema is missing TCP pairing fields.");
                return false;
            }

            pairingSecretProperty.stringValue = pairingSecret;
            pairingIdProperty.stringValue = LiveLinkAuthentication.ComputePairingId(pairingSecret);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(asset, ResourcePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[LiveLink] Baked Development Player manifest: " +
                fingerprint);

            return true;
        }

        internal static void Cleanup()
        {
            if (!AssetDatabase.LoadAssetAtPath<LiveLinkBuildIdentity>(
                    ResourcePath))
            {
                return;
            }

            AssetDatabase.DeleteAsset(ResourcePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureResourcesFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }
        }
    }
}
