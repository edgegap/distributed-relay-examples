using System;
using System.Collections.Generic;
using kcp2k;

namespace FishNet.Transporting.KCP
{
    public class ClientHostSocket : CommonSocket
    {
        private int _reliableMaxMessageSize;
        private byte[] _kcpSendBuffer;

        private readonly Queue<ClientHostSendData> _sendToServerQueue = new Queue<ClientHostSendData>();
        private readonly Queue<ClientHostSendData> _sendToClientQueue = new Queue<ClientHostSendData>();

        private readonly Queue<ClientHostSendData> _clientOutgoingQueue = new Queue<ClientHostSendData>();
        private readonly Queue<ClientHostSendData> _serverOutgoingQueue = new Queue<ClientHostSendData>();

        public const int CLIENT_HOST_ID = 0;

        /// <summary>
        /// Current RemoteConnectionState.
        /// </summary>
        private RemoteConnectionState _remoteConnectionState = RemoteConnectionState.Stopped;

        private LocalConnectionState _serverState;
        private KcpConfig _config;

        protected override void SetConnectionState(LocalConnectionState connectionState)
        {
            if (GetConnectionState() == connectionState)
            {
                return;
            }
            base.SetConnectionState(connectionState);
            Transport.HandleClientConnectionState(new ClientConnectionStateArgs(connectionState, Transport.Index));
        }

        /// <summary>
        /// Returns the current RemoteConnectionState.
        /// </summary>
        /// <returns></returns>
        public virtual RemoteConnectionState GetRemoteConnectionState()
        {
            return _remoteConnectionState;
        }
        /// <summary>
        /// Sets a new remote connection state.
        /// </summary>
        /// <param name="state"></param>
        protected virtual void SetRemoteConnectionState(RemoteConnectionState state)
        {
            if (_remoteConnectionState == state)
            {
                return;
            }
            _remoteConnectionState = state;
            Transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(state, CLIENT_HOST_ID, Transport.Index));
        }

        private readonly struct ClientHostSendData
        {
            public readonly ArraySegment<byte> Segment;
            public readonly Channel Channel;

            public ClientHostSendData(Channel channel, ArraySegment<byte> data)
            {
                if (data.Array == null) throw new InvalidOperationException();
                var array = new byte[data.Count];
                Buffer.BlockCopy(data.Array, data.Offset, array, 0, data.Count);
                Channel = channel;
                Segment = new ArraySegment<byte>(array);
            }
        }

        public ClientHostSocket(Transport transport) : base(transport)
        {
        }

        public bool StartConnection()
        {
            if (GetConnectionState() != LocalConnectionState.Stopped)
            {
                return false;
            }
            
            SetConnectionState(LocalConnectionState.Starting);
            
            if (_serverState == LocalConnectionState.Started)
            {
                SetConnectionState(LocalConnectionState.Started);
                SetRemoteConnectionState(RemoteConnectionState.Started);
            }
            
            return true;
        }

        public void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            if (GetConnectionState() == LocalConnectionState.Started)
            {
                _sendToServerQueue.Enqueue(new ClientHostSendData((Channel)channelId, segment));
            }
        }

        public void SendToClient(byte channelId, ArraySegment<byte> segment)
        {
            if (GetRemoteConnectionState() == RemoteConnectionState.Started)
            {
                _sendToClientQueue.Enqueue(new ClientHostSendData((Channel)channelId, segment));
            }
        }

        public void IterateOutgoing(bool server)
        {
            // if (!GetConnectionState().IsStartingOrStarted())
            // {
            //     return;
            // }
            //
            // if (server)
            // {
            //     while (_sendToClientQueue.TryDequeue(out ClientHostSendData message))
            //     {
            //         _serverOutgoingQueue.Enqueue(message);
            //     }
            // }
            // else
            // {
            //     while (_sendToServerQueue.TryDequeue(out ClientHostSendData message))
            //     {
            //         _clientOutgoingQueue.Enqueue(message);
            //     }
            // }
        }

        public void IterateIncoming(bool server)
        {
            if (!GetConnectionState().IsStartingOrStarted())
            {
                return;
            }
            
            if (server)
            {
                while (_sendToServerQueue.TryDequeue(out ClientHostSendData message))
                {
                    Transport.HandleServerReceivedDataArgs(new ServerReceivedDataArgs(message.Segment, message.Channel,
                        CLIENT_HOST_ID, Transport.Index));
                }
            }
            else
            {
                while (_sendToClientQueue.TryDequeue(out ClientHostSendData message))
                {
                    Transport.HandleClientReceivedDataArgs(new ClientReceivedDataArgs(message.Segment, message.Channel,
                        Transport.Index));
                }
            }
        }

        public bool StopConnection()
        {
            if (GetConnectionState().IsStoppingOrStopped())
            {
                return false;
            }
            
            StopAndReset();
            
            SetRemoteConnectionState(RemoteConnectionState.Stopped);
            return true;
        }

        public bool ServerRequestedDisconnect()
        {
            if (GetConnectionState().IsStoppingOrStopped())
            {
                return false;
            }
            
            SetRemoteConnectionState(RemoteConnectionState.Stopped);
            
            StopAndReset();
            return true;
        }

        private void StopAndReset()
        {
            SetConnectionState(LocalConnectionState.Stopping);
            Reset();
            SetConnectionState(LocalConnectionState.Stopped);
        }

        private void Reset()
        {
            _clientOutgoingQueue.Clear();
            _serverOutgoingQueue.Clear();
            _sendToClientQueue.Clear();
            _sendToServerQueue.Clear();
        }

        public void HandleServerStarted(LocalConnectionState serverState)
        {
            _serverState = serverState;
            if (_serverState == LocalConnectionState.Started)
            {
                if (GetConnectionState() == LocalConnectionState.Starting)
                {
                    SetRemoteConnectionState(RemoteConnectionState.Started);
                    SetConnectionState(LocalConnectionState.Started);
                }
            }
        }
    }
}