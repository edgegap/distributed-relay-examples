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
            : base(EdgegapProtocol.ServerOverhead)
        {
            _userAuthorizationToken = userAuthorizationToken;
            _sessionAuthorizationToken = sessionAuthorizationToken;

            _relay = relay;
        }

        public override void ProcessInboundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int offset, ref int length)
        {
            // Don't process packets not being received from the relay
            if ((!endPoint.Equals(_relay)) && endPoint.Port != 0) return;

            NetDataReader reader = new NetDataReader(data, offset, length);

            var messageType = (MessageType)reader.GetByte();
            if (messageType == MessageType.Ping)
            {
                var currentState = (RelayConnectionState)reader.GetByte();
                if (currentState != _prevState && OnStateChange != null)
                    OnStateChange(_prevState, currentState);

                _prevState = currentState;

                // No data
                length = 0;
            } else if (messageType == MessageType.Data)
            {
                var connectionId = reader.GetUInt();

                // Change the endpoint to a "Virtual" endpoint managed by the relay
                // Here the IP is used to store the connectionId and the port signifies it's a virtual endpoint
                endPoint.Address = new IPAddress(connectionId);
                endPoint.Port = 0;

                length = reader.AvailableBytes;
                Buffer.BlockCopy(data, reader.Position, data, 0, length);
            } else
                length = 0;
        }

        public override void ProcessOutBoundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int offset, ref int length)
        {
            // Don't process packets not being set to the relay
            if ((!endPoint.Equals(_relay)) && endPoint.Port != 0) return;

            NetDataWriter writer = new NetDataWriter(true, length + EdgegapProtocol.ServerOverhead);

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
#pragma warning restore CS0618

                writer.Put(peerId);
                writer.Put(data, offset, length);

                // Send the packet to the relay
                endPoint = _relay;
            }

            Buffer.BlockCopy(writer.Data, 0, data, 0, writer.Length);
            length = writer.Length;
        }
    }
}
