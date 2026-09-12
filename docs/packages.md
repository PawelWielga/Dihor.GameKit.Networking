# Package boundaries

PartyGameKit packages are being corrected from the historical `0.1.0-preview.1` session/game model to a communication-only architecture.

The authoritative decision is [Communication boundary](communication-boundary.md).

## Package rule

A PartyGameKit package may depend on communication concepts. It must not require consumers to adopt players, hosts, shared screens, game sessions, authority policy, scoring or game-state projections.

Breaking prerelease changes are expected in `[16]` and `[17]`.

## Current packages and decisions

### `PartyGameKit.Core`

**Decision: RENAME/GENERALIZE, split/collapse, or remove.**

The current package mixes valid communication primitives with product semantics.

Retain/generalize only:

- `ConnectionId`;
- optional stable neutral peer identity for reconnect;
- neutral connection-health/resume primitives;
- optional generic sequence/deduplication utility.

Move out:

- `PlayerId` as player semantics;
- `AuthorityId`;
- `ClientRole`;
- `RoomSession` and lifecycle;
- player capacity/admission/leave policy;
- public/private player snapshot projections;
- authority-bound game-state publication.

The package name itself is not protected. If the remaining responsibilities fit better under Protocol/Transport abstractions, `[16]` should remove or split Core rather than preserve an artificial layer.

### `PartyGameKit.Protocol`

**Decision: KEEP/GENERALIZE.**

Owns the language-neutral communication contract:

- protocol version;
- neutral envelopes/message identifiers/correlation;
- connection handshake/version validation;
- heartbeat/connectivity control;
- neutral resume control;
- opaque application-message boundary;
- connection descriptor codecs.

It must no longer require room/player/role/authority/state-snapshot semantics.

### `PartyGameKit.Transport.Abstractions`

**Decision: KEEP.**

Owns technology-neutral connection/message events, errors, send/broadcast/disconnect/stop/cancellation/disposal.

`IGameTransport` should be renamed to neutral transport vocabulary and must not depend on a product/session Core package merely to obtain `ConnectionId`.

### `PartyGameKit.Transport.InMemory`

**Decision: KEEP.**

Deterministic reference/test transport. Public game/session terminology should be neutralized where present.

### `PartyGameKit.Transport.Lan`

**Decision: KEEP.**

Direct LAN WebSocket host/client transport remains reusable communication infrastructure.

`LanJoinDescriptor` should become a technical LAN connection descriptor. LAN transport must not own party/player/session admission semantics.

### `PartyGameKit.Discovery.Lan`

**Decision: KEEP/GENERALIZE.**

UDP discovery remains optional. It discovers connection endpoints/services, not product game sessions.

`DiscoveredSessionRegistry` and discovery payload vocabulary should be renamed accordingly. Discovery failure must never prevent direct connection when a valid connection descriptor is available.

## Browser package

### `@partygamekit/client`

**Decision: KEEP/GENERALIZE.**

Target base API:

- connect/disconnect;
- send/receive opaque messages;
- connection state;
- heartbeat/connectivity;
- resume/reconnect;
- protocol compatibility;
- connection descriptor parsing;
- optional generic ordering/deduplication helpers.

Move out of the base SDK:

- player/shared-screen/host roles;
- player identity as product identity;
- room admission/capacity;
- authority policy;
- public/private game-state projection semantics.

PartyBeam may build a product-specific SDK/facade above the base client if useful.

## Dart package

### `partygamekit_protocol`

**Decision: KEEP/GENERALIZE.**

The Dart package remains a thin implementation of the PartyGameKit communication protocol and connection descriptors.

Państwa Miasta keeps its own player model, host-authoritative game engine, game snapshot schema and lifecycle policy above this package.

## Samples

`SharedCounter` and `DungeonPrototype` remain **consumer samples**, not package design sources whose domain types must be promoted into PartyGameKit.

They may locally define players, authority, game state and projections. Issue `[17]` adds or converts at least one sample into a neutral communication demonstration.

## Target dependency direction

Exact project names are finalized in `[16]`, but the dependency graph must resemble:

```text
communication identities / protocol primitives
                 │
       ┌─────────┴─────────┐
       ▼                   ▼
transport abstractions   protocol/control
       │                   │
       ├── in-memory       └── connection descriptor
       └── LAN WebSocket
                 │
                 ▼
          optional LAN discovery
```

Consumers sit above all PartyGameKit packages:

```text
PartyBeam / Państwa Miasta / other apps
                │
                ▼
          PartyGameKit packages
```

No base package may depend upward on PartyBeam or concrete game semantics.

## Packaging consequence

The next prerelease after the boundary refactor must be treated as a breaking prerelease update. Package metadata and migration notes must clearly state that `0.1.0-preview.1` room/player/session APIs are superseded.