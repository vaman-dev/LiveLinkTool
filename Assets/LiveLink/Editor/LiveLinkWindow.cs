
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
        private Light selectedLight;
        private int selectedLightIndex;
        private bool lastLightEnabled;
        private float lastLightIntensity;
        private Color lastLightColor;
        private float lastLightRange;
        private float lastLightSpotAngle;
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
            CaptureLight();
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

        private void CaptureLight()
        {
            selectedLight = null;
            selectedLightIndex = -1;

            if (selectedTarget == null)
                return;

            Light[] lights = selectedTarget.GetComponents<Light>();
            if (lights.Length == 0)
                return;

            selectedLight = lights[0];
            selectedLightIndex = 0;
            lastLightEnabled = selectedLight.enabled;
            lastLightIntensity = selectedLight.intensity;
            lastLightColor = selectedLight.color;
            lastLightRange = selectedLight.range;
            lastLightSpotAngle = selectedLight.spotAngle;
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
            {
                if (selectedLight == null)
                    return;

                bool lightChanged =
                    selectedLight.enabled != lastLightEnabled ||
                    !Mathf.Approximately(
                        selectedLight.intensity, lastLightIntensity) ||
                    selectedLight.color != lastLightColor ||
                    !Mathf.Approximately(
                        selectedLight.range, lastLightRange) ||
                    !Mathf.Approximately(
                        selectedLight.spotAngle, lastLightSpotAngle);

                if (!lightChanged)
                    return;

                CaptureLight();
                if (autoSync && !EditorApplication.isPlaying)
                    SendLight();
                return;
            }

            CaptureTransform();

            if (autoSync && !EditorApplication.isPlaying)
                SendTransform();
        }

        private void SendLight()
        {
            if (selectedTarget == null || selectedLight == null)
                return;

            var command = new LiveLinkCommand
            {
                type = "LIGHT_UPDATE",
                objectId = selectedTarget.ObjectId,
                lightComponentIndex = selectedLightIndex,
                light = new LiveLinkLightState
                {
                    enabled = selectedLight.enabled,
                    intensity = selectedLight.intensity,
                    color = selectedLight.color,
                    range = selectedLight.range,
                    spotAngle = selectedLight.spotAngle
                }
            };

            if (!LiveLinkCompatibilityManager.TrySendLight(
                    command, out string diagnostic))
            {
                lastResponse = "Light sync blocked: " +
                    (string.IsNullOrEmpty(diagnostic)
                        ? "Player authorization has not been verified."
                        : diagnostic);
            }
            else
            {
                lastResponse = "Light sent to compatible Player.";
            }

            Repaint();
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

            // A PlayerConnection-only count reports zero when the Player is
            // connected through the authenticated discovery/TCP fallback.
            // The compatibility manager is the transport-neutral source of
            // active sessions, so use it for the displayed connection count.
            int connections = LiveLinkCompatibilityManager.GetSessions()
                .Count(session => session.State != LiveLinkCompatibilityState.Disconnected);

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

                if (selectedLight != null &&
                    GUILayout.Button("Sync Light Now"))
                    SendLight();
            }
        }
    }
}
