using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
using Unity.Collections;

namespace kcp2k
{
    // convert KCP callback to NGO event
    public struct KcpEvent
    {
        public NetworkEvent eventType;
        public ulong clientId;
        public byte[] data; // TODO nonalloc

        public KcpEvent(NetworkEvent eventType, ulong clientId, byte[] data)
        {
            this.eventType = eventType;
            this.clientId = clientId;
            this.data = data;
        }
    }

    public class KCPTransport : NetworkTransport
    {
        // scheme used by this transport
        public const string Scheme = "kcp";

        // NGO specific
        [Header("NGO")]
        public string Address = "127.0.0.1";
        public override ulong ServerClientId => 0; // this seems to be client-only mode?
        protected readonly Queue<KcpEvent> serverEvents = new Queue<KcpEvent>(); // convert kcp callbacks to queue
        protected readonly Queue<KcpEvent> clientEvents = new Queue<KcpEvent>(); // convert kcp callbacks to queue

        // common
        [Header("Transport Configuration")]
        public ushort Port = 7777;
        [Tooltip("DualMode listens to IPv6 and IPv4 simultaneously. Disable if the platform only supports IPv4.")]
        public bool DualMode = true;
        [Tooltip("NoDelay is recommended to reduce latency. This also scales better without buffers getting full.")]
        public bool NoDelay = true;
        [Tooltip("KCP internal update interval. 100ms is KCP default, but a lower interval is recommended to minimize latency and to scale to more networked entities.")]
        public uint Interval = 10;
        [Tooltip("KCP timeout in milliseconds. Note that KCP sends a ping automatically.")]
        public int Timeout = 10000;
        [Tooltip("Socket receive buffer size. Large buffer helps support more connections. Increase operating system socket buffer size limits if needed.")]
        public int RecvBufferSize = 1024 * 1027 * 7;
        [Tooltip("Socket send buffer size. Large buffer helps support more connections. Increase operating system socket buffer size limits if needed.")]
        public int SendBufferSize = 1024 * 1027 * 7;

        [Header("Advanced")]
        [Tooltip("KCP fastresend parameter. Faster resend for the cost of higher bandwidth. 0 in normal mode, 2 in turbo mode.")]
        public int FastResend = 2;
        [Tooltip("KCP congestion window. Restricts window size to reduce congestion. Results in only 2-3 MTU messages per Flush even on loopback. Best to keept his disabled.")]
        /*public*/
        bool CongestionWindow = false; // KCP 'NoCongestionWindow' is false by default. here we negate it for ease of use.
        [Tooltip("KCP window size can be modified to support higher loads. This also increases max message size.")]
        public uint ReceiveWindowSize = 4096; //Kcp.WND_RCV; 128 by default. Mirror sends a lot, so we need a lot more.
        [Tooltip("KCP window size can be modified to support higher loads.")]
        public uint SendWindowSize = 4096; //Kcp.WND_SND; 32 by default. Mirror sends a lot, so we need a lot more.
        [Tooltip("KCP will try to retransmit lost messages up to MaxRetransmit (aka dead_link) before disconnecting.")]
        public uint MaxRetransmit = Kcp.DEADLINK * 2; // default prematurely disconnects a lot of people (#3022). use 2x.
        [Tooltip("Enable to automatically set client & server send/recv buffers to OS limit. Avoids issues with too small buffers under heavy load, potentially dropping connections. Increase the OS limit if this is still too small.")]
        [FormerlySerializedAs("MaximizeSendReceiveBuffersToOSLimit")]
        public bool MaximizeSocketBuffers = true;

        [Header("Allowed Max Message Sizes\nBased on Receive Window Size")]
        [Tooltip("KCP reliable max message size shown for convenience. Can be changed via ReceiveWindowSize.")]
        [ReadOnly] public int ReliableMaxMessageSize = 0; // readonly, displayed from OnValidate
        [Tooltip("KCP unreliable channel max message size for convenience. Not changeable.")]
        [ReadOnly] public int UnreliableMaxMessageSize = 0; // readonly, displayed from OnValidate

        // config is created from the serialized properties above.
        // we can expose the config directly in the future.
        // for now, let's not break people's old settings.
        protected KcpConfig config;

        // use default MTU for this transport.
        protected const int MTU = Kcp.MTU_DEF;

        // server & client
        protected KcpServer server;
        protected KcpClient client;

        // debugging
        [Header("Debug")]
        public bool debugLog;
        // show statistics in OnGUI
        public bool statisticsGUI;
        // log statistics for headless servers that can't show them in GUI
        public bool statisticsLog;

        public override bool IsSupported => Application.platform != RuntimePlatform.WebGLPlayer;

        // translate Kcp <-> NGO channels
        public static NetworkDelivery FromKcpChannel(KcpChannel channel)
        {
            switch (channel)
            {
                case KcpChannel.Reliable:   return NetworkDelivery.Reliable;
                case KcpChannel.Unreliable: return NetworkDelivery.Unreliable;
                default:                    return NetworkDelivery.Reliable;
            }
        }

        public static KcpChannel ToKcpChannel(NetworkDelivery channel)
        {
            switch (channel)
            {
                case NetworkDelivery.Unreliable:                  return KcpChannel.Unreliable;
                case NetworkDelivery.UnreliableSequenced:         throw new NotImplementedException("KcpTransport does not support UnreliableSequenced");
                case NetworkDelivery.Reliable:                    return KcpChannel.Reliable;
                case NetworkDelivery.ReliableSequenced:           return KcpChannel.Reliable;
                case NetworkDelivery.ReliableFragmentedSequenced: return KcpChannel.Reliable;
                default:                                          return KcpChannel.Reliable;
            }
        }

        public override void Initialize(NetworkManager networkManager = null)
        {
            // logging
            //   Log.Info should use Debug.Log if enabled, or nothing otherwise
            //   (don't want to spam the console on headless servers)
            if (debugLog)
                Log.Info = Debug.Log;
            else
                Log.Info = _ => {};
            Log.Warning = Debug.LogWarning;
            Log.Error = Debug.LogError;

            // create config from serialized settings
            config = new KcpConfig(DualMode, RecvBufferSize, SendBufferSize, MTU, NoDelay, Interval, FastResend, CongestionWindow, SendWindowSize, ReceiveWindowSize, Timeout, MaxRetransmit);

            // client: callbacks are pumped into the event queue
            client = new KcpClient(
                ()                 => clientEvents.Enqueue(new KcpEvent(NetworkEvent.Connect, ServerClientId, null)),
                (message, channel) => clientEvents.Enqueue(new KcpEvent(NetworkEvent.Data, ServerClientId, message.ToArray())),
                ()                 => clientEvents.Enqueue(new KcpEvent(NetworkEvent.Disconnect, ServerClientId, null)),
                (error, reason)    => clientEvents.Enqueue(new KcpEvent(NetworkEvent.TransportFailure, ServerClientId, null)),
                config
            );

            // server: callbacks are pumped into the event queue
            server = new KcpServer(
                (connectionId)                   => serverEvents.Enqueue(new KcpEvent(NetworkEvent.Connect, (ulong)connectionId, null)),
                (connectionId, message, channel) => serverEvents.Enqueue(new KcpEvent(NetworkEvent.Data, (ulong)connectionId, message.ToArray())),
                (connectionId)                   => serverEvents.Enqueue(new KcpEvent(NetworkEvent.Disconnect, (ulong)connectionId, null)),
                (connectionId, error, reason)    => serverEvents.Enqueue(new KcpEvent(NetworkEvent.TransportFailure, (ulong)connectionId, null)),
                config
            );

            // if (statisticsLog)
            //     InvokeRepeating(nameof(OnLogStatistics), 1, 1);

            Debug.Log("KcpTransport initialized!");
        }

        protected virtual void OnValidate()
        {
            // show max message sizes in inspector for convenience.
            // 'config' isn't available in edit mode yet, so use MTU define.
            ReliableMaxMessageSize = KcpPeer.ReliableMaxMessageSize(MTU, ReceiveWindowSize);
            UnreliableMaxMessageSize = KcpPeer.UnreliableMaxMessageSize(MTU);
        }

        public override bool StartClient()
        {
            client.Connect(Address, Port);
            Debug.Log($"KcpClient connected @ Address={Address} Port={Port}");
            return true;
        }

        public override void DisconnectLocalClient()
        {
            client.Disconnect();
        }

        public override bool StartServer()
        {
            server.Start(Port);
            Debug.Log($"KcpServer started @ Port = {Port}");
            return true;
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            server.Disconnect((int)clientId);
        }

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery delivery)
        {
            // client only?
            if (clientId == ServerClientId)
            {
                client.Send(data, ToKcpChannel(delivery));
            }
            // server connection?
            else
            {
                server.Send((int)clientId, data, ToKcpChannel(delivery));
            }
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            clientId = 0;
            payload = default;
            receiveTime = Time.time;

            // client events?
            if (clientEvents.TryDequeue(out KcpEvent clientEvent))
            {
                clientId = clientEvent.clientId;
                payload = clientEvent.data != null ? new ArraySegment<byte>(clientEvent.data) : default; // TODO recycle
                return clientEvent.eventType;
            }

            // server events?
            if (serverEvents.TryDequeue(out KcpEvent serverEvent))
            {
                clientId = serverEvent.clientId;
                payload = serverEvent.data != null ? new ArraySegment<byte>(serverEvent.data) : default; // TODO recycle
                return serverEvent.eventType;
            }

            // no events
            return NetworkEvent.Nothing;
        }

        public override ulong GetCurrentRtt(ulong clientId) => 0; // not supported

        void Update()
        {
            // TODO this would be better in NetworkEarly/LateUpdate but NGO doesn't have it

            if (client != null)
            {
                client.TickIncoming();
                client.TickOutgoing();
            }

            if (server != null)
            {
                server.TickIncoming();
                server.TickOutgoing();
            }
        }

        public override void Shutdown()
        {
            client.Disconnect();
            server.Stop();
        }
    }
}