#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using LiveLink;
using LiveLink.Editor.Identity;
using UnityEditor;
using UnityEditor.Networking.PlayerConnection;
using UnityEngine;

namespace LiveLink.Editor.Connection
{
    [InitializeOnLoad]
    public static class LiveLinkCompatibilityManager
    {
        public const int SupportedProtocolVersion = 1;
        private const double VerificationTimeoutSeconds = 3.0;
        private const int MaxVerificationAttempts = 3;
        private static readonly Dictionary<LiveLinkSessionKey, Session> sessions =
            new Dictionary<LiveLinkSessionKey, Session>();
        private static readonly Dictionary<int, LiveLinkSessionKey> playerSessions =
            new Dictionary<int, LiveLinkSessionKey>();
        private static readonly Dictionary<int, LiveLinkSessionKey> discoverySessions =
            new Dictionary<int, LiveLinkSessionKey>();
        private static readonly Dictionary<string, int> generations =
            new Dictionary<string, int>();
        private static string currentFingerprint;
        private static double nextManifestCheck;

        public sealed class SessionSnapshot
        {
            public LiveLinkSessionKey Key { get; internal set; }
            public LiveLinkCompatibilityState State { get; internal set; }
            public int ProtocolVersion { get; internal set; }
            public string PlayerFingerprint { get; internal set; }
            public string Diagnostic { get; internal set; }
        }

        private sealed class Session
        {
            internal SessionSnapshot Snapshot;
            internal string RequestId;
            internal double Deadline;
            internal int VerificationAttempts;
            internal string AuthorizationRequestId;
            internal bool TransformAuthorizationGranted;
        }

        static LiveLinkCompatibilityManager()
        {
            EditorApplication.update += Update;
            EditorConnection.instance.RegisterConnection(OnPlayerConnected);
            EditorConnection.instance.RegisterDisconnection(OnPlayerDisconnected);
            EditorConnection.instance.Register(
                LiveLinkProtocol.ResponseId,
                OnPlayerConnectionResponse);
            LiveLinkDiscoveryService.SessionConnected += OnDiscoveryConnected;
            LiveLinkDiscoveryService.SessionDisconnected += OnDiscoveryDisconnected;
            LiveLinkDiscoveryService.ResponseReceived += OnDiscoveryResponse;
            currentFingerprint = ComputeCurrentFingerprint();
        }

        public static IReadOnlyList<SessionSnapshot> GetSessions()
        {
            return sessions.Values.Select(session => session.Snapshot).ToArray();
        }

        public static bool IsCompatible(LiveLinkSessionKey key)
        {
            return sessions.TryGetValue(key, out Session session) &&
                LiveLinkCompatibilityEvaluator.CanSynchronize(
                    session.Snapshot.State) &&
                session.TransformAuthorizationGranted;
        }

        public static bool TrySendTransform(
            LiveLinkCommand command,
            out string diagnostic)
        {
            diagnostic = null;

            if (command == null || command.type != "TRANSFORM")
            {
                diagnostic = "Only TRANSFORM commands can be authorized.";
                return false;
            }

            var playerConnections = sessions.Values
                .Where(session =>
                    session.Snapshot.Key.Transport ==
                    LiveLinkTransportKind.PlayerConnection &&
                    session.Snapshot.State !=
                    LiveLinkCompatibilityState.Disconnected)
                .ToArray();

            if (playerConnections.Length > 0)
            {
                bool sent = false;
                foreach (Session session in playerConnections)
                {
                    if (!IsCompatible(session.Snapshot.Key))
                    {
                        diagnostic = session.Snapshot.Diagnostic;
                        continue;
                    }

                    int playerId = int.Parse(
                        session.Snapshot.Key.ConnectionId);
                    byte[] data = System.Text.Encoding.UTF8.GetBytes(
                        JsonUtility.ToJson(command));
                    sent |= EditorConnection.instance.TrySend(
                        LiveLinkProtocol.CommandId,
                        data,
                        playerId);
                }

                if (!sent && string.IsNullOrEmpty(diagnostic))
                    diagnostic = "No compatible PlayerConnection session.";

                return sent;
            }

            var discoveryConnections = sessions.Values
                .Where(session =>
                    session.Snapshot.Key.Transport ==
                    LiveLinkTransportKind.DiscoveryTcp &&
                    session.Snapshot.State !=
                    LiveLinkCompatibilityState.Disconnected)
                .ToArray();

            bool discoverySent = false;
            foreach (Session session in discoveryConnections)
            {
                if (!IsCompatible(session.Snapshot.Key))
                {
                    diagnostic = session.Snapshot.Diagnostic;
                    continue;
                }

                int playerId = int.Parse(
                    session.Snapshot.Key.ConnectionId);
                discoverySent |= LiveLinkDiscoveryService.TrySendTo(
                    playerId,
                    command);
            }

            if (!discoverySent && string.IsNullOrEmpty(diagnostic))
                diagnostic = discoveryConnections.Length == 0
                    ? "No connected Player session."
                    : "No compatible discovery session.";

            return discoverySent;
        }

        internal static void HandlePlayerConnectionResponse(
            int playerId,
            LiveLinkResponse response)
        {
            if (!playerSessions.TryGetValue(playerId, out LiveLinkSessionKey key))
                return;

            HandleResponse(key, response);
        }

        private static void OnPlayerConnectionResponse(
            UnityEngine.Networking.PlayerConnection.MessageEventArgs args)
        {
            LiveLinkResponse response = JsonUtility.FromJson<LiveLinkResponse>(
                System.Text.Encoding.UTF8.GetString(args.data));

            if (response != null)
                HandlePlayerConnectionResponse(args.playerId, response);
        }

        private static void OnDiscoveryResponse(
            int playerId,
            byte[] data)
        {
            LiveLinkResponse response = JsonUtility.FromJson<LiveLinkResponse>(
                System.Text.Encoding.UTF8.GetString(data));

            if (response != null)
                HandleDiscoveryResponse(playerId, response);
        }

        internal static void HandleDiscoveryResponse(
            int playerId,
            LiveLinkResponse response)
        {
            if (!discoverySessions.TryGetValue(playerId, out LiveLinkSessionKey key))
                return;

            HandleResponse(key, response);
        }

        internal static void InvalidateAll(string reason)
        {
            foreach (Session session in sessions.Values)
            {
                if (session.Snapshot.State ==
                    LiveLinkCompatibilityState.Disconnected)
                {
                    continue;
                }

                session.Snapshot.State =
                    LiveLinkCompatibilityState.NotVerified;
                session.Snapshot.Diagnostic = reason;
                session.VerificationAttempts = 0;
                session.TransformAuthorizationGranted = false;
                BeginVerification(session);
            }
        }

        private static void Update()
        {
            if (EditorApplication.timeSinceStartup >= nextManifestCheck)
            {
                nextManifestCheck = EditorApplication.timeSinceStartup + 1.0;
                string fingerprint = ComputeCurrentFingerprint();

                if (!string.Equals(fingerprint, currentFingerprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    currentFingerprint = fingerprint;
                    InvalidateAll("Editor identity manifest changed.");
                }
            }

            foreach (Session session in sessions.Values.ToArray())
            {
                if (session.Snapshot.State ==
                        LiveLinkCompatibilityState.Checking &&
                    EditorApplication.timeSinceStartup > session.Deadline)
                {
                    if (session.VerificationAttempts <
                        MaxVerificationAttempts)
                    {
                        BeginVerification(session);
                    }
                    else
                    {
                        session.Snapshot.State =
                            LiveLinkCompatibilityState.VerificationFailed;
                        session.Snapshot.Diagnostic =
                            "Manifest verification timed out.";
                    }
                }
            }
        }

        private static void OnPlayerConnected(int playerId)
        {
            RemovePlayerSession(playerId);
            LiveLinkSessionKey key = CreateKey(
                LiveLinkTransportKind.PlayerConnection,
                playerId.ToString());
            playerSessions[playerId] = key;
            Session session = CreateSession(key);
            BeginVerification(session);
        }

        private static void OnPlayerDisconnected(int playerId)
        {
            if (!playerSessions.TryGetValue(playerId, out LiveLinkSessionKey key))
                return;

            playerSessions.Remove(playerId);
            MarkDisconnected(key);
        }

        private static void OnDiscoveryConnected(int playerId)
        {
            if (discoverySessions.ContainsKey(playerId))
                return;

            LiveLinkSessionKey key = CreateKey(
                LiveLinkTransportKind.DiscoveryTcp,
                playerId.ToString());
            discoverySessions[playerId] = key;
            Session session = CreateSession(key);
            BeginVerification(session);
        }

        private static void OnDiscoveryDisconnected(int playerId)
        {
            if (!discoverySessions.TryGetValue(playerId, out LiveLinkSessionKey key))
                return;

            discoverySessions.Remove(playerId);
            MarkDisconnected(key);
        }

        private static LiveLinkSessionKey CreateKey(
            LiveLinkTransportKind transport,
            string connectionId)
        {
            string key = transport + ":" + connectionId;
            generations.TryGetValue(key, out int generation);
            generation++;
            generations[key] = generation;
            return new LiveLinkSessionKey(transport, connectionId, generation);
        }

        private static Session CreateSession(LiveLinkSessionKey key)
        {
            var session = new Session
            {
                Snapshot = new SessionSnapshot
                {
                    Key = key,
                    State = LiveLinkCompatibilityState.NotVerified,
                    PlayerFingerprint = string.Empty,
                    Diagnostic = "Session connected; manifest not verified."
                }
            };
            sessions[key] = session;
            return session;
        }

        private static void BeginVerification(Session session)
        {
            session.VerificationAttempts++;
            session.RequestId = Guid.NewGuid().ToString("N");
            session.AuthorizationRequestId = null;
            session.TransformAuthorizationGranted = false;
            session.Deadline = EditorApplication.timeSinceStartup +
                VerificationTimeoutSeconds;
            session.Snapshot.State = LiveLinkCompatibilityState.Checking;
            session.Snapshot.Diagnostic = "Requesting Player manifest.";

            var command = new LiveLinkCommand
            {
                requestId = session.RequestId,
                type = "MANIFEST_REQUEST"
            };

            bool sent;

            if (session.Snapshot.Key.Transport ==
                LiveLinkTransportKind.PlayerConnection)
            {
                int playerId = int.Parse(session.Snapshot.Key.ConnectionId);
                sent = EditorConnection.instance.TrySend(
                    LiveLinkProtocol.CommandId,
                    System.Text.Encoding.UTF8.GetBytes(
                        JsonUtility.ToJson(command)),
                    playerId);
            }
            else
            {
                int playerId = int.Parse(session.Snapshot.Key.ConnectionId);
                sent = LiveLinkDiscoveryService.TrySendTo(playerId, command);
            }

            if (!sent)
            {
                session.Snapshot.State =
                    LiveLinkCompatibilityState.VerificationFailed;
                session.Snapshot.Diagnostic =
                    "Could not send the manifest request.";
            }
        }

        private static void HandleResponse(
            LiveLinkSessionKey key,
            LiveLinkResponse response)
        {
            if (!sessions.TryGetValue(key, out Session session))
                return;

            if (session.Snapshot.State !=
                    LiveLinkCompatibilityState.Checking ||
                string.IsNullOrEmpty(response.requestId) ||
                response.requestId != session.RequestId)
            {
                return;
            }

            if (response.handshake == null ||
                string.IsNullOrWhiteSpace(response.handshake.fingerprint))
            {
                session.Snapshot.State =
                    LiveLinkCompatibilityState.VerificationFailed;
                session.Snapshot.Diagnostic =
                    "Manifest response did not contain a fingerprint.";
                return;
            }

            string fingerprint = response.handshake.fingerprint.Trim();
            session.Snapshot.ProtocolVersion =
                response.handshake.protocolVersion;
            session.Snapshot.PlayerFingerprint = fingerprint;

            session.Snapshot.State = LiveLinkCompatibilityEvaluator.Evaluate(
                response.handshake.protocolVersion,
                fingerprint,
                currentFingerprint);

            switch (session.Snapshot.State)
            {
                case LiveLinkCompatibilityState.Incompatible:
                    session.Snapshot.Diagnostic =
                        "Unsupported LiveLink protocol version.";
                    break;
                case LiveLinkCompatibilityState.VerificationFailed:
                    session.Snapshot.Diagnostic =
                        "Manifest fingerprint is malformed.";
                    break;
                case LiveLinkCompatibilityState.Mismatch:
                    session.Snapshot.Diagnostic =
                        "Player identity manifest differs from the Editor.";
                    break;
                case LiveLinkCompatibilityState.Compatible:
                    session.Snapshot.Diagnostic =
                        "Player identity manifest matches the Editor.";
                    BeginTransformAuthorization(session);
                    break;
            }
        }

        private static void BeginTransformAuthorization(Session session)
        {
            session.AuthorizationRequestId = Guid.NewGuid().ToString("N");
            var command = new LiveLinkCommand
            {
                requestId = session.AuthorizationRequestId,
                type = "SYNC_AUTHORIZATION",
                manifestFingerprint = currentFingerprint
            };

            bool sent;
            int playerId = int.Parse(session.Snapshot.Key.ConnectionId);
            byte[] data = System.Text.Encoding.UTF8.GetBytes(
                JsonUtility.ToJson(command));

            if (session.Snapshot.Key.Transport ==
                LiveLinkTransportKind.PlayerConnection)
            {
                sent = EditorConnection.instance.TrySend(
                    LiveLinkProtocol.CommandId,
                    data,
                    playerId);
            }
            else
            {
                sent = LiveLinkDiscoveryService.TrySendTo(
                    playerId,
                    command);
            }

            session.TransformAuthorizationGranted = sent;
            if (!sent)
            {
                session.Snapshot.Diagnostic =
                    "Manifest matched, but transform authorization could not be sent.";
            }
        }

        private static void MarkDisconnected(LiveLinkSessionKey key)
        {
            if (!sessions.TryGetValue(key, out Session session))
                return;

            session.Snapshot.State = LiveLinkCompatibilityState.Disconnected;
            session.Snapshot.Diagnostic = "Connection closed.";
            session.TransformAuthorizationGranted = false;
        }

        private static void RemovePlayerSession(int playerId)
        {
            if (!playerSessions.TryGetValue(playerId, out LiveLinkSessionKey key))
                return;

            playerSessions.Remove(playerId);
            MarkDisconnected(key);
        }

        private static string ComputeCurrentFingerprint()
        {
            var paths = new List<string>();

            foreach (EditorBuildSettingsScene scene in
                     EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                    paths.Add(scene.path);
            }

            return IdentityManifest.Compute(paths);
        }
    }
}
#endif
