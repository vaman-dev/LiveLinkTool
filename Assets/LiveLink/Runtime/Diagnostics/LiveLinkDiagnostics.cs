using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LiveLink.Diagnostics
{
    public static class LiveLinkDiagnostics
    {
        private sealed class Record
        {
            internal readonly LiveLinkDiagnosticSnapshot Snapshot;
            internal readonly Dictionary<LiveLinkDiagnosticStage, LiveLinkDiagnosticStageSnapshot> Stages =
                new Dictionary<LiveLinkDiagnosticStage, LiveLinkDiagnosticStageSnapshot>();
            internal string LastReportedVerdict;
            internal Record(string id, LiveLinkDiagnosticRole role,
                LiveLinkDiagnosticTransport transport, string endpoint,
                LiveLinkDiagnosticRecordKind kind)
            {
                Snapshot = new LiveLinkDiagnosticSnapshot
                {
                    SessionId = id, Role = role, Transport = transport, Kind = kind,
                    Endpoint = Safe(endpoint), CreatedUtc = DateTime.UtcNow, LastError = string.Empty
                };
                foreach (LiveLinkDiagnosticStage stage in Enum.GetValues(typeof(LiveLinkDiagnosticStage)))
                {
                    var state = new LiveLinkDiagnosticStageSnapshot
                    {
                        Stage = stage, Result = LiveLinkDiagnosticResult.NotTested,
                        Detail = "Not observed", TimestampUtc = Snapshot.CreatedUtc
                    };
                    Stages[stage] = state;
                    Snapshot.Stages.Add(state);
                }
            }
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Record> Active = new Dictionary<string, Record>();
        private static readonly LinkedList<LiveLinkDiagnosticSnapshot> History =
            new LinkedList<LiveLinkDiagnosticSnapshot>();
        private static readonly Queue<string> PendingReports = new Queue<string>();
        private static int historyLimit = 32;

        public static int HistoryLimit
        {
            get { lock (Gate) return historyLimit; }
            set { lock (Gate) historyLimit = Math.Max(4, value); }
        }

        public static string BeginSession(LiveLinkDiagnosticRole role,
            LiveLinkDiagnosticTransport transport, string sessionId, string endpoint,
            LiveLinkDiagnosticRecordKind kind = LiveLinkDiagnosticRecordKind.LiveLinkSession)
        {
            string id = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId;
            lock (Gate)
            {
                var record = new Record(id, role, transport, endpoint, kind);
                ApplyApplicability(record);
                Active[id] = record;
                return id;
            }
        }

        public static void Pass(string id, LiveLinkDiagnosticStage stage, string detail = null)
        { Mark(id, stage, LiveLinkDiagnosticResult.Pass, detail); }
        public static void Fail(string id, LiveLinkDiagnosticStage stage, string detail)
        { Mark(id, stage, LiveLinkDiagnosticResult.Fail, detail); }
        public static void Pending(string id, LiveLinkDiagnosticStage stage, string detail = null)
        { Mark(id, stage, LiveLinkDiagnosticResult.Pending, detail); }
        public static void NotApplicable(string id, LiveLinkDiagnosticStage stage, string detail)
        { Mark(id, stage, LiveLinkDiagnosticResult.NotApplicable, detail); }

        public static void Disconnect(string id, string reason)
        {
            lock (Gate)
            {
                Record record;
                if (string.IsNullOrEmpty(id) || !Active.TryGetValue(id, out record)) return;
                record.Snapshot.DisconnectedUtc = DateTime.UtcNow;
                record.Snapshot.LastError = Safe(reason);
                QueueReportNoLock(record);
                AddHistoryNoLock(record.Snapshot);
                Active.Remove(id);
            }
        }

        public static List<LiveLinkDiagnosticSnapshot> GetActive()
        { lock (Gate) return Active.Values.Select(record => Clone(record.Snapshot)).ToList(); }
        public static List<LiveLinkDiagnosticSnapshot> GetHistory()
        { lock (Gate) return History.Select(snapshot => Clone(snapshot)).ToList(); }

        public static List<string> DrainReports()
        {
            lock (Gate)
            {
                var reports = new List<string>();
                while (PendingReports.Count > 0) reports.Add(PendingReports.Dequeue());
                return reports;
            }
        }

        public static string Format(LiveLinkDiagnosticSnapshot snapshot)
        {
            if (snapshot == null) return "[LiveLink Diagnostic] No session recorded.";
            var sb = new StringBuilder();
            sb.AppendLine("========== LIVELINK CONNECTION DIAGNOSTIC ==========");
            sb.AppendLine("Role: " + snapshot.Role);
            sb.AppendLine("Transport: " + snapshot.Transport);
            sb.AppendLine("Record: " + snapshot.Kind);
            sb.AppendLine("Session: " + snapshot.SessionId);
            sb.AppendLine("Endpoint: " + snapshot.Endpoint);
            sb.AppendLine("Created UTC: " + snapshot.CreatedUtc.ToString("O"));
            foreach (var stage in snapshot.Stages)
            {
                sb.Append("  ").Append(stage.Stage.ToString().PadRight(26)).Append(" : ")
                    .Append(stage.Result.ToString().ToUpperInvariant());
                if (!string.IsNullOrWhiteSpace(stage.Detail)) sb.Append(" | ").Append(Safe(stage.Detail));
                sb.AppendLine();
            }
            sb.AppendLine((snapshot.Kind == LiveLinkDiagnosticRecordKind.Service
                ? "Service" : "Session") + " verdict: " + Verdict(snapshot));
            sb.AppendLine("First action: " + NextAction(snapshot));
            if (!string.IsNullOrWhiteSpace(snapshot.LastError)) sb.AppendLine("Last issue: " + Safe(snapshot.LastError));
            sb.AppendLine("====================================================");
            return sb.ToString();
        }

        public static string FormatActiveAndHistory()
        {
            lock (Gate)
            {
                var sb = new StringBuilder();
                sb.AppendLine("[LiveLink Diagnostic] Active sessions: " + Active.Count);
                foreach (Record record in Active.Values) sb.AppendLine(Format(record.Snapshot));
                if (Active.Count == 0 && History.Last != null) sb.AppendLine(Format(History.Last.Value));
                if (Active.Count == 0 && History.Last == null) sb.AppendLine("No session has been observed.");
                return sb.ToString();
            }
        }

        public static string Verdict(LiveLinkDiagnosticSnapshot snapshot)
        {
            if (snapshot.DisconnectedUtc.HasValue) return "DISCONNECTED";
            if (snapshot.Stages.Any(s => s.Result == LiveLinkDiagnosticResult.Fail)) return "FAILED";
            var required = Required(snapshot).ToList();
            if (required.Any(s => s.Result == LiveLinkDiagnosticResult.Pending)) return "CONNECTING";
            if (required.All(s => s.Result == LiveLinkDiagnosticResult.Pass ||
                s.Result == LiveLinkDiagnosticResult.NotApplicable)) return "READY";
            return "NOT_VERIFIED";
        }

        public static string NextAction(LiveLinkDiagnosticSnapshot snapshot)
        {
            var failed = snapshot.Stages.FirstOrDefault(s => s.Result == LiveLinkDiagnosticResult.Fail);
            if (failed != null) return "Inspect " + failed.Stage + ": " + Safe(failed.Detail);
            var pending = Required(snapshot).FirstOrDefault(s =>
                s.Result == LiveLinkDiagnosticResult.Pending || s.Result == LiveLinkDiagnosticResult.NotTested);
            return pending == null ? "Run a real Transform or Light update to verify acknowledgement." :
                "Wait for or inspect " + pending.Stage + ".";
        }

        private static IEnumerable<LiveLinkDiagnosticStageSnapshot> Required(LiveLinkDiagnosticSnapshot snapshot)
        { return snapshot.Stages.Where(s => s.Result != LiveLinkDiagnosticResult.NotApplicable &&
            s.Stage != LiveLinkDiagnosticStage.SyncAuthorizationAcknowledged &&
            s.Stage != LiveLinkDiagnosticStage.TransformCommandAcknowledged &&
            s.Stage != LiveLinkDiagnosticStage.LightCommandAcknowledged); }

        private static void Mark(string id, LiveLinkDiagnosticStage stage,
            LiveLinkDiagnosticResult result, string detail)
        {
            lock (Gate)
            {
                Record record;
                if (string.IsNullOrEmpty(id) || !Active.TryGetValue(id, out record)) return;
                var state = record.Stages[stage];
                string before = Verdict(record.Snapshot);
                state.Result = result; state.Detail = Safe(detail); state.TimestampUtc = DateTime.UtcNow;
                if (result == LiveLinkDiagnosticResult.Fail) record.Snapshot.LastError = state.Detail;
                string after = Verdict(record.Snapshot);
                if (result == LiveLinkDiagnosticResult.Fail ||
                    (after == "READY" && before != "READY")) QueueReportNoLock(record);
            }
        }

        private static void SetNotApplicable(Record record, LiveLinkDiagnosticStage stage, string detail)
        { record.Stages[stage].Result = LiveLinkDiagnosticResult.NotApplicable; record.Stages[stage].Detail = Safe(detail); }
        private static void AddHistoryNoLock(LiveLinkDiagnosticSnapshot snapshot)
        { History.AddLast(Clone(snapshot)); while (History.Count > historyLimit) History.RemoveFirst(); }
        private static LiveLinkDiagnosticSnapshot Clone(LiveLinkDiagnosticSnapshot source)
        {
            var copy = new LiveLinkDiagnosticSnapshot
            {
                SessionId = source.SessionId, Role = source.Role, Transport = source.Transport,
                Kind = source.Kind,
                Endpoint = source.Endpoint, CreatedUtc = source.CreatedUtc,
                DisconnectedUtc = source.DisconnectedUtc, LastError = source.LastError
            };
            copy.Stages = source.Stages.Select(s => new LiveLinkDiagnosticStageSnapshot
            { Stage = s.Stage, Result = s.Result, Detail = s.Detail, TimestampUtc = s.TimestampUtc }).ToList();
            return copy;
        }
        private static string Safe(string text)
        {
            string value = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            string lower = value.ToLowerInvariant();
            string[] markers = { "secret=", "hmac=", "proof=" };
            foreach (string marker in markers)
            {
                int index = lower.IndexOf(marker, StringComparison.Ordinal);
                if (index >= 0)
                {
                    int end = value.IndexOf(' ', index);
                    if (end < 0) end = value.Length;
                    value = value.Substring(0, index) + marker + "<redacted>" + value.Substring(end);
                    lower = value.ToLowerInvariant();
                }
            }
            return value;
        }

        private static void QueueReportNoLock(Record record)
        {
            string verdict = Verdict(record.Snapshot);
            if (record.LastReportedVerdict == verdict && verdict != "FAILED" && verdict != "DISCONNECTED") return;
            record.LastReportedVerdict = verdict;
            PendingReports.Enqueue(Format(record.Snapshot));
            while (PendingReports.Count > 64) PendingReports.Dequeue();
        }

        private static void ApplyApplicability(Record record)
        {
            foreach (LiveLinkDiagnosticStage stage in Enum.GetValues(typeof(LiveLinkDiagnosticStage)))
                SetNotApplicable(record, stage, "Not applicable to this record.");

            LiveLinkDiagnosticStage[] required;
            switch (record.Snapshot.Kind)
            {
                case LiveLinkDiagnosticRecordKind.Service:
                    required = new[] { LiveLinkDiagnosticStage.UdpAnnouncementSent,
                        LiveLinkDiagnosticStage.TcpListenerStarted };
                    break;
                case LiveLinkDiagnosticRecordKind.TcpConnection:
                    required = new[] { LiveLinkDiagnosticStage.TcpConnected,
                        LiveLinkDiagnosticStage.HmacAuthenticated };
                    break;
                default:
                    required = record.Snapshot.Role == LiveLinkDiagnosticRole.Player
                        ? new[] { LiveLinkDiagnosticStage.UdpAnnouncementReceived,
                            LiveLinkDiagnosticStage.TcpConnected,
                            LiveLinkDiagnosticStage.HmacAuthenticated,
                            LiveLinkDiagnosticStage.ManifestCompatible }
                        : record.Snapshot.Transport == LiveLinkDiagnosticTransport.PlayerConnection
                            ? new[] { LiveLinkDiagnosticStage.ManifestCompatible }
                            : new[] { LiveLinkDiagnosticStage.TcpConnected,
                                LiveLinkDiagnosticStage.HmacAuthenticated,
                                LiveLinkDiagnosticStage.ManifestCompatible };
                    break;
            }
            foreach (LiveLinkDiagnosticStage stage in required)
            {
                record.Stages[stage].Result = LiveLinkDiagnosticResult.NotTested;
                record.Stages[stage].Detail = "Not observed";
            }
        }
    }
}
