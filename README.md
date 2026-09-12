# PartyGameKit

PartyGameKit is a reusable **multiplayer communication/networking library** extracted from networking behavior proven in [Państwa Miasta](https://github.com/PawelWielga/panstwa-miasta).

It is intended to be used by [PartyBeam](https://github.com/PawelWielga/PartyBeam), Państwa Miasta and future multiplayer products without forcing them into one player/host/shared-screen model.

```text
PartyBeam / Państwa Miasta / future multiplayer products
                │
                │ players, roles, parties, game sessions,
                │ authority policy, state and game rules
                ▼
          PartyGameKit
      communication/networking
                │
        transports + discovery
                │
     LAN / SignalR / WebRTC
```

## Boundary correction

`0.1.0-preview.1` proved useful LAN transport, discovery, interoperability and reconnect behavior, but its public API also took ownership of product concepts such as `PlayerId`, `ClientRole`, `RoomSession`, authority and public/private game-state projections.

That boundary is being corrected in ordered issues `[15]`–`[17]`. Breaking prerelease changes are intentional.

The architecture decision is documented in [Communication boundary](docs/communication-boundary.md).

### PartyGameKit owns

- transport-neutral connection APIs;
- connection open/close/error lifecycle;
- send/receive and targeted/broadcast delivery primitives;
- transient `ConnectionId`;
- an optional neutral stable peer identity used for reconnect/resume;
- heartbeat/connectivity detection and resume mechanics;
- a versioned language-neutral communication envelope;
- opaque application payload transport;
- generic ordering/deduplication helpers where useful;
- technical connection descriptors;
- direct LAN WebSocket transport;
- LAN discovery;
- C#, TypeScript and Dart compatibility for the communication contract.

### Consumers own

PartyGameKit does **not** decide:

- who is a player;
- player capacity/admission;
- host/shared-screen/controller/spectator roles;
- lobby/party/game-session lifecycle;
- authority or coordinator policy;
- game start/pause/end behavior;
- score or game state;
- public/private/shared-screen state projections;
- PartyBeam TV/pilot/controller UX;
- game commands, phases or rules.

A consumer with no concept of players must be able to use the base library.

## Current prerelease status

The released `0.1.0-preview.1` API represents the historical v0.1 implementation and is **not** the final target boundary. Do not build new product architecture around its room/player/session APIs.

The ordered backlog is tracked in GitHub issue `#2`:

- `[15]` define and document the corrected communication-only boundary;
- `[16]` refactor Core/protocol/transports to that boundary;
- `[17]` align TypeScript, Dart, samples, packaging and interoperability;
- only then add SignalR, WebRTC and automatic fallback.

## Target package direction

The exact package names are finalized in `[16]`, but responsibilities are moving toward:

- communication identities/protocol primitives;
- transport abstractions;
- in-memory reference transport;
- LAN WebSocket transport;
- LAN discovery;
- browser and Dart communication SDKs.

`PartyGameKit.Core` is not protected as a package boundary. It may be split, renamed, collapsed or removed if that produces a cleaner communication-only dependency graph.

## Cross-language contract

PartyGameKit uses a versioned language-neutral wire contract and canonical fixtures rather than pretending Flutter/Dart can consume NuGet directly.

```text
canonical protocol fixtures
          │
    ┌─────┼─────┐
    ▼     ▼     ▼
   C#    Dart   TypeScript
```

The target protocol distinguishes PartyGameKit control messages from opaque consumer/application messages. PartyGameKit transports payloads without understanding player roles or game meaning.

## LAN and discovery

Direct LAN WebSocket transport remains a core capability and works without Internet/cloud. The listener must run in a server-capable runtime.

LAN discovery remains optional convenience infrastructure. A valid technical connection descriptor must still allow direct connection when UDP discovery is blocked or disabled.

## Reference consumers

The existing Shared Counter and Dungeon Prototype samples remain useful as **consumers** that demonstrate composition. Their player, authority, shared-screen and game-state concepts are application-owned and must not define the generic PartyGameKit API.

Issue `[17]` will add or convert a sample into a deliberately neutral communication demonstration with generic peers, opaque message exchange, targeted/broadcast delivery, reconnect and LAN discovery/direct connection.

## Developer setup

Requirements:

- .NET 10 SDK;
- Node.js 22 for the TypeScript SDK;
- Dart stable for Dart conformance tests.

Build and test .NET:

```bash
dotnet restore PartyGameKit.slnx
dotnet build PartyGameKit.slnx --configuration Release --no-restore
dotnet test PartyGameKit.slnx --configuration Release --no-build
```

Build and test TypeScript:

```bash
npm install --prefix clients/typescript --no-audit --no-fund
npm test --prefix clients/typescript
```

Validate Dart:

```bash
cd interop/dart
dart pub get
dart analyze
dart test
```

## Documentation

- [Communication boundary](docs/communication-boundary.md)
- [Architecture](docs/architecture.md)
- [Package boundaries](docs/packages.md)
- [Public API classification](docs/public-api.md)
- [Extraction from Państwa Miasta](docs/extraction-from-panstwa-miasta.md)
- [Roadmap](docs/roadmap.md)
- [Compatibility matrix](docs/compatibility.md)
- [Versioning](docs/versioning.md)
- [LAN WebSocket](docs/lan-websocket.md)
- [LAN discovery](docs/discovery.md)
- [Changelog](CHANGELOG.md)

## Design invariant

Future transports may change **how** messages move. They must not change **what a player, host, TV, party or game means**, because those concepts belong to the consumer, not PartyGameKit.