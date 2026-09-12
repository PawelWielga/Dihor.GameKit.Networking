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
       ├── SignalR later
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

`ConnectionId` identifies a transient network connection. It is not a player id.

The historical name `IGameTransport` will be neutralized in `[16]` because transports are useful outside games.

## Logical peer identity

Reconnect may require an optional stable `PeerId` that outlives one connection.

```text
PeerId P1
  ├── ConnectionId C1   (old/disconnected)
  └── ConnectionId C2   (replacement/resumed)
```

PartyGameKit may validate a resume credential and rebind C2 to P1. The consumer decides whether P1 represents a player, TV, controller, server or something else.

## Routing scope

Some transports may require an opaque routing scope/channel to isolate delivery. If retained, that id is communication-only and does not imply lobby/game lifecycle, capacity or authority.

## Application messages

Application payloads are opaque to PartyGameKit.

A consumer may transport:

- a Countries & Cities answer command;
- a PartyBeam controller action;
- a game snapshot;
- a collaborative document operation;
- any other consumer-defined payload.

None of those schemas become PartyGameKit base API.

## Connection health

Heartbeat/timeout reports connectivity. It does not trigger player leave, game pause or authority migration.

Consumers subscribe to communication state and apply their own product policies.

## LAN first

Direct LAN WebSocket remains the first real transport and must work without Internet/cloud after local dependencies are available.

The listening side requires a server-capable runtime. This is a transport constraint, not a generic host role.

## Future transports

SignalR/backend relay and WebRTC are added only after the communication-only refactor is complete. They must carry the same opaque consumer messages without introducing session/player semantics.

Automatic transport selection later chooses a communication path only; it does not make product lifecycle decisions.