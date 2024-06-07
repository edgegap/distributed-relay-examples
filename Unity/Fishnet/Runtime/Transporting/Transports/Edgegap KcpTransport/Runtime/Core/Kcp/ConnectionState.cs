namespace kcp2k.Edgegap
{
    internal enum ConnectionState : byte
    {
        Disconnected = 0,   // until the user calls connect()
        Checking = 1,       // recently connected, validation in progress
        Valid = 2,          // validation succeeded
        Invalid = 3,        // validation rejected by tower
        SessionTimeout = 4, // session owner timed out
        Error = 5,          // other error
    }
}