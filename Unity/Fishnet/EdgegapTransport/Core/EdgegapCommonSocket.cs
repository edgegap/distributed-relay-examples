using LiteNetLib;
using FishNet.Transporting.Tugboat;
using System.Net;

namespace FishNet.Transporting.Edgegap
{
    public abstract class EdgegapCommonSocket : CommonSocket
    {
        private uint _lastPingTick = 0;
        private float _pingInterval;

        protected IPEndPoint _relay;

        protected void Initialize(string relayAddress, ushort relayPort, float pingInterval)
        {
            _relay = NetUtils.MakeEndPoint(relayAddress, relayPort);
            _pingInterval = pingInterval;
        }

        protected void OnTick(NetManager manager)
        {
            // Send pings
            var timePassed = InstanceFinder.TimeManager.TimePassed(_lastPingTick);
            if (manager != null && (timePassed > _pingInterval))
            {
                _lastPingTick = InstanceFinder.TimeManager.Tick;

                // This hack is supported by the Relay Client/Server layers to detect outgoing pings
                // It's necessary since there's no way to bypass our Client/Server layer code
                // which would consider any packet a Data packet.
                manager.SendRaw(new byte[0], 0, 0, _relay);
            }
        }
    }

}