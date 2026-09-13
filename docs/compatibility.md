# Compatibility matrix

The language-neutral PartyGameKit wire protocol is the compatibility boundary. Package versions do not replace protocol negotiation.

## `[16]` transition state

| Implementation | Package line | Protocol | Runtime target | Scope |
| --- | --- | ---: | --- | --- |
| .NET | `PartyGameKit.*` `0.2.0-preview.1` | 2 | .NET 10 | neutral connection continuity, protocol, transports and LAN discovery |
| TypeScript | `@partygamekit/client` `0.1.0-preview.1` | 1 | ES2022 browser | historical player/shared-screen v1 client, migrated in `[17]` |
| Dart | `partygamekit_protocol` `0.1.0-preview.1` | 1 | Dart `>=3.3 <4.0` | historical protocol-v1 implementation, migrated in `[17]` |
| Państwa Miasta legacy LAN | application-owned v3 | separate contract | Flutter/Dart | existing game protocol remains consumer-owned |

The temporary v1/v2 split is intentional inside issue `[16]`; it is not a supported mixed-version runtime topology. Cross-language PartyGameKit v2 compatibility is restored in the immediately following ordered issue `[17]`.

## Protocol-v2 rules

- protocol v2 uses neutral connect/resume/heartbeat/disconnect control messages;
- stable `PeerId` is optional communication continuity identity, never a required player identity;
- `ConnectionId` is transient per network connection;
- `ConnectionDescriptor` contains technical connection metadata and optional `ChannelId` routing scope;
- consumer/game data travels as opaque `application.message` payloads;
- no base wire type defines player roles, authority, lobby capacity or game-state projections;
- a future SignalR or WebRTC transport must carry the same communication contract rather than transport-specific product rules.

## Version mismatch

Protocol v1 and v2 are deliberately incompatible. A protocol-v2 LAN listener rejects a v1 handshake with `protocol-version-mismatch` before exposing the connection to the application.

## Canonical compatibility checks

C# tests consume the new `v2-*` fixture set. Legacy root v1 fixtures remain temporarily for the still-v1 Dart/TypeScript tests and are removed or archived when `[17]` aligns those implementations.
