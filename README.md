# PartyGameKit

PartyGameKit is a reusable multiplayer foundation for **shared-screen party games**.

The primary interaction model is:

- a TV, laptop or browser is the shared game screen,
- players join from their phones,
- phones act as controllers and private player screens,
- the same game can run locally over LAN or use an optional backend/cloud path,
- game logic must not depend on SignalR, WebRTC, sockets or any other concrete transport.

The first real-world source of requirements is the existing **Państwa Miasta** game. A second, mechanically different dungeon-style sample will validate that the extracted API is genuinely reusable and not a renamed copy of one game's networking layer.

## Goals

PartyGameKit should incrementally provide reusable building blocks for:

- rooms/sessions and join descriptors,
- stable player identity distinct from network connection identity,
- join, leave, disconnect and reconnect,
- logical host/authority handling,
- public/shared-screen and private player state delivery,
- replaceable transports,
- backend-free LAN networking,
- later backend-assisted networking,
- later WebRTC/direct peer transport where useful,
- eventual automatic transport selection only after the individual transports are reliable.

Automatic host migration is not a v0.1 guarantee. The current Państwa Miasta implementation proves host-loss detection and reconnect, while host migration remains experimental.

## Non-goals

The generic library must **not** contain game-specific concepts such as categories, answers, letters, dungeon rooms, monsters, loot, tiles, attacks or scoring/combat rules.

PartyGameKit is also not a general-purpose MMO/network engine. The initial focus is couch/party games with one shared screen and multiple personal controllers.

## Design principles

Game code owns its rules, commands and state schema. PartyGameKit owns reusable session, protocol, replication and transport semantics.

```text
game-owned rules/state
        │
        ▼
session / authority
        │
        ├── versioned protocol + snapshots
        │
        └── transport abstraction
                ├── LAN
                ├── SignalR / cloud later
                └── WebRTC later
```

A stable `PlayerId` is not a `ConnectionId`. A dropped connection is not automatically a permanent leave. Host/authority is logical session state rather than a property of one WebSocket.

## Cross-language contract

PartyGameKit is centered on .NET, but Państwa Miasta is Flutter/Dart and PartyBeam will use browser/TypeScript clients.

The v0.1 compatibility boundary is therefore a **versioned language-neutral JSON protocol plus canonical JSON fixtures**. C#, Dart and TypeScript implement thin local protocol models and test against the same fixtures.

A NuGet package is not treated as the cross-language contract, and game engines are not duplicated across languages.

See [Extraction from Państwa Miasta](docs/extraction-from-panstwa-miasta.md) for the full decision and evidence.

## Initial repository shape

The first implementation slice deliberately stays small:

```text
PartyGameKit/
├── src/
│   ├── PartyGameKit.Core/
│   ├── PartyGameKit.Protocol/
│   └── PartyGameKit.Transport.Abstractions/
├── tests/
│   ├── PartyGameKit.Core.Tests/
│   ├── PartyGameKit.Protocol.Tests/
│   └── PartyGameKit.Transport.Tests/
├── protocol/
│   └── fixtures/
├── samples/
└── docs/
```

Concrete LAN, Dart and TypeScript packages are added only by the later issues that need them. SignalR and WebRTC are post-v0.1 transports.

## Developer setup

Requirements:

- .NET 10 SDK; `global.json` allows rolling forward within installed .NET 10 feature bands.

Build the complete solution:

```bash
dotnet build PartyGameKit.slnx --configuration Release
```

Run the complete test suite with one command:

```bash
dotnet test PartyGameKit.slnx --configuration Release
```

The repository enables nullable reference types, deterministic builds, .NET analyzers, code-style checks during build and warnings-as-errors. CI runs restore, Release build and the complete test suite for every pull request and for pushes to `main`.

## LAN host constraint

The first direct LAN transport will use WebSocket. Its listener must run in a server-capable local runtime such as a .NET/native/desktop/TV process or companion process.

A pure browser/PWA can be the shared-screen or phone client, but it cannot accept arbitrary inbound WebSocket connections. Pure-browser direct hosting is deferred to a later peer/WebRTC transport.

## Documentation

- [Extraction from Państwa Miasta](docs/extraction-from-panstwa-miasta.md)
- [Architecture](docs/architecture.md)
- [Networking](docs/networking.md)
- [Game session model](docs/game-session-model.md)
- [Roadmap](docs/roadmap.md)

## Current status

The extraction boundary and minimal .NET 10 repository/test infrastructure are in place. Implementation continues strictly in `[NN]` order from the tracker; protocol and identity primitives start in `[03]`.
