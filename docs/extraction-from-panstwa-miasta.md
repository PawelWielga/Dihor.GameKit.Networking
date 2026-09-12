# Extraction boundary from Państwa Miasta

This document records the architecture decision for PartyGameKit issue `[01]`. It is based on the current multiplayer implementation in `PawelWielga/panstwa-miasta`, not on an imagined greenfield framework.

## Decision summary

PartyGameKit will reuse **multiplayer semantics and a language-neutral wire contract**, not Dart implementation classes.

The cross-language boundary for v0.1 is:

```text
                  versioned JSON protocol
                 + canonical JSON fixtures
                           │
          ┌────────────────┼────────────────┐
          │                │                │
     C# contracts      Dart adapter      TypeScript SDK
          │                │                │
          └──────── same wire semantics ────┘
```

The protocol specification and canonical fixtures are the compatibility source of truth. C#, Dart and TypeScript each implement the small set of protocol models they need and verify them against the same fixtures.

A NuGet package is **not** the cross-language contract. Flutter/Dart cannot consume CLR types directly, and duplicating a game engine in several languages would create three implementations of game rules rather than one reusable multiplayer foundation.

PartyGameKit therefore owns transport/session/replication semantics. Each game owns its domain model, commands and state payload schema.

## What the reference implementation proves

The current Państwa Miasta implementation already demonstrates these reusable behaviors:

- a stable player identity can outlive an individual network connection;
- reconnecting with the same player identity replaces/rebinds the connection instead of creating a duplicate player;
- players with identical display names remain distinct because identity is based on `playerId`;
- a temporary disconnect during an active game does not imply permanent leave and does not erase game-owned state;
- lobby leave can free capacity while active-session disconnect preserves the player's slot/state for reconnect;
- the host is authoritative for game state and clients do not run the authoritative `CountriesCitiesGameEngine`;
- authoritative snapshots carry monotonically increasing sequence numbers and stale/out-of-order snapshots are rejected;
- reconnect restores the latest state instead of replaying the full history;
- heartbeat, timeout and reconnect-window policy are separate from the game rules;
- LAN discovery is separate from gameplay transport and can fail while direct QR/manual connection remains possible;
- the production LAN transport works without Internet or a cloud backend;
- automatic host migration is still experimental and is **not** a production guarantee.

These are the behaviors PartyGameKit should preserve. The exact Dart classes, Flutter state management and Countries & Cities payloads are not the reusable API.

## Current code to PartyGameKit mapping

| Państwa Miasta concept | Classification | PartyGameKit direction | Notes |
| --- | --- | --- | --- |
| `MultiplayerTransport`, `MultiplayerRoom`, `MultiplayerHost`, `MultiplayerClient` | Generic behavior, current API is Dart-specific | `PartyGameKit.Transport.Abstractions` | Keep transport I/O behind small technology-neutral contracts. Do not make the transport the owner of session truth. |
| `InMemoryMultiplayerTransport` | Generic test technique | in-memory transport/test harness | Proven useful for deterministic lifecycle tests before real sockets. |
| `LocalLanMultiplayerTransport` | Infrastructure | later `PartyGameKit.Transport.Lan` | `dart:io`, HTTP/WebSocket listener, IP validation and socket cleanup remain transport implementation details. |
| `LocalLanGameController` | Mixed composition root | **Do not copy as a Core type** | It combines Flutter `ChangeNotifier`, product UI concerns, session orchestration, discovery, reconnect, platform service wiring and Countries & Cities flows. Extract behaviors into smaller framework components only when their issues require them. |
| `LocalLanPlayerRegistry` | Mixed, mostly reusable semantics | session/player lifecycle in Core | Stable identity, connected/disconnected state and no duplicate player on rejoin are generic. `CountriesCitiesSessionState` coupling must disappear. |
| `LocalLanClientIdentityStore` | Generic client responsibility with platform persistence | client SDK/adapter concern | Stable `PlayerId` and reconnect credential are generic concepts. SharedPreferences itself is Flutter/platform-specific. |
| reconnect credential/token | Generic security/session concept | protocol/client SDK + host validation | Keep it distinct from public player profile and network connection identity. Raw credentials must not be broadcast. |
| `LocalLanConnectionHealthCoordinator` | Generic orchestration behavior | presence/reconnect orchestration | Configurable heartbeat, timeout and reconnect window are reusable. Dart `Timer` implementation is not. |
| `LocalLanClientReconnectCoordinator` | Generic behavior plus LAN discovery detail | generic reconnect policy + LAN adapter integration | Reconnect preserves identity; discovery can refresh a connection target. Discovery is not required for reconnect semantics themselves. |
| `GameStateSnapshot` | Mixed | generic snapshot envelope + game-owned payload | `roomId`, authority, target/projection and sequence metadata are reusable. Categories, letters, submissions, votes, scores and other Countries & Cities state stay in the game. |
| `LocalLanSnapshotPublisher` | Generic replication behavior | snapshot sequencing/publication | Monotonic sequence, latest-state publication and stale rejection are reusable. Payload construction is game-owned. |
| `BaseMultiplayerMessage` metadata | Generic protocol idea | versioned protocol envelope | Preserve stable JSON field names and optional correlation metadata where justified. Do not copy the current class hierarchy. |
| `PlayerHelloMessage`, `ClientRejoinMessage`, heartbeat/room-close messages | Generic protocol behavior | PartyGameKit protocol messages | Rework around stable IDs, transient `ConnectionId`, roles and explicit accept/reject results. |
| Countries Cities protocol messages | Game-specific | stay in `panstwa-miasta` / game package | `countries-cities:*`, answer/vote/wheel/round messages never become PartyGameKit Core/Protocol domain concepts. |
| `CountriesCitiesGameEngine` | Game-specific authority logic | stay in `panstwa-miasta` | It is evidence for host-authoritative behavior, not code to extract. |
| `LanDiscoveredRoom` / UDP broadcaster/listener | Generic discovery pattern with product metadata | later LAN discovery + portable `JoinDescriptor` | Stable room identity, protocol version, endpoint and expiry are reusable. Room labels/app version/game-state strings are optional product metadata, not Core state. |
| QR payload/manual IP + port + room code | Generic join behavior | `JoinDescriptor` | PartyGameKit should provide deterministic payload data. QR image rendering belongs to product/UI code. |
| Android foreground service | Platform-specific | stay in application/platform adapter | It keeps Android alive but is not multiplayer domain logic. |
| host migration coordinator/messages | Experimental | outside v0.1 production contract | Host-loss detection is required; automatic host migration is not. Revisit only after a working cross-game need exists. |

## Generic versus game-specific boundary

### PartyGameKit may own

The v0.1 foundation can own concepts that describe multiplayer infrastructure independently of a game:

- `RoomId` / session identity;
- stable `PlayerId`;
- transient `ConnectionId`;
- logical `Host` / `AuthorityId` or equivalent authority identity;
- `ClientRole` where a player and a non-player shared screen must be distinguished;
- join, accept/reject, leave, disconnect and rejoin semantics;
- capacity/admission results;
- connection/presence state;
- protocol version and message envelope metadata;
- transport-neutral send/receive/disconnect contracts;
- heartbeat and reconnect policy;
- snapshot sequence metadata, targeting/projection metadata and stale-state rejection;
- LAN connection/join descriptor and discovery metadata in the LAN package.

These abstractions are justified by current Państwa Miasta behavior or by the concrete PartyBeam requirement that a shared browser/TV can participate without being treated as a scored player.

### The game must own

PartyGameKit must not know the schema or meaning of game state or player intent. The following stay outside generic packages:

- `Category`, `Answer`, `Letter` and the Countries & Cities round/review/scoring model;
- dungeon concepts such as `Monster`, `Loot`, `Tile`, `Attack`, inventory rules or action points;
- concrete phase enums such as `answering`, `review` or `results`;
- score calculation, validation, random draws and game-specific timing;
- concrete game commands such as submit answer, vote, move or attack;
- serialization of a game's domain payload beyond carrying it as an opaque/versioned game-owned payload.

A game may define its own message/payload schema on top of PartyGameKit's envelope. PartyGameKit routes and sequences it without interpreting its meaning.

## Player identity, connection identity and presence

The reference implementation already proves that `playerId` cannot be the WebSocket identity:

1. the same `playerId` can reconnect over a replacement connection;
2. the old connection is replaced without duplicating the player;
3. the player can remain part of an active session while disconnected;
4. two players with the same display name remain different because their IDs differ.

PartyGameKit will therefore model these separately:

```text
PlayerId      stable identity inside the session
ConnectionId  transient transport connection
Presence      connected / disconnected / left semantics
```

A reconnect credential proves that a new connection may reclaim an existing player identity. It is not itself the player ID, must not be part of the public player profile, and must not appear in shared projections.

Permanent leave is an explicit lifecycle operation/policy. A dropped connection or missed heartbeat is not equivalent to leave.

## Host and authority

Państwa Miasta currently runs host-authoritative: the host owns the canonical `CountriesCitiesGameEngine` state and publishes snapshots. That validates an authority concept, but it does not justify binding authority to a socket object.

PartyGameKit should model authority logically. A transport connection can disappear and be rebound while the player's/session identity remains stable.

For v0.1:

- authority identity is independent of `ConnectionId`;
- host loss can be detected and represented;
- the framework may pause/wait/fail deterministically according to the implemented policy;
- **automatic host migration is not a v0.1 requirement**.

The reference app contains experimental host-migration code and protocol messages, but its own production documentation explicitly does not claim that flow as supported. PartyGameKit should not elevate an experiment into a stable abstraction.

## Snapshot and replication boundary

`GameStateSnapshot` in Państwa Miasta is deliberately rich because it serializes the whole Countries & Cities game. Only part of it is reusable.

PartyGameKit should own replication metadata such as:

```text
room/session identity
protocol/schema metadata
sequence number
authority identity
target/projection identity
game-owned payload
```

The game owns the payload schema.

The reusable behavioral requirements are:

- authority increments a monotonic sequence;
- a client ignores a snapshot whose sequence is not newer than the latest applied snapshot;
- the latest authoritative state can be sent directly to a late joiner/rejoining client;
- public/shared-screen and private per-player projections can be targeted separately;
- transport choice does not change snapshot semantics.

Chunking and maximum message sizes are transport/protocol concerns and should be introduced only when the concrete LAN implementation requires them, not as Core state concepts.

## Protocol boundary and compatibility strategy

### Source of truth

PartyGameKit v0.1 will define a small, versioned, language-neutral JSON wire protocol with canonical fixtures/test vectors stored in the repository.

The fixtures must be consumable verbatim by:

- C# contract tests;
- Dart compatibility tests;
- TypeScript client tests.

The fixture files, stable serialized names and protocol-version rules are the interoperability contract. CLR class names, Dart class names and TypeScript interface names are implementation details.

### Versioning

The current Państwa Miasta protocol already versions discovery/handshake because reconnect/security changes have required breaking revisions. PartyGameKit keeps explicit protocol versioning from the beginning.

A version mismatch must produce an explicit deterministic rejection, not a parser crash or a partially joined room.

Unknown optional fields should remain forward-compatible where safe. Unknown application/game message types may be carried as opaque game messages rather than forcing PartyGameKit to know every game's command set.

### Why not generated contracts yet

Generated code could be added later if maintaining three thin protocol implementations becomes costly, but it is not needed to prove v0.1. Introducing OpenAPI/Protobuf/Schema code generation now would expand tooling and compatibility surface before the actual contract has stabilized.

The minimum viable strategy is shared fixtures plus explicit implementations. YAGNI applies.

## LAN and browser boundary

The production Państwa Miasta LAN path proves that a local server can host HTTP/WebSocket traffic without a cloud backend. Its Dart runtime can bind a listening socket.

A pure browser/PWA cannot accept arbitrary inbound HTTP/WebSocket connections. Therefore v0.1 direct LAN WebSocket hosting requires a server-capable local runtime such as a .NET/native/desktop/TV process or companion process.

A browser may still be the shared-screen **client** and may run on the same device as a companion host. PartyGameKit must keep these roles separate:

```text
shared-screen role != network listener != authority (in the general model)
```

A pure-browser direct-host topology can be revisited with WebRTC/backend signaling in the post-v0.1 transport work. The initial LAN transport must not pretend the browser can listen like `dart:io` or ASP.NET Core.

## Discovery and join boundary

Państwa Miasta uses UDP broadcast discovery, deduplicates rooms by stable `roomId`, refreshes `lastSeen`, expires stale advertisements and retains QR/manual connection fallback.

PartyGameKit should keep the same separation:

- discovery advertises how to find a session;
- the gameplay protocol remains on the gameplay transport;
- discovery failure never invalidates a valid direct join descriptor;
- QR rendering is UI/product code;
- a portable `JoinDescriptor` is protocol/infrastructure data, not game state.

UDP broadcast is a proven first LAN implementation, not a Core dependency. Other discovery mechanisms can be adapters later.

## Behavior compatibility checklist

Every multiplayer issue after this audit should re-check the following invariants as applicable:

- stable `PlayerId` survives reconnect;
- `PlayerId` is not `ConnectionId`;
- replacing a connection for the same player does not duplicate the player;
- duplicate display names do not collapse identities;
- disconnect does not automatically remove the player/session state;
- explicit leave has deterministic capacity behavior;
- join/admission failure is explicit;
- host/authority is logical, not a particular socket instance;
- clients do not authoritatively mutate game state;
- snapshot sequence is monotonic at the authority;
- stale/equal snapshots are ignored by clients;
- reconnect receives the current authoritative snapshot;
- heartbeat/reconnect policy is configurable and game-agnostic;
- LAN gameplay remains possible with Internet unavailable;
- discovery is optional for direct connection;
- game logic does not reference WebSocket, SignalR, WebRTC, IP, ports, UDP, Flutter or Android APIs.

## Recommended first repository layout

Issue `[02]` should bootstrap only the projects needed before real LAN transport exists:

```text
PartyGameKit/
├── PartyGameKit.slnx
├── src/
│   ├── PartyGameKit.Core/
│   ├── PartyGameKit.Protocol/
│   └── PartyGameKit.Transport.Abstractions/
├── tests/
│   ├── PartyGameKit.Core.Tests/
│   ├── PartyGameKit.Protocol.Tests/
│   └── PartyGameKit.Transport.Tests/
├── protocol/
│   └── fixtures/
└── docs/
```

Dependency direction:

```text
PartyGameKit.Core        PartyGameKit.Protocol
       │                         │
       └────────┬────────────────┘
                ▼
  application/composition layer
                │
                ▼
PartyGameKit.Transport.Abstractions
```

The exact project-reference graph should keep Core independent of concrete transports and avoid making protocol DTOs the Core domain model. A composition/application layer can depend on both as concrete orchestration appears in later issues.

Do **not** create SignalR, WebRTC, database, Redis, authentication, Android, Flutter or React infrastructure in `[02]`.

Later issues can add only the validated packages they require, for example:

```text
src/PartyGameKit.Transport.Lan/
sdk/dart/partygamekit_protocol/
sdk/typescript/client/
samples/...
```

Names for later SDK folders are intentionally not a public compatibility promise yet.

## How Państwa Miasta will validate PartyGameKit

Państwa Miasta remains a Flutter/Dart application with its game engine and UI intact.

Validation happens incrementally:

1. PartyGameKit defines canonical JSON fixtures and version rules in `[03]`.
2. C# tests prove the PartyGameKit contract against those fixtures.
3. In `[10]`, a minimal Dart protocol adapter/package reads the **same files** and proves join/rejoin/identity/snapshot semantics match the current app.
4. If integration requires changes in `panstwa-miasta`, they are made on a dedicated branch/PR in that repository and linked to the PartyGameKit PR.
5. Existing Countries & Cities multiplayer tests remain the behavioral regression suite; its `CountriesCitiesGameEngine` remains game-owned.

This gives real cross-language compatibility without attempting to consume NuGet from Flutter or copy the Countries & Cities engine into PartyGameKit.

## v0.1 non-goals

The following are explicitly outside the v0.1 foundation unless a later `[01]`-`[14]` issue says otherwise:

- automatic host migration;
- cloud/backend-required gameplay;
- SignalR transport;
- WebRTC DataChannel transport;
- automatic transport fallback/`Auto` mode;
- a pure-browser process acting as the LAN WebSocket listener;
- persistent user accounts, matchmaking or authentication systems;
- database/Redis-backed room state;
- a general-purpose event-sourcing system or command bus;
- generated multi-language contracts before fixture-based interoperability proves insufficient;
- Flutter UI/state management abstractions;
- Android foreground/background-service management;
- QR image rendering;
- Countries & Cities rules or dungeon-specific rules.

## Consequences for the existing roadmap

The initial architecture documents used `host transfer` as if it were part of the first reusable core and did not clearly distinguish a browser shared screen from a server-capable LAN host. The audit changes those assumptions:

- v0.1 guarantees host-loss detection/reconnect semantics, not automatic host migration;
- protocol/fixtures become an explicit project boundary because Dart, C# and TypeScript must interoperate;
- the direct WebSocket LAN host must be server-capable;
- shared-screen is a client role independent from player identity and network-listener identity;
- concrete game commands/events remain application-owned payloads rather than a generic typed command hierarchy in Core.

The architecture, session-model and roadmap documents are updated alongside this decision so later issues do not implement the superseded assumptions.