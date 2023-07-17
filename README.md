# Edgegap Distributed Relay


The Edgegap Distributed Relay helps connect player hosted games.

The Relay is compatible with all game engines and all netcodes.</br>
This repository contains a detailed manual on how to implement it for your project.</br>
It also contains integrations for popular game engines & netcodes.</br>

Feel free to share implementations for other engines & netcodes by opening pull requests!
If you have trouble integrating the relay manually, please reach out or check one of the default integrations.

Fear not, this is very easy to implement!

---

## Relay Implementation Manual

### Requirements

Relay works with any game engine and any programming language.

The implementation guide is in C# for readability, but works just as well with C++, Java, Python, Rust, Go, etc.

The only requirement is access to low level UDP send/recv.

For example, some Unity netcodes may use native DLLs for their transports.</br>
The relay needs to be implemented on the socket layer, which may be difficult when using third-party native DLLs.</br>

### Overview

Unlike competing relays with large SDKs, the Edgegap relay is extremely easy to integrate.</br>
First, let's start with a high-level overview of the necessary components.

1. **Edgegap Login:** a **sessionID** and **userID** are needed in order to redirect game traffic over the relay. After matchmaking/lobby, those ids will be assigned to players from the Edgegap authentication. From the body of the API request, the "**sessionID**" is the sessions->authorization_token, and the "**userID**" is the session->session_users->authorization_token of the specific user.
2. **Redirect Traffic**: game clients and game servers no longer communicate with each other. Instead, both talk to the Relay directly. This is very easy to change, essentially all we need to do is connect to the relay and prepend some metadata to our messages.

The relay protocol is intentionally kept extremely simple, in order to target a wide range of game engines & netcodes.

### Relay Protocol

The Relay communicates over unreliable UDP messages.

- There is one UDP port for game servers.
- There is one UDP port for game clients.
- Max message size (MTU) is **1200** bytes.
- Relay adds up to 13 bytes of message overhead.
- Which leaves the max usable payload at 1200-13 = **1187** bytes.

Reliability, Fragmentation and Encryption differ between game engines & netcodes.</br>
Relay simply sends unreliable UDP messages.</br>
Each engine & netcode may implement its own logic on top of it.</br>

Note that your game may currently have the game server act as a UDP Server.</br>
Instead, both game client & game server need to act as UDP Clients.</br>
Which makes everything even easier.</br>

The following is an overview of the complete relay protocol.</br>
As promised, it's extremely simple.</br>

### Constants

UDP netcode always defines a max message size (MTU). Usually of around 1200-1400 bytes.</br>
Relay communication needs to prepend some metadata, so we need to define two different MTUs:

```cs
// relay prepends up to 13 bytes of meta-data
const int RELAY_OVERHEAD = 13;

// 1200 is the relay limit.
const int MTU = 1200;

// game may send up to MAX_PAYLOAD bytes
const int MAX_PAYLOAD = MTU - RELAY_OVERHEAD;
```

In other words, you may send up to **MAX_PAYLOAD** bytes.</br>
We then prepend some relay metadata, for a total of up to **1200** bytes</br>

Typically your netcode implements fragmentation on top of this, so you can send large messages.</br>

For example, a 10 KB message would be split into multiple MAX_PAYLOAD fragments, each fragment is sent to the relay with some metadata.</br>

Fragmentation, Reliability, and Encryption are implemented by the game engine's netcode transport. Some games may implement this themselves. Edgegap relay is transport agnostic, so it works with any game engine and netcode. In other words, it handles low-level MTU sized UDP messages, nothing else.

### Enums

Every message contains a message type byte:

```cs
enum MsgType : byte {
    PING = 1,
    DATA = 2
}
```

Some messages contain a state to notify the relay user:

```cs
enum State : byte {
    Disconnected = 0,   // until the user calls connect()
    Checking = 1,       // recently connected, validation in progress
    Valid = 2,          // validation succeeded
    Invalid = 3,        // validation rejected by tower
    SessionTimeout = 4, // session owner timed out
    Error = 5,          // other error
}
```

Both the game server and game client need to have a state variable:

```cs
// will be set by PING messages.
// show this in a GUI to indicate the connection state.
State state = State.Disconnected;
```

### Session Id & User Id

Before relaying game traffic, we need to get the sessionID and userID.</br>
The game server & clients will get this after the match is made / lobby is started.</br>

```cs
// from matchmaking/lobby
uint sessionId;
uint userId;
```

The ids will be serialized in **little endian** byte order.

---

## Game Server communication with Relay

Before we start, it's worth understanding that game server usually implement **UDP Servers**.</br>
In other words, they can handle multiple UDP client connects.</br>

When talking to a relay, the game server becomes a **UDP Client**.</br>
It only ever talks to the one relay.</br>

First, the game server needs to connect the UDP socket to the relay.</br>
After matchmaking / lobby, Edgegap will assign the relay address & port of a nearby relay.</br>

```cs
// from matchmaking/lobby
socket.Connect(relayAddress, relayServerPort)
```

While UDP sockets are connectionless, calling Connect() allows us to use Send() and Recv() instead of passing the address every time in SendTo() and RecvFrom().

The game server needs to send a Ping message roughly every 500 ms.</br>
The ping messages are used both as keep-alive and as authentication.</br>
Ping messages need to be sent immediately after connecting.</br>

```cs
// start sending pings immediately after connect, every 500 ms.
// faster sends make logins faster, but cost more bandwidth.
void SendPing() {
    // binary writer for convenience.
    // use what the language / netcode offer.
    writer = new NetworkWriter();
    writer.WriteUInt(userId);       // 4 bytes little endian
    writer.WriteUInt(sessionId);    // 4 bytes little endian
    writer.WriteByte(MsgType.PING);
    socket.Send(writer.Content());
}
```

The game server also sends Data messages. These are the messages that your game currently sends to the game client. Clients are identified by a connectionId. For security reasons, the relay will never expose a game client's IP address.

```cs
// messages that the game server sends to the game client with connectionId.
// wait for validation before sending them.
// connectionIds come from DATA messages.
void SendData(byte[] message, int connectionId) {
    // ensure max size
    if (message.Length > MAX_PAYLOAD) throw;

    // binary writer for convenience.
    // use what the language / netcode offer.
    writer = new NetworkWriter();
    writer.WriteUInt(userId);       // 4 bytes little endian
    writer.WriteUInt(sessionId);    // 4 bytes little endian
    writer.WriteByte(MsgType.DATA);
    writer.WriteUInt(connectionId); // 4 bytes little endian
    writer.WriteBytes(bytes);
    socket.Send(writer.Content());
}
```

The game server can receive two types of messages from the relay:

```cs
void ProcessMessage() {
    // check for new udp message.
    byte[] buffer = new byte[MTU];
    int size = socket.Receive(buffer);

    // binary reader to parse 'size' bytes.
    // use what the language / netcode offer.
    reader = new NetworkReader(buffer, size);

    // read message type
    MsgType msgType = reader.ReadByte();
    switch (msgType) {
        case MsgType.PING: {
            // read connection state.
            // after 1-2s, this will turn to Valid.
            // then we can send DATA messages.
            State state = reader.ReadByte();
            break;
        }
        case MsgType.DATA: {
            // connectionId indicates which game client this is from.
            uint connectionId = reader.ReadUInt();
            // remaining bytes are a game message
            byte[] payload = reader.ReadBytes(reader.remaining);
            // handle the game message as before
            HandleMessage(payload, connectionId);
            break;
        }
    }
}
```

That's it for the game server. Pretty simple.

---

## Game Client communication with Relay

First, the game client needs to connect the UDP socket to the relay.</br>
After matchmaking / lobby, Edgegap will assign the relay address & port of a nearby relay.</br>

```cs
// from matchmaking/lobby
socket.Connect(relayAddress, relayClientPort)
```

While UDP sockets are connectionless, calling Connect() allows us to use Send() and Recv() instead of passing the address every time in SendTo() and RecvFrom().

The game client needs to send a Ping message roughly every 500 ms.</br>
The ping messages are used both as keep-alive and as authentication.</br>
Ping messages need to be sent immediately after connecting.</br>

```cs
// start sending pings immediately after connecting, every 500 ms.
// faster sends make logins faster but cost more bandwidth.
void SendPing() {
    // binary writer for convenience.
    // use what the language / netcode offer.
    writer = new NetworkWriter();
    writer.WriteUInt(userId);       // 4 bytes little endian
    writer.WriteUInt(sessionId);    // 4 bytes little endian
    writer.WriteByte(MsgType.PING);
    socket.Send(writer.Content());
}
```

The game client also sends Data messages. These are the messages that your game currently sends to the game server:

```cs
// messages that the game client sends to the game server.
// wait for validation before sending them.
void SendData(byte[] message) {
    // ensure max size
    if (message.Length > MAX_PAYLOAD) throw;

    // binary writer for convenience.
    // use what the language / netcode offer.
    writer = new NetworkWriter();
    writer.WriteUInt(userId);       // 4 bytes little endian
    writer.WriteUInt(sessionId);    // 4 bytes little endian
    writer.WriteByte(MsgType.DATA);
    writer.WriteBytes(bytes);
    socket.Send(writer.Content());
}
```

The game client can receive two types of messages from the relay:

```cs
void ProcessMessage() {
    // check for new UDP message.
    byte[] buffer = new byte[MTU];
    int size = socket.Receive(buffer);

    // binary reader to parse 'size' bytes.
    // use what the language / netcode offer.
    reader = new NetworkReader(buffer, size);

    // read message type
    MsgType msgType = reader.ReadByte();
    switch (msgType) {
        case MsgType.PING: {
            // read connection state.
            // after 1-2s, this will turn to Valid.
            // then we can send DATA messages.
            State state = reader.ReadByte();
            break;
        }
        case MsgType.DATA: {
            // remaining bytes are a game message
            byte[] payload = reader.ReadBytes(reader.remaining);
            // handle the game message as before
            OnMessage(payload);
            break;
        }
    }
}
```

That's it for the game client. Pretty simple as well.
