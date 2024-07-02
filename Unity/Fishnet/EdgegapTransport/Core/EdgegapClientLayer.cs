using LiteNetLib.Utils;
using System;
using System.Net;

namespace FishNet.Transporting.Edgegap.Client
{
    public class EdgegapClientLayer : LiteNetLib.Layers.PacketLayerBase
    {
        private uint _userAuthorizationToken;
        private uint _sessionAuthorizationToken;
        private RelayConnectionState _prevState = RelayConnectionState.Disconnected;

        public Action<RelayConnectionState, RelayConnectionState> OnStateChange;

        public EdgegapClientLayer(uint userAuthorizationToken, uint sessionAuthorizationToken)
            : base(EdgegapProtocol.ClientOverhead)
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
            }
            else if (messageType != MessageType.Data)
            {
                // Invalid.
                length = 0;
                return;
            }

            length = reader.AvailableBytes;
            Buffer.BlockCopy(data, reader.Position, data, 0, length);
        }

        public override void ProcessOutBoundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int offset, ref int length)
        {
            NetDataWriter writer = new NetDataWriter(true, length + EdgegapProtocol.ClientOverhead);

            writer.Put(_userAuthorizationToken);
            writer.Put(_sessionAuthorizationToken);

            // Handle ping
            if (length == 0) 
                writer.Put((byte)MessageType.Ping);
            else if (_prevState == RelayConnectionState.Valid)
            {
                writer.Put((byte)MessageType.Data);
                writer.Put(data, offset, length);
            } else
                length = 0; // Drop the packet if the relay isn't ready

            Buffer.BlockCopy(writer.Data, 0, data, 0, writer.Length);
            length = writer.Length;
        }
    }
}
