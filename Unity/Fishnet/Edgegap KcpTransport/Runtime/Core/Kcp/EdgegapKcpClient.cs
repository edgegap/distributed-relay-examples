using System;
using System.Net.Sockets;
using UnityEngine;

namespace kcp2k.Edgegap
{
    public class EdgegapKcpClient : KcpClient
    {
        // need buffer larger than KcpClient.rawReceiveBuffer to add metadata
        private readonly byte[] _relayReceiveBuffer;
        private readonly byte[] _relaySendBuffer;

        // authentication
        private uint _userAuthorizationToken;
        private uint _sessionAuthorizationToken;
        private ConnectionState _state = ConnectionState.Disconnected;

        // ping
        private float _lastPingInterval;
        private readonly byte[] _pingMessage;

        public EdgegapKcpClient(
            Action onConnected,
            Action<ArraySegment<byte>, KcpChannel> onData,
            Action onDisconnected,
            Action<ErrorCode, string> onError,
            KcpConfig config) : base(onConnected, onData, onDisconnected, onError, config)
        {
            _relayReceiveBuffer = new byte[config.Mtu + EdgegapProtocol.Overhead];
            _relaySendBuffer = new byte[config.Mtu + EdgegapProtocol.Overhead];
            _pingMessage = new byte[9];
        }

        private bool _helloSent;
        
        // custom start function with relay parameters; connects udp client.
        public bool TryConnect(string relayAddress, ushort relayPort, uint userId, uint sessionId)
        {
            if (connected || remoteEndPoint != null)
            {
                return false;
            }
            _state = ConnectionState.Checking;
            _userAuthorizationToken = userId;
            _sessionAuthorizationToken = sessionId;
            
            Connect(relayAddress, relayPort);
            return remoteEndPoint != null;
        }

        // parse metadata, then pass to kcp
        protected override bool RawReceive(out ArraySegment<byte> segment)
        {
            segment = default;
            if (socket == null) return false;

            try
            {
                if (socket.ReceiveNonBlocking(_relayReceiveBuffer, out ArraySegment<byte> content))
                {
                    // parse message type
                    if (content.Array == null || content.Count == 0)
                    {
                        Debug.LogWarning($"EdgegapClient: message of {content.Count} is too small to parse.");
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
                            if (_state != last) Debug.Log($"EdgegapClient: state updated to: {_state}");

                            // return true indicates Mirror to keep checking
                            // for further messages.
                            return true;
                        }
                        case (byte)MessageType.Data:
                        {
                            segment = new ArraySegment<byte>(content.Array, content.Offset + 1, content.Count - 1);
                            return true;
                        }
                        // wrong message type. return false, don't throw.
                        default: return false;
                    }
                }
            }
            catch (SocketException e)
            {
                Log.Info($"EdgegapClient: looks like the other end has closed the connection. This is fine: {e}");
                Disconnect();
            }

            return false;
        }

        protected override void RawSend(ArraySegment<byte> data)
        {
            var message = new ArraySegment<byte>(_relaySendBuffer, 0, 9 + data.Count);
            Utils.Encode32U(message.Array, message.Offset + 0, _userAuthorizationToken);
            Utils.Encode32U(message.Array, message.Offset + 4, _sessionAuthorizationToken);
            message.Array![message.Offset + 8] = (byte)MessageType.Data;
            if (data.Array != null) {
                Array.Copy(data.Array, data.Offset, message.Array, message.Offset + 9, data.Count);
            }
            base.RawSend(message);
        }

        public override void TickOutgoing()
        {
            if (connected)
            {
                // ping every interval for keepalive & handshake
                if (Time.deltaTime + _lastPingInterval >= EdgegapProtocol.PingInterval)
                {
                    SendPing();
                    _lastPingInterval = 0;
                }
                else 
                {
                    _lastPingInterval += Time.deltaTime;
                }
            }

            base.TickOutgoing();
        }

        private void SendPing() {
            Utils.Encode32U(_pingMessage, 0, _userAuthorizationToken);
            Utils.Encode32U(_pingMessage, 4, _sessionAuthorizationToken);
            _pingMessage[8] = (byte)MessageType.Ping;
            base.RawSend(new ArraySegment<byte>(_pingMessage));
        }
    }
}
