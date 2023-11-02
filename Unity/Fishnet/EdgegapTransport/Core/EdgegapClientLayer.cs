using LiteNetLib.Utils;
using System;
using System.Net;

namespace FishNet.Transporting.Edgegap.Client
{
    public class EdgegapClientLayer : LiteNetLib.Layers.PacketLayerBase
    {
        const int EDGEGAP_CLIENT_HEADER_LENGTH = 9;

        private uint _userAuthorizationToken;
        private uint _sessionAuthorizationToken;
        private RelayConnectionState _prevState = RelayConnectionState.Disconnected;

        public Action<RelayConnectionState, RelayConnectionState> OnStateChange;

        public EdgegapClientLayer(uint userAuthorizationToken, uint sessionAuthorizationToken)
            : base(EDGEGAP_CLIENT_HEADER_LENGTH)
        {
            _userAuthorizationToken = userAuthorizationToken;
            _sessionAuthorizationToken = sessionAuthorizationToken;
        }

        public override void ProcessInboundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int offset, ref int length)
        {
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

                // Just in case, if there's a Data message attached to the ping
                if (!reader.EndOfData) ProcessInboundPacket(ref endPoint, ref data, ref offset, ref length);
            }
            else if (messageType == MessageType.Data)
            {
                length = reader.AvailableBytes;
            }
            else
            {
                // Invalid.
                length = 0;
                return;
            }

            Buffer.BlockCopy(data, reader.Position, data, 0, length);
            // NetDebug.WriteForce("Client Layer: got Data of raw size: " + length);
        }

        public override void ProcessOutBoundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int offset, ref int length)
        {
            NetDataWriter writer = new NetDataWriter(true, length + EDGEGAP_CLIENT_HEADER_LENGTH);

            writer.Put(_userAuthorizationToken);
            writer.Put(_sessionAuthorizationToken);

            // Handle ping
            // @TODO: This is still quite the hack... There might still be a better way
            if (length == 0)
            {
                writer.Put((byte)MessageType.Ping);
            }
            else if (_prevState == RelayConnectionState.Valid)
            {
                writer.Put((byte)MessageType.Data);
                writer.Put(data, offset, length);
                // NetDebug.WriteForce("Client Layer: Sending Data with Raw length: " + length);
            } else
            {
                // Drop the packet if the relay isn't ready
                length = 0;
            }

            // Copy the modified packet to the original buffer
            Buffer.BlockCopy(writer.Data, 0, data, 0, writer.Length);
            length = writer.Length;
        }
    }
}
