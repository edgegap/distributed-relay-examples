using System;
using kcp2k;

namespace FishNet.Transporting.KCP
{
    public abstract class CommonSocket
    {
        /// <summary>
        /// Current ConnectionState.
        /// </summary>
        private LocalConnectionState _connectionState = LocalConnectionState.Stopped;
        /// <summary>
        /// Returns the current ConnectionState.
        /// </summary>
        /// <returns></returns>
        public virtual LocalConnectionState GetConnectionState()
        {
            return _connectionState;
        }
        /// <summary>
        /// Sets a new connection state.
        /// </summary>
        /// <param name="connectionState"></param>
        protected virtual void SetConnectionState(LocalConnectionState connectionState)
        {
            _connectionState = connectionState;
        }
        #region Protected.
        /// <summary>
        /// Transport controlling this socket.
        /// </summary>
        protected readonly Transport Transport;
        #endregion

        protected CommonSocket(Transport transport)
        {
            Transport = transport;
        }

        protected static Channel ParseChannel(KcpChannel channel)
        {
            return channel switch
            {
                KcpChannel.Reliable => Channel.Reliable,
                KcpChannel.Unreliable => Channel.Unreliable,
                _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
            };
        }

        protected static KcpChannel ParseChannel(Channel channel)
        {
            return channel switch
            {
                Channel.Reliable => KcpChannel.Reliable,
                Channel.Unreliable => KcpChannel.Unreliable,
                _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
            };
        }
    }
}