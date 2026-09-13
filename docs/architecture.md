# Architecture

## Purpose

PartyGameKit is a reusable communication/networking layer for multiplayer applications. It is intentionally below PartyBeam, Państwa Miasta and future products.

The authoritative boundary decision is [Communication boundary](communication-boundary.md).

```text
PartyBeam / Państwa Miasta / future multiplayer products
                │
                │ product roles, players, parties, authority,
                │ state, game lifecycle and rules
                ▼
          PartyGameKit
      communication/networking
                │
        transports + discovery
                │
     LAN / SignalR / future WebRTC
```

The central architectural rule is:

> PartyGameKit moves messages and maintains communication continuity. Consumers decide what those messages and connected peers mean.

## Dependency licensing policy

PartyGameKit must not depend on external libraries or packages whose commercial use requires payment.

Every third-party dependency introduced into the project must permit commercial use without requiring:

- a paid commercial license;
- a subscription or recurring license fee;
- per-seat, per-device or per-runtime fees;
- royalties or revenue sharing;
- any other mandatory payment triggered by commercial use of PartyGameKit or products that consume it.

Prefer permissive open-source licenses such as MIT, Apache-2.0 or BSD when a suitable dependency exists. Dual-licensed libraries are acceptable only when PartyGameKit and its commercial consumers can legally use the dependency under a free, commercial-compatible license.

The license of a new external dependency must be verified before the dependency is added. If commercial-use rights are unclear, treat the dependency as unsuitable until the license is confirmed.

## Communication identities and control protocol

PartyGameKit defines only identities required by communication itself:

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

Application messages are consumer-owned. PartyGameKit does not require a game/session schema.

## Transport abstractions

`IMessageTransport` owns technology-neutral communication operations:

- connection opened/closed/faulted events;
- receive message events;
- targeted send;
- broadcast where supported;
- disconnect, stop, cancellation and disposal;
- transport-neutral errors and diagnostics.

The abstraction operates on transient `ConnectionId` plus opaque bytes. It has no player/session dependency.

## Concrete transports

Concrete adapters remain separate packages:

- in-memory reference/test transport;
- direct LAN WebSocket transport;
- optional SignalR/backend relay transport;
- later optional WebRTC DataChannel transport;
- later automatic selection/fallback above individually reliable transports.

The LAN and SignalR listener sides both implement `IMessageTransport`. Their single-peer client adapters are technology-specific (`LanWebSocketClient` and `SignalRRelayClient`), but they carry the same protocol-v2/application payload semantics.

Adding a transport must never add player/session/authority semantics to base APIs.

## SignalR relay architecture

The SignalR path deliberately separates the transport endpoint from communication continuity:

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

The relay backend does not parse `PeerId`, resume credentials or application-message meaning. Protocol-v2 connect/resume validation remains in the PartyGameKit transport/protocol layer, and `ConnectionContinuityCoordinator` remains responsible for rebinding a stable neutral peer to a replacement connection.

The public server API is limited to service registration, endpoint mapping and transport-level limits. The hub and routing registry are implementation details, not a session engine.

See [SignalR relay](signalr-relay.md).

## Discovery and connection descriptors

Discovery advertises technical connection endpoints/services. It is separate from message transport.

`ConnectionDescriptor` contains only data required to establish communication: transport kind, endpoint, protocol version and optional technical `ChannelId`.

Product invitation concepts such as party join code, game id, display name or QR presentation belong to the consumer. PartyBeam may wrap a PartyGameKit connection descriptor inside its own invite payload.

LAN UDP discovery remains optional and backend-free. SignalR does not turn discovery into a product lobby service.

## Client SDKs

TypeScript and Dart implement the same protocol-v2 communication contract as C#.

The browser SDK exposes connect/disconnect/send/receive/resume, connection state, protocol compatibility and descriptor parsing without requiring PartyBeam roles or player/game-state projections.

The Dart package remains a thin interoperability/protocol layer and deliberately does not duplicate a game/session runtime.

`0.2.0-preview.2` aligns the supported release versions, but the new SignalR transport itself is a .NET transport/server implementation. The TypeScript and Dart packages do not claim transport functionality they do not implement.

Framework UI/state-management concerns remain outside PartyGameKit.

## Cross-language boundary

The compatibility model is:

```text
protocol/fixtures/v2-*.json
             │
   ┌─────────┼─────────┐
   ▼         ▼         ▼
  C#        Dart   TypeScript
```

The fixtures specify the PartyGameKit communication contract. Consumer/game protocols may be independently versioned by their owners.

## Identity and reconnect

Reconnect is a communication concern only when expressed neutrally:

```text
ConnectionId = current transport connection
PeerId       = optional stable logical communication identity
```

A resume credential proves that a replacement connection may resume the same `PeerId`.

`ConnectionContinuityCoordinator` rebinds that communication identity and tracks connectivity state. It does not decide whether the peer is a player, whether it occupies a slot, whether a game pauses or whether the participant should be removed.

The same continuity semantics work over LAN and SignalR; changing transport does not redefine peer identity.

## Routing scope

`ChannelId` is an optional technical routing/discovery identifier, not a game session.

It may isolate communication but does not own:

- lifecycle;
- player membership;
- capacity;
- roles;
- authority;
- score;
- game phase.

SignalR uses `ChannelId` only to isolate relay delivery. Consumers remain free to maintain their own party/room/session identifiers independently.

## Ordering and snapshots

`MessageSequence` / `SequenceGate` are optional neutral monotonic ordering/deduplication utilities.

Game snapshot semantics are not PartyGameKit base responsibilities. Public/private player projections, authority-bound publication and game-state restoration belong to consumers.

Państwa Miasta may carry its own authoritative game snapshots as opaque application data and use the neutral sequence helper only where its application protocol needs it.

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

Both use the same PartyGameKit base APIs without changing the library.

## LAN host constraint

The direct LAN WebSocket listener requires a server-capable runtime. A browser can initiate WebSocket connections but cannot be the raw inbound WebSocket listener.

This is a transport capability constraint, not a reason to model `Host` or `SharedScreen` in PartyGameKit.

## Historical v0.1 implementation

`0.1.0-preview.1` included `RoomSession`, `PlayerId`, `ClientRole`, `AuthorityId`, player capacity/admission, session continuity tied to players, and public/private snapshots.

Those APIs were useful validation scaffolding but crossed the correct ownership boundary. `[15]`–`[17]` deliberately replaced that preview model with the communication-only `0.2` line.

See [Public API review](public-api.md), [Package boundaries](packages.md) and [Migration 0.1 → 0.2](migration-0.1-to-0.2.md).

## Architecture invariant

A minimal PartyGameKit consumer can:

1. connect generic clients;
2. exchange opaque messages;
3. target or broadcast messages;
4. detect disconnect/timeout;
5. resume the same neutral logical peer on a replacement connection when configured;
6. use LAN discovery or a directly supplied descriptor;
7. choose direct LAN or optional backend-assisted SignalR without changing product payload semantics;
8. do all of the above without defining `Player`, `Host`, `SharedScreen`, lobby, score or game state.

`samples/CommunicationDemo` exercises this invariant over both real LAN and SignalR transports in CI.
