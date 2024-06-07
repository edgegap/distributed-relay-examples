using System;
using System.Net;
using FishNet.Managing;
using kcp2k;

namespace FishNet.Transporting.KCP
{
    public class ClientSocket : CommonSocket
    {
        protected KcpClient Client;

        public ClientSocket(Transport transport) : base(transport)
        {
        }

        public bool StartConnection(KcpConfig config, string address, ushort port)
        {
            if (GetConnectionState() != LocalConnectionState.Stopped)
            {
                return false;
            }
            
            if (string.IsNullOrEmpty(address) || address == "localhost")
            {
                bool enableIPv6 = config.DualMode;
                address = enableIPv6 ? IPAddress.IPv6Loopback.ToString() : IPAddress.Loopback.ToString();
            }
            
            Client = new KcpClient(
                OnConnectedCallback,
                OnDataCallback,
                OnDisconnectedCallback,
                OnErrorCallback,
                config);
            
            SetConnectionState(LocalConnectionState.Starting);
            
            Client.Connect(address, port);
            return Client.remoteEndPoint != null;
        }

        protected override void SetConnectionState(LocalConnectionState connectionState)
        {
            if (GetConnectionState() == connectionState)
                return;
            base.SetConnectionState(connectionState);
            Transport.HandleClientConnectionState(new ClientConnectionStateArgs(connectionState, Transport.Index));
        }

        protected void OnConnectedCallback()
        {
            SetConnectionState(LocalConnectionState.Started);
        }

        protected void OnDisconnectedCallback()
        {
            Client = null;
            SetConnectionState(LocalConnectionState.Stopped);
        }

        protected void OnErrorCallback(ErrorCode errorCode, string message)
        {
            switch (errorCode)
            {
                case ErrorCode.DnsResolve:
                    SetConnectionState(LocalConnectionState.Stopping);
                    SetConnectionState(LocalConnectionState.Stopped);
                    break;
                case ErrorCode.Timeout:
                case ErrorCode.Congestion:
                case ErrorCode.InvalidReceive:
                case ErrorCode.ConnectionClosed:
                case ErrorCode.Unexpected:
                    SetConnectionState(LocalConnectionState.Stopping);
                    break;
                case ErrorCode.InvalidSend:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(errorCode), errorCode, null);
            }
            
            Transport.NetworkManager.LogError(errorCode + ": " + message);
        }

        protected void OnDataCallback(ArraySegment<byte> message, KcpChannel channel)
        {
            Channel fishNetChannel = ParseChannel(channel);
            var args = new ClientReceivedDataArgs(message, fishNetChannel, Transport.Index);
            Transport.HandleClientReceivedDataArgs(args);
        }

        public bool StopConnection()
        {
            if (GetConnectionState().IsStoppingOrStopped())
            {
                return false;
            }
            
            SetConnectionState(LocalConnectionState.Stopping);
            Client.Disconnect();
            return true;
        }

        public void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            if (GetConnectionState() == LocalConnectionState.Started)
            {
                Client.Send(segment, ParseChannel((Channel)channelId));
            }
        }

        public void IterateIncoming()
        {
            if (GetConnectionState().IsStartingOrStarted())
            {
                Client.TickIncoming();
            }
        }

        public void IterateOutgoing()
        {
            if (GetConnectionState().IsStartingOrStarted())
            {
                Client.TickOutgoing();
            }
        }
    }
}