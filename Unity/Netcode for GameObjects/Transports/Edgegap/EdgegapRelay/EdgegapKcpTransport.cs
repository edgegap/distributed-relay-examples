// edgegap relay transport.
// reuses KcpTransport with custom KcpServer/Client.

using System;
using System.Text.RegularExpressions;
using UnityEngine;
//using Mirror;
using kcp2k;
using Unity.Netcode;

namespace Edgegap
{
    [DisallowMultipleComponent]

    public class EdgegapKcpTransport : KCPTransport
    {

        [Header("Relay")]
        public string relayAddress = "127.0.0.1";
        public ushort relayGameServerPort = 8888;
        public ushort relayGameClientPort = 9999;

        // mtu for kcp transport. respects relay overhead.
        public const int MaxPayload = Kcp.MTU_DEF - Protocol.Overhead;

        [Header("Relay")]
        public bool relayGUI = true;
        public uint userId = 11111111;
        public uint sessionId = 22222222;

        // helper
        internal static String ReParse(String cmd, String pattern, String defaultValue)
        {
            Match match = Regex.Match(cmd, pattern);
            return match.Success ? match.Groups[1].Value : defaultValue;
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

            // create config from serialized settings.
            // with MaxPayload as max size to respect relay overhead.
            config = new KcpConfig(DualMode, RecvBufferSize, SendBufferSize, MaxPayload, NoDelay, Interval, FastResend, false, SendWindowSize, ReceiveWindowSize, Timeout, MaxRetransmit);
            
            client = new EdgegapKcpClient(
                ()                 => clientEvents.Enqueue(new KcpEvent(NetworkEvent.Connect, ServerClientId, null)),
                (message, channel) => clientEvents.Enqueue(new KcpEvent(NetworkEvent.Data, ServerClientId, message.ToArray())),
                ()                 => clientEvents.Enqueue(new KcpEvent(NetworkEvent.Disconnect, ServerClientId, null)),
                (error, reason)    => clientEvents.Enqueue(new KcpEvent(NetworkEvent.TransportFailure, ServerClientId, null)),
                config
            );

            // server
            server = new EdgegapKcpServer(
                (connectionId)                   => serverEvents.Enqueue(new KcpEvent(NetworkEvent.Connect, (ulong)connectionId, null)),
                (connectionId, message, channel) => serverEvents.Enqueue(new KcpEvent(NetworkEvent.Data, (ulong)connectionId, message.ToArray())),
                (connectionId)                   => serverEvents.Enqueue(new KcpEvent(NetworkEvent.Disconnect, (ulong)connectionId, null)),
                (connectionId, error, reason)    => serverEvents.Enqueue(new KcpEvent(NetworkEvent.TransportFailure, (ulong)connectionId, null)),
                config
            );

            Debug.Log("EdgegapTransport initialized!");
        }

        protected override void OnValidate()
        {
            // show max message sizes in inspector for convenience.
            // 'config' isn't available in edit mode yet, so use MTU define.
            ReliableMaxMessageSize = KcpPeer.ReliableMaxMessageSize(MaxPayload, ReceiveWindowSize);
            UnreliableMaxMessageSize = KcpPeer.UnreliableMaxMessageSize(MaxPayload);
        }

        // client overwrites to use EdgegapClient instead of KcpClient
        public override bool StartClient()
        {
            // connect to relay address:port instead of the expected server address
            EdgegapKcpClient client = (EdgegapKcpClient)this.client;
            client.userId = userId;
            client.sessionId = sessionId;
            client.connectionState = ConnectionState.Checking; // reset from last time
            client.Connect(relayAddress, relayGameClientPort);

            return true;
        }
        
        // server overwrites to use EdgegapServer instead of KcpServer
        public override bool StartServer()
        {
            // start the server
            EdgegapKcpServer server = (EdgegapKcpServer)this.server;
            server.Start(relayAddress, relayGameServerPort, userId, sessionId);

            return true;
        }

        void OnGUIRelay()
        {
            // if (server.IsActive()) return;

            GUILayout.BeginArea(new Rect(300, 30, 200, 100));

            GUILayout.BeginHorizontal();
            GUILayout.Label("SessionId:");
            sessionId = Convert.ToUInt32(GUILayout.TextField(sessionId.ToString()));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("UserId:");
            userId = Convert.ToUInt32(GUILayout.TextField(userId.ToString()));
            GUILayout.EndHorizontal();

            if (NetworkManager.Singleton.IsServer)
            {
                EdgegapKcpServer server = (EdgegapKcpServer)this.server;
                GUILayout.BeginHorizontal();
                GUILayout.Label("State:");
                GUILayout.Label(server.state.ToString());
                GUILayout.EndHorizontal();
            }
            else if (NetworkManager.Singleton.IsClient)
            {
                EdgegapKcpClient client = (EdgegapKcpClient)this.client;
                GUILayout.BeginHorizontal();
                GUILayout.Label("State:");
                GUILayout.Label(client.connectionState.ToString());
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }
        
        // base OnGUI only shows in editor & development builds.
        // here we always show it because we need the sessionid & userid buttons.
#pragma warning disable CS0109
        new void OnGUI()
        {
            if (relayGUI) OnGUIRelay();
        }
        
        public override string ToString() => "Edgegap Kcp Transport";
    }
#pragma warning restore CS0109
}
