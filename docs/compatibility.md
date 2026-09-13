# Compatibility matrix

The language-neutral PartyGameKit wire protocol is the compatibility boundary. Package versions do not replace protocol negotiation.

## 0.2 compatibility

| Implementation | Package line | Protocol | Runtime target | Scope |
| --- | --- | ---: | --- | --- |
| .NET | `PartyGameKit.*` `0.2.0-preview.4` | 2 | .NET 10 | neutral connection continuity, protocol, in-memory/LAN/SignalR transports, automatic client selection, WebRTC signaling and LAN discovery |
| TypeScript | `@partygamekit/client` `0.2.0-preview.4` | 2 | ES2022 browser | neutral connect/resume, opaque application messages, native WebRTC DataChannels and generic automatic transport selection |
| Dart | `partygamekit_protocol` `0.2.0-preview.4` | 2 | Dart `>=3.3 <4.0` | protocol-v2 envelopes and connection descriptors; no transport runtime or automatic selector |
| Państwa Miasta current LAN | application-owned contract | separate contract | Flutter/Dart | existing product/game protocol remains consumer-owned until an adapter migration is scheduled |

The PartyGameKit C#, TypeScript and Dart protocol surfaces continue to validate the same v2 canonical fixtures. A consumer does not need to adopt PartyGameKit protocol v2 merely to keep its existing product protocol alive; migration can happen behind an adapter boundary.

`0.2.0-preview.4` does not change the wire protocol from preview.1-preview.3. It adds deterministic runtime-side transport selection/fallback while carrying the same connect/resume and opaque application data unchanged.

## Protocol-v2 rules

- protocol v2 uses neutral connect/resume/heartbeat/disconnect control messages;
- stable `PeerId` is optional communication continuity identity, never a required player identity;
- `ConnectionId` is transient per network connection;
- `ConnectionDescriptor` contains technical connection metadata and optional `ChannelId` routing scope;
- consumer/game data may travel as opaque `application.message` payloads on the protocol-v2 WebSocket/SignalR paths;
- WebRTC DataChannels may carry opaque binary consumer data directly between browser peers;
- WebRTC signaling contains SDP/ICE negotiation data only and uses transient signaling connection IDs plus technical `ChannelId` routing;
- automatic connectivity selects among registered runtime candidates but does not reinterpret protocol/application payloads;
- no base wire/signaling/selection type defines player roles, authority, lobby capacity or game-state projections.

## Runtime support

Direct LAN WebSocket and SignalR relay remain .NET-backed communication transports. The initial WebRTC endpoint implementation is browser-native in `@partygamekit/client`; `PartyGameKit.Transport.SignalR.Server` optionally hosts the neutral signaling endpoint.

The .NET selector can orchestrate registered `IMessageTransportClient` candidates, including LAN and SignalR out of the box. The WebRTC identifier is part of the neutral selection vocabulary, but PartyGameKit does **not** claim a native .NET WebRTC DataChannel implementation.

The TypeScript selector is generic and can orchestrate browser-specific adapters including native `WebRtcPeer`. It does not imply that every registered browser runtime has a SignalR relay client or a LAN listener implementation.

Dart remains protocol-only in preview.4 and does not claim a Dart LAN, SignalR, WebRTC or automatic transport runtime.

## Version mismatch

Protocol v1 and v2 are deliberately incompatible. A protocol-v2 LAN listener or SignalR relay listener rejects a v1 handshake with `protocol-version-mismatch` before exposing the connection to the application.

WebRTC signaling itself is infrastructure negotiation and does not reinterpret protocol v1 as v2. Products that send PartyGameKit protocol envelopes over a DataChannel remain responsible for using a compatible envelope version.

Automatic selection also does not translate protocol versions. If a selected transport rejects an incompatible protocol handshake, that failure is observable as a connection-attempt failure; PartyGameKit does not mutate the handshake to make it compatible.

`0.1.0-preview.1` remains available through its historical tag/release. Active conformance fixtures on `main` represent protocol v2 only.

## Compatibility checks

The following implementations read the repository's `protocol/fixtures/v2-*.json` vectors directly:

- C# protocol tests;
- Dart interoperability tests;
- TypeScript browser-client tests.

Additional transport validation proves:

- LAN and SignalR retain protocol-v2 connect/resume/application-message semantics;
- automatic selection is bounded and deterministic across success, failure, timeout, cancellation and reconnect preference;
- selector tests preserve opaque handshake/payload data across transport changes;
- the automatic .NET sample preserves a stable `PeerId` and byte-for-byte opaque application payload;
- SignalR WebRTC signaling cannot target a peer in another technical channel;
- real Chromium peers establish direct reliable and low-latency DataChannels;
- application DataChannel traffic does not continue through the signaling path after negotiation;
- high-frequency browser traffic remains bounded by explicit DataChannel backpressure policy.

A change to the cross-language wire contract is incomplete unless the canonical fixture set and all protocol implementations agree.
