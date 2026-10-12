#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using LiveLink;
using UnityEditor;
using UnityEngine;

namespace LiveLink.Editor.Connection
{
    [InitializeOnLoad]
    internal static class LiveLinkDiscoveryService
    {
        private sealed class Client
        {
            internal readonly TcpClient Tcp;
            internal readonly StreamWriter Writer;
            internal readonly int PlayerId;

            internal Client(TcpClient tcp, int playerId)
            {
                Tcp = tcp;
                PlayerId = playerId;
                Writer = new StreamWriter(
                    tcp.GetStream(), new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
            }
        }

        private sealed class ReceivedResponse
        {
            internal readonly int PlayerId;
            internal readonly byte[] Data;

            internal ReceivedResponse(int playerId, byte[] data)
            {
                PlayerId = playerId;
                Data = data;
            }
        }

        private static readonly ConcurrentQueue<ReceivedResponse> responses =
            new ConcurrentQueue<ReceivedResponse>();
        private static readonly object gate = new object();
        private static readonly ConcurrentDictionary<int, Client> clients =
            new ConcurrentDictionary<int, Client>();
        private static TcpListener listener;
        private static UdpClient broadcaster;
        private static double nextAnnouncement;
        private static bool started;

        internal static event Action<int> SessionConnected;
        internal static event Action<int> SessionDisconnected;
        internal static event Action<int, byte[]> ResponseReceived;

        static LiveLinkDiscoveryService()
        {
            EditorApplication.update += Update;
            Start();
        }

        internal static bool TrySend(LiveLinkCommand command)
        {
            string json = JsonUtility.ToJson(command);
            bool sent = false;

            foreach (Client client in clients.Values)
            {
                try
                {
                    client.Writer.WriteLine(json);
                    sent = true;
                }
                catch
                {
                    Remove(client.PlayerId);
                }
            }

            return sent;
        }

        internal static bool TrySendTo(
            int playerId,
            LiveLinkCommand command)
        {
            if (!clients.TryGetValue(playerId, out Client client))
                return false;

            try
            {
                client.Writer.WriteLine(JsonUtility.ToJson(command));
                return true;
            }
            catch
            {
                Remove(playerId);
                return false;
            }
        }

        private static void Start()
        {
            if (started)
                return;

            try
            {
                listener = new TcpListener(
                    IPAddress.Any,
                    LiveLinkDiscoverySettings.CommandPort);
                listener.Start();
                broadcaster = new UdpClient
                {
                    EnableBroadcast = true
                };
                started = true;
                Debug.Log("[LiveLink] Discovery service listening on TCP " +
                    LiveLinkDiscoverySettings.CommandPort);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[LiveLink] Discovery service unavailable: " +
                    exception.Message);
            }
        }

        private static void Update()
        {
            if (!started)
                return;

            AcceptClients();

            if (EditorApplication.timeSinceStartup >= nextAnnouncement)
            {
                BroadcastAnnouncement();
                nextAnnouncement = EditorApplication.timeSinceStartup + 1.0;
            }

            while (responses.TryDequeue(out ReceivedResponse response))
            {
                ResponseReceived?.Invoke(
                    response.PlayerId,
                    response.Data);
                LiveLinkWindow.ReceiveDiscoveryResponse(
                    response.PlayerId,
                    response.Data);
            }
        }

        private static void AcceptClients()
        {
            while (listener.Pending())
            {
                TcpClient tcp = listener.AcceptTcpClient();
                tcp.NoDelay = true;
                int playerId = -Math.Abs(tcp.Client.RemoteEndPoint.GetHashCode());
                var client = new Client(tcp, playerId);
                clients[playerId] = client;
                SessionConnected?.Invoke(playerId);

                Thread reader = new Thread(() => ReadClient(client))
                {
                    IsBackground = true,
                    Name = "LiveLink Discovery Client"
                };
                reader.Start();
            }
        }

        private static void ReadClient(Client client)
        {
            try
            {
                using (StreamReader reader = new StreamReader(
                           client.Tcp.GetStream(),
                           new UTF8Encoding(false)))
                {
                    while (client.Tcp.Connected)
                    {
                        string line = reader.ReadLine();

                        if (line == null)
                            break;

                        if (!line.StartsWith(
                                LiveLinkDiscoverySettings.HelloPrefix,
                                StringComparison.Ordinal))
                        {
                            responses.Enqueue(new ReceivedResponse(
                                client.PlayerId,
                                Encoding.UTF8.GetBytes(line)));
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[LiveLink] Discovery client stopped: " +
                    exception.Message);
            }
            finally
            {
                Remove(client.PlayerId);
            }
        }

        private static void BroadcastAnnouncement()
        {
            if (!LiveLinkNetworkDetector.TryGetPreferredAddress(
                    out string address,
                    out _))
            {
                return;
            }

            string message = LiveLinkDiscoverySettings.DiscoveryPrefix +
                "|" + address + "|" +
                LiveLinkDiscoverySettings.CommandPort;
            byte[] data = Encoding.UTF8.GetBytes(message);

            try
            {
                broadcaster.Send(
                    data,
                    data.Length,
                    new IPEndPoint(IPAddress.Broadcast,
                        LiveLinkDiscoverySettings.DiscoveryPort));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[LiveLink] Discovery broadcast failed: " +
                    exception.Message);
            }
        }

        private static void Remove(int playerId)
        {
            if (!clients.TryRemove(playerId, out Client client))
                return;

            try { client.Writer.Dispose(); } catch { }
            try { client.Tcp.Close(); } catch { }
            SessionDisconnected?.Invoke(playerId);
        }
    }
}
#endif
