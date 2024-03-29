// overwrite RawSend/Receive
using System;
using System.Net.Sockets;
using Mirror;
using UnityEngine;
using kcp2k;

namespace Edgegap
{
    public class EdgegapClient : KcpClient
    {
        // need buffer larger than KcpClient.rawReceiveBuffer to add metadata
        readonly byte[] relayReceiveBuffer;

        // authentication
        public uint userAuthorizationToken;
        public uint sessionAuthorizationToken;
        public ConnectionState state = ConnectionState.Disconnected;

        // ping
        double lastPingTime;

        public EdgegapClient(
            Action OnConnected,
            Action<ArraySegment<byte>, KcpChannel> OnData,
            Action OnDisconnected,
            Action<ErrorCode, string> OnError,
            KcpConfig config)
              : base(OnConnected, OnData, OnDisconnected, OnError, config)
        {
            relayReceiveBuffer = new byte[config.Mtu + Protocol.Overhead];
        }

        // custom start function with relay parameters; connects udp client.
        public void Connect(string relayAddress, ushort relayPort, uint userAuthorizationToken, uint sessionAuthorizationToken)
        {
            // reset last state
            state = ConnectionState.Checking;
            this.userAuthorizationToken = userAuthorizationToken;
            this.sessionAuthorizationToken = sessionAuthorizationToken;

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
                    using (NetworkReaderPooled reader = NetworkReaderPool.Get(content))
                    {
                        // parse message type
                        if (reader.Remaining == 0)
                        {
                            Debug.LogWarning($"EdgegapClient: message of {content.Count} is too small to parse.");
                            return false;
                        }
                        byte messageType = reader.ReadByte();

                        // handle message type
                        switch (messageType)
                        {
                            case (byte)MessageType.Ping:
                            {
                                // parse state
                                if (reader.Remaining < 1) return false;
                                ConnectionState last = state;
                                state = (ConnectionState)reader.ReadByte();

                                // log state changes for debugging.
                                if (state != last) Debug.Log($"EdgegapClient: state updated to: {state}");

                                // return true indicates Mirror to keep checking
                                // for further messages.
                                return true;
                            }
                            case (byte)MessageType.Data:
                            {
                                segment = reader.ReadBytesSegment(reader.Remaining);
                                return true;
                            }
                            // wrong message type. return false, don't throw.
                            default: return false;
                        }
                    }
                }
            }
            catch (SocketException e)
            {
                Log.Info($"EdgegapClient: looks like the other end has closed the connection. This is fine: {e}");
                peer.Disconnect();
            }

            return false;
        }

        protected override void RawSend(ArraySegment<byte> data)
        {
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                writer.WriteUInt(userAuthorizationToken);
                writer.WriteUInt(sessionAuthorizationToken);
                writer.WriteByte((byte)MessageType.Data);
                writer.WriteBytes(data.Array, data.Offset, data.Count);
                base.RawSend(writer);
            }
        }

        void SendPing()
        {
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                writer.WriteUInt(userAuthorizationToken);
                writer.WriteUInt(sessionAuthorizationToken);
                writer.WriteByte((byte)MessageType.Ping);
                base.RawSend(writer);
            }
        }

        public override void TickOutgoing()
        {
            if (connected)
            {
                // ping every interval for keepalive & handshake
                if (NetworkTime.localTime >= lastPingTime + Protocol.PingInterval)
                {
                    SendPing();
                    lastPingTime = NetworkTime.localTime;
                }
            }

            base.TickOutgoing();
        }
    }
}
