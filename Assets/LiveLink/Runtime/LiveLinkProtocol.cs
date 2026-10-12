
using System;
using UnityEngine;

namespace LiveLink
{
    public static class LiveLinkProtocol
    {
        public static readonly Guid CommandId =
            new Guid("a8525cfb-6730-4fa1-b42f-c617181ab361");

        public static readonly Guid ResponseId =
            new Guid("4be8ca7b-502e-4d42-b14b-a4829c2243bc");
    }

    [Serializable]
    public class LiveLinkCommand
    {
        public string requestId;
        public string type;
        public string manifestFingerprint;
        public string objectId;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
    }

    [Serializable]
    public class LiveLinkResponse
    {
        public string requestId;
        public string status;
        public string message;
        public LiveLinkHandshakeResponse handshake;
    }

    [Serializable]
    public class LiveLinkHandshakeResponse
    {
        public int protocolVersion;
        public string fingerprint;
        public string buildName;
    }
}
