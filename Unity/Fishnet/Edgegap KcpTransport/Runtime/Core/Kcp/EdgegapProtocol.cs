namespace kcp2k.Edgegap
{
    internal class EdgegapProtocol
    {
        public const int MTU = 1200 - Overhead;

        // MTU: relay adds up to 13 bytes of metadata in the worst case.
        public const int Overhead = 13;

        // ping interval should be between 100 ms and 1 second.
        // faster ping gives faster authentication, but higher bandwidth.
        public const float PingInterval = 0.5f;
    }
}