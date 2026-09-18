# Wire protocol

## Status

Dihor.GameKit.Networking `0.2.0-preview.1` uses wire protocol **2**. Protocol v2 is communication-only and intentionally incompatible with the historical room/player/session protocol v1 from `0.1.0-preview.1`.

See [Communication boundary](communication-boundary.md) and [Compatibility](compatibility.md).

## Protocol layers

The Dihor.GameKit.Networking wire contract has two distinct layers:

1. **control protocol** owned by Dihor.GameKit.Networking;
2. **application messages** owned by the consumer and treated as opaque payloads.

```text
Dihor.GameKit.Networking protocol v2 envelope
├── protocol compatibility
├── connection lifecycle
├── heartbeat/connectivity
├── neutral peer resume
└── application.message
       └── opaque consumer-owned data
```

## Envelope

Every protocol-v2 message uses:

- `type`;
- integer `protocolVersion` = `2`;
- non-empty `messageId`;
- optional non-empty `correlationId`;
- `payload`.

Unsupported protocol versions are rejected before application payloads are used.

## Control messages

Protocol v2 defines neutral communication control message families:

- `connection.connect.request`;
- `connection.connect.accepted`;
- `connection.connect.rejected`;
- `connection.resume.request`;
- `connection.resume.accepted`;
- `connection.resume.rejected`;
- `connection.heartbeat`;
- `connection.disconnect`.

These messages may carry transient `ConnectionId`, optional stable `PeerId`, resume credentials and connection-level rejection information. They do not carry player roles, lobby capacity, authority or game state.

## Consumer responsibilities

The base protocol does not define:

- `PlayerId` or player admission;
- `Host`, `Player`, `SharedScreen`, controller or spectator roles;
- player capacity/room-full policy;
- `AuthorityId`;
- lobby or game-session lifecycle;
- required state snapshot messages;
- public/private/player projection targeting;
- game commands, phases or state schemas.

Consumers independently define and version those concepts above Dihor.GameKit.Networking.

## Identity

Communication identities are:

```text
ConnectionId = transient network/transport connection
PeerId       = optional stable logical communication identity for resume
```

A product may map its own participant identifier to `PeerId` at an adapter boundary. Dihor.GameKit.Networking does not interpret that mapping.

## Connect

A client begins a protocol-v2 LAN connection with `connection.connect.request`.

`peerId` is optional. An anonymous client can connect without a stable logical identity when resume is unnecessary.

A successful response provides a transient `connectionId`; when a stable peer is registered, it may also provide a resume token.

## Resume

Resume is a communication operation, not player rejoin semantics.

A valid `connection.resume.request` proves that a replacement network connection may reclaim the same neutral `PeerId`. The resulting `ConnectionId` is new and transient. Product state, player slots and authority policy remain consumer decisions.

## Application messages

Consumer data uses `application.message`:

```json
{
  "type": "application.message",
  "protocolVersion": 2,
  "messageId": "message-42",
  "payload": {
    "applicationType": "consumer.command",
    "data": {
      "consumerOwned": true
    }
  }
}
```

`applicationType` belongs to the consumer's protocol namespace. `data` is opaque to Dihor.GameKit.Networking beyond valid JSON serialization/deserialization.

Consumers may version their own application payload schemas without changing Dihor.GameKit.Networking protocol version 2, provided the Dihor.GameKit.Networking envelope/control contract does not change.

## Transient latest-value replay

Reconnect-safe transient replay does not add a new control-message family. Replayable values continue to travel as ordinary `application.message` envelopes.

`LatestValueReplayBuffer` stores the complete staged application message outside concrete transports. Multiple updates for one caller-owned key coalesce to the newest value, and a caller-owned scope/epoch token prevents an old value from replaying into a new logical context.

A new logical value should use a new `messageId`. If that staged value must be replayed after connection replacement, the exact same envelope should be sent again so its `messageId` remains stable across the ambiguous retry.

This mechanism is not exactly-once delivery. Receiver-side bounded `messageId` deduplication is tracked in issue [25]. Commands that need acknowledgement/retry-until-ACK semantics require a separate reliable-command mechanism.

See [Transient latest-value replay](transient-replay.md).

## Heartbeat

`connection.heartbeat` communicates liveness. It may reference the neutral peer when available but does not encode game presence, player-leave policy, snapshot progress or replayable application data.

Heartbeat timeout reports communication state only. The consumer decides what a timeout means for its product/session.

## Ordering

`MessageSequence` / `SequenceGate` are optional local utilities and are not required fields in every protocol message.

A consumer such as Państwa Miasta may put its own snapshot or command sequence inside application data and optionally reuse the neutral helper.

## Connection descriptors

`ConnectionDescriptor` serializes technical connectivity information:

- `protocolVersion`;
- `transport`;
- absolute `endpoint`;
- optional technical `channelId`.

The deterministic URI form uses:

```text
dihor-gamekit-networking://connect?protocolVersion=2&transport=...&endpoint=...&channelId=...
```

Product join codes, party names, game identifiers and QR presentation remain outside the base descriptor.

## Discovery announcements

LAN discovery uses `connection.discovery.announce` with a technical `ConnectionDescriptor`. Discovery metadata does not contain player lists, game phase, scores or reconnect credentials.

## Canonical compatibility source

The active cross-language source of truth is:

```text
protocol/fixtures/v2-*.json
```

The fixture set covers:

- connect request;
- resume request;
- heartbeat;
- opaque application message;
- connection descriptor;
- discovery announcement.

C#, Dart and TypeScript tests consume this same set directly. Historical protocol-v1 fixtures remain available through Git history / the v0.1 tag rather than coexisting as active canonical vectors.

## Versioning

Protocol v1 and v2 are deliberately incompatible. A v2 transport rejects a v1 handshake deterministically with protocol-mismatch behavior rather than attempting to reinterpret historical session messages.

See [Versioning](versioning.md) for the relationship between package SemVer and wire protocol versions.
