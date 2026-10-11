
using System;
using System.Text;
using UnityEditor;
using UnityEditor.Networking.PlayerConnection;
using UnityEngine;

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

        [MenuItem("Tools/LiveLink")]
        public static void Open()
        {
            GetWindow<LiveLinkWindow>("LiveLink");
        }

        private void OnEnable()
        {
            EditorConnection.instance.Initialize();

            EditorConnection.instance.Register(
                LiveLinkProtocol.ResponseId,
                OnResponse);

            EditorApplication.update += OnEditorUpdate;
            Selection.selectionChanged += OnSelectionChanged;

            OnSelectionChanged();
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

        private void SendTransform()
        {
            if (selectedTarget == null)
                return;

            Transform t = selectedTarget.transform;

            Send(new LiveLinkCommand
            {
                type = "TRANSFORM",
                objectId = selectedTarget.ObjectId,
                position = t.localPosition,
                rotation = t.localEulerAngles,
                scale = t.localScale
            });
        }

        private void Send(LiveLinkCommand command)
        {
            if (EditorConnection.instance
                    .ConnectedPlayers.Count == 0)
            {
                lastResponse = "No connected Player";
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
            string json = Encoding.UTF8.GetString(args.data);
            LiveLinkResponse response =
                JsonUtility.FromJson<LiveLinkResponse>(json);

            if (response == null)
                return;

            lastResponse =
                response.status + ": " + response.message;

            Repaint();
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
