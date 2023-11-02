using FishNet.Managing;
using FishNet.Managing.Timing;
using FishNet.Managing.Transporting;
using FishNet.Serializing;
using FishNet.Transporting.Edgegap.Client;
using FishNet.Transporting.Tugboat.Client;
using LiteNetLib;
using LiteNetLib.Layers;
using LiteNetLib.Utils;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishNet.Transporting.Edgegap
{
    [DisallowMultipleComponent]
    [AddComponentMenu("FishNet/Transport/EdgegapTransport")]
    public class EdgegapTransport : Transport
    {
        ~EdgegapTransport()
        {
            Shutdown();
        }

        #region Serialized.
        [Header("Channels")]
        /// <summary>
        /// Maximum transmission unit for the unreliable channel.
        /// </summary>
        [Tooltip("Maximum transmission unit for the unreliable channel.")]
        [Range(MINIMUM_UDP_MTU, MAXIMUM_UDP_MTU)]
        [SerializeField]
        private int _unreliableMTU = MAXIMUM_UDP_MTU;

        [Header("Relay")]
        /// <summary>
        /// The address of the relay
        /// </summary>
        [Tooltip("The address of the relay.")]
        [SerializeField]
        private string _relayAddress;
        [Tooltip("The interval between ping messages to the relay.")]
        [SerializeField]
        private float _pingInterval = EdgegapProtocol.PingInterval;

        [Tooltip("The user authorization token.")]
        [SerializeField]
        private uint _userAuthorizationToken;
        [Tooltip("The session authorization token.")]
        [SerializeField]
        private uint _sessionAuthorizationToken;

        [Header("Server")]
        /// <summary>
        /// Port to use.
        /// </summary>
        [Tooltip("Relay server port.")]
        [SerializeField]
        private ushort _relayServerPort;
        [Tooltip("The port to bind on the local server")]
        [SerializeField]
        private ushort _localPort;


        /// <summary>
        /// Maximum number of players which may be connected at once.
        /// </summary>
        [Tooltip("Maximum number of players which may be connected at once.")]
        [Range(1, 9999)]
        [SerializeField]
        private int _maximumClients = 4095;


        [Header("Client")]
        /// <summary>
        /// Address to connect.
        /// </summary>
        [Tooltip("Relay client port")]
        [SerializeField]
        private ushort _relayClientPort;
        [Tooltip("If true, the client will attempt to connect directly to the localhost server, bypassing the relay.")]
        [SerializeField]
        private bool _clientConnectLocal;


        [Header("Misc")]
        /// <summary>
        /// How long in seconds until either the server or client socket must go without data before being timed out. Use 0f to disable timing out.
        /// </summary>
        [Tooltip("How long in seconds until either the server or client socket must go without data before being timed out. Use 0f to disable timing out.")]
        [Range(0, MAX_TIMEOUT_SECONDS)]
        [SerializeField]
        private ushort _timeout = 15;
        #endregion

        #region Private.
        /// <summary>
        /// Server socket and handler.
        /// </summary>
        private Server.EdgegapServerSocket _server = new Server.EdgegapServerSocket();
        /// <summary>
        /// Client socket and handler.
        /// </summary>
        private Client.EdgegapClientSocket _client = new Client.EdgegapClientSocket();
        private ClientSocket _localClient = new ClientSocket();

        #endregion

        #region Const.
        private const ushort MAX_TIMEOUT_SECONDS = 1800;
        /// <summary>
        /// Minimum UDP packet size allowed.
        /// </summary>
        private const int MINIMUM_UDP_MTU = 576;
        /// <summary>
        /// Maximum UDP packet size allowed.
        /// </summary>
        private const int MAXIMUM_UDP_MTU = 1023;
        #endregion

        #region Initialization and unity.
        public override void Initialize(NetworkManager networkManager, int transportIndex)
        {
            base.Initialize(networkManager, transportIndex);
        }

        protected void OnDestroy()
        {
            Shutdown();
        }
        #endregion

        #region ConnectionStates.
        /// <summary>
        /// Gets the address of a remote connection Id.
        /// </summary>
        /// <param name="connectionId"></param>
        /// <returns></returns>
        public override string GetConnectionAddress(int connectionId)
        {
            return _server.GetConnectionAddress(connectionId);
        }
        /// <summary>
        /// Called when a connection state changes for the local client.
        /// </summary>
        public override event Action<ClientConnectionStateArgs> OnClientConnectionState;
        /// <summary>
        /// Called when a connection state changes for the local server.
        /// </summary>
        public override event Action<ServerConnectionStateArgs> OnServerConnectionState;
        /// <summary>
        /// Called when a connection state changes for a remote client.
        /// </summary>
        public override event Action<RemoteConnectionStateArgs> OnRemoteConnectionState;
        /// <summary>
        /// Gets the current local ConnectionState.
        /// </summary>
        /// <param name="server">True if getting ConnectionState for the server.</param>
        public override LocalConnectionState GetConnectionState(bool server)
        {
            if (server)
                return _server.GetConnectionState();
            else
                return _client.GetConnectionState();
        }
        /// <summary>
        /// Gets the current ConnectionState of a remote client on the server.
        /// </summary>
        /// <param name="connectionId">ConnectionId to get ConnectionState for.</param>
        public override RemoteConnectionState GetConnectionState(int connectionId)
        {
            return _server.GetConnectionState(connectionId);
        }
        /// <summary>
        /// Handles a ConnectionStateArgs for the local client.
        /// </summary>
        /// <param name="connectionStateArgs"></param>
        public override void HandleClientConnectionState(ClientConnectionStateArgs connectionStateArgs)
        {
            OnClientConnectionState?.Invoke(connectionStateArgs);
        }
        /// <summary>
        /// Handles a ConnectionStateArgs for the local server.
        /// </summary>
        /// <param name="connectionStateArgs"></param>
        public override void HandleServerConnectionState(ServerConnectionStateArgs connectionStateArgs)
        {
            OnServerConnectionState?.Invoke(connectionStateArgs);
            UpdateTimeout();
        }
        /// <summary>
        /// Handles a ConnectionStateArgs for a remote client.
        /// </summary>
        /// <param name="connectionStateArgs"></param>
        public override void HandleRemoteConnectionState(RemoteConnectionStateArgs connectionStateArgs)
        {
            OnRemoteConnectionState?.Invoke(connectionStateArgs);
        }
        #endregion

        #region Iterating.
        /// <summary>
        /// Processes data received by the socket.
        /// </summary>
        /// <param name="server">True to process data received on the server.</param>
        public override void IterateIncoming(bool server)
        {
            if (server)
                _server.IterateIncoming();
            else
                _client.IterateIncoming();
        }

        /// <summary>
        /// Processes data to be sent by the socket.
        /// </summary>
        /// <param name="server">True to process data received on the server.</param>
        public override void IterateOutgoing(bool server)
        {
            if (server)
                _server.IterateOutgoing();
            else
                _client.IterateOutgoing();
        }
        #endregion

        #region ReceivedData.
        /// <summary>
        /// Called when client receives data.
        /// </summary>
        public override event Action<ClientReceivedDataArgs> OnClientReceivedData;
        /// <summary>
        /// Handles a ClientReceivedDataArgs.
        /// </summary>
        /// <param name="receivedDataArgs"></param>
        public override void HandleClientReceivedDataArgs(ClientReceivedDataArgs receivedDataArgs)
        {
            OnClientReceivedData?.Invoke(receivedDataArgs);
        }
        /// <summary>
        /// Called when server receives data.
        /// </summary>
        public override event Action<ServerReceivedDataArgs> OnServerReceivedData;
        /// <summary>
        /// Handles a ClientReceivedDataArgs.
        /// </summary>
        /// <param name="receivedDataArgs"></param>
        public override void HandleServerReceivedDataArgs(ServerReceivedDataArgs receivedDataArgs)
        {
            OnServerReceivedData?.Invoke(receivedDataArgs);
        }
        #endregion

        #region Sending.
        /// <summary>
        /// Sends to the server or all clients.
        /// </summary>
        /// <param name="channelId">Channel to use.</param>
        /// <param name="segment">Data to send.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            SanitizeChannel(ref channelId);
            _client.SendToServer(channelId, segment);
        }
        /// <summary>
        /// Sends data to a client.
        /// </summary>
        /// <param name="channelId"></param>
        /// <param name="segment"></param>
        /// <param name="connectionId"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            SanitizeChannel(ref channelId);
            _server.SendToClient(channelId, segment, connectionId);
        }
        #endregion

        #region Configuration.
        
        /// <summary>
        /// How long in seconds until either the server or client socket must go without data before being timed out.
        /// </summary>
        /// <param name="asServer">True to get the timeout for the server socket, false for the client socket.</param>
        /// <returns></returns>
        public override float GetTimeout(bool asServer)
        {
            //Server and client uses the same timeout.
            return (float)_timeout;
        }
        /// <summary>
        /// Sets how long in seconds until either the server or client socket must go without data before being timed out.
        /// </summary>
        /// <param name="asServer">True to set the timeout for the server socket, false for the client socket.</param>
        public override void SetTimeout(float value, bool asServer)
        {
            _timeout = (ushort)value;
        }
        /// <summary>
        /// Returns the maximum number of clients allowed to connect to the server. If the transport does not support this method the value -1 is returned.
        /// </summary>
        /// <returns></returns>
        public override int GetMaximumClients()
        {
            return _server.GetMaximumClients();
        }
        /// <summary>
        /// Sets maximum number of clients allowed to connect to the server. If applied at runtime and clients exceed this value existing clients will stay connected but new clients may not connect.
        /// </summary>
        /// <param name="value"></param>
        public override void SetMaximumClients(int value)
        {
            _maximumClients = value;
            _server.SetMaximumClients(value);
        }

        /// <summary>
        /// Sets the adress and port/s of the relay. Doesn't affect the transport at runtime.
        /// These can be retrieved using the Edgegap Relay API
        /// </summary>
        /// <param name="address">The IP address or fqdn of the relay to use</param>
        /// <param name="clientPort">The port used to send client messages to the relay. Use 0 to ignore./param>
        /// <param name="serverPort">The port used to send server messages to the relay. Use 0 to ignore.</param>
        public void SetRelayEndpoint(string address, ushort clientPort = 0, ushort serverPort = 0)
        {
            _relayAddress = address;
            _relayClientPort = clientPort != 0 ? clientPort : _relayClientPort;
            _relayServerPort = serverPort != 0 ? serverPort : _relayClientPort;
        }

        /// <summary>
        /// Sets the interval between ping messages to the relay.
        /// </summary>
        /// <param name="interval">The interval. The lower the interval the faster the authorization but the higher the overhead.</param>
        public void SetPingInterval(int interval)
        {
            _pingInterval = interval;
        }

        /// <summary>
        /// Sets the authorization headers required by the relay.
        /// These can be retrieved using the Edgegap Relay API
        /// </summary>
        /// <param name="userAuth">The user authorization token</param>
        /// <param name="sessionAuth">The session authorization token</param>
        public void SetRelayAuth(uint userAuth, uint sessionAuth)
        {
            _userAuthorizationToken = userAuth;
            _sessionAuthorizationToken = sessionAuth;
        }

        #endregion
        #region Start and stop.
        /// <summary>
        /// Starts the local server or client using configured settings.
        /// </summary>
        /// <param name="server">True to start server.</param>
        public override bool StartConnection(bool server)
        {
            if (server)
                return StartServer();
            else
            {
                return StartClient();
            }
        }

        /// <summary>
        /// Stops the local server or client.
        /// </summary>
        /// <param name="server">True to stop server.</param>
        public override bool StopConnection(bool server)
        {
            if (server)
                return StopServer();
            else
                return StopClient();
        }

        /// <summary>
        /// Stops a remote client from the server, disconnecting the client.
        /// </summary>
        /// <param name="connectionId">ConnectionId of the client to disconnect.</param>
        /// <param name="immediately">True to abrutly stop the client socket. The technique used to accomplish immediate disconnects may vary depending on the transport.
        /// When not using immediate disconnects it's recommended to perform disconnects using the ServerManager rather than accessing the transport directly.
        /// </param>
        public override bool StopConnection(int connectionId, bool immediately)
        {
            return _server.StopConnection(connectionId);
        }

        /// <summary>
        /// Stops both client and server.
        /// </summary>
        public override void Shutdown()
        {
            //Stops client then server connections.
            StopConnection(false);
            StopConnection(true);
        }

        #region Privates.
        /// <summary>
        /// Starts server.
        /// </summary>
        private bool StartServer()
        {
            _server.Initialize(this, _unreliableMTU);
            UpdateTimeout();
            return _server.StartConnection(_relayAddress, _relayServerPort, _userAuthorizationToken, _sessionAuthorizationToken, _localPort, _maximumClients);
        }

        /// <summary>
        /// Stops server.
        /// </summary>
        private bool StopServer()
        {
            if (_server == null)
                return false;
            else
                return _server.StopConnection();
        }

        /// <summary>
        /// Starts the client.
        /// </summary>
        /// <param name="address"></param>
        private bool StartClient()
        {
            _client.Initialize(this, _unreliableMTU);
            UpdateTimeout();

            if (_clientConnectLocal)
                return _client.StartConnection(_localPort);
            
            return _client.StartConnection(_relayAddress, _relayClientPort, _userAuthorizationToken, _sessionAuthorizationToken);
        }

        /// <summary>
        /// Updates clients timeout values.
        /// </summary>
        private void UpdateTimeout()
        {
            //If server is running set timeout to max. This is for host only.
            //int timeout = (GetConnectionState(true) != LocalConnectionState.Stopped) ? MAX_TIMEOUT_SECONDS : _timeout;
            int timeout = (Application.isEditor) ? MAX_TIMEOUT_SECONDS : _timeout;
            _client.UpdateTimeout(timeout);
            _server.UpdateTimeout(timeout);
        }
        /// <summary>
        /// Stops the client.
        /// </summary>
        private bool StopClient()
        {
            if (_client == null)
                return false;
            else
                return _client.StopConnection();
        }
        #endregion
        #endregion

        #region Channels.
        /// <summary>
        /// If channelId is invalid then channelId becomes forced to reliable.
        /// </summary>
        /// <param name="channelId"></param>
        private void SanitizeChannel(ref byte channelId)
        {
            if (channelId < 0 || channelId >= TransportManager.CHANNEL_COUNT)
            {
                NetworkManager.LogWarning($"Channel of {channelId} is out of range of supported channels. Channel will be defaulted to reliable.");
                channelId = 0;
            }
        }
        /// <summary>
        /// Gets the MTU for a channel. This should take header size into consideration.
        /// For example, if MTU is 1200 and a packet header for this channel is 10 in size, this method should return 1190.
        /// </summary>
        /// <param name="channel"></param>
        /// <returns></returns>
        public override int GetMTU(byte channel)
        {
            return _unreliableMTU;
        }
        #endregion

        #region Editor.
#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_unreliableMTU < 0)
                _unreliableMTU = MINIMUM_UDP_MTU;
            else if (_unreliableMTU > MAXIMUM_UDP_MTU)
                _unreliableMTU = MAXIMUM_UDP_MTU;
        }
#endif
        #endregion
    }
}
