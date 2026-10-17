#if LIVELINK_ENABLED && !UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using LiveLink.Diagnostics;

namespace LiveLink
{
    internal sealed class LiveLinkDiscoveryAgent : MonoBehaviour
    {
        private static LiveLinkDiscoveryAgent instance;
        private readonly ConcurrentQueue<string> pendingCommands =
            new ConcurrentQueue<string>();
        private readonly object clientLock = new object();
        private volatile bool stopping;
        private Thread discoveryThread;
        private TcpClient client;
        private StreamWriter writer;
        private string productName;
        private bool transformAuthorizationGranted;
        private bool authenticationGranted;
        private string pendingAuthenticationChallenge;
        private string diagnosticSessionId;

        internal static void Start()
        {
            if (instance != null)
                return;

            GameObject host = new GameObject("[LiveLink Discovery]");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<LiveLinkDiscoveryAgent>();
        }

        private void OnEnable()
        {
            // Unity application properties must be read on the main thread.
            productName = Application.productName;
            discoveryThread = new Thread(DiscoveryLoop)
            {
                IsBackground = true,
                Name = "LiveLink Discovery"
            };
            discoveryThread.Start();
            Debug.Log("[LiveLink] Discovery agent started");
        }

        private void Update()
        {
            while (pendingCommands.TryDequeue(out string json))
                HandleCommand(json);
            foreach (string report in LiveLinkDiagnostics.DrainReports())
                Debug.Log("[LiveLink Diagnostic]" + Environment.NewLine + report);
        }

        private void DiscoveryLoop()
        {
            UdpClient udp = null;
            try
            {
                udp = new UdpClient(LiveLinkDiscoverySettings.DiscoveryPort);
                udp.Client.ReceiveTimeout = 750;

                while (!stopping)
                {
                    try
                    {
                        IPEndPoint endpoint = new IPEndPoint(
                            IPAddress.Any, 0);
                        byte[] data = udp.Receive(ref endpoint);
                        string announcement = Encoding.UTF8.GetString(data);
                        string sessionId = "player-" + Guid.NewGuid().ToString("N");
                        diagnosticSessionId = LiveLinkDiagnostics.BeginSession(
                            LiveLinkDiagnosticRole.Player,
                            LiveLinkDiagnosticTransport.DiscoveryTcp,
                            sessionId,
                            endpoint == null ? "UDP discovery" : endpoint.ToString(),
                            LiveLinkDiagnosticRecordKind.LiveLinkSession);
                        LiveLinkDiagnostics.Pass(diagnosticSessionId,
                            LiveLinkDiagnosticStage.UdpAnnouncementReceived,
                            "Validated Editor discovery announcement.");
                        TryConnect(announcement);
                    }
                    catch (SocketException)
                    {
                        // Timeout: continue discovery while the Player runs.
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("[LiveLink] Discovery error: " +
                            exception.Message);
                    }
                }
            }
            catch (SocketException exception)
            {
                if (!stopping)
                {
                    Debug.LogWarning(
                        "[LiveLink] Discovery UDP unavailable: " +
                        exception.Message);
                }
            }
            finally
            {
                udp?.Close();
            }
        }

        private void TryConnect(string announcement)
        {
            string[] parts = announcement.Split('|');

            if (parts.Length < 3 ||
                parts[0] != LiveLinkDiscoverySettings.DiscoveryPrefix ||
                !int.TryParse(parts[2], out int port) ||
                !IPAddress.TryParse(parts[1], out IPAddress address))
            {
                return;
            }

            lock (clientLock)
            {
                if (client != null && client.Connected)
                    return;

                try
                {
                    client = new TcpClient();
                    client.NoDelay = true;
                    client.Connect(address, port);
                    LiveLinkDiagnostics.Pass(diagnosticSessionId,
                        LiveLinkDiagnosticStage.TcpConnected,
                        address + ":" + port);
                    writer = new StreamWriter(client.GetStream(),
                        new UTF8Encoding(false))
                    {
                        AutoFlush = true
                    };
                    writer.WriteLine(LiveLinkDiscoverySettings.HelloPrefix +
                        "|" + productName);
                    authenticationGranted = false;
                    transformAuthorizationGranted = false;
                    Debug.Log("[LiveLink] Discovery connected to Editor " +
                        address + ":" + port);

                    Thread reader = new Thread(ReadLoop)
                    {
                        IsBackground = true,
                        Name = "LiveLink Discovery Reader"
                    };
                    reader.Start(client);
                }
                catch (Exception exception)
                {
                    CloseClient();
                    Debug.LogWarning("[LiveLink] Discovery connection failed: " +
                        exception.Message);
                }
            }
        }

        private void ReadLoop(object state)
        {
            var tcp = (TcpClient)state;

            try
            {
                using (StreamReader reader = new StreamReader(
                           tcp.GetStream(), new UTF8Encoding(false)))
                {
                    while (!stopping && tcp.Connected)
                    {
                        string line = reader.ReadLine();

                        if (line == null)
                            break;

                        if (!string.IsNullOrWhiteSpace(line))
                            pendingCommands.Enqueue(line);
                    }
                }
            }
            catch (Exception exception)
            {
                if (!stopping)
                    Debug.LogWarning("[LiveLink] Discovery read stopped: " +
                        exception.Message);
            }
            finally
            {
                CloseClient();
            }
        }

        private void HandleCommand(string json)
        {
            try
            {
                LiveLinkCommand command =
                    JsonUtility.FromJson<LiveLinkCommand>(json);

                if (command == null)
                {
                    SendReply("ERROR", "Invalid command");
                    return;
                }

                switch (command.type)
                {
                    case "AUTH_CHALLENGE":
                        Authenticate(command);
                        break;
                    case "AUTH_ACCEPT":
                        AcceptAuthentication(command);
                        break;
                    case "PING":
                        SendReply("OK", "PONG", null, command.requestId);
                        break;
                    case "MANIFEST_REQUEST":
                        if (!authenticationGranted)
                        {
                            SendReply("ERROR", "TCP authentication required.", null, command.requestId);
                            break;
                        }
                        SendManifestResponse(command.requestId);
                        break;
                    case "SYNC_AUTHORIZATION":
                        if (!authenticationGranted)
                        {
                            SendReply("ERROR", "TCP authentication required.", null, command.requestId);
                            break;
                        }
                        AuthorizeTransforms(command);
                        break;
                    case "TRANSFORM":
                        if (!authenticationGranted)
                        {
                            SendReply("ERROR", "TCP authentication required.", null, command.requestId);
                            break;
                        }
                        ApplyTransform(command);
                        break;
                    case "LIGHT_UPDATE":
                        if (!authenticationGranted)
                        {
                            SendReply("ERROR", "TCP authentication required.", null, command.requestId);
                            break;
                        }
                        ApplyLight(command);
                        break;
                    case "MATERIAL_UPDATE":
                        if (!authenticationGranted)
                        {
                            SendReply("ERROR", "TCP authentication required.", null, command.requestId);
                            break;
                        }
                        ApplyMaterial(command);
                        break;
                    default:
                        SendReply("ERROR", "Unknown command");
                        break;
                }
            }
            catch (Exception exception)
            {
                SendReply("ERROR", exception.Message);
            }
        }

        private void Authenticate(LiveLinkCommand command)
        {
            LiveLinkBuildIdentity identity = Resources.Load<LiveLinkBuildIdentity>(
                "LiveLinkBuildIdentity");
            string rejectionReason = null;
            string expectedPairingId = null;
            if (identity == null)
                rejectionReason = "manifest resource missing";
            else if (string.IsNullOrWhiteSpace(identity.PairingSecret))
                rejectionReason = "pairing secret missing from build";
            else
            {
                expectedPairingId = LiveLinkAuthentication.ComputePairingId(
                    identity.PairingSecret);
                if (!string.Equals(expectedPairingId, command.authPairingId, StringComparison.Ordinal))
                    rejectionReason = "pairing ID mismatch";
                else if (string.IsNullOrWhiteSpace(command.authChallenge))
                    rejectionReason = "challenge missing";
            }

            bool valid = rejectionReason == null;

            pendingAuthenticationChallenge = valid ? command.authChallenge : null;
            authenticationGranted = false;
            Debug.Log(valid
                ? "[LiveLink] TCP authentication challenge received."
                : "[LiveLink] TCP authentication challenge rejected: " + rejectionReason);
            SendReply(
                valid ? "OK" : "ERROR",
                valid ? "AUTH_RESPONSE" : "TCP authentication failed.",
                null,
                command.requestId,
                expectedPairingId,
                command.authChallenge,
                valid ? LiveLinkAuthentication.ComputeProof(
                    identity.PairingSecret,
                    expectedPairingId,
                    command.authChallenge) : null);
        }

        private void AcceptAuthentication(LiveLinkCommand command)
        {
            LiveLinkBuildIdentity identity = Resources.Load<LiveLinkBuildIdentity>(
                "LiveLinkBuildIdentity");
            authenticationGranted = identity != null &&
                string.Equals(identity.PairingId, command.authPairingId, StringComparison.Ordinal) &&
                string.Equals(pendingAuthenticationChallenge, command.authChallenge, StringComparison.Ordinal);
            if (!authenticationGranted)
            {
                transformAuthorizationGranted = false;
            }
            else
            {
                Debug.Log("[LiveLink] TCP authentication accepted by Editor.");
                LiveLinkDiagnostics.Pass(diagnosticSessionId,
                    LiveLinkDiagnosticStage.HmacAuthenticated,
                    "Editor accepted the Player proof for this session.");
            }
        }

        private void AuthorizeTransforms(LiveLinkCommand command)
        {
            LiveLinkBuildIdentity identity =
                Resources.Load<LiveLinkBuildIdentity>(
                    "LiveLinkBuildIdentity");

            bool authorized =
                identity != null &&
                !string.IsNullOrWhiteSpace(identity.ManifestFingerprint) &&
                string.Equals(
                    identity.ManifestFingerprint,
                    command.manifestFingerprint,
                    StringComparison.OrdinalIgnoreCase);

            transformAuthorizationGranted = authorized;
            if (authorized)
                LiveLinkDiagnostics.Pass(diagnosticSessionId,
                    LiveLinkDiagnosticStage.ManifestCompatible,
                    "Editor fingerprint matches the baked Player manifest.");
            else
                LiveLinkDiagnostics.Fail(diagnosticSessionId,
                    LiveLinkDiagnosticStage.ManifestCompatible,
                    "Editor and Player identity manifests differ. Rebuild required.");
            SendReply(
                authorized ? "OK" : "ERROR",
                authorized
                    ? "SYNC_AUTHORIZED"
                    : "Transform authorization fingerprint mismatch.",
                null,
                command.requestId);
        }

        private void SendManifestResponse(string requestId)
        {
            LiveLinkBuildIdentity identity =
                Resources.Load<LiveLinkBuildIdentity>(
                    "LiveLinkBuildIdentity");

            if (identity == null)
            {
                SendReply("ERROR", "Development manifest resource is missing.",
                    null, requestId);
                return;
            }

            if (identity.ProtocolVersion <= 0 ||
                string.IsNullOrWhiteSpace(identity.ManifestFingerprint))
            {
                SendReply("ERROR", "Development manifest resource is invalid.",
                    null, requestId);
                return;
            }

            SendReply("OK", "MANIFEST_RESPONSE",
                new LiveLinkHandshakeResponse
                {
                    protocolVersion = identity.ProtocolVersion,
                    fingerprint = identity.ManifestFingerprint,
                    buildName = "LiveLink"
                },
                requestId);
        }

        private void ApplyTransform(LiveLinkCommand command)
        {
            if (!transformAuthorizationGranted)
            {
                SendReply(
                    "ERROR",
                    "Transform synchronization is not authorized for this build.",
                    null,
                    command.requestId);
                return;
            }

            if (!LiveLinkObjectRegistry.TryGet(
                    command.objectId,
                    out LiveLinkIdentity identity,
                    out string error))
            {
                SendReply("ERROR", error);
                return;
            }

            Transform target = identity.transform;
            target.localPosition = command.position;
            target.localRotation = Quaternion.Euler(command.rotation);
            target.localScale = command.scale;
            SendReply("OK", "Updated: " + command.objectId,
                null, command.requestId);
            LiveLinkDiagnostics.Pass(diagnosticSessionId,
                LiveLinkDiagnosticStage.TransformCommandAcknowledged,
                "TRANSFORM applied by the Player.");
        }

        private void ApplyLight(LiveLinkCommand command)
        {
            if (!transformAuthorizationGranted)
            {
                SendReply("ERROR",
                    "Light synchronization is not authorized for this build.",
                    null, command.requestId);
                return;
            }

            if (!LiveLinkLightStateValidator.TryValidate(
                    command.light, out string validationError))
            {
                SendReply("ERROR", validationError, null, command.requestId);
                return;
            }

            if (!LiveLinkObjectRegistry.TryGet(
                    command.objectId, out LiveLinkIdentity identity,
                    out string error))
            {
                SendReply("ERROR", error);
                return;
            }

            Light[] lights = identity.GetComponents<Light>();
            if (command.lightComponentIndex < 0 ||
                command.lightComponentIndex >= lights.Length)
            {
                SendReply("ERROR", "Light component index is invalid.",
                    null, command.requestId);
                return;
            }

            Light target = lights[command.lightComponentIndex];
            target.enabled = command.light.enabled;
            target.intensity = command.light.intensity;
            target.color = command.light.color;

            if (target.type == LightType.Point ||
                target.type == LightType.Spot)
            {
                target.range = command.light.range;
            }

            if (target.type == LightType.Spot)
                target.spotAngle = command.light.spotAngle;

            SendReply("OK", "Updated Light: " + command.objectId,
                null, command.requestId);
            LiveLinkDiagnostics.Pass(diagnosticSessionId,
                LiveLinkDiagnosticStage.LightCommandAcknowledged,
                "LIGHT_UPDATE applied by the Player.");
        }

        private void ApplyMaterial(LiveLinkCommand command)
        {
            if (!transformAuthorizationGranted)
            {
                SendReply("ERROR",
                    "Material synchronization is not authorized for this build.",
                    null, command.requestId);
                return;
            }

            if (!LiveLinkMaterialStateValidator.TryValidate(
                    command.materialProperty,
                    command.material,
                    out string validationError))
            {
                SendReply("ERROR", validationError, null, command.requestId);
                return;
            }

            if (!LiveLinkObjectRegistry.TryGet(
                    command.objectId, out LiveLinkIdentity identity,
                    out string error))
            {
                SendReply("ERROR", error, null, command.requestId);
                return;
            }

            Renderer[] renderers = identity.GetComponents<Renderer>();
            if (command.rendererComponentIndex < 0 ||
                command.rendererComponentIndex >= renderers.Length)
            {
                SendReply("ERROR", "Renderer component index is invalid.",
                    null, command.requestId);
                return;
            }

            Renderer target = renderers[command.rendererComponentIndex];
            Material[] materials = target.sharedMaterials;
            if (command.materialSlot < 0 ||
                command.materialSlot >= materials.Length ||
                materials[command.materialSlot] == null)
            {
                SendReply("ERROR", "Material slot is invalid or empty.",
                    null, command.requestId);
                return;
            }

            Material source = materials[command.materialSlot];
            int propertyId;
            if (command.materialProperty == "BaseColor")
                propertyId = source.HasProperty("_BaseColor")
                    ? Shader.PropertyToID("_BaseColor")
                    : source.HasProperty("_Color")
                        ? Shader.PropertyToID("_Color") : -1;
            else if (command.materialProperty == "Metallic")
                propertyId = source.HasProperty("_Metallic")
                    ? Shader.PropertyToID("_Metallic") : -1;
            else if (command.materialProperty == "Smoothness")
                propertyId = source.HasProperty("_Smoothness")
                    ? Shader.PropertyToID("_Smoothness")
                    : source.HasProperty("_Glossiness")
                        ? Shader.PropertyToID("_Glossiness") : -1;
            else
                propertyId = source.HasProperty("_EmissionColor")
                    ? Shader.PropertyToID("_EmissionColor") : -1;

            if (propertyId < 0)
            {
                SendReply("ERROR", "The selected shader does not support " +
                    command.materialProperty + ".", null, command.requestId);
                return;
            }

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            target.GetPropertyBlock(block, command.materialSlot);
            if (command.materialProperty == "BaseColor" ||
                command.materialProperty == "EmissionColor")
                block.SetColor(propertyId, command.material.color);
            else
                block.SetFloat(propertyId, command.material.value);
            target.SetPropertyBlock(block, command.materialSlot);

            SendReply("OK", "Updated Material: " + command.objectId,
                null, command.requestId);
            LiveLinkDiagnostics.Pass(diagnosticSessionId,
                LiveLinkDiagnosticStage.MaterialCommandAcknowledged,
                "MATERIAL_UPDATE applied by the Player.");
        }

        private void SendReply(
            string status,
            string message,
            LiveLinkHandshakeResponse handshake = null,
            string requestId = null,
            string authPairingId = null,
            string authChallenge = null,
            string authProof = null)
        {
            LiveLinkResponse response = new LiveLinkResponse
            {
                status = status,
                message = message,
                requestId = requestId,
                handshake = handshake,
                authPairingId = authPairingId,
                authChallenge = authChallenge,
                authProof = authProof
            };

            string json = JsonUtility.ToJson(response);

            lock (clientLock)
            {
                if (writer == null || client == null || !client.Connected)
                    return;

                try
                {
                    writer.WriteLine(json);
                }
                catch
                {
                    CloseClient();
                }
            }
        }

        private void CloseClient()
        {
            lock (clientLock)
            {
                try { writer?.Dispose(); } catch { }
                try { client?.Close(); } catch { }
                writer = null;
                client = null;
                authenticationGranted = false;
                transformAuthorizationGranted = false;
                pendingAuthenticationChallenge = null;
            }
        }

        private void OnDisable()
        {
            stopping = true;
            CloseClient();
        }
    }
}
#endif
