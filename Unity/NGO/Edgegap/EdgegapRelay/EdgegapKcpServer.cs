using System;
using System.Net;
using System.Net.Sockets;
//using Mirror;
using UnityEngine;
using kcp2k;

namespace Edgegap
{
    public class EdgegapKcpServer : KcpServer
    {
        // need buffer larger than KcpClient.rawReceiveBuffer to add metadata
        readonly byte[] relayReceiveBuffer;
        readonly byte[] relaySendBuffer;

        // authentication
        public uint userId;
        public uint sessionId;
        public ConnectionState state = ConnectionState.Disconnected;

        // server is an UDP client talking to relay
        protected Socket relaySocket;
        public EndPoint remoteEndPoint;

        // ping
        float lastPingInterval = 0.0f;
        byte[] pingMessage;

        // custom 'active'. while connected to relay
        bool relayActive;
        

        public EdgegapKcpServer(
            Action<int> OnConnected,
            Action<int, ArraySegment<byte>, KcpChannel> OnData,
            Action<int> OnDisconnected,
            Action<int, ErrorCode, string> OnError,
            KcpConfig config)
              // TODO don't call base. don't listen to local UdpServer at all?
              : base(OnConnected, OnData, OnDisconnected, OnError, config)
        {
            relayReceiveBuffer = new byte[config.Mtu + Protocol.Overhead];
            relaySendBuffer = new byte[config.Mtu + Protocol.Overhead];
            pingMessage = new byte[9];
        }

        public override bool IsActive() => relayActive;

        // custom start function with relay parameters; connects udp client.
        public void Start(string relayAddress, ushort relayPort, uint userId, uint sessionId)
        {
            // reset last state
            state = ConnectionState.Checking;
            this.userId = userId;
            this.sessionId = sessionId;

            // try resolve host name
            if (!Common.ResolveHostname(relayAddress, out IPAddress[] addresses))
            {
                OnError(0, ErrorCode.DnsResolve, $"Failed to resolve host: {relayAddress}");
                return;
            }

            // create socket
            remoteEndPoint = new IPEndPoint(addresses[0], relayPort);
            relaySocket = new Socket(remoteEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            relaySocket.Blocking = false;

            // configure buffer sizes
            Common.ConfigureSocketBuffers(relaySocket, config.RecvBufferSize, config.SendBufferSize);

            // bind to endpoint for Send/Receive instead of SendTo/ReceiveFrom
            relaySocket.Connect(remoteEndPoint);
            relayActive = true;
        }

        public override void Stop()
        {
            relayActive = false;
        }

        protected override bool RawReceiveFrom(out ArraySegment<byte> segment, out int connectionId)
        {
            segment = default;
            connectionId = 0;

            if (relaySocket == null) return false;

            try
            {
                // TODO need separate buffer. don't write into result yet. only payload

                if (relaySocket.ReceiveNonBlocking(relayReceiveBuffer, out ArraySegment<byte> content))
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
                            ConnectionState last = state;
                            state = (ConnectionState)content.Array[content.Offset + 1];

                            // log state changes for debugging.
                            if (state != last) Debug.Log($"EdgegapServer: state updated to: {state}");

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
                                           (content.Array[content.Offset + 1 + 3] << 24);;

                            segment = new ArraySegment<byte>(content.Array, content.Offset + 1 + 4, content.Count - 1 - 4);

                            // Debug.Log($"EdgegapServer: receiving from connId={connectionId}: {segment.ToHexString()}");
                            return true;
                        }
                        // wrong message type. return false, don't throw.
                        default: return false;
                    }
                }
            }
            catch (SocketException e)
            {
                Log.Info($"EdgegapServer: looks like the other end has closed the connection. This is fine: {e}");
            }
            return false;
        }

        protected override void RawSend(int connectionId, ArraySegment<byte> data)
        {
            // Debug.Log($"EdgegapServer: sending to connId={connectionId}: {data.ToHexString()}");
            
            // Create array segment from relaysendbuffer with length of 13 + data.Count
            ArraySegment<byte> message = new ArraySegment<byte>(relaySendBuffer, 0, 13 + data.Count);
            Utils.Encode32U(message.Array, message.Offset, userId);
            Utils.Encode32U(message.Array, message.Offset + 4, sessionId);
            message[message.Offset + 8] = (byte)MessageType.Data;
            Utils.Encode32U(message.Array, message.Offset + 9, (uint)connectionId);

            if (data.Array != null) {
                Array.Copy(data.Array, data.Offset, message.Array, message.Offset + 13, data.Count);
            }

            try
            {
                relaySocket.SendNonBlocking(message);
            }
            catch (SocketException e)
            {
                Log.Error($"KcpRleayServer: RawSend failed: {e}");
            }
        }

        void SendPing() {
            Utils.Encode32U(pingMessage, 0, userId);
            Utils.Encode32U(pingMessage, 4, sessionId);
            pingMessage[8] = (byte)MessageType.Ping;

            try
            {
                relaySocket.SendNonBlocking(pingMessage);
            }
            catch (SocketException e)
            {
                    Debug.LogWarning($"EdgegapServer: failed to ping. perhaps the relay isn't running? {e}");
            }
        }

        public override void TickOutgoing()
        {
            if (relayActive)
            {
                // ping every interval for keepalive & handshake
                if(Time.deltaTime + lastPingInterval >= Protocol.PingInterval)
                {
                    SendPing();
                    lastPingInterval = 0.0f;
                }
                else
                {
                    lastPingInterval += Time.deltaTime;
                }

            }

            // base processing
            base.TickOutgoing();
        }
    }
}
