using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace kcp2k
{
    internal static class KcpServerUtils
    {
        public static bool BindServerSocket(bool enableIPv6, string addressIPv4, string addressIPv6, ushort port, out Socket serverSocket)
        {
            if (!Socket.OSSupportsIPv6)
            {
                enableIPv6 = false;
                Log.Warning("IPv6 is not supported on this platform. Use IPv4.");
            }

            serverSocket = null;

            IPAddress address;
            if (enableIPv6)
            {
                if (string.IsNullOrEmpty(addressIPv6))
                {
                    address = IPAddress.IPv6Any;
                }
                else if (!IPAddress.TryParse(addressIPv6, out address))
                {
                    return false;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(addressIPv4))
                {
                    address = IPAddress.Any;
                }
                else if (!IPAddress.TryParse(addressIPv4, out address))
                {
                    return false;
                }
            }

            var endPoint = new IPEndPoint(address, port);
            serverSocket = new Socket(endPoint.AddressFamily, SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
            if (enableIPv6)
            {
                // enabling DualMode may throw:
                // https://learn.microsoft.com/en-us/dotnet/api/System.Net.Sockets.Socket.DualMode?view=net-7.0
                // attempt it, otherwise log but continue
                // fixes: https://github.com/MirrorNetworking/Mirror/issues/3358
                try
                {
                    serverSocket.DualMode = true;
                }
                catch (NotSupportedException e)
                {
                    Log.Warning($"[KCP] Failed to set Dual Mode, continuing with IPv6 without Dual Mode. Error: {e}");
                }

                // for windows sockets, there's a rare issue where when using
                // a server socket with multiple clients, if one of the clients
                // is closed, the single server socket throws exceptions when
                // sending/receiving. even if the socket is made for N clients.
                //
                // this actually happened to one of our users:
                // https://github.com/MirrorNetworking/Mirror/issues/3611
                //
                // here's the in-depth explanation & solution:
                //
                // "As you may be aware, if a host receives a packet for a UDP
                // port that is not currently bound, it may send back an ICMP
                // "Port Unreachable" message. Whether or not it does this is
                // dependent on the firewall, private/public settings, etc.
                // On localhost, however, it will pretty much always send this
                // packet back.
                //
                // Now, on Windows (and only on Windows), by default, a received
                // ICMP Port Unreachable message will close the UDP socket that
                // sent it; hence, the next time you try to receive on the
                // socket, it will throw an exception because the socket has
                // been closed by the OS.
                //
                // Obviously, this causes a headache in the multi-client,
                // single-server socket set-up you have here, but luckily there
                // is a fix:
                //
                // You need to utilise the not-often-required SIO_UDP_CONNRESET
                // Winsock control code, which turns off this built-in behaviour
                // of automatically closing the socket.
                //
                // Note that this ioctl code is only supported on Windows
                // (XP and later), not on Linux, since it is provided by the
                // Winsock extensions. Of course, since the described behavior
                // is only the default behavior on Windows, this omission is not
                // a major loss. If you are attempting to create a
                // cross-platform library, you should cordon this off as
                // Windows-specific code."
                // https://stackoverflow.com/questions/74327225/why-does-sending-via-a-udpclient-cause-subsequent-receiving-to-fail
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    const uint IOC_IN = 0x80000000U;
                    const uint IOC_VENDOR = 0x18000000U;
                    const int SIO_UDP_CONNRESET = unchecked((int)(IOC_IN | IOC_VENDOR | 12));
                    serverSocket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0x00 }, null);
                }
            }

            serverSocket.Bind(endPoint);

            return true;
        }
    }
}