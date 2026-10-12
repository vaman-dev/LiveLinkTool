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
        }

        private void DiscoveryLoop()
        {
            using (var udp = new UdpClient(LiveLinkDiscoverySettings.DiscoveryPort))
            {
                udp.Client.ReceiveTimeout = 750;

                while (!stopping)
                {
                    try
                    {
                        IPEndPoint endpoint = new IPEndPoint(
                            IPAddress.Any, 0);
                        byte[] data = udp.Receive(ref endpoint);
                        string announcement = Encoding.UTF8.GetString(data);
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
                    writer = new StreamWriter(client.GetStream(),
                        new UTF8Encoding(false))
                    {
                        AutoFlush = true
                    };
                    writer.WriteLine(LiveLinkDiscoverySettings.HelloPrefix +
                        "|" + productName);
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
                    case "PING":
                        SendReply("OK", "PONG");
                        break;
                    case "MANIFEST_REQUEST":
                        SendManifestResponse();
                        break;
                    case "TRANSFORM":
                        ApplyTransform(command);
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

        private void SendManifestResponse()
        {
            LiveLinkBuildIdentity identity =
                Resources.Load<LiveLinkBuildIdentity>(
                    "LiveLinkBuildIdentity");

            if (identity == null)
            {
                SendReply("ERROR", "Development manifest resource is missing.");
                return;
            }

            if (identity.ProtocolVersion <= 0 ||
                string.IsNullOrWhiteSpace(identity.ManifestFingerprint))
            {
                SendReply("ERROR", "Development manifest resource is invalid.");
                return;
            }

            SendReply("OK", "MANIFEST_RESPONSE",
                new LiveLinkHandshakeResponse
                {
                    protocolVersion = identity.ProtocolVersion,
                    fingerprint = identity.ManifestFingerprint,
                    buildName = "LiveLink"
                });
        }

        private void ApplyTransform(LiveLinkCommand command)
        {
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
            SendReply("OK", "Updated: " + command.objectId);
        }

        private void SendReply(
            string status,
            string message,
            LiveLinkHandshakeResponse handshake = null)
        {
            LiveLinkResponse response = new LiveLinkResponse
            {
                status = status,
                message = message,
                handshake = handshake
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
