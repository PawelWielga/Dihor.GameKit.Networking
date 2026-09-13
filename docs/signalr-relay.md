# SignalR relay transport

Issue `[18]` adds optional backend-assisted connectivity as another PartyGameKit communication transport. It does not add a cloud-owned party, lobby or game-session runtime.

## Boundary

The relay understands only communication concepts:

- a backend SignalR connection;
- a PartyGameKit `ConnectionId` assigned to a remote peer connection;
- an optional technical `ChannelId` used to isolate relay traffic;
- opaque byte payloads;
- targeted delivery, broadcast and disconnect lifecycle.

The relay must not interpret or own:

- players or player capacity;
- TV/controller/shared-screen roles;
- PartyBeam parties or game sessions;
- authority/game-master policy;
- product join codes;
- game state, commands, snapshots or scoring.

Product code may map one of its own identifiers to a `ChannelId`, but PartyGameKit treats that value only as a routing scope.

## Shape

The transport follows the same split already used by LAN:

```text
consumer communication runtime
        │
        ▼
SignalRRelayTransport : IMessageTransport
        │
        │ SignalR
        ▼
SignalRRelayHub / backend relay
        │
        ▼
SignalRRelayClient(s)
```

`SignalRRelayTransport` is the listener-side abstraction seen by the consumer. It exposes remote relay clients as ordinary PartyGameKit `ConnectionId` values through `TransportConnectionOpened`, `TransportMessageReceived`, `TransportConnectionClosed` and `TransportFaulted` events.

`SignalRRelayClient` is the single-connection counterpart, analogous to `LanWebSocketClient`.

The backend hub forwards bytes. It does not parse PartyGameKit protocol-v2 envelopes. Connect/resume/application messages therefore keep exactly the same schema as the LAN path.

## Relay routing

A relay endpoint is identified by a technical `ChannelId`. One `SignalRRelayTransport` owns the listener registration for that channel at a time. Remote clients attach to the same channel and receive an opaque transport `ConnectionId`.

The hub maintains only ephemeral routing bindings required to deliver traffic. It does not persist application sessions or participant records.

Independent channels must be isolated: traffic for channel A must never be visible to channel B.

## Lifecycle

1. `SignalRRelayTransport` connects to the backend and registers its channel as the current listener.
2. `SignalRRelayClient` connects to the backend and attaches to that channel.
3. The relay assigns/returns a transient `ConnectionId` and notifies the listener transport.
4. Client bytes become `TransportMessageReceived` events on the listener.
5. Listener `SendAsync` targets one relay client; `BroadcastAsync` targets all clients in its channel.
6. Disconnects become `TransportConnectionClosed` events.
7. PartyGameKit protocol v2 and `ConnectionContinuityCoordinator` remain responsible for stable `PeerId` resume semantics above this transport, just as with LAN.

SignalR automatic reconnection may restore the physical backend connection, but it must not silently invent application/product continuity. Stable PartyGameKit peer continuity still requires the protocol-v2 resume credential.

## Backend availability

SignalR is optional. No LAN project depends on the SignalR package, and no direct-LAN flow requires Internet or a deployed relay.

## Security assumptions

The first implementation is transport infrastructure, not an Internet-ready authentication system. Deployment documentation must make explicit that:

- TLS (`https`/`wss`) should be used outside trusted development networks;
- production deployments should authenticate/authorize listener and client registrations before exposing the relay publicly;
- `ChannelId` is routing metadata, not an authorization secret;
- payload size limits must be enforced;
- relay state is ephemeral and should be bounded/cleaned up on disconnect;
- application payloads remain opaque to the relay and require consumer-level validation.

## Validation target

Integration tests use an in-process ASP.NET Core server and prove:

- multiple generic clients;
- listener/client connection and disconnect events;
- client-to-listener opaque payloads;
- targeted listener-to-client delivery;
- channel-local broadcast;
- channel isolation;
- protocol mismatch handling above the transport;
- replacement transport connections can participate in the same neutral protocol-v2 resume flow;
- clean server/client disposal.

The neutral `CommunicationDemo` should be able to execute the same application-message scenario over LAN or SignalR without changing the application payload schema.