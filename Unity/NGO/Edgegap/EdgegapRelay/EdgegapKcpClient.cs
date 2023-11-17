// overwrite RawSend/Receive
using System;
using System.Net.Sockets;
//using Mirror;
using UnityEngine;
using kcp2k;

namespace Edgegap
{
    public class EdgegapKcpClient : KcpClient
    {
        // need buffer larger than KcpClient.rawReceiveBuffer to add metadata
        readonly byte[] relayReceiveBuffer;
        readonly byte[] relaySendBuffer;

        // authentication
        public uint userId;
        public uint sessionId;
        public ConnectionState connectionState = ConnectionState.Disconnected;

        // ping
        float lastPingInterval = 0.0f;
        byte[] pingMessage;

        public EdgegapKcpClient(
            Action OnConnected,
            Action<ArraySegment<byte>, KcpChannel> OnData,
            Action OnDisconnected,
            Action<ErrorCode, string> OnError,
            KcpConfig config)
              : base(OnConnected, OnData, OnDisconnected, OnError, config)
        {
            relayReceiveBuffer = new byte[config.Mtu + Protocol.Overhead];
            relaySendBuffer = new byte[config.Mtu + Protocol.Overhead];
            pingMessage = new byte[9];
        }

        // custom start function with relay parameters; connects udp client.
        public void Connect(string relayAddress, ushort relayPort, uint userId, uint sessionId)
        {
            // reset last state
            connectionState = ConnectionState.Checking;
            this.userId = userId;
            this.sessionId = sessionId;

            // reuse base connect
            base.Connect(relayAddress, relayPort);
        }

        // parse metadata, then pass to kcp
        protected override bool RawReceive(out ArraySegment<byte> segment)
        {
            segment = default;
            if (socket == null) return false;

            try
            {
                if (socket.ReceiveNonBlocking(relayReceiveBuffer, out ArraySegment<byte> content))
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
                            ConnectionState last = connectionState;
                            connectionState = (ConnectionState)content.Array[content.Offset + 1];

                            // log state changes for debugging.
                            if (connectionState != last) Debug.Log($"EdgegapClient: state updated to: {connectionState}");

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
            ArraySegment<byte> message = new ArraySegment<byte>(relaySendBuffer, 0, 9 + data.Count);
            Utils.Encode32U(message.Array, message.Offset + 0, userId);
            Utils.Encode32U(message.Array, message.Offset + 4, sessionId);
            message[message.Offset + 8] = (byte)MessageType.Data;
            if (data.Array != null) {
                Array.Copy(data.Array, data.Offset, message.Array, message.Offset + 9, data.Count);
            }

            base.RawSend(message);
        }

        void SendPing() {
            Utils.Encode32U(pingMessage, 0, userId);
            Utils.Encode32U(pingMessage, 4, sessionId);
            pingMessage[8] = (byte)MessageType.Ping;
            base.RawSend(pingMessage);
        }

        public override void TickOutgoing()
        {
            if (connected)
            {
                // ping every interval for keepalive & handshake
                if (Time.deltaTime + lastPingInterval >= Protocol.PingInterval)
                {
                    SendPing();
                    lastPingInterval = 0;
                } 
                else 
                {
                    lastPingInterval += Time.deltaTime;
                }
            }

            base.TickOutgoing();
        }
    }
}
