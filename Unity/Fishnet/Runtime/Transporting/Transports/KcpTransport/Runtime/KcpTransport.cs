using System;
using FishNet.Managing;
using kcp2k;
using UnityEngine;

namespace FishNet.Transporting.KCP
{
    [DisallowMultipleComponent]
    [AddComponentMenu("FishNet/Transport/KcpTransport")]
    public class KcpTransport : Transport
    {
        private const ushort MAX_TIMEOUT_SECONDS = 1800;

        /// <summary>
        /// KCP internal update interval. 100ms is KCP default, but a lower interval is recommended to minimize latency and to scale to more networked entities.
        /// </summary>
        protected const uint INTERVAL_MS = 10;

        #region Configs
        /* Settings. */
        [Tooltip("NoDelay is recommended to reduce latency. This also scales better without buffers getting full.")]
        [SerializeField] private bool _noDelay = true;
        /// <summary>
        /// NoDelay is recommended to reduce latency. This also scales better without buffers getting full.
        /// </summary>
        public bool NoDelay
        {
            get => _noDelay;
            set => _noDelay = value;
        }

        [Tooltip("Maximum transmission unit.")]
        [Range(576, 1200)]
        [SerializeField] private int _mtu = Kcp.MTU_DEF;

        [Tooltip("Socket receive buffer size. Large buffer helps support more connections. Increase operating system socket buffer size limits if needed.")]
        [SerializeField] private int _receiveBufferSize = 1024 * 1027 * 7;
        /// <summary>
        /// Socket receive buffer size. Large buffer helps support more connections. Increase operating system socket buffer size limits if needed.
        /// </summary>
        public int ReceiveBufferSize
        {
            get => _receiveBufferSize;
            set => _receiveBufferSize = value;
        }

        [Tooltip("Socket send buffer size. Large buffer helps support more connections. Increase operating system socket buffer size limits if needed.")]
        [SerializeField] private int _sendBufferSize = 1024 * 1027 * 7;
        /// <summary>
        /// Socket send buffer size. Large buffer helps support more connections. Increase operating system socket buffer size limits if needed.
        /// </summary>
        public int SendBufferSize
        {
            get => _sendBufferSize;
            set => _sendBufferSize = value;
        }

        /* Server. */
        [Tooltip("IPv4 Address to bind server to.")]
        [SerializeField] private string _bindAddressIPv4;

        [Tooltip("Enable IPv6, Server listens on IPv4 and IPv6 address")]
        [SerializeField] private bool _enableIPv6;
        /// <summary>
        /// Enable IPv6, Server listens on IPv4 and IPv6 address
        /// </summary>
        public bool EnableIPv6
        {
            get => _enableIPv6;
            set => _enableIPv6 = value;
        }

        [Tooltip("IPv6 Address to bind server to.")]
        [SerializeField] private string _bindAddressIPv6;

        [Tooltip("Port of the server.")]
        [SerializeField] private ushort _port = 7777;

        /* Client. */
        [Tooltip("IP address of the server (address to which clients will connect to).")]
        [SerializeField] private string _clientAddress = "localhost";

        /* Advanced. */
        [Tooltip("KCP fastresend parameter. Faster resend for the cost of higher bandwidth. 0 in normal mode, 2 in turbo mode.")]
        [SerializeField] private int _fastResend = 2;
        /// <summary>
        /// KCP fastresend parameter. Faster resend for the cost of higher bandwidth. 0 in normal mode, 2 in turbo mode.
        /// </summary>
        public int FastResend
        {
            get => _fastResend;
            set => _fastResend = value;
        }

        [Tooltip("KCP congestion window. Restricts window size to reduce congestion. Results in only 2-3 MTU messages per Flush even on loopback. Best to keept his disabled.")]
        [SerializeField] private bool _congestionWindow; // KCP 'NoCongestionWindow' is false by default. here we negate it for ease of use.
        /// <summary>
        /// KCP congestion window. Restricts window size to reduce congestion. Results in only 2-3 MTU messages per Flush even on loopback. Best to keept his disabled.
        /// </summary>
        public bool CongestionWindow
        {
            get => _congestionWindow;
            set => _congestionWindow = !value;
        }

        [Tooltip("KCP window size can be modified to support higher loads. This also increases max message size.")]
        [SerializeField] private uint _receiveWindowSize = 4096; //Kcp.WND_RCV; 128 by default. Mirror sends a lot, so we need a lot more.
        /// <summary>
        /// KCP window size can be modified to support higher loads. This also increases max message size.
        /// </summary>
        public uint ReceiveWindowSize
        {
            get => _receiveWindowSize;
            set => _receiveWindowSize = value;
        }

        [Tooltip("KCP window size can be modified to support higher loads.")]
        [SerializeField] private uint _sendWindowSize = 4096; //Kcp.WND_SND; 32 by default. Mirror sends a lot, so we need a lot more.
        /// <summary>
        /// KCP window size can be modified to support higher loads.
        /// </summary>
        public uint SendWindowSize
        {
            get => _sendWindowSize;
            set => _sendWindowSize = value;
        }

        [Tooltip("KCP will try to retransmit lost messages up to MaxRetransmit (aka dead_link) before disconnecting.")]
        [SerializeField] private uint _maxRetransmits = Kcp.DEADLINK * 2; // default prematurely disconnects a lot of people (#3022). use 2x.
        /// <summary>
        /// KCP will try to retransmit lost messages up to MaxRetransmit (aka dead_link) before disconnecting.
        /// </summary>
        public uint MaxRetransmits
        {
            get => _maxRetransmits;
            set => _maxRetransmits = value;
        }

        #endregion

        // server & client
        protected ServerSocket Server;
        protected ClientSocket Client;
        protected ClientHostSocket ClientHost;

        public sealed override void Initialize(NetworkManager networkManager, int transportIndex)
        {
            base.Initialize(networkManager, transportIndex);
        }

        private void Awake()
        {
            InitializeLogger();
            InitializeSockets();
        }

        protected virtual void InitializeSockets()
        {
            Server = new ServerSocket(this);
            Client = new ClientSocket(this);
            ClientHost = new ClientHostSocket(this);
        }

        private void InitializeLogger()
        {
            Log.Info = _ => {};
            Log.Warning = NetworkManager.LogWarning;
            Log.Error = NetworkManager.LogError;
        }

        private KcpConfig CreateConfig(bool asServer)
        {
            // create config from serialized settings
            var timeout = (int)(GetTimeout(asServer) * 1000);
            return new KcpConfig(_enableIPv6, _receiveBufferSize, _sendBufferSize, _mtu, _noDelay, INTERVAL_MS, _fastResend, _congestionWindow, _sendWindowSize, _receiveWindowSize, timeout, _maxRetransmits);
        }

        protected virtual bool StartServer()
        {
            KcpConfig config = CreateConfig(true);
            
            return Server.StartConnection(config, _bindAddressIPv4, _bindAddressIPv6, _port);
        }

        protected virtual bool StartClient()
        {
            return Client.StartConnection(CreateConfig(false), GetClientAddress(), GetPort());
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        #region FishNet Overrides

        public override event Action<ClientConnectionStateArgs> OnClientConnectionState;

        public override event Action<ServerConnectionStateArgs> OnServerConnectionState;

        public override event Action<RemoteConnectionStateArgs> OnRemoteConnectionState;

        public override event Action<ClientReceivedDataArgs> OnClientReceivedData;

        public override event Action<ServerReceivedDataArgs> OnServerReceivedData;

        public override void HandleClientConnectionState(ClientConnectionStateArgs connectionStateArgs)
        {
            OnClientConnectionState?.Invoke(connectionStateArgs);
        }

        public override void HandleServerConnectionState(ServerConnectionStateArgs connectionStateArgs)
        {
            OnServerConnectionState?.Invoke(connectionStateArgs);
            ClientHost.HandleServerStarted(connectionStateArgs.ConnectionState);
        }

        public override void HandleRemoteConnectionState(RemoteConnectionStateArgs connectionStateArgs)
        {
            OnRemoteConnectionState?.Invoke(connectionStateArgs);
        }

        public override void HandleClientReceivedDataArgs(ClientReceivedDataArgs receivedDataArgs)
        {
            OnClientReceivedData?.Invoke(receivedDataArgs);
        }

        public override void HandleServerReceivedDataArgs(ServerReceivedDataArgs receivedDataArgs)
        {
            OnServerReceivedData?.Invoke(receivedDataArgs);
        }

        public override LocalConnectionState GetConnectionState(bool server)
        {
            if (server)
            {
                return Server.GetConnectionState();
            }
            
            return IsClientHost ? ClientHost.GetConnectionState() : Client.GetConnectionState();
        }

        public override RemoteConnectionState GetConnectionState(int connectionId)
        {
            return IsLocalClientHost(connectionId) ? ClientHost.GetRemoteConnectionState() : Server.GetConnectionState(connectionId);
        }

        public override string GetConnectionAddress(int connectionId)
        {
            return IsLocalClientHost(connectionId) ? "localhost" : Server.GetConnectionAddress(connectionId);
        }

        public override void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            ClientHost.SendToServer(channelId, segment);
            Client.SendToServer(channelId, segment);
        }

        public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            if (IsLocalClientHost(connectionId))
            {
                ClientHost.SendToClient(channelId, segment);
            }
            else
            {
                Server.SendToClient(channelId, segment, connectionId);
            }
        }

        public override void IterateIncoming(bool server)
        {
            ClientHost.IterateIncoming(server);
            if (server)
            {
                Server.IterateIncoming();
            }
            else
            {
                Client.IterateIncoming();
            }
        }

        public override void IterateOutgoing(bool server)
        {
            ClientHost.IterateOutgoing(server);
            if (server)
            {
                Server.IterateOutgoing();
            }
            else
            {
                Client.IterateOutgoing();
            }
        }

        private bool IsClientHost => ClientHost.GetConnectionState() != LocalConnectionState.Stopped;

        public sealed override bool StartConnection(bool server)
        {
            if (Server.GetConnectionState().IsStartingOrStarted())
            {
                return ClientHost.StartConnection();
            }
            return server ? StartServer() : StartClient();
        }

        public override bool StopConnection(bool server)
        {
            if (server)
            {
                if (IsClientHost)
                {
                    ClientHost.StopConnection();
                }
                
                return Server.StopConnection();
            }
            
            if (IsClientHost)
            {
                return ClientHost.StopConnection();
            }
            return Client.StopConnection();
        }

        public override bool StopConnection(int connectionId, bool immediately)
        {
            return IsLocalClientHost(connectionId) ? ClientHost.ServerRequestedDisconnect() : Server.StopConnection(connectionId);
        }

        private static bool IsLocalClientHost(int connectionId)
        {
            return connectionId == ClientHostSocket.CLIENT_HOST_ID;
        }

        public override void Shutdown()
        {
            StopConnection(false);
            StopConnection(true);
        }

        public override int GetMTU(byte channel) => _mtu;

        public override float GetTimeout(bool asServer) => MAX_TIMEOUT_SECONDS;

        public override void SetTimeout(float value, bool asServer)
        {
            
        }

        public override ushort GetPort() => _port;

        public override void SetPort(ushort port) => _port = port;

        public override string GetClientAddress() => _clientAddress;

        public override void SetClientAddress(string address) => _clientAddress = address;

        public override string GetServerBindAddress(IPAddressType addressType)
        {
            return addressType switch
            {
                IPAddressType.IPv4 => _bindAddressIPv4,
                IPAddressType.IPv6 => _bindAddressIPv6,
                _ => throw new ArgumentOutOfRangeException(nameof(addressType), addressType, null)
            };
        }

        public override void SetServerBindAddress(string address, IPAddressType addressType)
        {
            switch (addressType)
            {
                case IPAddressType.IPv4:
                    _bindAddressIPv4 = address;
                    break;
                case IPAddressType.IPv6:
                    _bindAddressIPv6 = address;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(addressType), addressType, null);
            }
        }

        public override bool IsLocalTransport(int connectionId) => false;

        #endregion
    }
}