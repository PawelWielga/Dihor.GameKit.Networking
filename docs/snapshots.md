# State snapshots and client projections

PartyGameKit owns snapshot metadata and ordering. Games own the state contained inside each projection.

## What is generic

A snapshot carries only reusable replication metadata:

- `RoomId`;
- logical `AuthorityId`;
- a positive, monotonically increasing sequence;
- a projection target;
- a game-owned projection payload.

The library does not know categories, answers, letters, scores, tiles, enemies, inventory or game phases.

This is deliberately narrower than the current Państwa Miasta `GameStateSnapshot`, which combines generic replication metadata with Countries/Cities state.

## Authority publication

`AuthoritativeSnapshotPublisher<TPublicState, TPrivateState>` creates one state revision at a time. Each publication increments one sequence and produces:

1. one public snapshot for shared-screen/public clients;
2. one player-targeted snapshot per supplied `PlayerId`.

A player projection contains the public state for that revision plus only that player's private state. The public projection type has no private-state member.

The publisher retains only the newest published snapshot set. A late join or rejoin therefore restores the current authoritative state directly instead of replaying historical snapshots.

No network transport is involved in publication. Application code decides how a snapshot is encoded and delivered.

## Ordering

`SnapshotSequenceGate` accepts a snapshot only when its sequence is strictly greater than the last applied sequence.

```text
accepted: 41 -> 42 -> 45
ignored:  45 -> 44
ignored:  45 -> 45
```

This mirrors the proven stale-snapshot behavior in Państwa Miasta while remaining independent of its game state.

A client restoring from reconnect can initialize the gate with its last applied sequence. The newest authoritative snapshot can then be applied if it is newer.

## Targets

There are two v0.1 targets:

```text
public
player:<PlayerId>
```

`public` is appropriate for a shared screen and other clients that only need common state.

A `player` target is explicit and contains the stable `PlayerId`. It is not derived from a socket or `ConnectionId`.

Transport/application code must resolve the target to the player's current connection through the session model. This keeps snapshot semantics independent of WebSocket, SignalR and WebRTC.

## Wire format

The protocol message type is `state.snapshot`. The generic state value is opaque JSON from PartyGameKit's perspective.

Public target:

```json
{"kind":"public"}
```

Player target:

```json
{"kind":"player","playerId":"player-001"}
```

Canonical examples are stored in:

- `protocol/fixtures/snapshot-public.json`;
- `protocol/fixtures/snapshot-player.json`.

The protocol version remains `1`: this issue adds a new message type without changing the meaning of existing v1 messages.

## Not part of this issue

- heartbeat/presence/reconnect-token policy (`[07]`);
- transport delivery strategy and LAN WebSocket (`[08]`);
- snapshot chunking/compression;
- delta snapshots or event replay;
- automatic authority migration;
- game-specific state schemas.

These are intentionally omitted until concrete requirements justify them.
