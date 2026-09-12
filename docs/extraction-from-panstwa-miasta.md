# Extraction boundary from Państwa Miasta

This document records what PartyGameKit should actually extract from `PawelWielga/panstwa-miasta` after the boundary correction in issue `[15]`.

The earlier v0.1 interpretation extracted too much product/session meaning. The corrected rule is:

> Reuse proven networking and reconnect behavior. Do not turn Państwa Miasta player/session/game policy into generic PartyGameKit API.

The detailed target classification is in [Communication boundary](communication-boundary.md).

## Cross-language decision

PartyGameKit reuses a versioned language-neutral communication protocol and behavior, not Dart implementation classes.

```text
                  communication protocol
                 + canonical JSON fixtures
                           │
          ┌────────────────┼────────────────┐
          ▼                ▼                ▼
         C#               Dart          TypeScript
```

A NuGet package is not the cross-language contract. Flutter/Dart cannot directly consume CLR types, and duplicating a game/session engine in multiple languages would be the wrong reuse boundary.

## What Państwa Miasta proves at communication level

The reference implementation proves reusable networking behavior:

- a network connection has a transient identity;
- a logical client may need a stable identity to resume after a replacement connection;
- reconnect can rebind a replacement connection without creating a duplicate logical client;
- heartbeat/timeout policy can be separated from game rules;
- reconnect credentials are distinct from public display identity and from the socket itself;
- a local WebSocket path can operate without Internet/cloud;
- LAN discovery can be separate from gameplay/message transport;
- direct connection remains possible when discovery is unavailable;
- protocol version mismatch must fail deterministically;
- monotonic sequence metadata can be used to reject stale messages/state;
- transport choice can remain separate from application/game payload meaning;
- automatic host migration is not a proven generic requirement.

These behaviors justify PartyGameKit communication primitives.

## What Państwa Miasta does not justify as PartyGameKit ownership

The following are real application behaviors, but they stay in Państwa Miasta or another consumer:

- player membership and player capacity;
- lobby/open/closed game-session lifecycle;
- host/player/shared-screen/controller roles;
- host authority policy;
- whether disconnect preserves a player slot;
- explicit player leave policy;
- Countries & Cities state/schema;
- public/private/player state projections;
- game-state restore policy;
- categories, answers, voting, scoring and phases;
- `CountriesCitiesGameEngine`;
- UI/platform concerns such as Flutter `ChangeNotifier`, SharedPreferences and Android foreground services.

PartyBeam may have similar concepts, but that still does not make them communication-library concerns.

## Current code to corrected PartyGameKit mapping

| Państwa Miasta concept | Corrected classification | PartyGameKit direction |
| --- | --- | --- |
| `MultiplayerTransport` / host/client transport behavior | Communication infrastructure | Transport abstractions and concrete transports. |
| `InMemoryMultiplayerTransport` | Generic test technique | In-memory reference/test transport. |
| `LocalLanMultiplayerTransport` | Communication infrastructure | LAN WebSocket transport. |
| socket connection identity | Generic | `ConnectionId`. |
| stable reconnecting client identity | Generic only at communication level | Neutral `PeerId`/logical client id, not `PlayerId`. |
| reconnect credential/token | Generic communication security | Neutral resume credential handling. |
| heartbeat / connection-health coordinator | Generic communication behavior | Connection health + timeout primitives. |
| reconnect coordinator | Mixed | Keep neutral resume/rebind mechanics; keep product/game recovery policy outside. |
| `LocalLanGameController` | Mixed composition root | Do not copy. Product/session/UI orchestration remains consumer-owned. |
| `LocalLanPlayerRegistry` | Product/session semantics mixed with useful reconnect behavior | Do not create generic player registry. Extract only neutral peer-to-connection rebinding. |
| `GameStateSnapshot` | Consumer state plus generic ordering idea | Game schema stays consumer-owned; optional generic sequence/dedupe utility may remain. |
| snapshot publisher | Product/game replication policy | Consumer-owned. PartyGameKit transports opaque payloads. |
| base message envelope metadata | Generic communication protocol | Keep version/message id/correlation/application payload envelope. |
| join/rejoin messages | Mixed | Replace with neutral connection handshake/resume control messages. Product admission stays outside. |
| Countries & Cities protocol messages | Game-specific | Stay in Państwa Miasta. |
| `CountriesCitiesGameEngine` | Game-specific authority logic | Stay in Państwa Miasta. |
| UDP discovery | Communication infrastructure | LAN discovery of connection endpoints/services. |
| discovered room/session metadata | Mixed | Keep only technical endpoint/protocol/routing metadata; product labels/state stay outside. |
| QR/manual IP+port connection | Mixed UI + technical descriptor | PartyGameKit provides a technical connection descriptor; product renders QR and defines invitation UX. |
| Android foreground service | Platform-specific | Application/platform adapter. |
| host migration experiments | Experimental product policy | Outside PartyGameKit base contract. |

## Identity boundary

The corrected communication identity model is:

```text
ConnectionId = transient transport connection
PeerId       = optional stable logical communication identity used for resume
```

A PartyBeam/Państwa Miasta player id may be mapped to a `PeerId`, but PartyGameKit does not interpret that mapping.

Two consequences are important:

1. communication resume does not imply player rejoin semantics;
2. a timeout/disconnect does not imply that a player leaves, loses a slot or that a game should pause/end.

Those are consumer decisions.

## Protocol boundary

PartyGameKit protocol owns only communication lifecycle/control and an opaque application-message boundary.

Keep/generalize:

- protocol version;
- message envelope;
- connection handshake/version validation;
- heartbeat/connectivity;
- neutral resume;
- connection descriptor serialization;
- message ids/correlation;
- opaque application messages.

Move out:

- room-full/player-admission semantics;
- host/player/shared-screen roles;
- authority id;
- player leave lifecycle;
- required game snapshot message;
- public/private player projection targeting.

Consumer protocols may be versioned independently and transported as opaque PartyGameKit application payloads.

## Ordering and state replication boundary

Państwa Miasta proves that monotonic ordering is useful. It does not prove that PartyGameKit should own a generic game snapshot engine.

PartyGameKit may retain an optional neutral sequence/deduplication helper. Państwa Miasta continues to own:

- authoritative game snapshot schema;
- snapshot construction;
- per-player/public projection rules;
- latest-game-state restore policy;
- authority semantics.

Those snapshots can travel through PartyGameKit as ordinary consumer messages.

## LAN and discovery boundary

The direct LAN behavior remains a strong extraction target:

- server-capable local runtime hosts a WebSocket listener;
- clients connect over LAN without cloud dependency;
- transport errors/close/cleanup are communication concerns;
- UDP discovery is optional;
- direct descriptor connection works without discovery.

A browser's inability to accept raw inbound WebSocket connections is a transport capability constraint, not evidence for a generic `Host` or `SharedScreen` role.

## Consumer mapping examples

### Państwa Miasta

```text
CountriesCities PlayerId ───────┐
                                ├─ mapped by the app to PartyGameKit PeerId
CountriesCities game session ───┘

CountriesCities commands/snapshots
               │
               ▼
opaque PartyGameKit application messages
```

### PartyBeam

PartyBeam owns TV/pilot/controller/player/party/authority semantics and may map any connected product participant to a PartyGameKit peer. PartyGameKit does not need to know which peer is the TV or which phone is a player.

## Compatibility checklist after `[16]`

The corrected library should still preserve these networking invariants:

- transient connection identity is distinct from stable resume identity;
- reconnect may rebind a replacement connection to the same neutral peer;
- resume does not create duplicate logical peers;
- reconnect credentials are not public identity;
- heartbeat/timeout is configurable and does not invoke product leave semantics;
- protocol version mismatch fails deterministically;
- opaque consumer payloads can be targeted/broadcast;
- LAN works without Internet/cloud;
- discovery is optional for direct connection;
- application/game logic does not depend on WebSocket/UDP implementation types;
- game/player/authority/session semantics remain above PartyGameKit.

## Historical note

Issues `[01]`–`[14]` were valuable implementation experiments and proved transport, protocol interoperability, discovery and reconnect mechanics. They also demonstrated that validating the same abstraction against two game-like samples is not enough to prove that the abstraction belongs in a communication library.

Issues `[15]`–`[17]` intentionally correct that ownership mistake before additional transports are added.