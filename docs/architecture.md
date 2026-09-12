# Architecture

## Purpose

PartyGameKit provides reusable multiplayer infrastructure for games where one device may act as a shared screen while phones act as controllers or private player screens.

The central architectural rule is that **game logic is transport-agnostic**.

A game must not know whether communication uses local WebSocket, SignalR, WebRTC, an in-memory test transport or another future adapter.

The extraction boundary and cross-language decision are documented in [Extraction from Państwa Miasta](extraction-from-panstwa-miasta.md).

## High-level model

```text
Game-owned rules/state
        │
        ▼
Session / authority orchestration
        │
        ├── versioned protocol + snapshots
        │
        └── transport abstraction
                │
                ├── in-memory tests
                ├── LAN WebSocket
                ├── SignalR later
                └── WebRTC later
```

Display, host, authority, player identity and network connection are related but distinct concepts.

A common local topology may place the authority on the same machine as the shared screen, but the generic model must not require that coincidence.

## Layers

### PartyGameKit.Core

Contains reusable multiplayer/session domain behavior only.

Expected v0.1 responsibilities are justified by current Państwa Miasta behavior:

- room/session identity and lifecycle;
- stable `PlayerId`;
- transient connection binding without treating `ConnectionId` as player identity;
- player admission, capacity, leave, disconnect and rejoin semantics;
- logical host/authority identity;
- participant/client roles where a non-player shared screen must be represented;
- deterministic lifecycle results/events needed by orchestration.

Core must not reference SignalR, WebRTC, HTTP, sockets, IP addresses, ports, UDP, Flutter, Android, React or a concrete game.

Core also must not contain game concepts such as categories, answers, letters, monsters, loot, tiles, attacks or a concrete game's phase enum.

Automatic host migration is **not** part of the v0.1 Core contract. Host loss may be represented, but the production reference does not yet prove a portable host-transfer policy.

### PartyGameKit.Protocol

Defines the language-neutral wire contract shared by C#, Dart and TypeScript implementations.

Responsibilities include only infrastructure-level contracts such as:

- protocol version;
- stable serialized names;
- identity primitives;
- client role;
- join/accept/reject/leave/rejoin messages;
- heartbeat/presence metadata;
- snapshot envelope/sequence metadata;
- portable join-descriptor data when introduced by the LAN roadmap;
- a generic application/game payload boundary without understanding payload meaning.

Canonical JSON fixtures under `protocol/fixtures/` are the compatibility source of truth. CLR serialization behavior is not the specification.

Game-specific messages are defined by the consuming game and may be carried through a generic envelope without PartyGameKit interpreting them.

### PartyGameKit.Transport.Abstractions

Defines the minimum technology-neutral communication contracts required by session orchestration.

The API will be introduced only after the lifecycle/protocol behavior that uses it is concrete. It must not leak WebSocket, SignalR, WebRTC or platform socket types.

The existing Państwa Miasta transport interface is evidence that this separation works, but PartyGameKit does not copy that Dart API verbatim because the current transport also exposes room/player concepts that belong in the session model.

### Transport implementations

Concrete adapters are added incrementally after their abstractions are proven:

- `PartyGameKit.Transport.Lan` for direct LAN WebSocket;
- SignalR only in the post-v0.1 backend issue;
- WebRTC only after SignalR/signaling support exists and a latency-sensitive sample requires it.

Transport packages implement the shared abstractions and never define game rules.

### Client SDKs

Dart and TypeScript clients implement the same wire protocol rather than consuming CLR types.

Planned roles:

- Dart compatibility adapter for the existing Państwa Miasta application;
- TypeScript browser SDK for PartyBeam phones and shared screens;
- framework-specific UI adapters only if concrete product use proves they are useful.

React, Flutter state management and platform persistence are not Core concerns.

## Cross-language boundary

The v0.1 interoperability model is deliberately small:

```text
canonical JSON fixtures + documented version rules
                     │
        ┌────────────┼────────────┐
        ▼            ▼            ▼
       C#           Dart      TypeScript
```

Each language has thin local models/parsers and tests the exact same fixture files.

This avoids two bad alternatives:

1. pretending Flutter can directly consume a NuGet package;
2. duplicating the entire game/session engine in multiple languages.

Generated schemas/code can be reconsidered later if maintaining the small explicit contract becomes a real problem.

## Authority

Authority is a logical session concern, not a property of a socket.

The current Państwa Miasta behavior is host-authoritative: clients send intent and the host owns canonical game state. PartyGameKit preserves that semantic while keeping authority identity independent from the active transport connection.

A reconnect may attach a new `ConnectionId` to the same stable player/session identity without changing who the logical participant is.

Future deployments may use:

```text
local companion/TV process = authority
browser TV                  = shared-screen client
```

or later:

```text
backend = authority
TV      = shared-screen client
```

The game code should not change because the authority is reached over a different transport.

## Game-owned commands and state

PartyGameKit standardizes only infrastructure messages needed to establish and maintain a session.

A concrete game owns its action vocabulary and state schema. For example, one game may define typed-answer actions while another defines movement/combat actions. These concepts must not become PartyGameKit Core APIs.

Conceptually:

```text
controller intent (game-owned payload)
             │
             ▼
        game authority
             │
             ▼
        game-owned rules
             │
             ▼
authoritative game state/projection
             │
             ▼
PartyGameKit snapshot envelope + transport
```

PartyGameKit may route, correlate, target and sequence opaque application payloads. It does not validate their game meaning.

## Public and private projections

Shared-screen games need different views of one authoritative game state.

PartyGameKit owns targeting/delivery semantics, not the content:

```text
game-owned canonical state
          │
          ├── game builds public/shared-screen payload
          ├── game builds private payload for Player A
          └── game builds private payload for Player B
                         │
                         ▼
              generic snapshot metadata
```

This prevents the framework from knowing cards, inventory, answers or other hidden mechanics while still making it possible to avoid broadcasting private data to every client.

## Player and connection identity

A stable player identity must survive temporary connection loss.

```text
PlayerId      = stable identity inside the session
ConnectionId  = current transient network connection
```

A dropped connection marks presence/disconnect state. It does not automatically perform a permanent leave or erase game-owned state.

Reconnect rebinds a new connection to the existing player after ownership is validated. A reconnect credential is separate from both public player identity and the network connection.

## LAN host constraint

The first backend-free LAN implementation uses a WebSocket listener. Therefore its authority host must run in a server-capable runtime such as a .NET/native/desktop/TV process or local companion process.

A pure browser/PWA can initiate WebSocket connections but cannot act as that listener. A browser can still be the PartyBeam shared-screen client.

Pure-browser direct hosting belongs to a later peer-transport/WebRTC design, not the v0.1 LAN WebSocket contract.

## Reuse boundary

The reusable behavior proven in Państwa Miasta includes:

- room/session lifecycle;
- stable player identity;
- explicit join/leave;
- disconnect distinct from leave;
- reconnect without duplicate players;
- host-authoritative state;
- heartbeat and timeout policy;
- monotonic snapshot sequencing and stale rejection;
- LAN discovery separated from gameplay transport;
- backend-free local transport.

It does **not** justify copying:

- `LocalLanGameController` as a giant framework facade;
- `CountriesCitiesGameEngine` or its models;
- Flutter `ChangeNotifier`/SharedPreferences abstractions;
- Android foreground services;
- the current mixed Countries & Cities snapshot schema;
- experimental automatic host migration.

## Initial package strategy

Issue `[02]` should bootstrap only:

```text
src/
├── PartyGameKit.Core/
├── PartyGameKit.Protocol/
└── PartyGameKit.Transport.Abstractions/

tests/
├── PartyGameKit.Core.Tests/
├── PartyGameKit.Protocol.Tests/
└── PartyGameKit.Transport.Tests/

protocol/
└── fixtures/
```

Concrete LAN, Dart and TypeScript packages arrive in the later issues that actually need them. NuGet/npm/Dart prerelease packaging is stabilized only after both the shared-screen sample and dungeon validation pass.
