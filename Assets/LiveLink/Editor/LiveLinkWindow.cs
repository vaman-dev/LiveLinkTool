
using System;
using System.Collections.Generic;
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
        private enum TrackingScope
        {
            SelectedObject,
            AllValidLiveLinkObjects
        }

        private sealed class TrackedObjectState
        {
            public LiveLinkIdentity identity;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public Light light;
            public int lightIndex = -1;
            public bool lightEnabled;
            public float lightIntensity;
            public Color lightColor;
            public float lightRange;
            public float lightSpotAngle;
            public Renderer renderer;
            public int rendererIndex = -1;
            public Material[] materials = Array.Empty<Material>();
            public MaterialSnapshot[] materialSnapshots = Array.Empty<MaterialSnapshot>();
            public readonly List<int> pendingMaterialSlots = new List<int>();
            public bool pendingTransform;
            public bool pendingLight;
            public bool pendingMaterial;
            public double pendingDeadline;
        }

        private struct MaterialSnapshot
        {
            public Material material;
            public Color color;
            public float metallic;
            public float smoothness;
            public Color emission;
        }

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
        private Renderer selectedRenderer;
        private int selectedRendererIndex;
        private int selectedMaterialSlot;
        private Material selectedMaterial;
        private Color lastMaterialColor;
        private float lastMaterialMetallic;
        private float lastMaterialSmoothness;
        private Color lastMaterialEmission;
        private const double AutoSyncDebounceSeconds = 0.15;
        private bool pendingLightSync;
        private bool pendingMaterialSync;
        private LiveLinkIdentity pendingSyncTarget;
        private double pendingSyncDeadline;
        private Material lastMaterialReference;
        private int lastMaterialSlotCount;
        private bool autoSync = true;
        private TrackingScope trackingScope = TrackingScope.SelectedObject;
        private readonly List<TrackedObjectState> trackedObjects =
            new List<TrackedObjectState>();
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
            ClearPendingAutomaticSync();
            selectedTarget = null;

            if (Selection.activeGameObject != null)
            {
                selectedTarget =
                    Selection.activeGameObject
                        .GetComponent<LiveLinkIdentity>();
            }

            selectedMaterialSlot = 0;
            CaptureTransform();
            CaptureLight();
            CaptureMaterial();
            Repaint();
        }

        private void CaptureTransform()
        {
            if (trackingScope == TrackingScope.AllValidLiveLinkObjects)
            {
                DetectAllTrackedObjects();
                return;
            }

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

        private void CaptureMaterial()
        {
            selectedRenderer = null;
            selectedMaterial = null;
            selectedRendererIndex = -1;

            if (selectedTarget == null)
                return;

            Renderer[] renderers = selectedTarget.GetComponents<Renderer>();
            if (renderers.Length == 0 || renderers[0] == null)
                return;

            Material[] materials = renderers[0].sharedMaterials;
            if (materials.Length == 0)
                return;

            selectedMaterialSlot = Mathf.Clamp(
                selectedMaterialSlot, 0, materials.Length - 1);
            if (materials[selectedMaterialSlot] == null)
                return;

            selectedRenderer = renderers[0];
            selectedRendererIndex = 0;
            selectedMaterial = materials[selectedMaterialSlot];
            lastMaterialReference = selectedMaterial;
            lastMaterialSlotCount = materials.Length;
            lastMaterialColor = ReadMaterialColor(selectedMaterial);
            lastMaterialMetallic = ReadMaterialFloat(
                selectedMaterial, "_Metallic", 0f);
            lastMaterialSmoothness = selectedMaterial.HasProperty("_Smoothness")
                ? selectedMaterial.GetFloat("_Smoothness")
                : ReadMaterialFloat(selectedMaterial, "_Glossiness", 0f);
            lastMaterialEmission = ReadMaterialColor(
                selectedMaterial, "_EmissionColor");
        }

        private static Color ReadMaterialColor(
            Material material,
            string preferred = "_BaseColor")
        {
            if (material == null)
                return Color.white;
            if (material.HasProperty(preferred))
                return material.GetColor(preferred);
            if (preferred == "_BaseColor" && material.HasProperty("_Color"))
                return material.GetColor("_Color");
            return Color.white;
        }

        private static float ReadMaterialFloat(
            Material material,
            string property,
            float fallback)
        {
            return material != null && material.HasProperty(property)
                ? material.GetFloat(property) : fallback;
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup >= nextNetworkRefresh)
            {
                RefreshNetworkConfiguration();
            }

            if (selectedTarget == null)
                return;

            if (!autoSync)
            {
                ClearPendingAutomaticSync();
            }
            else if (pendingSyncTarget == selectedTarget &&
                     EditorApplication.timeSinceStartup >= pendingSyncDeadline)
            {
                bool sendLight = pendingLightSync;
                bool sendMaterial = pendingMaterialSync;
                ClearPendingAutomaticSync();

                if (!EditorApplication.isPlaying)
                {
                    if (sendLight)
                        SendLight();
                    if (sendMaterial)
                        SendMaterial();
                }
            }

            Transform t = selectedTarget.transform;

            bool changed =
                t.localPosition != lastPosition ||
                Quaternion.Angle(
                    t.localRotation, lastRotation) > 0.01f ||
                t.localScale != lastScale;

            if (!changed)
            {
                if (selectedLight == null)
                {
                    DetectAndSendMaterial();
                    return;
                }

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
                {
                    if (selectedRenderer == null || selectedMaterial == null)
                        return;

                    Material[] materials = selectedRenderer.sharedMaterials;
                    if (selectedMaterialSlot >= materials.Length ||
                        materials[selectedMaterialSlot] == null)
                        return;

                    Material currentMaterial = materials[selectedMaterialSlot];
                    if (materials.Length != lastMaterialSlotCount ||
                        currentMaterial != lastMaterialReference)
                    {
                        // Material assignment/slot layout changes are not a
                        // property update. Refresh the baseline safely and
                        // wait for a supported property edit.
                        CaptureMaterial();
                        return;
                    }

                    selectedMaterial = currentMaterial;
                    bool materialChanged =
                        ReadMaterialColor(selectedMaterial) != lastMaterialColor ||
                        !Mathf.Approximately(ReadMaterialFloat(
                            selectedMaterial, "_Metallic", 0f),
                            lastMaterialMetallic) ||
                        !Mathf.Approximately(
                            selectedMaterial.HasProperty("_Smoothness")
                                ? selectedMaterial.GetFloat("_Smoothness")
                                : ReadMaterialFloat(
                                    selectedMaterial, "_Glossiness", 0f),
                            lastMaterialSmoothness) ||
                        ReadMaterialColor(
                            selectedMaterial, "_EmissionColor") !=
                            lastMaterialEmission;

                    if (!materialChanged)
                        return;

                    CaptureMaterial();
                    QueueAutomaticSync(material: true);
                    return;
                }

                CaptureLight();
                QueueAutomaticSync(light: true);
                return;
            }

            CaptureTransform();

            if (autoSync && !EditorApplication.isPlaying)
                SendTransform();
        }

        private void DetectAndSendMaterial()
        {
            if (selectedRenderer == null || selectedMaterial == null)
                return;

            Material[] materials = selectedRenderer.sharedMaterials;
            if (selectedMaterialSlot >= materials.Length ||
                materials[selectedMaterialSlot] == null)
                return;

            Material currentMaterial = materials[selectedMaterialSlot];
            if (materials.Length != lastMaterialSlotCount ||
                currentMaterial != lastMaterialReference)
            {
                CaptureMaterial();
                return;
            }

            selectedMaterial = currentMaterial;
            bool materialChanged =
                ReadMaterialColor(selectedMaterial) != lastMaterialColor ||
                !Mathf.Approximately(ReadMaterialFloat(
                    selectedMaterial, "_Metallic", 0f),
                    lastMaterialMetallic) ||
                !Mathf.Approximately(
                    selectedMaterial.HasProperty("_Smoothness")
                        ? selectedMaterial.GetFloat("_Smoothness")
                        : ReadMaterialFloat(
                            selectedMaterial, "_Glossiness", 0f),
                    lastMaterialSmoothness) ||
                ReadMaterialColor(
                    selectedMaterial, "_EmissionColor") !=
                    lastMaterialEmission;

            if (!materialChanged)
                return;

            CaptureMaterial();
            QueueAutomaticSync(material: true);
        }

        private void QueueAutomaticSync(bool light = false, bool material = false)
        {
            if (!autoSync || selectedTarget == null || EditorApplication.isPlaying)
                return;

            pendingLightSync |= light;
            pendingMaterialSync |= material;
            pendingSyncTarget = selectedTarget;
            pendingSyncDeadline =
                EditorApplication.timeSinceStartup + AutoSyncDebounceSeconds;
        }

        private void ClearPendingAutomaticSync()
        {
            pendingLightSync = false;
            pendingMaterialSync = false;
            pendingSyncTarget = null;
            pendingSyncDeadline = 0d;
        }

        private void DetectAllTrackedObjects()
        {
            ReconcileTrackedObjects();

            if (!autoSync || EditorApplication.isPlaying)
            {
                foreach (TrackedObjectState state in trackedObjects)
                {
                    state.pendingTransform = false;
                    state.pendingLight = false;
                    state.pendingMaterial = false;
                }
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            foreach (TrackedObjectState state in trackedObjects)
            {
                DetectTrackedObjectChanges(state, now);
                if (now < state.pendingDeadline)
                    continue;

                if (state.pendingTransform)
                    SendTransformFor(state);
                if (state.pendingLight)
                    SendLightFor(state);
                if (state.pendingMaterial)
                    SendMaterialFor(state);

                state.pendingTransform = false;
                state.pendingLight = false;
                state.pendingMaterial = false;
                state.pendingMaterialSlots.Clear();
            }
        }

        private void ReconcileTrackedObjects()
        {
            LiveLinkIdentity[] identities =
                Resources.FindObjectsOfTypeAll<LiveLinkIdentity>();

            for (int i = trackedObjects.Count - 1; i >= 0; i--)
            {
                TrackedObjectState state = trackedObjects[i];
                if (state.identity == null ||
                    !state.identity.gameObject.scene.IsValid() ||
                    !state.identity.gameObject.scene.isLoaded ||
                    !ContainsIdentity(identities, state.identity))
                {
                    trackedObjects.RemoveAt(i);
                }
            }

            foreach (LiveLinkIdentity identity in identities)
            {
                if (identity == null ||
                    !identity.gameObject.scene.IsValid() ||
                    !identity.gameObject.scene.isLoaded ||
                    string.IsNullOrEmpty(identity.ObjectId))
                    continue;

                if (FindTrackedState(identity) == null)
                    trackedObjects.Add(CreateTrackedState(identity));
            }
        }

        private static bool ContainsIdentity(
            LiveLinkIdentity[] identities,
            LiveLinkIdentity target)
        {
            foreach (LiveLinkIdentity identity in identities)
            {
                if (identity == target)
                    return true;
            }
            return false;
        }

        private TrackedObjectState FindTrackedState(LiveLinkIdentity identity)
        {
            foreach (TrackedObjectState state in trackedObjects)
            {
                if (state.identity == identity)
                    return state;
            }
            return null;
        }

        private TrackedObjectState CreateTrackedState(LiveLinkIdentity identity)
        {
            TrackedObjectState state = new TrackedObjectState
            {
                identity = identity,
                position = identity.transform.localPosition,
                rotation = identity.transform.localRotation,
                scale = identity.transform.localScale
            };
            CaptureTrackedLight(state);
            CaptureTrackedMaterials(state);
            return state;
        }

        private void DetectTrackedObjectChanges(
            TrackedObjectState state,
            double now)
        {
            if (state.identity == null)
                return;

            Transform transform = state.identity.transform;
            if (transform.localPosition != state.position ||
                Quaternion.Angle(transform.localRotation, state.rotation) > 0.01f ||
                transform.localScale != state.scale)
            {
                state.position = transform.localPosition;
                state.rotation = transform.localRotation;
                state.scale = transform.localScale;
                QueueTrackedChange(state, transformChange: true);
            }

            Light currentLight = state.identity.GetComponent<Light>();
            if (currentLight != state.light)
            {
                CaptureTrackedLight(state);
                return;
            }

            if (currentLight != null &&
                (currentLight.enabled != state.lightEnabled ||
                 !Mathf.Approximately(currentLight.intensity, state.lightIntensity) ||
                 currentLight.color != state.lightColor ||
                 !Mathf.Approximately(currentLight.range, state.lightRange) ||
                 !Mathf.Approximately(currentLight.spotAngle, state.lightSpotAngle)))
            {
                CaptureTrackedLight(state);
                QueueTrackedChange(state, lightChange: true);
            }

            Renderer currentRenderer = state.identity.GetComponent<Renderer>();
            if (currentRenderer != state.renderer)
            {
                CaptureTrackedMaterials(state);
                return;
            }

            if (currentRenderer == null)
                return;

            Material[] materials = currentRenderer.sharedMaterials;
            if (materials.Length != state.materialSnapshots.Length)
            {
                CaptureTrackedMaterials(state);
                return;
            }

            bool materialChanged = false;
            foreach (int i in state.pendingMaterialSlots)
            {
                if (i < 0 || i >= materials.Length)
                    continue;

                Material material = materials[i];
                MaterialSnapshot snapshot = state.materialSnapshots[i];
                if (material != snapshot.material)
                {
                    CaptureTrackedMaterials(state);
                    return;
                }

                if (material == null)
                    continue;

                if (ReadMaterialColor(material) != snapshot.color ||
                    !Mathf.Approximately(ReadMaterialFloat(material, "_Metallic", 0f), snapshot.metallic) ||
                    !Mathf.Approximately(ReadMaterialSmoothness(material), snapshot.smoothness) ||
                    ReadMaterialColor(material, "_EmissionColor") != snapshot.emission)
                {
                    materialChanged = true;
                    if (!state.pendingMaterialSlots.Contains(i))
                        state.pendingMaterialSlots.Add(i);
                }
            }

            if (materialChanged)
            {
                CaptureTrackedMaterials(state);
                QueueTrackedChange(state, materialChange: true);
            }
        }

        private void QueueTrackedChange(
            TrackedObjectState state,
            bool transformChange = false,
            bool lightChange = false,
            bool materialChange = false)
        {
            state.pendingTransform |= transformChange;
            state.pendingLight |= lightChange;
            state.pendingMaterial |= materialChange;
            state.pendingDeadline =
                EditorApplication.timeSinceStartup + AutoSyncDebounceSeconds;
        }

        private void CaptureTrackedLight(TrackedObjectState state)
        {
            state.light = state.identity.GetComponent<Light>();
            state.lightIndex = state.light == null ? -1 : 0;
            if (state.light == null)
                return;

            state.lightEnabled = state.light.enabled;
            state.lightIntensity = state.light.intensity;
            state.lightColor = state.light.color;
            state.lightRange = state.light.range;
            state.lightSpotAngle = state.light.spotAngle;
        }

        private void CaptureTrackedMaterials(TrackedObjectState state)
        {
            state.renderer = state.identity.GetComponent<Renderer>();
            state.rendererIndex = state.renderer == null ? -1 : 0;
            state.materials = state.renderer == null
                ? Array.Empty<Material>()
                : state.renderer.sharedMaterials;
            state.materialSnapshots = new MaterialSnapshot[state.materials.Length];

            for (int i = 0; i < state.materials.Length; i++)
            {
                Material material = state.materials[i];
                state.materialSnapshots[i] = new MaterialSnapshot
                {
                    material = material,
                    color = ReadMaterialColor(material),
                    metallic = ReadMaterialFloat(material, "_Metallic", 0f),
                    smoothness = ReadMaterialSmoothness(material),
                    emission = ReadMaterialColor(material, "_EmissionColor")
                };
            }
        }

        private static float ReadMaterialSmoothness(Material material)
        {
            return material != null && material.HasProperty("_Smoothness")
                ? material.GetFloat("_Smoothness")
                : ReadMaterialFloat(material, "_Glossiness", 0f);
        }

        private void SendTransformFor(TrackedObjectState state)
        {
            Transform transform = state.identity.transform;
            LiveLinkCompatibilityManager.TrySendTransform(new LiveLinkCommand
            {
                type = "TRANSFORM",
                objectId = state.identity.ObjectId,
                position = transform.localPosition,
                rotation = transform.localEulerAngles,
                scale = transform.localScale
            }, out _);
        }

        private void SendLightFor(TrackedObjectState state)
        {
            if (state.light == null)
                return;

            LiveLinkCompatibilityManager.TrySendLight(new LiveLinkCommand
            {
                type = "LIGHT_UPDATE",
                objectId = state.identity.ObjectId,
                lightComponentIndex = state.lightIndex,
                light = new LiveLinkLightState
                {
                    enabled = state.light.enabled,
                    intensity = state.light.intensity,
                    color = state.light.color,
                    range = state.light.range,
                    spotAngle = state.light.spotAngle
                }
            }, out _);
        }

        private void SendMaterialFor(TrackedObjectState state)
        {
            if (state.renderer == null)
                return;

            Material[] materials = state.renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                    continue;

                SendMaterialPropertyFor(state.identity, state.rendererIndex, i, material,
                    "BaseColor", new LiveLinkMaterialState
                    {
                        color = ReadMaterialColor(material)
                    });
                SendMaterialPropertyFor(state.identity, state.rendererIndex, i, material,
                    "Metallic", new LiveLinkMaterialState
                    {
                        value = ReadMaterialFloat(material, "_Metallic", 0f)
                    });
                SendMaterialPropertyFor(state.identity, state.rendererIndex, i, material,
                    "Smoothness", new LiveLinkMaterialState
                    {
                        value = ReadMaterialSmoothness(material)
                    });

                if (material.HasProperty("_EmissionColor"))
                {
                    SendMaterialPropertyFor(state.identity, state.rendererIndex, i, material,
                        "EmissionColor", new LiveLinkMaterialState
                        {
                            color = material.GetColor("_EmissionColor")
                        });
                }
            }
        }

        private static void SendMaterialPropertyFor(
            LiveLinkIdentity identity,
            int rendererIndex,
            int materialSlot,
            Material material,
            string property,
            LiveLinkMaterialState materialState)
        {
            LiveLinkCompatibilityManager.TrySendMaterial(new LiveLinkCommand
            {
                type = "MATERIAL_UPDATE",
                objectId = identity.ObjectId,
                rendererComponentIndex = rendererIndex,
                materialSlot = materialSlot,
                materialProperty = property,
                material = materialState
            }, out _);
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

        private void SendMaterial()
        {
            if (selectedTarget == null || selectedRenderer == null ||
                selectedMaterial == null)
                return;

            SendMaterialProperty("BaseColor", new LiveLinkMaterialState
            {
                color = ReadMaterialColor(selectedMaterial)
            });
            SendMaterialProperty("Metallic", new LiveLinkMaterialState
            {
                value = ReadMaterialFloat(selectedMaterial, "_Metallic", 0f)
            });
            SendMaterialProperty("Smoothness", new LiveLinkMaterialState
            {
                value = selectedMaterial.HasProperty("_Smoothness")
                    ? selectedMaterial.GetFloat("_Smoothness")
                    : ReadMaterialFloat(selectedMaterial, "_Glossiness", 0f)
            });

            if (selectedMaterial.HasProperty("_EmissionColor"))
                SendMaterialProperty("EmissionColor", new LiveLinkMaterialState
                {
                    color = selectedMaterial.GetColor("_EmissionColor")
                });

            lastResponse = "Material sent to compatible Player.";
            Repaint();
        }

        private void SendMaterialProperty(
            string property,
            LiveLinkMaterialState state)
        {
            var command = new LiveLinkCommand
            {
                type = "MATERIAL_UPDATE",
                objectId = selectedTarget.ObjectId,
                rendererComponentIndex = selectedRendererIndex,
                materialSlot = selectedMaterialSlot,
                materialProperty = property,
                material = state
            };

            if (!LiveLinkCompatibilityManager.TrySendMaterial(
                    command, out string diagnostic))
            {
                lastResponse = "Material sync blocked: " +
                    (string.IsNullOrEmpty(diagnostic)
                        ? "Player authorization has not been verified."
                        : diagnostic);
            }
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

            TrackingScope previousTrackingScope = trackingScope;
            trackingScope = (TrackingScope)EditorGUILayout.EnumPopup(
                "Tracking Scope", trackingScope);
            if (previousTrackingScope != trackingScope)
            {
                ClearPendingAutomaticSync();
                trackedObjects.Clear();
                Repaint();
            }
            if (trackingScope == TrackingScope.AllValidLiveLinkObjects)
            {
                EditorGUILayout.LabelField(
                    "Tracked LiveLink Objects",
                    trackedObjects.Count.ToString());
            }

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

                if (selectedMaterial != null &&
                    GUILayout.Button("Sync Material Now"))
                    SendMaterial();

                if (selectedRenderer != null)
                {
                    Material[] materials = selectedRenderer.sharedMaterials;
                    if (materials.Length > 1)
                    {
                        int previousSlot = selectedMaterialSlot;
                        selectedMaterialSlot = EditorGUILayout.IntSlider(
                            "Material Slot",
                            selectedMaterialSlot,
                            0,
                            materials.Length - 1);
                        if (previousSlot != selectedMaterialSlot)
                        {
                            ClearPendingAutomaticSync();
                            CaptureMaterial();
                        }
                    }
                }
            }
        }
    }
}
