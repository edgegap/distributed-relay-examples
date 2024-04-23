using System;

namespace FishNet.Transporting.KCP.Edgegap
{
    [Serializable]
    public enum ProtocolType : byte
    {
        KcpTransport = 0,
        EdgegapRelay = 1
    }
}