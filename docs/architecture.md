# Architecture

## Purpose

Dihor.GameKit.Networking is a reusable communication/networking layer for multiplayer applications. It is intentionally below PartyBeam, Państwa Miasta and future products.

The authoritative boundary decision is [Communication boundary](communication-boundary.md).

```text
PartyBeam / Państwa Miasta / future multiplayer products
                │
                │ product roles, players, parties, authority,
                │ state, game lifecycle and rules
                ▼
          Dihor.GameKit.Networking
      communication/networking
                │
        transports + discovery
                │
       LAN / SignalR / WebRTC
```

The central architectural rule is:

> Dihor.GameKit.Networking moves messages and maintains communication continuity. Consumers decide what those messages and connected peers mean.

## Dependency licensing policy

Dihor.GameKit.Networking must not depend on external libraries or packages whose commercial use requires payment.

Every third-party dependency introduced into the project must permit commercial use without requiring:

- a paid commercial license;
- a subscription or recurring license fee;
- per-seat, per-device or per-runtime fees;
- royalties or revenue sharing;
- any other mandatory payment triggered by commercial use of Dihor.GameKit.Networking or products that consume it.

Prefer permissive open-source licenses such as MIT, Apache-2.0 or BSD when a suitable dependency exists. Dual-licensed libraries are acceptable only when Dihor.GameKit.Networking and its commercial consumers can legally use the dependency under a free, commercial-compatible license.

The license of a new external dependency must be verified before the dependency is added. If commercial-use rights are unclear, treat the dependency as unsuitable until the license is confirmed.

For `[19]`, native browser WebRTC was chosen rather than introducing a native .NET WebRTC runtime with unsuitable licensing/maintenance characteristics. `@microsoft/signalr` is MIT-licensed; Playwright is Apache-2.0 and used only for browser integration testing.

## Communication identities and control protocol

Dihor.GameKit.Networking defines only identities required by communication itself:

- transient `ConnectionId`;
- optional stable neutral `PeerId` for resume/reconnect;
- optional technical `ChannelId` for routing/discovery isolation.

These identities do not imply player, host, TV, controller, spectator, party or game-session semantics.

The language-neutral protocol owns:

- protocol version and compatibility;
- connection handshake/version validation;
- heartbeat/connectivity messages;
- neutral resume request/accept/reject;
- message identifiers/correlation metadata;
- opaque `application.message` payload transport.

Application messages are consumer-owned. Dihor.GameKit.Networking does not require a game/session schema.

## Transport abstractions

`IMessageTransport` owns technology-neutral .NET listener operations:

- connection opened/closed/faulted events;
- receive message events;
- targeted send;
- broadcast where supported;
- disconnect, stop, cancellation and disposal;
- transport-neutral errors and diagnostics.

The abstraction operates on transient `ConnectionId` plus opaque bytes. It has no player/session dependency.

Not every runtime-specific communication capability must pretend to implement this exact .NET interface. The browser WebRTC endpoint is exposed through the TypeScript SDK using native browser APIs while preserving the same communication-only ownership boundary.

## Transient application replay

Reconnect-safe transient application delivery is an application-data concern, not a heartbeat or concrete-transport concern.

`LatestValueReplayBuffer` lives in `Dihor.GameKit.Networking.Transport.Abstractions`. It stores opaque application-message bytes by caller-owned replay key and caller-owned scope/epoch. TypeScript exposes the same lifecycle through `LatestValueReplayBuffer<TMessage>` and `ReplaySender<TMessage>`.

The primitive intentionally uses latest-value/coalescing semantics rather than an unbounded FIFO retry queue. Rebinding a replacement sender immediately replays the newest still-valid buffered value, including when automatic connectivity chooses a different physical transport.

The buffer preserves a staged message across ambiguous reconnect retry but does not promise exactly-once delivery. Receiver-side bounded `messageId` deduplication is a separate concern tracked by issue [25]. Reliable command/ACK semantics remain a future, stronger mechanism.

Heartbeat remains control-plane liveness only and never carries consumer replay state.

See [Transient latest-value replay](transient-replay.md).

## Concrete communication paths

Concrete adapters remain separate by technology/runtime:

- in-memory reference/test transport;
- direct LAN WebSocket transport;
- optional SignalR/backend relay transport;
- browser-native WebRTC DataChannel communication;
- later automatic selection/fallback above individually reliable paths.

The LAN and SignalR listener sides implement `IMessageTransport`. Their single-peer client adapters are technology-specific (`LanWebSocketClient` and `SignalRRelayClient`), but they carry the same protocol-v2/application payload semantics.

Browser WebRTC uses `WebRtcPeer` in `@dihor/gamekit-networking`. It carries opaque binary consumer data directly over `RTCDataChannel`; it does not claim to be a native .NET or Dart endpoint.

Adding a communication path must never add player/session/authority semantics to base APIs.

## SignalR relay architecture

The SignalR relay path deliberately separates the transport endpoint from communication continuity:

```text
consumer/listener
       │ IMessageTransport
       ▼
SignalRRelayTransport
       │ SignalR
       ▼
minimal relay backend
       │ ChannelId + transient ConnectionId + opaque bytes
       ▼
SignalRRelayClient
       │
       ▼
consumer peer
```

The relay backend does not parse `PeerId`, resume credentials or application-message meaning. Protocol-v2 connect/resume validation remains in the Dihor.GameKit.Networking transport/protocol layer, and `ConnectionContinuityCoordinator` remains responsible for rebinding a stable neutral peer to a replacement connection.

See [SignalR relay](signalr-relay.md).

## WebRTC architecture

The WebRTC path separates negotiation from application data:

```text
browser peer A                         browser peer B
     │                                      │
     │        SDP / ICE signaling           │
     ├──────── optional SignalR ─────────────┤
     │                                      │
     ╰══════ RTCDataChannel (direct) ═══════╯
              opaque application bytes
```

The browser SDK provides:

- neutral `WebRtcSignalingChannel` abstraction;
- `WebRtcPeer` around `RTCPeerConnection` / `RTCDataChannel`;
- reliable ordered and low-latency unordered/no-retransmit profiles;
- bounded DataChannel buffering/backpressure;
- bounded pending ICE buffering;
- connection state and communication diagnostics.

The optional ASP.NET Core signaling endpoint provides only technical SDP/ICE routing. It scopes transient SignalR connection IDs by `ChannelId`, rejects cross-channel targets and bounds signal payload size.

It does not relay normal DataChannel application traffic and does not own `PeerId`, player identity, parties or game state.

TURN may be required in some Internet/NAT topologies, but Dihor.GameKit.Networking does not implement or operate a TURN service in `[19]`.

See [WebRTC DataChannel](webrtc-datachannel.md).

## Discovery and connection descriptors

Discovery advertises technical connection endpoints/services. It is separate from message transport.

`ConnectionDescriptor` contains only data required to establish communication: transport kind, endpoint, protocol version and optional technical `ChannelId`.

Product invitation concepts such as party join code, game id, display name or QR presentation belong to the consumer. PartyBeam may wrap a Dihor.GameKit.Networking connection descriptor inside its own invite payload.

LAN UDP discovery remains optional and backend-free. SignalR does not turn discovery into a product lobby service.

WebRTC peer negotiation currently uses transient signaling membership rather than redefining `ConnectionDescriptor` as a product session object.

## Client SDKs

TypeScript and Dart implement the same protocol-v2 communication contract where applicable.

The browser SDK exposes protocol-v2 connect/disconnect/send/receive/resume plus native WebRTC DataChannels without requiring PartyBeam roles or player/game-state projections.

The Dart package remains a thin interoperability/protocol layer and deliberately does not duplicate a game/session runtime or claim a WebRTC transport it does not implement.

`0.2.0-preview.3` aligns package versions across supported surfaces while allowing runtime-specific capability differences. Version alignment does not mean every language implements every transport.

Framework UI/state-management concerns remain outside Dihor.GameKit.Networking.

## Cross-language boundary

The compatibility model for Dihor.GameKit.Networking protocol v2 is:

```text
protocol/fixtures/v2-*.json
             │
   ┌─────────┼─────────┐
   ▼         ▼         ▼
  C#        Dart   TypeScript
```

The fixtures specify the Dihor.GameKit.Networking protocol communication contract. Consumer/game protocols may be independently versioned by their owners.

WebRTC DataChannel tests validate browser transport behavior separately; adding WebRTC does not modify the canonical protocol-v2 fixture set.

## Identity and reconnect

Reconnect is a communication concern only when expressed neutrally:

```text
ConnectionId = current transport connection
PeerId       = optional stable logical communication identity
```

A resume credential proves that a replacement connection may resume the same `PeerId`.

`ConnectionContinuityCoordinator` rebinds that communication identity and tracks connectivity state. It does not decide whether the peer is a player, whether it occupies a slot, whether a game pauses or whether the participant should be removed.

WebRTC signaling connection IDs are ephemeral negotiation-routing identifiers and are not a replacement for `PeerId`.

## Routing scope

`ChannelId` is an optional technical routing/discovery identifier, not a game session.

It may isolate communication/signaling but does not own:

- lifecycle;
- player membership;
- capacity;
- roles;
- authority;
- score;
- game phase.

SignalR relay uses `ChannelId` to isolate application-data delivery. WebRTC signaling uses it to isolate SDP/ICE exchange. Consumers remain free to maintain their own party/room/session identifiers independently.

## Ordering, buffering and snapshots

`MessageSequence` / `SequenceGate` are optional neutral monotonic ordering/deduplication utilities.

WebRTC adds transport-level buffering policy, not game-state semantics. Reliable DataChannel traffic reports backpressure when the configured buffer bound would be exceeded; low-latency traffic may drop newest payloads according to explicit communication policy.

Game snapshot semantics are not Dihor.GameKit.Networking base responsibilities. Public/private player projections, authority-bound publication and game-state restoration belong to consumers.

## Consumer examples

PartyBeam may choose:

```text
TV/companion = product coordinator
a phone      = controller/player
pilot        = privileged product control surface
```

Another product may choose:

```text
dedicated server = authority
browser clients  = collaborators
no players at all
```

Both use the same Dihor.GameKit.Networking communication foundation without changing the library's ownership boundary.

## LAN host constraint

The direct LAN WebSocket listener requires a server-capable runtime. A browser can initiate WebSocket connections but cannot be the raw inbound WebSocket listener.

This is a transport capability constraint, not a reason to model `Host` or `SharedScreen` in Dihor.GameKit.Networking.

Browser WebRTC changes the peer-to-peer capability picture for browser consumers but does not turn browser transport capability into a product role.

## Historical v0.1 implementation

`0.1.0-preview.1` included `RoomSession`, `PlayerId`, `ClientRole`, `AuthorityId`, player capacity/admission, session continuity tied to players, and public/private snapshots.

Those APIs were useful validation scaffolding but crossed the correct ownership boundary. `[15]`–`[17]` deliberately replaced that preview model with the communication-only `0.2` line.

See [Public API review](public-api.md), [Package boundaries](packages.md) and [Migration 0.1 → 0.2](migration-0.1-to-0.2.md).

## Architecture invariant

A minimal Dihor.GameKit.Networking consumer can:

1. connect generic clients;
2. exchange opaque messages;
3. target or broadcast messages where the selected path supports it;
4. detect communication failure/state changes;
5. resume the same neutral logical peer on a replacement protocol-v2 connection when configured;
6. use LAN discovery or a directly supplied descriptor;
7. choose direct LAN or optional backend-assisted SignalR without changing product payload semantics;
8. establish direct browser WebRTC DataChannels with signaling kept separate from application data;
9. use reliable or low-latency communication profiles without defining product/game roles;
10. do all of the above without defining `Player`, `Host`, `SharedScreen`, lobby, score or game state.

`samples/CommunicationDemo` validates LAN and SignalR relay. Real Chromium integration validates WebRTC peer-to-peer DataChannels.
