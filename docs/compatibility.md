# Compatibility matrix

The language-neutral PartyGameKit wire protocol is the compatibility boundary. Package versions do not replace protocol negotiation.

| Implementation | Preview package | Protocol | Runtime target | Scope |
| --- | --- | ---: | --- | --- |
| .NET | `PartyGameKit.*` `0.1.0-preview.1` | 1 | .NET 10 | Core/session, protocol, transports and LAN discovery |
| TypeScript | `@partygamekit/client` `0.1.0-preview.1` | 1 | ES2022 browser | Player/shared-screen WebSocket client |
| Dart | `partygamekit_protocol` `0.1.0-preview.1` | 1 | Dart `>=3.3 <4.0` | Protocol/join-descriptor compatibility; no Dart transport package yet |
| Państwa Miasta legacy LAN | application-owned v3 | separate contract | Flutter/Dart | Existing game protocol remains game-owned; validated semantically against PartyGameKit |

## Rules

- `protocolVersion: 1` messages are accepted only by implementations that advertise PartyGameKit protocol v1.
- Stable `playerId`, transient `connectionId`, reconnect credential ownership and snapshot sequence semantics are consistent across C#, Dart and TypeScript.
- Public and player-private snapshot targets use the same JSON shape in every implementation.
- `JoinDescriptor` JSON and `partygamekit://join` URI encoding are canonicalized by shared fixture tests.
- A future SignalR or WebRTC transport must carry the same infrastructure protocol semantics rather than inventing transport-specific game rules.

## Canonical compatibility checks

C#, Dart and TypeScript tests consume the same files in `protocol/fixtures/`. CI fails if any supported implementation can no longer consume those vectors.
