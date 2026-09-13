# Compatibility matrix

The language-neutral PartyGameKit wire protocol is the compatibility boundary. Package versions do not replace protocol negotiation.

## 0.2 compatibility

| Implementation | Package line | Protocol | Runtime target | Scope |
| --- | --- | ---: | --- | --- |
| .NET | `PartyGameKit.*` `0.2.0-preview.2` | 2 | .NET 10 | neutral connection continuity, protocol, in-memory/LAN/SignalR transports and LAN discovery |
| TypeScript | `@partygamekit/client` `0.2.0-preview.2` | 2 | ES2022 browser | neutral connect/resume and opaque application messages |
| Dart | `partygamekit_protocol` `0.2.0-preview.2` | 2 | Dart `>=3.3 <4.0` | protocol-v2 envelopes and connection descriptors |
| Państwa Miasta current LAN | application-owned contract | separate contract | Flutter/Dart | existing product/game protocol remains consumer-owned until an adapter migration is scheduled |

The PartyGameKit C#, TypeScript and Dart surfaces all validate the same v2 canonical fixtures. A consumer does not need to adopt PartyGameKit protocol v2 merely to keep its existing product protocol alive; migration can happen behind an adapter boundary.

`0.2.0-preview.2` does not change the wire protocol from preview.1. It adds optional .NET SignalR relay transport packages behind the same protocol-v2 and `IMessageTransport` boundaries. LAN and SignalR can therefore carry the same application-owned payload schema.

## Protocol-v2 rules

- protocol v2 uses neutral connect/resume/heartbeat/disconnect control messages;
- stable `PeerId` is optional communication continuity identity, never a required player identity;
- `ConnectionId` is transient per network connection;
- `ConnectionDescriptor` contains technical connection metadata and optional `ChannelId` routing scope;
- consumer/game data travels as opaque `application.message` payloads;
- no base wire type defines player roles, authority, lobby capacity or game-state projections;
- LAN WebSocket and SignalR relay carry the same communication contract rather than transport-specific product rules;
- future WebRTC must preserve that same boundary.

## Version mismatch

Protocol v1 and v2 are deliberately incompatible. A protocol-v2 LAN listener or SignalR relay listener rejects a v1 handshake with `protocol-version-mismatch` before exposing the connection to the application.

`0.1.0-preview.1` remains available through its historical tag/release. Active conformance fixtures on `main` represent protocol v2 only.

## Canonical compatibility checks

The following implementations read the repository's `protocol/fixtures/v2-*.json` vectors directly:

- C# protocol tests;
- Dart interoperability tests;
- TypeScript browser-client tests.

The SignalR integration tests additionally prove that protocol-v2 connect/resume and opaque application messages retain the same semantics over the backend-assisted path.

A change to the cross-language wire contract is incomplete unless the canonical fixture set and all three implementations agree.
