# PartyGameKit

PartyGameKit is a reusable **multiplayer communication/networking library** extracted from networking behavior proven in [Państwa Miasta](https://github.com/PawelWielga/panstwa-miasta).

It is infrastructure below [PartyBeam](https://github.com/PawelWielga/PartyBeam), Państwa Miasta and future multiplayer products. It does not require consumers to adopt a player/host/shared-screen model.

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
      LAN / future transports
```

## 0.2 communication boundary

`0.1.0-preview.1` proved the LAN transport, discovery and reconnect approach, but also exposed product concepts such as players, client roles, room sessions, authority and public/private game-state projections.

`0.2.0-preview.1` corrects that boundary. Protocol v2 and the base APIs use communication-neutral concepts:

- transient `ConnectionId`;
- optional stable `PeerId` for resume/reconnect;
- optional technical `ChannelId` for routing scope;
- `ConnectionDescriptor` for transport/endpoint discovery and direct connection;
- connect/resume/heartbeat/disconnect control messages;
- opaque `application.message` payloads owned by the consumer;
- transport-neutral send/receive, targeted delivery and broadcast;
- deterministic connection continuity and generic ordering helpers;
- direct LAN WebSocket transport and optional UDP LAN discovery.

The migration from v0.1 is intentionally breaking. See [Migration 0.1 → 0.2](docs/migration-0.1-to-0.2.md).

## What PartyGameKit does not own

PartyGameKit does **not** decide:

- who is a player;
- host, TV, controller, spectator or other product roles;
- player capacity/admission;
- lobby, party or game-session lifecycle;
- authority/coordinator policy;
- game start/pause/end behavior;
- score or game state;
- public/private/shared-screen projections;
- PartyBeam UX or game catalog behavior;
- game commands, phases or rules.

A consumer with no concept of players can use the library successfully.

## Packages

The .NET prerelease is split by communication responsibility:

- `PartyGameKit.Core` — neutral identity, connection continuity and ordering primitives;
- `PartyGameKit.Protocol` — protocol v2 envelopes, connection descriptors and codecs;
- `PartyGameKit.Transport.Abstractions` — transport-neutral message contracts;
- `PartyGameKit.Transport.InMemory` — deterministic reference/test transport;
- `PartyGameKit.Transport.Lan` — direct LAN WebSocket transport;
- `PartyGameKit.Discovery.Lan` — optional UDP LAN discovery.

Browser consumers use `@partygamekit/client`. Flutter/Dart consumers can use the small `interop/dart` protocol package when they need canonical protocol compatibility without a duplicated game/session engine.

## Cross-language contract

Protocol v2 uses canonical fixtures shared by C#, Dart and TypeScript:

```text
protocol/fixtures/v2-*.json
          │
    ┌─────┼─────┐
    ▼     ▼     ▼
   C#    Dart   TypeScript
```

PartyGameKit control messages are distinct from `application.message`; the library carries application data without understanding its game/product meaning.

## LAN and discovery

Direct LAN WebSocket transport works without Internet or a cloud backend. UDP discovery is optional convenience infrastructure, not a prerequisite for connecting.

A serialized `ConnectionDescriptor` can be passed directly through any product-owned invitation flow, including QR, deep links, manual codes or another backend.

## Neutral reference sample

`samples/CommunicationDemo` exercises the real communication-only stack with no player/game model:

- real Kestrel/WebSocket listener;
- UDP discovery plus direct descriptor connection;
- two generic peers;
- opaque peer-to-host application messages;
- targeted and broadcast host delivery;
- disconnect and resume of the same `PeerId` on a replacement `ConnectionId`.

Run it from the repository root:

```bash
dotnet run --project samples/CommunicationDemo/PartyGameKit.Sample.CommunicationDemo.csproj
```

The former `SharedCounter` and `DungeonPrototype` samples belonged to the historical v0.1 session-oriented API. They were retired from the active v0.2 tree during the boundary correction and remain available through Git history and the v0.1 tag.

## TypeScript

```ts
import {
  PartyGameClient,
  parseConnectionDescriptor,
} from "@partygamekit/client";

const client = new PartyGameClient();
await client.connect(parseConnectionDescriptor(connectionPayload));
client.sendApplicationMessage("my-product.command", { value: 42 });
```

The browser SDK can persist a neutral peer identity and resume credential, but it does not assign a product role to that peer.

## Developer setup

Requirements:

- .NET 10 SDK;
- Node.js 22 for the TypeScript SDK;
- Dart stable for Dart conformance tests.

Build/test the repository and run the neutral demo:

```bash
dotnet restore PartyGameKit.slnx
dotnet build PartyGameKit.slnx --configuration Release --no-restore
dotnet test PartyGameKit.slnx --configuration Release --no-build
dotnet run --project samples/CommunicationDemo/PartyGameKit.Sample.CommunicationDemo.csproj --configuration Release --no-build
```

TypeScript:

```bash
npm install --prefix clients/typescript --no-audit --no-fund
npm test --prefix clients/typescript
```

Dart:

```bash
cd interop/dart
dart pub get
dart format --output=none --set-exit-if-changed .
dart analyze
dart test
```

CI also packs all .NET packages and runs `packaging/consumer` from those generated NuGet artifacts only. The package-only consumer verifies opaque message exchange and neutral peer resume without source-project references.

## Documentation

- [Communication boundary](docs/communication-boundary.md)
- [Architecture](docs/architecture.md)
- [Migration 0.1 → 0.2](docs/migration-0.1-to-0.2.md)
- [Cross-repository validation](docs/cross-repo-validation.md)
- [Package boundaries](docs/packages.md)
- [Public API classification](docs/public-api.md)
- [Protocol](docs/protocol.md)
- [Networking](docs/networking.md)
- [LAN WebSocket](docs/lan-websocket.md)
- [LAN discovery](docs/discovery.md)
- [Compatibility matrix](docs/compatibility.md)
- [Versioning](docs/versioning.md)
- [Extraction from Państwa Miasta](docs/extraction-from-panstwa-miasta.md)
- [Roadmap](docs/roadmap.md)
- [Changelog](CHANGELOG.md)

## Design invariant

Future transports may change **how** messages move. They must not change **what a player, host, TV, party or game means**, because those concepts belong to the consumer, not PartyGameKit.
