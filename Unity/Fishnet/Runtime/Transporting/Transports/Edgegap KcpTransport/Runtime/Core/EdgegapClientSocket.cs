using kcp2k.Edgegap;
using kcp2k;

namespace FishNet.Transporting.KCP.Edgegap
{
    public class EdgegapClientSocket : ClientSocket
    {
        private readonly EdgegapServerSocket _server;

        private new EdgegapKcpClient Client
        {
            get => (EdgegapKcpClient)base.Client;
            set => base.Client = value;
        }

        public EdgegapClientSocket(Transport transport) : base(transport)
        {
        }

        public bool StartConnection(KcpConfig config, string relayAddress, ushort relayPort, uint userId, uint sessionId)
        {
            if (GetConnectionState() != LocalConnectionState.Stopped)
            {
                return false;
            }

            SetConnectionState(LocalConnectionState.Starting);

            Client = new EdgegapKcpClient(
                OnConnectedCallback,
                OnDataCallback,
                OnDisconnectedCallback,
                OnErrorCallback,
                config);

            if (!Client.TryConnect(relayAddress, relayPort, userId, sessionId))
            {
                SetConnectionState(LocalConnectionState.Stopping);
                SetConnectionState(LocalConnectionState.Stopped);
                return false;
            }

            return true;
        }
    }
}