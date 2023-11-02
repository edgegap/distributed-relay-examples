using FishNet.Connection;
using FishNet.Managing;
using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using FishNet.Transporting.Tugboat;
using FishNet.Transporting.Tugboat.Server;
using System.Net;

namespace FishNet.Transporting.Edgegap.Server
{
    public class EdgegapServerSocket : EdgegapCommonSocket
    {

        #region Public.
        public RelayConnectionState relayConnectionState = RelayConnectionState.Disconnected;

        /// <summary>
        /// Gets the current ConnectionState of a remote client on the server.
        /// </summary>
        /// <param name="connectionId">ConnectionId to get ConnectionState for.</param>
        internal RemoteConnectionState GetConnectionState(int connectionId)
        {
            NetPeer peer = GetNetPeer(connectionId, false);
            if (peer == null || peer.ConnectionState != ConnectionState.Connected)
                return RemoteConnectionState.Stopped;
            else
                return RemoteConnectionState.Started;
        }

        #endregion

        #region Private.
        #region Configuration.
        // authentication
        private uint _userAuthorizationToken;
        private uint _sessionAuthorizationToken;
        /// <summary>
        /// Maximum number of allowed clients.
        /// </summary>
        private int _maximumClients;
        /// <summary>
        /// MTU size per packet.
        /// </summary>
        private int _mtu;
        #endregion
        #region Queues.
        /// <summary>
        /// Changes to the sockets local connection state.
        /// </summary>
        private Queue<LocalConnectionState> _localConnectionStates = new Queue<LocalConnectionState>();
        /// <summary>
        /// Inbound messages which need to be handled.
        /// </summary>
        private Queue<Packet> _incoming = new Queue<Packet>();
        /// <summary>
        /// Outbound messages which need to be handled.
        /// </summary>
        private Queue<Packet> _outgoing = new Queue<Packet>();
        /// <summary>
        /// ConnectionEvents which need to be handled.
        /// </summary>
        private Queue<RemoteConnectionEvent> _remoteConnectionEvents = new Queue<RemoteConnectionEvent>();
        #endregion
        /// <summary>
        /// Key required to connect.
        /// </summary>
        private string _key = string.Empty;
        /// <summary>
        /// How long in seconds until client times from server.
        /// </summary>
        private int _timeout;
        /// <summary>
        /// Server socket manager.
        /// </summary>
        private NetManager _server;
        /// <summary>
        /// PacketLayer to use with LiteNetLib.
        /// </summary>
        private EdgegapServerLayer _packetLayer;
        /// <summary>
        /// Locks the NetManager to stop it.
        /// </summary>
        private readonly object _stopLock = new object();
        #endregion

        ~EdgegapServerSocket()
        {
            StopConnection();
        }

        /// <summary>
        /// Initializes this for use.
        /// </summary>
        /// <param name="t"></param>
        internal void Initialize(Transport t, int unreliableMTU)
        {
            base.Transport = t;
            _mtu = unreliableMTU;
        }

        /// <summary>
        /// Updates the Timeout value as seconds.
        /// </summary>
        internal void UpdateTimeout(int timeout)
        {
            _timeout = timeout;
            base.UpdateTimeout(_server, timeout);
        }


        /// <summary>
        /// Threaded operation to process server actions.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThreadedSocket()
        {
            EventBasedNetListener listener = new EventBasedNetListener();
            listener.ConnectionRequestEvent += Listener_ConnectionRequestEvent;
            listener.PeerConnectedEvent += Listener_PeerConnectedEvent;
            listener.NetworkReceiveEvent += Listener_NetworkReceiveEvent;
            listener.PeerDisconnectedEvent += Listener_PeerDisconnectedEvent;

            _server = new NetManager(listener, _packetLayer);
            _server.MtuOverride = (_mtu + NetConstants.FragmentedHeaderTotalSize);

            UpdateTimeout(_timeout);

            _localConnectionStates.Enqueue(LocalConnectionState.Starting);
            _server.Start();

            relayConnectionState = RelayConnectionState.Checking;
        }

        /// <summary>
        /// Stops the socket on a new thread.
        /// </summary>
        private void StopSocketOnThread()
        {
            if (_server == null)
                return;

            Task t = Task.Run(() =>
            {
                lock (_stopLock)
                {
                    _server?.Stop();
                    _server = null;
                }

                //If not stopped yet also enqueue stop.
                if (base.GetConnectionState() != LocalConnectionState.Stopped)
                    _localConnectionStates.Enqueue(LocalConnectionState.Stopped);
            });
        }

        /// <summary>
        /// Gets the address of a remote connection Id.
        /// </summary>
        /// <param name="connectionId"></param>
        /// <returns>Returns string.empty if Id is not found.</returns>
        internal string GetConnectionAddress(int connectionId)
        {
            if (GetConnectionState() != LocalConnectionState.Started)
            {
                string msg = "Server socket is not started.";
                if (Transport == null)
                    NetworkManager.StaticLogWarning(msg);
                else
                    Transport.NetworkManager.LogWarning(msg);
                return string.Empty;
            }

            NetPeer peer = GetNetPeer(connectionId, false);
            if (peer == null)
            { 
                Transport.NetworkManager.LogWarning($"Connection Id {connectionId} returned a null NetPeer.");
                return string.Empty;
            }

            return peer.EndPoint.Address.ToString();
        }

        /// <summary>
        /// Returns a NetPeer for connectionId.
        /// </summary>
        /// <param name="connectionId"></param>
        /// <returns></returns>
        private NetPeer GetNetPeer(int connectionId, bool connectedOnly)
        {
            if (_server != null)
            {
                NetPeer peer = _server.GetPeerById(connectionId);
                if (connectedOnly && peer != null && peer.ConnectionState != ConnectionState.Connected)
                    peer = null;

                return peer;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Starts the server.
        /// </summary>
        internal bool StartConnection(string relayAddress, ushort relayPort, uint userAuthorizationToken, uint sessionAuthorizationToken, int maximumClients, float pingInterval = EdgegapProtocol.PingInterval)
        {
            base.Initialize(relayAddress, relayPort, pingInterval);

            if (base.GetConnectionState() != LocalConnectionState.Stopped)
                return false;


            SetConnectionState(LocalConnectionState.Starting, true);

            //Assign properties.
            _userAuthorizationToken = userAuthorizationToken;
            _sessionAuthorizationToken = sessionAuthorizationToken;
            _maximumClients = maximumClients;

            _relay = NetUtils.MakeEndPoint(relayAddress, relayPort);
            _packetLayer = new EdgegapServerLayer(_relay, userAuthorizationToken, sessionAuthorizationToken);
            _packetLayer.OnStateChange += Listener_OnRelayStateChange;

            ResetQueues();

            Task t = Task.Run(() => ThreadedSocket());

            return true;
        }
        private void Listener_OnRelayStateChange(RelayConnectionState prev, RelayConnectionState current)
        {
            relayConnectionState = current;

            if (prev == current) return;

            switch (current)
            {
                case RelayConnectionState.Valid:
                    Debug.Log("Server: Relay state is now Valid");
                    _localConnectionStates.Enqueue(LocalConnectionState.Started);
                    break;
                case RelayConnectionState.Invalid:
                case RelayConnectionState.Error:
                case RelayConnectionState.SessionTimeout:
                case RelayConnectionState.Disconnected:
                    Debug.Log("Server: Bad relay state. Stopping...");
                    StopConnection();
                    break;
            }
        }

        /// <summary>
        /// Stops the local socket.
        /// </summary>
        internal bool StopConnection()
        {
            if (_server == null || base.GetConnectionState() == LocalConnectionState.Stopped || base.GetConnectionState() == LocalConnectionState.Stopping)
                return false;

            _localConnectionStates.Enqueue(LocalConnectionState.Stopping);
            StopSocketOnThread();
            return true;
        }

        /// <summary>
        /// Stops a remote client disconnecting the client from the server.
        /// </summary>
        /// <param name="connectionId">ConnectionId of the client to disconnect.</param>
        internal bool StopConnection(int connectionId)
        {
            //Server isn't running.
            if (_server == null || base.GetConnectionState() != LocalConnectionState.Started)
                return false;

            NetPeer peer = GetNetPeer(connectionId, false);
            if (peer == null)
                return false;

            try
            {
                peer.Disconnect();
            }
            catch
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Resets queues.
        /// </summary>
        private void ResetQueues()
        {
            _localConnectionStates.Clear();
            base.ClearPacketQueue(ref _incoming);
            base.ClearPacketQueue(ref _outgoing);
            _remoteConnectionEvents.Clear();
        }


        /// <summary>
        /// Called when a peer disconnects or times out.
        /// </summary>
        private void Listener_PeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            _remoteConnectionEvents.Enqueue(new RemoteConnectionEvent(false, peer.Id));
        }

        /// <summary>
        /// Called when a peer completes connection.
        /// </summary>
        private void Listener_PeerConnectedEvent(NetPeer peer)
        {
            _remoteConnectionEvents.Enqueue(new RemoteConnectionEvent(true, peer.Id));
        }

        /// <summary>
        /// Called when data is received from a peer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Listener_NetworkReceiveEvent(NetPeer fromPeer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            //If over the MTU.
            if (reader.AvailableBytes > _mtu)
            {
                _remoteConnectionEvents.Enqueue(new RemoteConnectionEvent(false, fromPeer.Id));
                fromPeer.Disconnect();
            }
            else
            {
                base.Listener_NetworkReceiveEvent(_incoming, fromPeer, reader, deliveryMethod, _mtu);
            }
        }


        /// <summary>
        /// Called when a remote connection request is made.
        /// </summary>
        private void Listener_ConnectionRequestEvent(ConnectionRequest request)
        {
            if (_server == null)
                return;

            //At maximum peers.
            if (_server.ConnectedPeersCount >= _maximumClients)
            {
                request.Reject();
                return;
            }

            request.AcceptIfKey(_key);
        }

        /// <summary>
        /// Dequeues and processes outgoing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DequeueOutgoing()
        {
            if (base.GetConnectionState() != LocalConnectionState.Started || _server == null)
            {
                //Not started, clear outgoing.
                base.ClearPacketQueue(ref _outgoing);
            }
            else
            {
                int count = _outgoing.Count;
                for (int i = 0; i < count; i++)
                {
                    Packet outgoing = _outgoing.Dequeue();
                    int connectionId = outgoing.ConnectionId;

                    ArraySegment<byte> segment = outgoing.GetArraySegment();
                    DeliveryMethod dm = (outgoing.Channel == (byte)Channel.Reliable) ?
                         DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;

                    //If over the MTU.
                    if (outgoing.Channel == (byte)Channel.Unreliable && segment.Count > _mtu)
                    {
                        base.Transport.NetworkManager.LogWarning($"Server is sending of {segment.Count} length on the unreliable channel, while the MTU is only {_mtu}. The channel has been changed to reliable for this send.");
                        dm = DeliveryMethod.ReliableOrdered;
                    }

                    //Send to all clients.
                    if (connectionId == NetworkConnection.UNSET_CLIENTID_VALUE)
                    {
                        _server.SendToAll(segment.Array, segment.Offset, segment.Count, dm);
                    }
                    //Send to one client.
                    else
                    {
                        NetPeer peer = GetNetPeer(connectionId, true);
                        //If peer is found.
                        if (peer != null)
                            peer.Send(segment.Array, segment.Offset, segment.Count, dm);
                    }

                    outgoing.Dispose();
                }
            }
        }

        /// <summary>
        /// Allows for Outgoing queue to be iterated.
        /// </summary>
        internal void IterateOutgoing()
        {
            base.OnTick(_server);
            DequeueOutgoing();
        }

        /// <summary>
        /// Iterates the Incoming queue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void IterateIncoming()
        {
            _server?.PollEvents();

            /* Run local connection states first so we can begin
             * to read for data at the start of the frame, as that's
             * where incoming is read. */
            while (_localConnectionStates.Count > 0)
                base.SetConnectionState(_localConnectionStates.Dequeue(), true);

            //Not yet started.
            LocalConnectionState localState = base.GetConnectionState();
            if (localState != LocalConnectionState.Started)
            {
                ResetQueues();
                //If stopped try to kill task.
                if (localState == LocalConnectionState.Stopped)
                {
                    StopSocketOnThread();
                    return;
                }
            }

            //Handle connection and disconnection events.
            while (_remoteConnectionEvents.Count > 0)
            {
                RemoteConnectionEvent connectionEvent = _remoteConnectionEvents.Dequeue();
                RemoteConnectionState state = (connectionEvent.Connected) ? RemoteConnectionState.Started : RemoteConnectionState.Stopped;
                base.Transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(state, connectionEvent.ConnectionId, base.Transport.Index));
            }

            //Handle packets.
            while (_incoming.Count > 0)
            {
                Packet incoming = _incoming.Dequeue();
                //Make sure peer is still connected.
                NetPeer peer = GetNetPeer(incoming.ConnectionId, true);
                if (peer != null)
                {
                    ServerReceivedDataArgs dataArgs = new ServerReceivedDataArgs(
                        incoming.GetArraySegment(),
                        (Channel)incoming.Channel,
                        incoming.ConnectionId,
                        base.Transport.Index);

                    base.Transport.HandleServerReceivedDataArgs(dataArgs);
                }

                incoming.Dispose();
            }

        }

        /// <summary>
        /// Sends a packet to a single, or all clients.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            Send(ref _outgoing, channelId, segment, connectionId, _mtu);
        }

        /// <summary>
        /// Returns the maximum number of clients allowed to connect to the server. If the transport does not support this method the value -1 is returned.
        /// </summary>
        /// <returns></returns>
        internal int GetMaximumClients()
        {
            return _maximumClients;
        }

        /// <summary>
        /// Sets the MaximumClients value.
        /// </summary>
        /// <param name="value"></param>
        internal void SetMaximumClients(int value)
        {
            _maximumClients = value;
        }
    }
}
