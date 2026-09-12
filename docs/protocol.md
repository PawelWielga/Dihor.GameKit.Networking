# PartyGameKit wire protocol

PartyGameKit v0.1 uses a versioned JSON protocol that can be implemented independently in C#, Dart and TypeScript.

The canonical compatibility examples live in `protocol/fixtures/`. Consumers must match the serialized field names and values, not CLR class names.

## Envelope

Every gameplay/session infrastructure message uses this shape:

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

Version 1 reserves these generic gameplay/session message types:

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

## Portable join descriptor

Connection/bootstrap data is represented by a language-neutral `JoinDescriptor` with exactly these fields:

```json
{
  "protocolVersion": 1,
  "roomId": "room-001",
  "joinCode": "ROOM42",
  "transport": "lan-websocket",
  "endpoint": "ws://192.168.1.20:5042/partygamekit"
}
```

The descriptor is intentionally not a CLR-specific object contract. It can be serialized as canonical compact JSON or as the deterministic QR/deep-link URI:

```text
partygamekit://join?protocolVersion=1&roomId=room-001&joinCode=ROOM42&transport=lan-websocket&endpoint=ws%3A%2F%2F192.168.1.20%3A5042%2Fpartygamekit
```

Required fields are `protocolVersion`, `roomId`, `joinCode`, `transport` and an absolute `endpoint`. The descriptor contains no reconnect token, player-private state or game-specific data.

For direct LAN WebSocket, `transport` is `lan-websocket` and `endpoint` uses `ws` or `wss`. Later transports may define their own transport name and endpoint form while preserving the generic descriptor boundary.

## Discovery announcement

LAN discovery is separate from the gameplay/session envelope. Its compact UDP payload is:

```json
{
  "type": "session.discovery.announce",
  "protocolVersion": 1,
  "descriptor": {
    "protocolVersion": 1,
    "roomId": "room-001",
    "joinCode": "ROOM42",
    "transport": "lan-websocket",
    "endpoint": "ws://192.168.1.20:5042/partygamekit"
  }
}
```

`session.discovery.announce` is an unauthenticated LAN hint, not a game-state message and not a membership action. Receivers reject incompatible protocol versions. Duplicate announcements refresh the registry entry identified by stable `RoomId`; they do not create additional logical rooms.

Canonical fixtures are `join-descriptor.json` and `discovery-announcement.json`.

## Version mismatch

A reader must inspect `type` and `protocolVersion` before interpreting the payload.

If `protocolVersion` differs from the supported version, the message is rejected with a protocol-version-mismatch result. Implementations must not partially join a client by attempting best-effort deserialization of an incompatible protocol revision.

The canonical C# implementation exposes the received version in the read result so transports can return a deterministic rejection rather than crashing a parser.

## Sequence metadata

There is deliberately no generic envelope sequence number in version 1. Państwa Miasta proves sequence ordering for **authoritative snapshots**, not for every network message. `state.snapshot` owns the monotonic authoritative sequence.

A client ignores a snapshot whose sequence is less than or equal to the last applied sequence. `lastSeenSnapshotSequence` in heartbeat/rejoin messages reports the client's progress but does not transfer authority to the client.

## Game messages

PartyGameKit does not reserve game actions such as answer submission, voting, movement or attacks. A consuming game owns its own application payload/message schema and sends it through the transport/session boundary without adding those concepts to PartyGameKit Core.
