#if UNITY_EDITOR
using System;

namespace LiveLink.Editor.Connection
{
    public enum LiveLinkTransportKind
    {
        PlayerConnection,
        DiscoveryTcp
    }

    public struct LiveLinkSessionKey : IEquatable<LiveLinkSessionKey>
    {
        public readonly LiveLinkTransportKind Transport;
        public readonly string ConnectionId;
        public readonly int Generation;

        public LiveLinkSessionKey(
            LiveLinkTransportKind transport,
            string connectionId,
            int generation)
        {
            Transport = transport;
            ConnectionId = connectionId ?? string.Empty;
            Generation = generation;
        }

        public bool Equals(LiveLinkSessionKey other)
        {
            return Transport == other.Transport &&
                Generation == other.Generation &&
                string.Equals(ConnectionId, other.ConnectionId,
                    StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is LiveLinkSessionKey &&
                Equals((LiveLinkSessionKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Transport * 397 + Generation;
                return hash * 397 + ConnectionId.GetHashCode();
            }
        }

        public override string ToString()
        {
            return Transport + ":" + ConnectionId + ":" + Generation;
        }
    }
}
#endif
