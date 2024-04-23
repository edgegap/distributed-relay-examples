using System;

namespace FishNet.Transporting.KCP.Edgegap
{
    [Serializable]
    public struct EdgegapRelayData
    {
        public string Address;
        public ushort ServerPort;
        public ushort ClientPort;

        public uint SessionAuthorizationToken;
        public uint UserAuthorizationToken;

        public EdgegapRelayData(string address, ushort serverPort, ushort clientPort, uint userAuthorizationToken, uint sessionAuthorizationToken)
        {
            Address = address;
            ServerPort = serverPort;
            ClientPort = clientPort;
            UserAuthorizationToken = userAuthorizationToken;
            SessionAuthorizationToken = sessionAuthorizationToken;
        }
    }
}