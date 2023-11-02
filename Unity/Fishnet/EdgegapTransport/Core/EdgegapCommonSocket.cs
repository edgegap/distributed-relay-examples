using FishNet.Utility.Performance;
using LiteNetLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

                // This hack is supported by the Relay Client and Server layers to detect outgoing pings
                manager.SendRaw(new byte[0], 0, 0, _relay);
            }
        }
    }

}