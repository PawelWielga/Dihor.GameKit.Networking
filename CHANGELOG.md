# Changelog

All notable PartyGameKit changes are documented here. Package versions follow the policy in `docs/versioning.md`; the wire protocol has its own independent version.

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

The Shared Counter and Dungeon Prototype both use the same generic room/session/protocol/transport/reconnect foundation. No Countries & Cities, counter, dungeon, monster, loot, tile, attack, inventory or other game-domain concept is part of the generic PartyGameKit packages.

### Distribution

`0.1.0-preview.1` is intentionally a prerelease. CI produces NuGet, npm and Dart-source artifacts, and a matching Git tag can publish them as a GitHub prerelease. Public NuGet/npm/pub.dev registry publication is not automated in this preview.
