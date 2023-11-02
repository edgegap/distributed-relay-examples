using LiteNetLib.Utils;
using System;
using System.Net;

namespace FishNet.Transporting.Edgegap.Server
{
    public class EdgegapServerLayer : LiteNetLib.Layers.PacketLayerBase
    {
        private IPEndPoint _relay;
        private uint _userAuthorizationToken;
        private uint _sessionAuthorizationToken;
        private RelayConnectionState _prevState = RelayConnectionState.Disconnected;

        public Action<RelayConnectionState, RelayConnectionState> OnStateChange;

        public EdgegapServerLayer(IPEndPoint relay, uint userAuthorizationToken, uint sessionAuthorizationToken)
            : base(EdgegapProtocol.Overhead)
        {
            _userAuthorizationToken = userAuthorizationToken;
            _sessionAuthorizationToken = sessionAuthorizationToken;

            _relay = relay;
        }

        public override void ProcessInboundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int offset, ref int length)
        {
            // If relay isn't active, don't touch the packet
            if (_relay == null) return;

            NetDataReader reader = new NetDataReader(data, offset, length);

            var messageType = (MessageType)reader.GetByte();

            if (messageType == MessageType.Ping)
            {
                var currentState = (RelayConnectionState)reader.GetByte();

                if (currentState != _prevState && OnStateChange != null)
                {
                    OnStateChange(_prevState, currentState);
                }

                _prevState = currentState;

                length = reader.AvailableBytes;
                offset = reader.Position;

                if (!reader.EndOfData) ProcessInboundPacket(ref endPoint, ref data, ref offset, ref length);
            } else if (messageType == MessageType.Data)
            {
                var connectionId = reader.GetUInt();

                // Port = 0 means it's a Virtual Address
                endPoint.Address = new IPAddress(connectionId);
                endPoint.Port = 0;

                length = reader.AvailableBytes;
                Buffer.BlockCopy(data, reader.Position, data, 0, length);

                // NetDebug.WriteForce("Server Layer: Got message for connection ID: " + connectionId + " with Raw length: " + length);
            } else
            {
                // Invalid.
                length = 0;
            }
        }

        public override void ProcessOutBoundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int offset, ref int length)
        {
            // If relay isn't active, don't touch the packet
            if (_relay == null) return;

            NetDataWriter writer = new NetDataWriter(true, length + EdgegapProtocol.Overhead);

            writer.Put(_userAuthorizationToken);
            writer.Put(_sessionAuthorizationToken);

            // Handle ping
            if (length == 0)
            {
                writer.Put((byte)MessageType.Ping);
            }
            else
            {
                // No sense sending data if the connection isn't valid
                if (_prevState != RelayConnectionState.Valid) return;

                writer.Put((byte)MessageType.Data);

#pragma warning disable CS0618 // Type or member is obsolete
                int peerId = (int)endPoint.Address.Address;
                // NetDebug.WriteForce("Server Layer: Sending message to Peer: " + peerId + " with raw length: " + length);
#pragma warning restore CS0618
                writer.Put(peerId);
                writer.Put(data, offset, length);

                // Send the packet to the relay
                endPoint = _relay;
            }

            // Copy the modified packet to the original buffer
            Buffer.BlockCopy(writer.Data, 0, data, 0, writer.Length);
            length = writer.Length;
        }
    }
}
