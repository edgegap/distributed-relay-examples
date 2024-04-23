using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace kcp2k.Edgegap
{
    public class EdgegapKcpServer : KcpServer
    {
        // need buffer larger than KcpClient.rawReceiveBuffer to add metadata
        private readonly byte[] _relayReceiveBuffer;
        private readonly byte[] _relaySendBuffer;

        // authentication
        private uint _userAuthenticationToken;
        private uint _sessionAuthenticationToken;
        private ConnectionState _state = ConnectionState.Disconnected;

        // ping
        private float _lastPingInterval;

        private readonly byte[] _pingMessage;

        // custom 'active'. while connected to relay
        private bool _relayActive;

        public EdgegapKcpServer(
            Action<int> onConnected,
            Action<int, ArraySegment<byte>, KcpChannel> onData,
            Action<int> onDisconnected,
            Action<int, ErrorCode, string> onError,
            KcpConfig config) : base(onConnected, onData, onDisconnected, onError, config)
        {
            _relayReceiveBuffer = new byte[config.Mtu + EdgegapProtocol.Overhead];
            _relaySendBuffer = new byte[config.Mtu + EdgegapProtocol.Overhead];
            _pingMessage = new byte[9];
        }

        // custom start function with relay parameters; connects udp client.
        public bool TryConnect(string relayAddress, ushort relayPort, uint userId, uint sessionId)
        {
            if (_relayActive)
            {
                return false;
            }

            // try resolve host name
            if (!Common.ResolveHostname(relayAddress, out IPAddress[] addresses))
            {
                OnError(0, ErrorCode.DnsResolve, $"Failed to resolve host: {relayAddress}");
                return false;
            }

            // reset last state
            _state = ConnectionState.Checking;
            _userAuthenticationToken = userId;
            _sessionAuthenticationToken = sessionId;

            // create socket
            var remoteEndPoint = new IPEndPoint(addresses[0], relayPort);
            socket = new Socket(remoteEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Blocking = false;

            // configure buffer sizes
            Common.ConfigureSocketBuffers(socket, config.RecvBufferSize, config.SendBufferSize);

            // bind to endpoint for Send/Receive instead of SendTo/ReceiveFrom
            try
            {
                socket.Connect(remoteEndPoint);
            }
            catch (SocketException e)
            {
                Log.Warning(e.Message);
                return false;
            }
            _relayActive = true;

            return true;
        }

        public override void Stop()
        {
            _relayActive = false;
        }

        protected override bool RawReceiveFrom(out ArraySegment<byte> segment, out int connectionId)
        {
            segment = default;
            connectionId = 0;
            
            if (socket == null) return false;
            
            try
            {
                // TODO need separate buffer. don't write into result yet. only payload
                if (socket.ReceiveNonBlocking(_relayReceiveBuffer, out ArraySegment<byte> content))
                {
                    // parse message type
                    if (content.Array == null || content.Count == 0)
                    {
                        Debug.LogWarning($"EdgegapServer: message of {content.Count} is too small to parse header.");
                        return false;
                    }
                    byte messageType = content.Array[content.Offset];
                    // handle message type
                    switch (messageType)
                    {
                        case (byte)MessageType.Ping:
                        {
                            // parse state
                            if (content.Count < 2) return false;
                            ConnectionState last = _state;
                            _state = (ConnectionState)content.Array[content.Offset + 1];

                            // log state changes for debugging.
                            if (_state != last) Debug.Log($"EdgegapServer: state updated to: {_state}");

                            // return true indicates Mirror to keep checking
                            // for further messages.
                            return true;
                        }
                        case (byte)MessageType.Data:
                        {
                            // parse connectionId and payload
                            if (content.Count <= 5)
                            {
                                Debug.LogWarning($"EdgegapServer: message of {content.Count} is too small to parse connId.");
                                return false;
                            }

                            connectionId = content.Array[content.Offset + 1 + 0] | 
                                           (content.Array[content.Offset + 1 + 1] << 8) | 
                                           (content.Array[content.Offset + 1 + 2] << 16) | 
                                           (content.Array[content.Offset + 1 + 3] << 24);

                            segment = new ArraySegment<byte>(content.Array, content.Offset + 1 + 4, content.Count - 1 - 4);
                            return true;
                        }
                        // wrong message type. return false, don't throw.
                        default: return false;
                    }
                }
            }
            catch (SocketException e)
            {
                Log.Warning($"EdgegapServer: looks like the other end has closed the connection. This is fine: {e}");
            }
            return false;
        }

        // process incoming messages. should be called before updating the world.
        // virtual because relay may need to inject their own ping or similar.
        readonly HashSet<int> connectionsToRemove = new HashSet<int>();
        public override void TickIncoming()
        {
            // input all received messages into kcp
            while (RawReceiveFrom(out ArraySegment<byte> segment, out int connectionId))
            {
                if (connectionId != 0)
                {
                    ProcessMessage(segment, connectionId);
                }
            }

            // process inputs for all server connections
            // (even if we didn't receive anything. need to tick ping etc.)
            foreach (KcpServerConnection connection in connections.Values)
            {
                connection.TickIncoming();
            }

            // remove disconnected connections
            // (can't do it in connection.OnDisconnected because Tick is called
            //  while iterating connections)
            foreach (int connectionId in connectionsToRemove)
            {
                connections.Remove(connectionId);
            }
            connectionsToRemove.Clear();
        }

        // receive + add + process once.
        // best to call this as long as there is more data to receive.
        protected override KcpServerConnection CreateConnection(int connectionId)
        {
            // generate a random cookie for this connection to avoid UDP spoofing.
            // needs to be random, but without allocations to avoid GC.
            uint cookie = Common.GenerateCookie();

            // create empty connection without peer first.
            // we need it to set up peer callbacks.
            // afterwards we assign the peer.
            // events need to be wrapped with connectionIds
            var connection = new EdgegapKcpServerConnection(
                OnConnectedCallback,
                (message,  channel) => OnData(connectionId, message, channel),
                OnDisconnectedCallback,
                (error, reason) => OnError(connectionId, error, reason),
                (data) => RawSend(connectionId, data),
                config,
                cookie);

            return connection;

            // setup authenticated event that also adds to connections
            void OnConnectedCallback(KcpServerConnection conn)
            {
                // add to connections dict after being authenticated.
                connections.Add(connectionId, conn);
                Log.Info($"[KCP] Server: added connection({connectionId})");

                // setup Data + Disconnected events only AFTER the
                // handshake. we don't want to fire OnServerDisconnected
                // every time we receive invalid random data from the
                // internet.

                // setup data event

                // finally, call mirror OnConnected event
                Log.Info($"[KCP] Server: OnConnected({connectionId})");
                OnConnected(connectionId);
            }

            void OnDisconnectedCallback()
            {
                // flag for removal
                // (can't remove directly because connection is updated
                //  and event is called while iterating all connections)
                connectionsToRemove.Add(connectionId);

                // call mirror event
                Log.Info($"[KCP] Server: OnDisconnected({connectionId})");
                OnDisconnected(connectionId);
            }
        }

        private void ProcessMessage(ArraySegment<byte> segment, int connectionId)
        {
            //Log.Info($"[KCP] server raw recv {msgLength} bytes = {BitConverter.ToString(buffer, 0, msgLength)}");

            // is this a new connection?
            if (!connections.TryGetValue(connectionId, out KcpServerConnection connection))
            {
                // create a new KcpConnection based on last received
                // EndPoint. can be overwritten for where-allocation.
                connection = CreateConnection(connectionId);

                // DO NOT add to connections yet. only if the first message
                // is actually the kcp handshake. otherwise it's either:
                // * random data from the internet
                // * or from a client connection that we just disconnected
                //   but that hasn't realized it yet, still sending data
                //   from last session that we should absolutely ignore.
                //
                //
                // TODO this allocates a new KcpConnection for each new
                // internet connection. not ideal, but C# UDP Receive
                // already allocated anyway.
                //
                // expecting a MAGIC byte[] would work, but sending the raw
                // UDP message without kcp's reliability will have low
                // probability of being received.
                //
                // for now, this is fine.


                // now input the message & process received ones
                // connected event was set up.
                // tick will process the first message and adds the
                // connection if it was the handshake.
                connection.RawInput(segment);
                connection.TickIncoming();

                // again, do not add to connections.
                // if the first message wasn't the kcp handshake then
                // connection will simply be garbage collected.
            }
            // existing connection: simply input the message into kcp
            else
            {
                var edgegapConnection = (EdgegapKcpServerConnection)connection;
                // This is to prevent the warning "ServerConnection: dropped message with invalid cookie" from appearing when connecting a client when RawInput().
                // We simply do not display this warning if the client has not previously sent a valid cookie
                if (!edgegapConnection.HelloReceived)
                {
                    if (segment.Count <= 5) return;
                    // all server->client messages include the server's security cookie.
                    // all client->server messages except for the initial 'hello' include it too.
                    // parse the cookie and make sure it matches (except for initial hello).
                    Utils.Decode32U(segment.Array, segment.Offset + 1, out uint messageCookie);
                    if (messageCookie != 0)
                    {
                        edgegapConnection.HelloReceived = true;
                    }
                    else
                    {
                        return;
                    }
                }
                
                connection.RawInput(segment);
            }
        }

        protected override void RawSend(int connectionId, ArraySegment<byte> data)
        {
            // Create array segment from relaysendbuffer with length of 13 + data.Count
            ArraySegment<byte> message = new ArraySegment<byte>(_relaySendBuffer, 0, 13 + data.Count);
            Utils.Encode32U(message.Array, message.Offset, _userAuthenticationToken);
            Utils.Encode32U(message.Array, message.Offset + 4, _sessionAuthenticationToken);
            message.Array![message.Offset + 8] = (byte)MessageType.Data;
            Utils.Encode32U(message.Array, message.Offset + 9, (uint)connectionId);

            if (data.Array != null) {
                Array.Copy(data.Array, data.Offset, message.Array, message.Offset + 13, data.Count);
            }

            try
            {
                socket.SendNonBlocking(message);
            }
            catch (SocketException e)
            {
                Log.Error($"KcpRleayServer: RawSend failed: {e}");
            }
        }

        private void SendPing()
        {
            Utils.Encode32U(_pingMessage, 0, _userAuthenticationToken);
            Utils.Encode32U(_pingMessage, 4, _sessionAuthenticationToken);
            _pingMessage[8] = (byte)MessageType.Ping;

            try
            {
                socket.SendNonBlocking(new ArraySegment<byte>(_pingMessage));
            }
            catch (SocketException e)
            {
                Debug.LogWarning($"EdgegapServer: failed to ping. perhaps the relay isn't running? {e}");
            }
        }

        public override void TickOutgoing()
        {
            if (_relayActive)
            {
                // ping every interval for keepalive & handshake
                if(Time.deltaTime + _lastPingInterval >= EdgegapProtocol.PingInterval)
                {
                    SendPing();
                    _lastPingInterval = 0.0f;
                }
                else
                {
                    _lastPingInterval += Time.deltaTime;
                }

            }

            // base processing
            base.TickOutgoing();
        }
    }
}
