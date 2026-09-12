# PartyGameKit wire protocol

PartyGameKit v0.1 uses a versioned JSON protocol that can be implemented independently in C#, Dart and TypeScript.

The canonical compatibility examples live in `protocol/fixtures/`. Consumers must match the serialized field names and values, not CLR class names.

## Envelope

Every infrastructure message uses this shape:

```json
{
  "type": "session.join.accepted",
  "protocolVersion": 1,
  "messageId": "msg-join-accepted-1",
  "correlationId": "msg-join-request-1",
  "payload": {}
}
```

`correlationId` is optional and is omitted when it is not needed. `messageId` and `type` are non-empty strings. The current protocol version is `1`.

Property order is deterministic in the C# serializer so fixture diffs remain stable, but non-.NET implementations must not depend on JSON object property order when parsing.

## Stable serialized values

Client roles are serialized as:

- `host`
- `player`
- `shared-screen`

Join rejection codes are serialized as:

- `room-not-found`
- `room-closed`
- `room-full`
- `protocol-mismatch`
- `resume-rejected`

Resume rejection codes are stable strings:

- `room-closed`
- `invalid-resume-identity`
- `reconnect-window-expired`
- `connection-already-in-use`

`RoomId`, `PlayerId`, `ConnectionId`, `AuthorityId` and `JoinCode` are JSON strings. Their language-specific wrapper/class names are not part of the wire format.

`PlayerId` is stable session/player identity. `ConnectionId` is transient network-connection identity. They must never be treated as interchangeable even when their string contents happen to be equal.

`JoinCode` is normalized to uppercase by the C# implementation. Clients should treat the canonical join code as case-insensitive input and send the normalized uppercase value.

## Infrastructure message types

Version 1 reserves these generic message types:

- `session.join.request`
- `session.join.accepted`
- `session.join.rejected`
- `session.leave`
- `session.disconnected`
- `session.heartbeat`
- `session.rejoin.request`
- `session.rejoin.accepted`
- `session.rejoin.rejected`
- `state.snapshot`

These messages establish and maintain session membership and authoritative state replication. They do not contain game-specific commands.

A player join request can carry a stable `playerId`. A non-player shared-screen can omit it. A successful join returns the transient `connectionId` and the logical `authorityId` independently. Player joins managed by the continuity layer also receive an opaque `reconnectToken`; non-player clients omit that field.

A heartbeat carries the room identifier and the last authoritative snapshot sequence observed by the sender. Sequence `0` means that no snapshot has been applied yet.

A rejoin request carries the stable `playerId`, the opaque reconnect token and the last snapshot sequence already observed by the client. The token authenticates ownership of the existing player slot; it is not a connection identifier.

The authority stores only a one-way fingerprint of the reconnect credential. After a successful rejoin it responds with `session.rejoin.accepted` and then sends the newest applicable `state.snapshot`. Historical snapshot replay is not required. Terminal resume failures use `session.rejoin.rejected` with one of the stable codes above.

## Version mismatch

A reader must inspect `type` and `protocolVersion` before interpreting the payload.

If `protocolVersion` differs from the supported version, the message is rejected with a protocol-version-mismatch result. Implementations must not partially join a client by attempting best-effort deserialization of an incompatible protocol revision.

The canonical C# implementation exposes the received version in the read result so transports can return a deterministic rejection rather than crashing a parser.

## Sequence metadata

There is deliberately no generic envelope sequence number in version 1. Państwa Miasta proves sequence ordering for **authoritative snapshots**, not for every network message. `state.snapshot` owns the monotonic authoritative sequence.

A client ignores a snapshot whose sequence is less than or equal to the last applied sequence. `lastSeenSnapshotSequence` in heartbeat/rejoin messages reports the client's progress but does not transfer authority to the client.

## Game messages

PartyGameKit does not reserve game actions such as answer submission, voting, movement or attacks. A consuming game owns its own application payload/message schema and sends it through the transport/session boundary without adding those concepts to PartyGameKit Core.
