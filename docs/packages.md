# Package boundaries

PartyGameKit `0.2.0-preview.1` is a communication/networking library. The historical `0.1.0-preview.1` room/player/session surface has been removed from the active package line.

The authoritative ownership decision is [Communication boundary](communication-boundary.md).

## Package rule

A PartyGameKit package may depend on communication concepts. It must not require consumers to adopt players, hosts, shared screens, game sessions, authority policy, scoring or game-state projections.

## Current packages

### `PartyGameKit.Core`

Contains small communication-neutral primitives:

- transient `ConnectionId`;
- optional stable `PeerId` used for connection continuity;
- optional technical `ChannelId`;
- `ConnectionDescriptor`;
- `ConnectionContinuityCoordinator` and peer-presence state;
- `MessageSequence` / `SequenceGate` generic ordering helpers.

It does not contain player membership, room lifecycle, product roles, authority or game snapshots.

### `PartyGameKit.Protocol`

Owns the language-neutral protocol-v2 communication contract:

- neutral envelopes, message IDs and correlation IDs;
- connect/resume/heartbeat/disconnect control messages;
- opaque `application.message` boundary;
- protocol compatibility validation;
- `ConnectionDescriptor` JSON/URI codecs;
- LAN discovery announcement codec.

### `PartyGameKit.Transport.Abstractions`

Owns technology-neutral connection/message events and operations:

- `IMessageTransport`;
- transient connection open/close lifecycle;
- receive events;
- targeted send;
- broadcast;
- transport fault reporting;
- cancellation/disposal.

### `PartyGameKit.Transport.InMemory`

Deterministic transport for tests, package consumers and applications that need an in-process implementation of the same message contract.

### `PartyGameKit.Transport.Lan`

Direct LAN WebSocket communication using the same neutral protocol/transport boundary. It validates connect/resume admission but does not own product/player admission or game-session rules.

### `PartyGameKit.Discovery.Lan`

Optional UDP discovery for technical `ConnectionDescriptor` endpoints. Discovery is convenience infrastructure only; a valid descriptor can always be supplied directly.

## Browser package

### `@partygamekit/client`

The `0.2.0-preview.1` browser client exposes communication-neutral capabilities:

- connect/disconnect;
- send/receive opaque application messages;
- connection state;
- heartbeat;
- optional stable peer identity;
- resume/reconnect;
- protocol compatibility;
- `ConnectionDescriptor` parsing.

It does not require `player`, `host` or `shared-screen` roles and does not implement public/private game-state projection policy.

PartyBeam may build a product-specific facade above it if that improves its TV/controller UX.

## Dart package

### `partygamekit_protocol`

The Dart package is a thin implementation of the PartyGameKit v2 protocol and connection-descriptor contract.

It deliberately does not include a Dart game/session runtime. Państwa Miasta keeps its player model, host-authoritative game engine, snapshots and lifecycle policy above the adapter boundary.

## Reference sample

`samples/CommunicationDemo` is the active v0.2 reference sample. It demonstrates the real LAN WebSocket transport and UDP discovery with two generic peers, opaque messages, targeted/broadcast delivery and resume on a replacement connection.

The former `SharedCounter` and `DungeonPrototype` samples belonged to the historical v0.1 session-oriented API and were retired from the active tree during `[17]`. Their implementations remain available in Git history and the v0.1 tag; they are not compatibility requirements for the corrected API.

## Dependency direction

```text
        PartyBeam / Państwa Miasta / other apps
                         │
                         ▼
              application/game protocol
                         │
                         ▼
                  PartyGameKit 0.2
        ┌────────────────┼────────────────┐
        ▼                ▼                ▼
      Core            Protocol     Transport abstractions
        │                │                │
        │                │         ┌──────┴──────┐
        │                │         ▼             ▼
        │                │    InMemory       LAN WebSocket
        │                │                       │
        └────────────────┴────────────── optional LAN discovery
```

No base package may depend upward on PartyBeam or concrete game semantics.

## Package-only validation

CI builds NuGet packages first, restores `packaging/consumer` using only those generated artifacts and then validates:

- opaque `application.message` exchange;
- neutral connection events;
- stable `PeerId` resume on a replacement `ConnectionId`;
- absence of source-project dependencies in the consumer.

This catches packaging/dependency mistakes that ordinary project-reference tests cannot catch.
