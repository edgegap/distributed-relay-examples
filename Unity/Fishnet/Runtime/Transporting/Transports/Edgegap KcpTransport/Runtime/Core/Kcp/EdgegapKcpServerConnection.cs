using System;

namespace kcp2k.Edgegap
{
    public class EdgegapKcpServerConnection : KcpServerConnection
    {
        public bool HelloReceived;

        public EdgegapKcpServerConnection(
            Action<KcpServerConnection> onConnected,
            Action<ArraySegment<byte>, KcpChannel> onData,
            Action onDisconnected,
            Action<ErrorCode, string> onError,
            Action<ArraySegment<byte>> onRawSend,
            KcpConfig config,
            uint cookie)
            : base(onConnected, onData, onDisconnected, onError, onRawSend, config, cookie, default)
        {
        }
    }
}