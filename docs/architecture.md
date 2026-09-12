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
     LAN / SignalR / WebRTC
```

The central architectural rule is:

> PartyGameKit moves messages and maintains communication continuity. Consumers decide what those messages and connected peers mean.

## Target layers

### Communication identities and control protocol

PartyGameKit may define only identities required by communication itself:

- transient `ConnectionId`;
- optional stable neutral peer identity for resume/reconnect;
- optional neutral routing scope if a transport needs isolation.

These identities must not imply player, host, TV, controller, spectator, party or game-session semantics.

The language-neutral control protocol owns:

- protocol version and compatibility;
- connection handshake/version validation;
- heartbeat/connectivity messages;
- neutral resume request/accept/reject;
- message identifiers/correlation metadata;
- opaque application payload transport.

Application messages are consumer-owned. PartyGameKit must not require a game/session schema.

### Transport abstractions

Transport abstractions own technology-neutral communication operations:

- connection opened/closed/faulted events;
- receive message events;
- targeted send;
- broadcast/multicast where supported;
- disconnect, stop, cancellation and disposal;
- transport-neutral errors and diagnostics.

The current `IGameTransport` capability is valid but the name is too product-specific. Issue `[16]` will neutralize this vocabulary and remove any dependency on session/player Core types.

### Concrete transports

Concrete adapters remain separate packages:

- in-memory reference/test transport;
- direct LAN WebSocket transport;
- later optional SignalR/backend relay;
- later optional WebRTC DataChannel transport;
- later automatic selection/fallback above individually reliable transports.

Adding a transport must never add player/session/authority semantics to base APIs.

### Discovery and connection descriptors

Discovery advertises technical connection endpoints/services. It is separate from message transport.

A neutral connection descriptor may contain only data required to establish communication, for example transport kind, endpoint, protocol version and optional routing scope.

Product invitation concepts such as party join code, game id, display name or QR presentation belong to the consumer. PartyBeam may wrap a PartyGameKit connection descriptor inside its own invite payload.

### Client SDKs

TypeScript and Dart SDKs implement the same communication contract as C#.

The base SDKs may expose connect/disconnect/send/receive/resume, connection state, protocol compatibility and descriptor parsing. They must not require PartyBeam roles or player/game-state projections.

Framework UI/state-management concerns remain outside PartyGameKit.

## Cross-language boundary

The compatibility model remains:

```text
canonical JSON fixtures + documented version rules
                     │
        ┌────────────┼────────────┐
        ▼            ▼            ▼
       C#           Dart      TypeScript
```

The fixtures specify the PartyGameKit communication contract. Consumer/game protocols may be independently versioned by their owners.

## Identity and reconnect

Reconnect is a communication concern only when expressed neutrally:

```text
ConnectionId = current transport connection
PeerId       = optional stable logical communication identity
```

A reconnect credential may prove that a new connection can resume the same `PeerId`.

PartyGameKit may then rebind the replacement connection and report connectivity state. It must not decide whether that peer is a player, whether it occupies a slot, whether a game pauses or whether the participant should be removed.

## Routing scope

If a routing identifier is needed, it is a technical `ChannelId`/`ScopeId`-like concept, not a game session.

It may isolate delivery but does not own:

- lifecycle;
- player membership;
- capacity;
- roles;
- authority;
- score;
- game phase.

Current `RoomId` is retained only if `[16]` can justify it as this neutral routing concept; otherwise it moves to consumers.

## Ordering and snapshots

Monotonic ordering/deduplication can be a generic optional utility.

Game snapshot semantics are not base PartyGameKit responsibilities. Current public/private player projections, authority-bound snapshot publication and game-state restoration move to consumers.

Państwa Miasta may continue to publish its own authoritative game snapshots as opaque application messages and use a neutral sequence helper to reject stale payloads.

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

Both must use the same PartyGameKit base APIs without changing the library.

## LAN host constraint

The direct LAN WebSocket listener requires a server-capable runtime. A browser can initiate WebSocket connections but cannot be the raw inbound WebSocket listener.

This is a transport capability constraint, not a reason to model `Host` or `SharedScreen` in PartyGameKit.

## Historical v0.1 implementation

`0.1.0-preview.1` included `RoomSession`, `PlayerId`, `ClientRole`, `AuthorityId`, player capacity/admission, session continuity tied to players, and public/private snapshots.

Those APIs were useful validation scaffolding but crossed the correct ownership boundary. Issues `[15]`–`[17]` deliberately break that preview model.

See [Public API classification](public-api.md) for the per-abstraction decision and [Package boundaries](packages.md) for the intended package direction.

## Architecture invariant

A minimal PartyGameKit consumer must be able to:

1. connect two generic clients;
2. exchange opaque messages;
3. target or broadcast messages;
4. detect disconnect/timeout;
5. resume the same neutral logical peer on a replacement connection when configured;
6. do all of the above without defining `Player`, `Host`, `SharedScreen`, lobby, score or game state.