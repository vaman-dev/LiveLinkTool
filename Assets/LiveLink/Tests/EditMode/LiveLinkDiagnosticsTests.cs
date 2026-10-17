using System.Linq;
using LiveLink.Diagnostics;
using NUnit.Framework;

namespace LiveLink.Tests
{
    public sealed class LiveLinkDiagnosticsTests
    {
        [Test]
        public void TcpSessionStartsAsNotVerified()
        {
            string id = LiveLinkDiagnostics.BeginSession(
                LiveLinkDiagnosticRole.Editor,
                LiveLinkDiagnosticTransport.DiscoveryTcp,
                "diag-start", "127.0.0.1:27071");
            var snapshot = LiveLinkDiagnostics.GetActive().Single(s => s.SessionId == id);
            Assert.AreEqual("NOT_VERIFIED", LiveLinkDiagnostics.Verdict(snapshot));
        }

        [Test]
        public void NativeSessionSeparatesTransportStages()
        {
            string id = LiveLinkDiagnostics.BeginSession(
                LiveLinkDiagnosticRole.Editor,
                LiveLinkDiagnosticTransport.PlayerConnection,
                "native-diag", "player-1");
            var snapshot = LiveLinkDiagnostics.GetActive().Single(s => s.SessionId == id);
            Assert.IsTrue(snapshot.Stages.Any(s => s.Stage == LiveLinkDiagnosticStage.TcpConnected &&
                s.Result == LiveLinkDiagnosticResult.NotApplicable));
        }

        [Test]
        public void StaleSessionCannotChangeAfterDisconnect()
        {
            string id = LiveLinkDiagnostics.BeginSession(
                LiveLinkDiagnosticRole.Player,
                LiveLinkDiagnosticTransport.DiscoveryTcp,
                "diag-disconnect", "editor");
            LiveLinkDiagnostics.Disconnect(id, "test disconnect");
            Assert.IsFalse(LiveLinkDiagnostics.GetActive().Any(s => s.SessionId == id));
            int historyCount = LiveLinkDiagnostics.GetHistory().Count(s => s.SessionId == id);
            LiveLinkDiagnostics.Pass(id, LiveLinkDiagnosticStage.ManifestCompatible, "stale callback");
            Assert.AreEqual(historyCount, LiveLinkDiagnostics.GetHistory().Count(s => s.SessionId == id));
        }

        [Test]
        public void ReadyRequiresManifestAndConnectionStages()
        {
            string id = LiveLinkDiagnostics.BeginSession(
                LiveLinkDiagnosticRole.Editor,
                LiveLinkDiagnosticTransport.DiscoveryTcp,
                "diag-ready", "editor");
            LiveLinkDiagnostics.Pass(id, LiveLinkDiagnosticStage.TcpListenerStarted);
            LiveLinkDiagnostics.Pass(id, LiveLinkDiagnosticStage.UdpAnnouncementSent);
            LiveLinkDiagnostics.Pass(id, LiveLinkDiagnosticStage.TcpConnected);
            LiveLinkDiagnostics.Pass(id, LiveLinkDiagnosticStage.HmacAuthenticated);
            LiveLinkDiagnostics.Pass(id, LiveLinkDiagnosticStage.ManifestCompatible);
            Assert.AreEqual("READY", LiveLinkDiagnostics.Verdict(
                LiveLinkDiagnostics.GetActive().Single(s => s.SessionId == id)));
        }

        [Test]
        public void ServiceRecordDoesNotRequireTcpConnection()
        {
            string id = LiveLinkDiagnostics.BeginSession(
                LiveLinkDiagnosticRole.Editor,
                LiveLinkDiagnosticTransport.DiscoveryTcp,
                "diag-service", "0.0.0.0:27071",
                LiveLinkDiagnosticRecordKind.Service);
            LiveLinkDiagnostics.Pass(id, LiveLinkDiagnosticStage.UdpAnnouncementSent);
            LiveLinkDiagnostics.Pass(id, LiveLinkDiagnosticStage.TcpListenerStarted);
            var snapshot = LiveLinkDiagnostics.GetActive().Single(s => s.SessionId == id);
            Assert.AreEqual("READY", LiveLinkDiagnostics.Verdict(snapshot));
        }

        [Test]
        public void TcpConnectionRecordDoesNotRequireManifest()
        {
            string id = LiveLinkDiagnostics.BeginSession(
                LiveLinkDiagnosticRole.Editor,
                LiveLinkDiagnosticTransport.DiscoveryTcp,
                "diag-tcp", "peer",
                LiveLinkDiagnosticRecordKind.TcpConnection);
            LiveLinkDiagnostics.Pass(id, LiveLinkDiagnosticStage.TcpConnected);
            LiveLinkDiagnostics.Pass(id, LiveLinkDiagnosticStage.HmacAuthenticated);
            Assert.AreEqual("READY", LiveLinkDiagnostics.Verdict(
                LiveLinkDiagnostics.GetActive().Single(s => s.SessionId == id)));
        }

        [Test]
        public void AuthorizationAckIsSeparateFromPropertyAcks()
        {
            string id = LiveLinkDiagnostics.BeginSession(
                LiveLinkDiagnosticRole.Editor,
                LiveLinkDiagnosticTransport.DiscoveryTcp,
                "diag-acks", "peer");
            LiveLinkDiagnostics.Pass(id, LiveLinkDiagnosticStage.SyncAuthorizationAcknowledged);
            var snapshot = LiveLinkDiagnostics.GetActive().Single(s => s.SessionId == id);
            Assert.AreEqual(LiveLinkDiagnosticResult.Pass, snapshot.Stages.Single(s =>
                s.Stage == LiveLinkDiagnosticStage.SyncAuthorizationAcknowledged).Result);
            Assert.AreEqual(LiveLinkDiagnosticResult.NotApplicable, snapshot.Stages.Single(s =>
                s.Stage == LiveLinkDiagnosticStage.TransformCommandAcknowledged).Result);
            Assert.AreEqual(LiveLinkDiagnosticResult.NotApplicable, snapshot.Stages.Single(s =>
                s.Stage == LiveLinkDiagnosticStage.LightCommandAcknowledged).Result);
        }

        [Test]
        public void FailedStageProducesFailedVerdict()
        {
            string id = LiveLinkDiagnostics.BeginSession(
                LiveLinkDiagnosticRole.Editor,
                LiveLinkDiagnosticTransport.DiscoveryTcp,
                "diag-fail", "editor");
            LiveLinkDiagnostics.Fail(id, LiveLinkDiagnosticStage.HmacAuthenticated, "invalid proof");
            Assert.AreEqual("FAILED", LiveLink.Diagnostics.LiveLinkDiagnostics.Verdict(
                LiveLinkDiagnostics.GetActive().Single(s => s.SessionId == id)));
        }

        [Test]
        public void ReportDoesNotContainCredentialMaterial()
        {
            string id = LiveLinkDiagnostics.BeginSession(
                LiveLinkDiagnosticRole.Editor,
                LiveLinkDiagnosticTransport.DiscoveryTcp,
                "diag-safe", "editor");
            LiveLinkDiagnostics.Pass(id, LiveLinkDiagnosticStage.HmacAuthenticated,
                "proof verified; secret=SHOULD_NOT_BE_LOGGED");
            string report = LiveLinkDiagnostics.Format(
                LiveLinkDiagnostics.GetActive().Single(s => s.SessionId == id));
            Assert.IsFalse(report.Contains("SHOULD_NOT_BE_LOGGED"));
        }

        [Test]
        public void HistoryIsBounded()
        {
            int oldLimit = LiveLinkDiagnostics.HistoryLimit;
            LiveLinkDiagnostics.HistoryLimit = 4;
            for (int i = 0; i < 8; i++)
            {
                string id = LiveLinkDiagnostics.BeginSession(
                    LiveLinkDiagnosticRole.Editor,
                    LiveLinkDiagnosticTransport.DiscoveryTcp,
                    "diag-history-" + i, "editor");
                LiveLinkDiagnostics.Disconnect(id, "done");
            }
            Assert.LessOrEqual(LiveLinkDiagnostics.GetHistory().Count, 4);
            LiveLinkDiagnostics.HistoryLimit = oldLimit;
        }
    }
}
