# PartyGameKit

PartyGameKit is a reusable multiplayer foundation for **shared-screen party games**.

The primary interaction model is:

- a TV, laptop or browser is the shared game screen,
- players join from their phones,
- phones act as controllers and private player screens,
- the same game can run locally over LAN or later use an optional backend/cloud path,
- game logic does not depend on SignalR, WebRTC, sockets or another concrete transport.

The API has been validated against two mechanically different games: the existing **Państwa Miasta** implementation and the repository's turn-based **Dungeon Prototype**. The generic packages intentionally contain no categories, answers, monsters, loot, combat rules or other game-domain concepts.

## v0.1 prerelease

The current package line is `0.1.0-preview.1`. It is a prerelease: package boundaries and protocol v1 are usable, but source compatibility may still change before a stable release.

PartyGameKit uses two independent versions:

- package/API version: SemVer, currently `0.1.0-preview.1`,
- wire protocol version: integer `1` carried by protocol envelopes and join descriptors.

See [Versioning](docs/versioning.md), [Compatibility](docs/compatibility.md), [Package boundaries](docs/packages.md) and [Public API review](docs/public-api.md).

## Package map

The .NET prerelease is split by responsibility:

- `PartyGameKit.Core` — rooms, players, authority, presence, reconnect and snapshot primitives,
- `PartyGameKit.Protocol` — protocol v1 envelopes, payloads and join descriptors,
- `PartyGameKit.Transport.Abstractions` — transport-neutral contracts,
- `PartyGameKit.Transport.InMemory` — deterministic test/reference transport,
- `PartyGameKit.Transport.Lan` — direct LAN WebSocket transport,
- `PartyGameKit.Discovery.Lan` — optional UDP LAN discovery.

Browser clients use `@partygamekit/client`. Dart/Flutter consumers currently get the language-neutral protocol package from `interop/dart`; it does not include a Dart transport or duplicate the .NET session engine.

## Getting started

Prerelease artifacts are built by CI and by the `Prerelease` workflow. Until a project license and public-registry credentials are deliberately configured, GitHub Releases are the distribution boundary rather than NuGet.org, npmjs.com or pub.dev.

### .NET consumer

Download the `.nupkg` files from the matching GitHub prerelease into a local folder and add the package that provides the capability you need. NuGet resolves the PartyGameKit package dependencies from the same folder.

```bash
dotnet add package PartyGameKit.Transport.Lan \
  --version 0.1.0-preview.1 \
  --source ./partygamekit-packages
```

For LAN discovery, add `PartyGameKit.Discovery.Lan` as well. A package-only smoke consumer lives in `packaging/consumer`; CI restores, builds and runs it from generated `.nupkg` files with no PartyGameKit `ProjectReference`.

### TypeScript/browser consumer

Download the npm tarball from the GitHub prerelease and install it directly:

```bash
npm install ./partygamekit-client-0.1.0-preview.1.tgz
```

```ts
import { PartyGameClient, parseJoinDescriptor } from '@partygamekit/client';
```

The SDK supports player/shared-screen join, reconnect, heartbeat, snapshot ordering and the LAN WebSocket client path. It intentionally contains no React or PartyBeam-specific logic.

### Dart/Flutter protocol consumer

The Dart package is currently distributed from the repository/tag rather than pub.dev:

```yaml
dependencies:
  partygamekit_protocol:
    git:
      url: https://github.com/PawelWielga/PartyGameKit.git
      ref: v0.1.0-preview.1
      path: interop/dart
```

It implements protocol v1 parsing, join descriptors, discovery announcements and snapshot sequence handling against the same canonical fixtures as C# and TypeScript.

## Reference samples

`SharedCounter` is the smallest full end-to-end sample: one authoritative .NET host, a browser shared screen and multiple browser/phone players over real LAN WebSocket transport.

```bash
npm install --prefix clients/typescript --no-audit --no-fund
npm run build --prefix clients/typescript
dotnet run --project samples/SharedCounter/SharedCounter.Host -- --host 192.168.1.20
```

Replace `192.168.1.20` with the host's LAN address. The host prints the shared-screen URL, player URL and canonical join payload.

`DungeonPrototype` validates the same foundation with character selection, turns/action points, movement, private inventory, enemy combat and reconnect during an active turn.

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
        ├── discovery + portable join descriptor
        │
        └── transport abstraction
                ├── LAN WebSocket
                ├── SignalR / cloud later
                └── WebRTC later
```

A stable `PlayerId` is not a `ConnectionId`. A dropped connection is not automatically a permanent leave. Host/authority is logical session state rather than a property of one WebSocket.

Discovery is optional convenience infrastructure. A valid portable join descriptor can connect directly even when UDP discovery is blocked or disabled.

Automatic host migration is not a v0.1 guarantee. SignalR/cloud and WebRTC are post-v0.1 transports.

## Cross-language contract

PartyGameKit is centered on .NET, while Państwa Miasta is Flutter/Dart and PartyBeam uses browser/TypeScript clients. The shared compatibility boundary is therefore the **versioned language-neutral JSON protocol plus canonical fixtures**, not a shared runtime implementation.

C#, Dart and TypeScript test their protocol behavior against the fixtures under `protocol/fixtures`.

## Repository shape

```text
PartyGameKit/
├── src/                         # .NET packages
├── clients/typescript/          # browser SDK
├── interop/dart/                # Dart protocol package
├── protocol/fixtures/           # language-neutral compatibility fixtures
├── packaging/consumer/          # package-only NuGet smoke consumer
├── samples/
│   ├── SharedCounter/
│   └── DungeonPrototype/
├── tests/
└── docs/
```

## Developer setup

Requirements:

- .NET 10 SDK,
- Node.js 22 for the TypeScript SDK,
- Dart stable for Dart protocol conformance tests.

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

The repository enables nullable reference types, deterministic builds, .NET analyzers, code-style checks during build and warnings-as-errors. CI also creates all prerelease artifacts and verifies that a fresh .NET consumer can restore and run from the generated packages.

## LAN host constraint

The direct LAN transport uses WebSocket. Its listener must run in a server-capable local runtime such as a .NET/native/desktop/TV process or companion process.

A pure browser/PWA can be the shared-screen or phone client, but it cannot accept arbitrary inbound WebSocket connections. Pure-browser direct hosting is deferred to a later peer/WebRTC transport.

## Documentation

- [Package boundaries](docs/packages.md)
- [Versioning](docs/versioning.md)
- [Compatibility matrix](docs/compatibility.md)
- [Public API review](docs/public-api.md)
- [Extraction from Państwa Miasta](docs/extraction-from-panstwa-miasta.md)
- [Architecture](docs/architecture.md)
- [Wire protocol](docs/protocol.md)
- [Networking](docs/networking.md)
- [Game session model](docs/game-session-model.md)
- [Snapshots](docs/snapshots.md)
- [Reconnect and presence](docs/reconnect.md)
- [Direct LAN WebSocket transport](docs/lan-websocket.md)
- [LAN discovery and join descriptors](docs/discovery.md)
- [Roadmap](docs/roadmap.md)
- [Changelog](CHANGELOG.md)

## Current status

`[01]` through `[13]` have validated the reusable LAN/session/protocol foundation against Państwa Miasta, Shared Counter and Dungeon Prototype. `[14]` turns that validated surface into reproducible `0.1.0-preview` packages and release artifacts. SignalR, WebRTC and automatic fallback remain explicitly post-v0.1 work.
