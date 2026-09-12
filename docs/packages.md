# Package boundaries

PartyGameKit keeps packages small and aligned with behavior proven by Państwa Miasta, Shared Counter and Dungeon Prototype.

## .NET packages

### `PartyGameKit.Core`

Transport-independent multiplayer primitives: room/player/connection/authority identities, `RoomSession`, lifecycle state, authoritative snapshots and continuity/reconnect coordination. It must not reference WebSocket, UDP, SignalR, WebRTC, UI frameworks or game-domain concepts.

### `PartyGameKit.Protocol`

Protocol-v1 envelopes, infrastructure messages, JSON codecs, canonical `JoinDescriptor` encoding and discovery-announcement encoding. Depends only on Core.

### `PartyGameKit.Transport.Abstractions`

Technology-neutral connection/message events and `IGameTransport`. Depends only on Core.

### `PartyGameKit.Transport.Lan`

Concrete direct-LAN WebSocket host/client implementation. Depends on Core, Protocol and Transport.Abstractions. It uses ASP.NET Core/Kestrel and is optional for consumers that use another transport.

### `PartyGameKit.Transport.SignalR`

Optional backend-assisted SignalR transport. Depends on Core, Protocol and Transport.Abstractions plus ASP.NET Core SignalR. It provides process-local room/join-code routing, `SignalRRoomTransport : IGameTransport`, ASP.NET Core registration/mapping helpers and a .NET SignalR client. Backend routing stays outside Core and game state remains application-owned.

### `PartyGameKit.Discovery.Lan`

Optional UDP discovery for finding local sessions. Depends on Core and Protocol. Discovery is not required when a consumer already has a join descriptor.

### `PartyGameKit.Transport.InMemory`

Deterministic transport intended for consumer tests, samples and simulations. Depends on Core and Transport.Abstractions. It is packaged separately so production applications do not need to take it as a runtime dependency.

## Browser package

### `@partygamekit/client`

Framework-agnostic ES module for browser player/shared-screen clients. It implements protocol v1, join/rejoin/leave, heartbeat, reconnect, persistent browser identity and public/private projection filtering. LAN uses the native WebSocket path; SignalR can use the exported `createSignalRSocket` adapter with the official browser SignalR runtime. It has no React dependency and contains no PartyBeam game logic.

## Dart package

### `partygamekit_protocol`

Small Dart implementation of protocol v1 and portable join descriptors. It exists for cross-language compatibility and Flutter/Dart consumers that need the PartyGameKit wire contract. The current package does not provide a Dart transport implementation.

The repository keeps `publish_to: none` for the preview so it can be consumed from the tagged Git repository without implying a pub.dev release.

## Dependency graph

```text
PartyGameKit.Core
├── PartyGameKit.Protocol
├── PartyGameKit.Transport.Abstractions
│   ├── PartyGameKit.Transport.InMemory
│   └── PartyGameKit.Protocol
│       ├── PartyGameKit.Transport.Lan
│       └── PartyGameKit.Transport.SignalR
└── PartyGameKit.Protocol
    └── PartyGameKit.Discovery.Lan

protocol v1 fixtures
├── C# Protocol
├── @partygamekit/client
└── partygamekit_protocol (Dart)
```

The LAN and SignalR packages are siblings behind `IGameTransport`. A consumer does not need SignalR to run LAN mode, and adding SignalR does not change the wire protocol or Core session model.

## What is deliberately not a package

Shared Counter and Dungeon Prototype remain validation samples. Their counters, maps, characters, turns, action points, enemies, inventory and commands are application-owned and are not reusable PartyGameKit APIs.

For backend deployment and room-routing details see [SignalR backend transport](signalr.md).
