# Roadmap

PartyGameKit is developed in the exact ordered backlog tracked by GitHub issue #2. The sequence is intentional: each abstraction is introduced only after the previous behavior is implemented and tested.

The extraction decision and evidence are documented in [Extraction from Państwa Miasta](extraction-from-panstwa-miasta.md).

## v0.1 foundation

### `[01]` Audit Państwa Miasta and define the extraction boundary

- classify reusable behavior versus game-specific code;
- decide the C#/Dart/TypeScript interoperability boundary;
- correct speculative assumptions in the initial architecture.

Decision: use a versioned language-neutral JSON wire protocol plus canonical fixtures. Do not copy the Dart game engine or pretend Flutter can consume NuGet directly.

### `[02]` Bootstrap solution, quality gates and tests

Create only the initial .NET projects required by the audit:

- `PartyGameKit.Core`;
- `PartyGameKit.Protocol`;
- `PartyGameKit.Transport.Abstractions`;
- corresponding tests and CI;
- `protocol/fixtures/` for cross-language vectors.

No SignalR, WebRTC, database, Redis or authentication yet.

### `[03]` Versioned protocol contracts and identity primitives

Define the small language-neutral infrastructure protocol:

- room/player/connection identity;
- client roles;
- logical authority identity;
- protocol envelope/versioning;
- join/accept/reject/leave/rejoin;
- canonical JSON fixtures.

Game commands remain opaque/game-owned.

### `[04]` Room/player/session lifecycle

Implement transport-independent lifecycle semantics:

- create/join/admission/capacity;
- explicit leave;
- temporary disconnect;
- rejoin without duplicate player;
- logical host/authority;
- non-player shared-screen role.

Automatic host migration is not required.

### `[05]` Transport abstractions and in-memory transport

Introduce only the communication operations needed by implemented lifecycle behavior, plus deterministic in-memory tests.

Core remains independent from concrete transports.

### `[06]` Snapshot sequencing and client projections

Generalize the proven state replication behavior:

- monotonic sequence numbers;
- stale snapshot rejection;
- latest-state restore for join/rejoin;
- public/shared-screen and private-player targeting;
- opaque game-owned state payloads.

### `[07]` Presence, heartbeat and reconnect

Generalize configurable heartbeat/timeout/reconnect-window behavior with stable identity rebinding and current-snapshot restore.

Host loss is detected, but automatic host transfer is not promised by v0.1.

### `[08]` Direct LAN WebSocket transport

Add the first real transport with no cloud requirement.

The local listener must run in a server-capable runtime. A pure browser cannot be the inbound WebSocket listener; it can still be a shared-screen client connected to a local host/companion process.

### `[09]` LAN discovery and portable join descriptors

Add discovery separately from gameplay transport:

- UDP broadcast as the first proven implementation;
- dedupe/refresh/expiry;
- deterministic join descriptor suitable for QR/manual entry;
- direct join remains possible when discovery is blocked.

### `[10]` Validate against Państwa Miasta + Dart interoperability

Use the exact canonical fixtures from `[03]` in Dart and add the minimum adapter/package needed to prove:

- stable identity/rejoin semantics;
- protocol version compatibility;
- snapshot ordering behavior;
- existing Countries & Cities game rules remain game-owned.

Cross-repo changes use a dedicated `panstwa-miasta` branch/PR.

### `[11]` TypeScript browser client SDK

Add a browser-friendly TypeScript implementation of the same protocol with LAN WebSocket client, join/rejoin, projection handling and reconnect.

React-specific code stays outside the base SDK unless concrete use proves it necessary.

### `[12]` End-to-end shared-screen reference sample

Prove the real PartyBeam-style topology:

- server-capable local host/authority;
- browser shared-screen client;
- 2+ phone/browser player clients;
- game-owned command/payload;
- public/private projections;
- reconnect without identity loss.

### `[13]` Dungeon-style second-game validation

Build a mechanically different sample specifically to break bad abstractions.

Dungeon concepts remain in the sample. If the prototype appears to require `Monster`, `Loot`, `Tile`, `Attack` or equivalent concepts in PartyGameKit Core, the abstraction is wrong.

### `[14]` Stabilize v0.1 packages and prerelease publishing

Only after both samples pass:

- review/remove accidental public API;
- settle actual package boundaries;
- document supported APIs;
- build NuGet/npm/Dart prerelease artifacts as applicable;
- publish compatibility/version policy;
- keep release prerelease (`0.1.0-alpha`/`preview`), not `1.0.0`.

## v0.1 completion criteria

The first meaningful v0.1 exists when:

- the generic Core and language-neutral protocol are tested;
- LAN host/join/reconnect works without Internet/cloud;
- C#, Dart and TypeScript prove compatibility where required;
- a real shared-screen sample works end to end;
- the dungeon sample validates that abstractions are not Countries & Cities-specific;
- consumers no longer need to copy session/networking plumbing between games.

## Post-v0.1 transports

These issues intentionally wait until the v0.1 model has survived real usage.

### `[15]` Backend-assisted rooms and SignalR

Add optional remote/cloud connectivity behind the same transport/session semantics. LAN remains backend-free.

### `[16]` WebRTC DataChannel

Add low-latency direct input after signaling/backend support exists. Validate with a latency-sensitive sample rather than distorting the turn-based dungeon sample.

### `[17]` Automatic transport selection/fallback

Only after LAN, SignalR and WebRTC are individually reliable, introduce deterministic `Auto` selection/fallback with diagnostics and explicit time budgets.

## Explicitly deferred ideas

Do not pull these into early Core without a concrete issue/requirement:

- automatic host migration;
- persistent accounts and matchmaking;
- database/Redis room persistence;
- analytics/telemetry framework;
- generic command/event sourcing framework;
- generated cross-language contracts before fixture-based compatibility proves insufficient;
- native UI frameworks/components;
- a browser pretending to be a raw LAN WebSocket server.
