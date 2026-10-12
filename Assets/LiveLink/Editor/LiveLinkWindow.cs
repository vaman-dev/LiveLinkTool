
using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Networking.PlayerConnection;
using UnityEngine;
using LiveLink.Editor.Connection;

namespace LiveLink.Editor
{
    public sealed class LiveLinkWindow : EditorWindow
    {
        private LiveLinkIdentity selectedTarget;
        private Vector3 lastPosition;
        private Quaternion lastRotation;
        private Vector3 lastScale;
        private bool autoSync = true;
        private string lastResponse = "Not connected";
        // Unity PlayerConnection IDs may be negative. Use a dedicated
        // sentinel instead of treating non-negative IDs as valid only.
        private int manifestPlayerId = int.MinValue;
        private int manifestProtocolVersion;
        private string manifestFingerprint = "";
        private string manifestBuildName = "";
        private string detectedNetworkAddress = "Detecting...";
        private string networkDiagnostic = "";
        private double nextNetworkRefresh;

        [MenuItem("Tools/LiveLink/Open LiveLink Window")]
        public static void Open()
        {
            GetWindow<LiveLinkWindow>("LiveLink");
        }

        private void OnEnable()
        {
            // Initialize session compatibility tracking before connections
            // begin delivering responses to this window.
            _ = LiveLinkCompatibilityManager.GetSessions();
            EditorConnection.instance.Initialize();

            EditorConnection.instance.Register(
                LiveLinkProtocol.ResponseId,
                OnResponse);

            EditorApplication.update += OnEditorUpdate;
            Selection.selectionChanged += OnSelectionChanged;

            OnSelectionChanged();
            RefreshNetworkConfiguration();
        }

        private void OnDisable()
        {
            EditorConnection.instance.Unregister(
                LiveLinkProtocol.ResponseId,
                OnResponse);

            EditorApplication.update -= OnEditorUpdate;
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            selectedTarget = null;

            if (Selection.activeGameObject != null)
            {
                selectedTarget =
                    Selection.activeGameObject
                        .GetComponent<LiveLinkIdentity>();
            }

            CaptureTransform();
            Repaint();
        }

        private void CaptureTransform()
        {
            if (selectedTarget == null)
                return;

            Transform t = selectedTarget.transform;

            lastPosition = t.localPosition;
            lastRotation = t.localRotation;
            lastScale = t.localScale;
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup >= nextNetworkRefresh)
            {
                RefreshNetworkConfiguration();
            }

            if (selectedTarget == null)
                return;

            Transform t = selectedTarget.transform;

            bool changed =
                t.localPosition != lastPosition ||
                Quaternion.Angle(
                    t.localRotation, lastRotation) > 0.01f ||
                t.localScale != lastScale;

            if (!changed)
                return;

            CaptureTransform();

            if (autoSync && !EditorApplication.isPlaying)
                SendTransform();
        }

        private void RefreshNetworkConfiguration()
        {
            nextNetworkRefresh = EditorApplication.timeSinceStartup + 2.0;

            if (LiveLinkNetworkDetector.TryGetPreferredAddress(
                    out string address,
                    out string diagnostic))
            {
                detectedNetworkAddress = address;
                networkDiagnostic = diagnostic;
            }
            else
            {
                detectedNetworkAddress = "Unavailable / ambiguous";
                networkDiagnostic = diagnostic;
            }

            Repaint();
        }

        private void SendTransform()
        {
            if (selectedTarget == null)
                return;

            Transform t = selectedTarget.transform;

            var command = new LiveLinkCommand
            {
                type = "TRANSFORM",
                objectId = selectedTarget.ObjectId,
                position = t.localPosition,
                rotation = t.localEulerAngles,
                scale = t.localScale
            };

            if (!LiveLinkCompatibilityManager.TrySendTransform(
                    command,
                    out string diagnostic))
            {
                lastResponse = "Sync blocked: " +
                    (string.IsNullOrEmpty(diagnostic)
                        ? "Player compatibility has not been verified."
                        : diagnostic);
                Repaint();
            }
            else
            {
                lastResponse = "Transform sent to compatible Player.";
                Repaint();
            }
        }

        private void Send(LiveLinkCommand command)
        {
            if (EditorConnection.instance
                    .ConnectedPlayers.Count == 0)
            {
                if (LiveLinkDiscoveryService.TrySend(command))
                {
                    lastResponse = "Sent through discovery connection";
                }
                else
                {
                    lastResponse = "No connected Player";
                }
                Repaint();
                return;
            }

            byte[] data = Encoding.UTF8.GetBytes(
                JsonUtility.ToJson(command));

            bool sent = EditorConnection.instance.TrySend(
                LiveLinkProtocol.CommandId,
                data);

            if (!sent)
                lastResponse = "Send failed";

            Repaint();
        }

        private void OnResponse(
            UnityEngine.Networking.PlayerConnection.MessageEventArgs args)
        {
            LiveLinkResponse response = JsonUtility.FromJson<LiveLinkResponse>(
                Encoding.UTF8.GetString(args.data));
            if (response != null)
            {
                LiveLinkCompatibilityManager.HandlePlayerConnectionResponse(
                    args.playerId,
                    response);
            }

            ProcessResponse(args.playerId, args.data);
        }

        private void ProcessResponse(int playerId, byte[] data)
        {
            string json = Encoding.UTF8.GetString(data);
            LiveLinkResponse response =
                JsonUtility.FromJson<LiveLinkResponse>(json);

            if (response == null)
                return;

            if (response.handshake != null)
            {
                manifestPlayerId = playerId;
                manifestProtocolVersion =
                    response.handshake.protocolVersion;
                manifestFingerprint =
                    response.handshake.fingerprint ?? "";
                manifestBuildName =
                    response.handshake.buildName ?? "";
            }

            lastResponse =
                response.status + ": " + response.message;

            Repaint();
        }

        internal static void ReceiveDiscoveryResponse(
            int playerId,
            byte[] data)
        {
            LiveLinkWindow[] windows =
                Resources.FindObjectsOfTypeAll<LiveLinkWindow>();

            if (windows.Length > 0)
            {
                LiveLinkResponse response = JsonUtility.FromJson<LiveLinkResponse>(
                    Encoding.UTF8.GetString(data));
                if (response != null)
                {
                    LiveLinkCompatibilityManager.HandleDiscoveryResponse(
                        playerId,
                        response);
                }

                windows[0].ProcessResponse(playerId, data);
            }
        }

        private void OnGUI()
        {
            GUILayout.Label(
                "Unity LiveLink MVP",
                EditorStyles.boldLabel);

            int connections =
                EditorConnection.instance.ConnectedPlayers.Count;

            EditorGUILayout.LabelField(
                "Connected Players",
                connections.ToString());

            EditorGUILayout.LabelField(
                "Status", lastResponse);

            var compatibilitySessions =
                LiveLinkCompatibilityManager.GetSessions();
            if (compatibilitySessions.Count > 0)
            {
                LiveLinkCompatibilityManager.SessionSnapshot active =
                    compatibilitySessions
                        .LastOrDefault(session =>
                            session.State !=
                            LiveLinkCompatibilityState.Disconnected);

                if (active != null)
                {
                    EditorGUILayout.LabelField(
                        "Compatibility",
                        active.State.ToString());
                    EditorGUILayout.LabelField(
                        "Compatibility Detail",
                        active.Diagnostic);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Network Configuration",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Detected Editor IPv4",
                detectedNetworkAddress);
            EditorGUILayout.LabelField(
                "Network Diagnostic",
                networkDiagnostic);

            if (GUILayout.Button("Refresh Network Configuration"))
                RefreshNetworkConfiguration();

            EditorGUILayout.Space();

            autoSync = EditorGUILayout.Toggle(
                "Automatic Sync", autoSync);

            if (GUILayout.Button("PING Player"))
            {
                Send(new LiveLinkCommand
                {
                    type = "PING"
                });
            }

            if (GUILayout.Button("Request Manifest"))
            {
                Send(new LiveLinkCommand
                {
                    type = "MANIFEST_REQUEST"
                });
            }

            if (manifestPlayerId != int.MinValue)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(
                    "Manifest Player ID",
                    manifestPlayerId.ToString());
                EditorGUILayout.LabelField(
                    "Player Protocol",
                    manifestProtocolVersion.ToString());
                EditorGUILayout.LabelField(
                    "Player Build",
                    manifestBuildName);
                EditorGUILayout.LabelField(
                    "Player Fingerprint",
                    manifestFingerprint);
            }

            EditorGUILayout.Space();

            if (selectedTarget == null)
            {
                EditorGUILayout.HelpBox(
                    "Select an object with LiveLinkIdentity.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField(
                    "Selected Object",
                    selectedTarget.name);

                EditorGUILayout.LabelField(
                    "Object ID",
                    selectedTarget.ObjectId);

                if (GUILayout.Button("Sync Transform Now"))
                    SendTransform();
            }
        }
    }
}
