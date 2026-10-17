using UnityEngine;

namespace LiveLink
{
    [CreateAssetMenu(
        fileName = "LiveLinkBuildIdentity",
        menuName = "LiveLink/Build Identity")]
    public sealed class LiveLinkBuildIdentity : ScriptableObject
    {
        [SerializeField]
        private int protocolVersion = 1;

        [SerializeField]
        private string manifestFingerprint = "";

        [SerializeField]
        private string pairingId = "";

        [SerializeField]
        private string pairingSecret = "";

        public int ProtocolVersion => protocolVersion;

        public string ManifestFingerprint => manifestFingerprint;

        public string PairingId => pairingId;

        public string PairingSecret => pairingSecret;
    }
}
