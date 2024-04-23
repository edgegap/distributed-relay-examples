using kcp2k.Edgegap;
using kcp2k;

namespace FishNet.Transporting.KCP.Edgegap
{
    public class EdgegapServerSocket : ServerSocket
    {
        private new EdgegapKcpServer Server
        {
            get => (EdgegapKcpServer)base.Server;
            set => base.Server = value;
        }

        public EdgegapServerSocket(Transport transport) : base(transport)
        {
        }

        public bool StartConnection(KcpConfig config, string relayAddress, ushort relayPort, uint userId, uint sessionId)
        {
            if (GetConnectionState() != LocalConnectionState.Stopped)
            {
                return false;
            }
            
            SetConnectionState(LocalConnectionState.Starting);
            
            Server = new EdgegapKcpServer(
                OnConnectedCallback,
                OnDataCallback,
                OnDisconnectedCallback,
                OnErrorCallback,
                config);
            
            if (!Server.TryConnect(relayAddress, relayPort, userId, sessionId))
            {
                SetConnectionState(LocalConnectionState.Stopping);
                SetConnectionState(LocalConnectionState.Stopped);
                return false;
            }
            
            SetConnectionState(LocalConnectionState.Started);
            return true;
        }
    }
}