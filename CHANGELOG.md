# Changelog

All notable PartyGameKit changes are documented here. Package versions follow the policy in `docs/versioning.md`; the wire protocol has its own independent version.

## 0.2.0-preview.1 - 2026-09-13

Breaking correction of the public architecture boundary. PartyGameKit is now a communication/networking library rather than a generic party/game-session runtime.

### Breaking changes

- wire protocol incremented from `1` to `2`;
- `PlayerId` was replaced by neutral `PeerId` for optional communication continuity;
- `RoomId`/`JoinCode`/`JoinDescriptor` were removed from the base connection model and replaced by optional `ChannelId` plus `ConnectionDescriptor`;
- `ClientRole`, `AuthorityId`, `RoomSession`, player membership/capacity/lifecycle and game snapshot projection APIs were removed from production packages;
- `SessionContinuityCoordinator` was replaced by neutral `ConnectionContinuityCoordinator`;
- `SnapshotSequence`/`SnapshotSequenceGate` became generic `MessageSequence`/`SequenceGate` utilities;
- `IGameTransport` became `IMessageTransport`;
- `PartyGameTransportException` became `TransportException`;
- `InMemoryGameTransport` became `InMemoryTransport`;
- `LanJoinDescriptor` became `LanConnectionDescriptor`;
- `DiscoveredSessionRegistry` became `DiscoveredEndpointRegistry`.

### Protocol v2

- connection lifecycle messages use `connection.connect.*`, `connection.resume.*`, `connection.heartbeat` and `connection.disconnect`;
- consumer data is carried as `application.message` with opaque consumer-owned data;
- LAN WebSocket handshake accepts connect/resume control messages only;
- LAN discovery advertises technical `ConnectionDescriptor` data rather than product/game sessions;
- protocol version mismatch remains deterministic;
- protocol-v1 canonical fixtures were removed after C#, Dart and TypeScript moved to the v2 fixture set.

### TypeScript and Dart

- `@partygamekit/client` moved to neutral connect/resume APIs with optional stable peer identity;
- browser role requirements (`player`, `shared-screen`) and snapshot projection handling were removed;
- browser descriptors now use `ConnectionDescriptor` / `partygamekit://connect`;
- the Dart interoperability package moved to protocol v2, neutral peer/connection vocabulary and connection descriptors;
- both language surfaces validate the same `protocol/fixtures/v2-*.json` contract as C#.

### Samples and package validation

- added `samples/CommunicationDemo`, a game-agnostic real-LAN reference sample covering UDP discovery, direct descriptor connection, two generic peers, opaque application messages, targeted delivery, broadcast and resume on a replacement connection;
- the neutral communication demo runs in CI and prerelease validation;
- `packaging/consumer` now restores generated NuGet packages only and verifies opaque message exchange plus neutral peer resume instead of merely checking that package types load;
- historical Shared Counter and Dungeon Prototype remain application-layer examples and are not sources of generic library semantics.

### Validation

- neutral peer continuity tests prove replacement connections resume the same logical peer without duplication;
- in-memory transport tests cover generic targeted and broadcast payloads;
- LAN WebSocket integration tests cover opaque bidirectional messaging and protocol mismatch rejection;
- UDP discovery tests cover endpoint discovery and direct-descriptor independence;
- public API guard tests prevent removed v0.1 product/session abstractions from returning to production assemblies;
- C#, Dart and TypeScript conformance tests target protocol v2.

### Migration

See `docs/migration-0.1-to-0.2.md`.

## 0.1.0-preview.1 - 2026-09-12

First packaged preview of the foundation validated by two mechanically different games.

### Added

- transport-independent room/session lifecycle with stable `PlayerId` and transient `ConnectionId`;
- versioned protocol-v1 envelopes, join/rejoin/leave/heartbeat messages and canonical cross-language fixtures;
- authoritative public/shared-screen and private-player snapshots with monotonic sequences;
- reconnect credentials, presence tracking and reconnect-window semantics;
- transport abstractions plus deterministic in-memory transport;
- direct LAN WebSocket transport and UDP LAN discovery;
- deterministic portable join descriptors suitable for QR/manual transfer;
- Dart protocol implementation tested against the same fixtures as C#;
- browser-first TypeScript client with join/rejoin, heartbeat, reconnect and projection filtering;
- Shared Counter end-to-end sample with two player browsers and a shared screen;
- Dungeon Prototype validation with character selection, turns/AP, movement, private inventory, combat and reconnect;
- package-consumer smoke test and reproducible prerelease artifact workflows.

### Validation outcome

The preview proved the networking mechanisms but also exposed that the generic package boundary was too broad. Player/session/authority/snapshot semantics are therefore historical v0.1 behavior, not the target architecture.

### Distribution

`0.1.0-preview.1` is intentionally a prerelease. CI produced NuGet, npm and Dart-source artifacts. It remains the legacy protocol-v1 line.
