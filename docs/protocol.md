# Wire protocol

## Status

The `0.1.0-preview.1` protocol contains historical room/player/session messages. Issue `[15]` established that the target PartyGameKit protocol is communication-only. Production contracts are refactored in `[16]` and C#/Dart/TypeScript fixtures are aligned in `[17]`.

See [Communication boundary](communication-boundary.md).

## Target protocol layers

The PartyGameKit wire contract has two distinct layers:

1. **control protocol** owned by PartyGameKit;
2. **application messages** owned by the consumer and treated as opaque payloads.

```text
PartyGameKit control envelope
├── protocol compatibility
├── connection lifecycle
├── heartbeat/connectivity
├── neutral peer resume
└── application message
       └── opaque consumer payload
```

## PartyGameKit control responsibilities

The base protocol may define:

- integer protocol version;
- message type;
- message id;
- optional correlation id;
- connection handshake/version validation;
- transient connection identity;
- optional neutral stable peer identity for resume;
- heartbeat/connectivity metadata;
- resume request/accept/reject;
- connection close/error information;
- generic routing/sequence metadata only where communication requires it;
- opaque application-message envelope.

## Consumer responsibilities

The base protocol must not require:

- `PlayerId` or player admission;
- `Host`, `Player`, `SharedScreen`, controller or spectator roles;
- player capacity/room-full logic;
- `AuthorityId`;
- lobby or game-session lifecycle;
- required state snapshot messages;
- public/private/player projection targeting;
- game commands, phases or state schemas.

Consumers can define and independently version their own payload types above PartyGameKit.

## Identity

Target communication identities:

```text
ConnectionId = transient connection
PeerId       = optional stable logical communication identity for resume
```

A product may map its own participant/player id to `PeerId`. PartyGameKit does not interpret that mapping.

## Resume

Resume is a control-protocol operation, not player rejoin semantics.

A valid resume flow proves that a replacement network connection can reclaim the same neutral peer identity. The library may report success/failure and update connection binding. It does not decide what the product does with that peer's player slot, game state or authority.

## Application payloads

PartyGameKit must be able to carry arbitrary consumer-owned payloads without understanding them.

Conceptually:

```json
{
  "type": "application.message",
  "protocolVersion": 2,
  "messageId": "...",
  "correlationId": null,
  "payload": {
    "applicationType": "consumer-defined-type",
    "data": "consumer-defined opaque content"
  }
}
```

The exact post-refactor shape/version is finalized in `[16]`; this example shows the ownership boundary, not a frozen schema.

## Ordering

Generic sequence/deduplication metadata may exist where communication needs it. A required game snapshot sequence does not.

A consumer such as Państwa Miasta may put its own snapshot sequence inside its application payload and optionally reuse a neutral sequence helper.

## Connection descriptors

The protocol may serialize a technical connection descriptor containing transport/endpoint/version and optional routing metadata.

Product join codes, party names, game identifiers and QR presentation do not belong to the PartyGameKit base descriptor.

## Compatibility source of truth

Canonical fixtures under `protocol/fixtures/` remain the cross-language source of truth for C#, Dart and TypeScript.

After `[16]`, fixtures must cover at least:

- compatible connection handshake;
- protocol mismatch rejection;
- opaque application message;
- heartbeat/connectivity;
- neutral resume accepted/rejected;
- technical connection descriptor.

Historical v1 room/player/session fixtures are migration inputs, not the target protocol.

## Versioning

Breaking changes to the prerelease protocol are allowed while correcting the ownership boundary. The post-refactor protocol version must make incompatibility explicit rather than attempting to parse old session messages as the new control contract.