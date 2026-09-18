# Compatibility matrix

The language-neutral Dihor.GameKit.Networking wire protocol is the compatibility boundary. Package versions do not replace protocol negotiation.

## 0.2 compatibility

| Implementation | Package line | Protocol | Runtime target | Scope |
| --- | --- | ---: | --- | --- |
| .NET | `Dihor.GameKit.Networking.*` `0.2.0-preview.5` | 2 | .NET 10 | neutral connection continuity, protocol, in-memory/LAN/SignalR transports, automatic client selection, monotonic timing, WebRTC signaling and LAN discovery |
| TypeScript | `@dihor/gamekit-networking` `0.2.0-preview.5` | 2 | ES2022 browser | neutral connect/resume, opaque application messages, native WebRTC DataChannels, generic automatic transport selection and monotonic timing |
| Dart | `dihor_gamekit_networking_protocol` `0.2.0-preview.5` | 2 | Dart `>=3.3 <4.0` | protocol-v2 envelopes and connection descriptors; no transport runtime, automatic selector or synchronized timing implementation |
| Państwa Miasta current LAN | application-owned contract | separate contract | Flutter/Dart | existing product/game protocol remains consumer-owned until an adapter migration is scheduled |

The Dihor.GameKit.Networking C#, TypeScript and Dart protocol surfaces continue to validate the same v2 canonical fixtures. A consumer does not need to adopt Dihor.GameKit.Networking protocol v2 merely to keep its existing product protocol alive; migration can happen behind an adapter boundary.

`0.2.0-preview.5` does not change the wire protocol from preview.1-preview.4. It adds synchronized monotonic timing as an optional utility while existing connect/resume and opaque application data remain unchanged.

## Protocol-v2 rules

- protocol v2 uses neutral connect/resume/heartbeat/disconnect control messages;
- stable `PeerId` is optional communication continuity identity, never a required player identity;
- `ConnectionId` is transient per network connection;
- `ConnectionDescriptor` contains technical connection metadata and optional `ChannelId` routing scope;
- consumer/game data may travel as opaque `application.message` payloads on the protocol-v2 WebSocket/SignalR paths;
- WebRTC DataChannels may carry opaque binary consumer data directly between browser peers;
- WebRTC signaling contains SDP/ICE negotiation data only and uses transient signaling connection IDs plus technical `ChannelId` routing;
- automatic connectivity selects among registered runtime candidates but does not reinterpret protocol/application payloads;
- synchronized timing consumes caller-supplied monotonic probe/reply evidence and does not reserve a new protocol-v2 control message;
- no base wire/signaling/selection/timing type defines player roles, authority policy, lobby capacity or game-state projections.

## Runtime support

Direct LAN WebSocket and SignalR relay remain .NET-backed communication transports. The initial WebRTC endpoint implementation is browser-native in `@dihor/gamekit-networking`; `Dihor.GameKit.Networking.Transport.SignalR.Server` optionally hosts the neutral signaling endpoint.

The .NET selector can orchestrate registered `IMessageTransportClient` candidates, including LAN and SignalR out of the box. The WebRTC identifier is part of the neutral selection vocabulary, but Dihor.GameKit.Networking does **not** claim a native .NET WebRTC DataChannel implementation.

The TypeScript selector is generic and can orchestrate browser-specific adapters including native `WebRtcPeer`. It does not imply that every registered browser runtime has a SignalR relay client or a LAN listener implementation.

Monotonic timing is transport-independent in both .NET and TypeScript. Callers decide how to carry probe IDs and peer timestamps over LAN, WebRTC, SignalR or another adapter. A reconnect or path replacement requires timing-state reset and resynchronization.

Dart remains protocol-only in preview.5 and does not claim a Dart LAN, SignalR, WebRTC, automatic transport or synchronized timing runtime.

## Version mismatch

Protocol v1 and v2 are deliberately incompatible. A protocol-v2 LAN listener or SignalR relay listener rejects a v1 handshake with `protocol-version-mismatch` before exposing the connection to the application.

WebRTC signaling itself is infrastructure negotiation and does not reinterpret protocol v1 as v2. Products that send Dihor.GameKit.Networking protocol envelopes over a DataChannel remain responsible for using a compatible envelope version.

Automatic selection also does not translate protocol versions. If a selected transport rejects an incompatible protocol handshake, that failure is observable as a connection-attempt failure; Dihor.GameKit.Networking does not mutate the handshake to make it compatible.

Timing synchronization likewise does not translate protocol versions or validate product/application payload schemas. It only evaluates monotonic timing evidence supplied to the timing API.

`0.1.0-preview.1` remains available through its historical tag/release. Active conformance fixtures on `main` represent protocol v2 only.

## Compatibility checks

The following implementations read the repository's `protocol/fixtures/v2-*.json` vectors directly:

- C# protocol tests;
- Dart interoperability tests;
- TypeScript browser-client tests.

Additional transport/utility validation proves:

- LAN and SignalR retain protocol-v2 connect/resume/application-message semantics;
- automatic selection is bounded and deterministic across success, failure, timeout, cancellation and reconnect preference;
- selector tests preserve opaque handshake/payload data across transport changes;
- synchronized timing estimates known offsets under deterministic fake time and reports RTT/jitter/uncertainty;
- timestamp validation rejects stale, non-monotonic, too-old and implausibly future evidence;
- reconnect reset invalidates an old timing model before new evidence is accepted;
- two independently synchronized peers can produce normalized event ordering that differs from packet-arrival ordering;
- sample history and outstanding-probe bookkeeping remain bounded;
- SignalR WebRTC signaling cannot target a peer in another technical channel;
- real Chromium peers establish direct reliable and low-latency DataChannels;
- application DataChannel traffic does not continue through the signaling path after negotiation;
- high-frequency browser traffic remains bounded by explicit DataChannel backpressure policy.

A change to the cross-language wire contract is incomplete unless the canonical fixture set and all protocol implementations agree.
