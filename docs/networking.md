# Networking

PartyGameKit networking is intentionally product-neutral. It connects peers and transports messages; it does not model players, parties or game sessions.

See [Communication boundary](communication-boundary.md).

## Core communication flow

```text
consumer application
       │ opaque application message
       ▼
PartyGameKit protocol/control
       │
       ▼
transport abstraction
       │
       ├── in-memory
       ├── LAN WebSocket
       ├── SignalR relay
       └── WebRTC later
```

## Transport abstraction

A transport exposes only communication concerns:

- connection open/close/error events;
- incoming opaque payloads;
- targeted send;
- broadcast/multicast where supported;
- disconnect/stop/disposal;
- cancellation;
- technology-neutral errors/diagnostics.

`IMessageTransport` is the listener-side abstraction used by both direct LAN and SignalR relay transports. A transport may have a technology-specific single-peer client counterpart, such as `LanWebSocketClient` or `SignalRRelayClient`, without changing the application protocol.

`ConnectionId` identifies a transient network connection. It is not a player id.

## Logical peer identity

Reconnect may require an optional stable `PeerId` that outlives one connection.

```text
PeerId P1
  ├── ConnectionId C1   (old/disconnected)
  └── ConnectionId C2   (replacement/resumed)
```

PartyGameKit may validate a resume credential and rebind C2 to P1. The consumer decides whether P1 represents a player, TV, controller, server or something else.

The same continuity coordinator can be used when the physical path is LAN or SignalR. The relay backend itself never owns `PeerId` or resume credentials.

## Routing scope

Some transports require an opaque routing scope/channel to isolate delivery. `ChannelId` is communication-only and does not imply lobby/game lifecycle, capacity or authority.

For SignalR, the backend registry maps a `ChannelId` to one active listener and its transient client connections. That registry is routing state, not a PartyBeam party/session store.

## Application messages

Application payloads are opaque to PartyGameKit.

A consumer may transport:

- a Countries & Cities answer command;
- a PartyBeam controller action;
- a game snapshot;
- a collaborative document operation;
- any other consumer-defined payload.

None of those schemas become PartyGameKit base API.

The same `application.message` bytes can be sent over LAN WebSocket or SignalR relay without changing the consumer schema.

## Connection health

Heartbeat/timeout reports connectivity. It does not trigger player leave, game pause or authority migration.

Consumers subscribe to communication state and apply their own product policies.

## LAN first

Direct LAN WebSocket remains the backend-free transport and works without Internet/cloud after local dependencies are available.

The listening side requires a server-capable runtime. This is a transport constraint, not a generic host role.

UDP LAN discovery is optional and remains independent from backend-assisted connectivity.

## Optional SignalR relay

`PartyGameKit.Transport.SignalR` and `PartyGameKit.Transport.SignalR.Server` provide an optional path for peers that cannot use direct LAN communication or are on different networks.

The relay:

- carries the same protocol-v2 handshake and opaque application payloads;
- uses technical `ChannelId` routing only;
- exposes targeted send and broadcast through `IMessageTransport`;
- removes transient bindings when clients/listeners disconnect;
- leaves stable `PeerId` continuity to the PartyGameKit protocol/core layer;
- does not implement PartyBeam parties, player admission, product join codes or game state.

SignalR deployment is optional. See [SignalR relay](signalr-relay.md) for server configuration and security assumptions.

## Future transports

WebRTC may be added as another communication path after SignalR. It must carry the same opaque consumer messages without introducing session/player semantics.

Automatic transport selection later chooses a communication path only; it does not make product lifecycle decisions.
