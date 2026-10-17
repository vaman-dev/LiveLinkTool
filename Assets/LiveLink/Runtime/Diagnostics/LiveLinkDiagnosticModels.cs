using System;
using System.Collections.Generic;

namespace LiveLink.Diagnostics
{
    public enum LiveLinkDiagnosticRole { Editor, Player }
    public enum LiveLinkDiagnosticTransport { PlayerConnection, DiscoveryTcp }
    public enum LiveLinkDiagnosticRecordKind { Service, TcpConnection, LiveLinkSession }
    public enum LiveLinkDiagnosticStage
    {
        UdpAnnouncementSent,
        UdpAnnouncementReceived,
        TcpListenerStarted,
        TcpConnected,
        HmacAuthenticated,
        ManifestCompatible,
        SyncAuthorizationAcknowledged,
        TransformCommandAcknowledged,
        LightCommandAcknowledged
    }
    public enum LiveLinkDiagnosticResult { Pass, Fail, Pending, NotTested, NotApplicable }

    [Serializable]
    public sealed class LiveLinkDiagnosticStageSnapshot
    {
        public LiveLinkDiagnosticStage Stage;
        public LiveLinkDiagnosticResult Result;
        public string Detail;
        public DateTime TimestampUtc;
    }

    [Serializable]
    public sealed class LiveLinkDiagnosticSnapshot
    {
        public string SessionId;
        public LiveLinkDiagnosticRole Role;
        public LiveLinkDiagnosticTransport Transport;
        public LiveLinkDiagnosticRecordKind Kind;
        public string Endpoint;
        public DateTime CreatedUtc;
        public DateTime? DisconnectedUtc;
        public string LastError;
        public List<LiveLinkDiagnosticStageSnapshot> Stages = new List<LiveLinkDiagnosticStageSnapshot>();
    }
}
