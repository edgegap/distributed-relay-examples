using System;
using System.Collections.Generic;
using FishNet.Managing;
using kcp2k;

namespace FishNet.Transporting.KCP
{
    public class ServerSocket : CommonSocket
    {
        protected KcpServer Server;

        private int _nextClientId = 1;
        private readonly Dictionary<int, int> _fishNetIdToTransportIdMap = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _transportIdToFishNetIdMap = new Dictionary<int, int>();

        public ServerSocket(Transport transport) : base(transport)
        {
        }

        public bool StartConnection(KcpConfig config, string addressIPv4, string addressIPv6, ushort port)
        {
            if (GetConnectionState() != LocalConnectionState.Stopped)
            {
                return false;
            }
            
            SetConnectionState(LocalConnectionState.Starting);
            
            Server = new KcpServer(
                OnConnectedCallback,
                OnDataCallback,
                OnDisconnectedCallback,
                OnErrorCallback,
                config);
            
            if (!Server.TryStart(addressIPv4, addressIPv6, port))
            {
                SetConnectionState(LocalConnectionState.Stopping);
                SetConnectionState(LocalConnectionState.Stopped);
                return false;
            }
            
            SetConnectionState(LocalConnectionState.Started);
            return true;
        }

        public bool StopConnection()
        {
            if (GetConnectionState().IsStoppingOrStopped())
            {
                return false;
            }
            
            foreach (KcpServerConnection connection in Server.connections.Values)
            {
                connection.Disconnect();
            }
            
            SetConnectionState(LocalConnectionState.Stopping);
            _nextClientId = 1;
            Server.Stop();
            _fishNetIdToTransportIdMap.Clear();
            _transportIdToFishNetIdMap.Clear();
            Server = null;
            SetConnectionState(LocalConnectionState.Stopped);
            return true;
        }

        public string GetConnectionAddress(int connectionId)
        {
            if (GetConnectionState() != LocalConnectionState.Started)
            {
                NetworkManager nm = Transport == null ? null : Transport.NetworkManager;
                const string MESSAGE = "Server socket is not started.";
                nm.LogWarning(MESSAGE);
                return string.Empty;
            }
            
            if (GetConnectionState(connectionId) != RemoteConnectionState.Started)
            {
                NetworkManager nm = Transport == null ? null : Transport.NetworkManager;
                const string MESSAGE = "Connection is not started.";
                nm.LogWarning(MESSAGE);
                return string.Empty;
            }
            
            int transportId = FishNetIdToTransportId(connectionId);
            
            if (Server.connections.TryGetValue(transportId, out KcpServerConnection connection))
            {
                return connection.remoteEndPoint.ToString();
            }
            return string.Empty;
        }

        public RemoteConnectionState GetConnectionState(int connectionId)
        {
            return IsConnected(connectionId) ? RemoteConnectionState.Started : RemoteConnectionState.Stopped;
        }

        public void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            if (GetConnectionState(connectionId) != RemoteConnectionState.Started)
            {
                return;
            }
            int transportId = FishNetIdToTransportId(connectionId);
            Server.Send(transportId, segment, ParseChannel((Channel)channelId));
        }

        public void IterateIncoming()
        {
            if (GetConnectionState().IsStartingOrStarted())
            {
                Server.TickIncoming();
            }
        }

        public void IterateOutgoing()
        {
            if (GetConnectionState().IsStartingOrStarted())
            {
                Server.TickOutgoing();
            }
        }

        public bool StopConnection(int connectionId)
        {
            if (GetConnectionState(connectionId) != RemoteConnectionState.Started)
            {
                int fishNetIdToTransportId = FishNetIdToTransportId(connectionId);
                Server.Disconnect(fishNetIdToTransportId);
                return true;
            }
            return false;
        }

        protected override void SetConnectionState(LocalConnectionState state)
        {
            if (GetConnectionState() == state) return;
            
            base.SetConnectionState(state);
            Transport.HandleServerConnectionState(new ServerConnectionStateArgs(state, Transport.Index));
        }

        private int FishNetIdToTransportId(int connectionId) => _fishNetIdToTransportIdMap[connectionId];

        private int TransportIdToFishNetId(int transportId) => _transportIdToFishNetIdMap[transportId];

        private void HandleRemoteConnectionState(RemoteConnectionState state, int transportId)
        {
            int fishNetId;
            switch (state)
            {
                case RemoteConnectionState.Started:
                    fishNetId = _nextClientId++;
                    _fishNetIdToTransportIdMap[fishNetId] = transportId;
                    _transportIdToFishNetIdMap[transportId] = fishNetId;
                    Transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(state, fishNetId, Transport.Index));
                    break;
                case RemoteConnectionState.Stopped:
                    fishNetId = _transportIdToFishNetIdMap[transportId];
                    Transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(state, fishNetId, Transport.Index));
                    _fishNetIdToTransportIdMap.Remove(fishNetId);
                    _transportIdToFishNetIdMap.Remove(transportId);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        protected void OnErrorCallback(int transportId, ErrorCode errorCode, string message)
        {
            Transport.NetworkManager.LogError(message);
        }

        protected void OnConnectedCallback(int transportId)
        {
            HandleRemoteConnectionState(RemoteConnectionState.Started, transportId);
        }

        protected void OnDisconnectedCallback(int transportId)
        {
            HandleRemoteConnectionState(RemoteConnectionState.Stopped, transportId);
        }

        protected void OnDataCallback(int transportId, ArraySegment<byte> data, KcpChannel kcpChannel)
        {
            Channel fishNetChannel = ParseChannel(kcpChannel);
            int connectionId = TransportIdToFishNetId(transportId);
            var args = new ServerReceivedDataArgs(data, fishNetChannel, connectionId, Transport.Index);
            Transport.HandleServerReceivedDataArgs(args);
        }

        private bool IsConnected(int connectionId)
        {
            if (_fishNetIdToTransportIdMap.TryGetValue(connectionId, out int transportId))
            {
                return Server != null && Server.connections.ContainsKey(transportId);
            }
            return false;
        }
    }
}