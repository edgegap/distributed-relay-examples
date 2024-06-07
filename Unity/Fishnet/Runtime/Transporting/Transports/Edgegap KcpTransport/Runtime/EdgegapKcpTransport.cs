using System;
using kcp2k.Edgegap;
using kcp2k;
using UnityEngine;

namespace FishNet.Transporting.KCP.Edgegap
{
    [DisallowMultipleComponent]
    [AddComponentMenu("FishNet/Transport/Edgegap KcpTransport")]
    public class EdgegapKcpTransport : KcpTransport
    {
        [SerializeField] private ProtocolType _protocolType;

        public ProtocolType Protocol
        {
            get => _protocolType;
            set => _protocolType = value;
        }

        private EdgegapRelayData _edgegapRelayData;

        private new EdgegapServerSocket Server
        {
            get => (EdgegapServerSocket)base.Server;
            set => base.Server = value;
        }

        private new EdgegapClientSocket Client
        {
            get => (EdgegapClientSocket)base.Client;
            set => base.Client = value;
        }

        private bool IsServerStartingOrStarted =>
            GetConnectionState(true) == LocalConnectionState.Starting || GetConnectionState(true) == LocalConnectionState.Started;

        private bool IsClientStartingOrStarted =>
            GetConnectionState(false) == LocalConnectionState.Starting || GetConnectionState(false) == LocalConnectionState.Started;

        protected override void InitializeSockets()
        {
            Server = new EdgegapServerSocket(this);
            Client = new EdgegapClientSocket(this);
            ClientHost = new ClientHostSocket(this);
        }

        protected override bool StartServer()
        {
            return _protocolType switch
            {
                ProtocolType.KcpTransport => base.StartServer(),
                ProtocolType.EdgegapRelay => StartRelayServer(),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        protected override bool StartClient()
        {
            return _protocolType switch
            {
                ProtocolType.KcpTransport => base.StartClient(),
                ProtocolType.EdgegapRelay => StartRelayClient(),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        public void SetEdgegapRelayData(EdgegapRelayData edgegapRelayData)
        {
            _edgegapRelayData = edgegapRelayData;
            Protocol = ProtocolType.EdgegapRelay;
        }

        private bool StartRelayServer()
        {
            return Server.StartConnection(
                CreateRelayConfig(true), 
                _edgegapRelayData.Address,
                _edgegapRelayData.ServerPort,
                _edgegapRelayData.UserAuthorizationToken,
                _edgegapRelayData.SessionAuthorizationToken);
        }

        private bool StartRelayClient()
        {
            return Client.StartConnection(
                CreateRelayConfig(false), 
                _edgegapRelayData.Address,
                _edgegapRelayData.ClientPort,
                _edgegapRelayData.UserAuthorizationToken,
                _edgegapRelayData.SessionAuthorizationToken);
        }

        private KcpConfig CreateRelayConfig(bool asServer)
        {
            var timeout = (int)(GetTimeout(asServer) * 1000);
            return new KcpConfig(EnableIPv6, ReceiveBufferSize, SendBufferSize, EdgegapProtocol.MTU, NoDelay, INTERVAL_MS, FastResend, false, SendWindowSize, ReceiveWindowSize, timeout, MaxRetransmits);
        }

        public override void SetClientAddress(string address)
        {
            base.SetClientAddress(address);
            Protocol = ProtocolType.KcpTransport;
        }

        public override void SetPort(ushort port)
        {
            base.SetPort(port);
            Protocol = ProtocolType.KcpTransport;
        }

        public override ushort GetPort()
        {
            if (_protocolType == ProtocolType.EdgegapRelay)
            {
                if (IsServerStartingOrStarted)
                {
                    return _edgegapRelayData.ServerPort;
                }
                if (IsClientStartingOrStarted)
                {
                    return _edgegapRelayData.ClientPort;
                }
            }
            return base.GetPort();
        }

        public override string GetClientAddress()
        {
            if (_protocolType == ProtocolType.EdgegapRelay)
            {
                if (IsServerStartingOrStarted)
                {
                    return _edgegapRelayData.Address;
                }
                if (IsClientStartingOrStarted)
                {
                    return _edgegapRelayData.Address;
                }
            }
            return base.GetClientAddress();
        }

        public override string GetServerBindAddress(IPAddressType addressType)
        {
            if (_protocolType == ProtocolType.EdgegapRelay)
            {
                if (IsServerStartingOrStarted || IsClientStartingOrStarted)
                {
                    return _edgegapRelayData.Address;
                }
            }
            return base.GetServerBindAddress(addressType);
        }

        public override void SetServerBindAddress(string address, IPAddressType addressType)
        {
            base.SetServerBindAddress(address, addressType);
            Protocol = ProtocolType.KcpTransport;
        }

        public override int GetMTU(byte channel)
        {
            if (_protocolType == ProtocolType.EdgegapRelay)
            {
                return EdgegapProtocol.MTU;
            }
            return base.GetMTU(channel);
        }
    }
}