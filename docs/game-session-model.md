# Game session model

This document describes the smallest session model justified by the current Państwa Miasta multiplayer behavior and the concrete PartyBeam shared-screen requirement. It deliberately avoids freezing game lifecycle concepts that belong to individual games.

See [Extraction from Państwa Miasta](extraction-from-panstwa-miasta.md) for the evidence and cross-language boundary.

## Room and session

A `RoomSession` is the joinable multiplayer container and logical multiplayer lifetime. The v0.1 infrastructure lifecycle is intentionally small:

```text
Open -> Closed
```

This is **not** a game-phase model. A concrete game owns states such as lobby, answering, combat, results or finished inside its own authoritative payload.

A room session owns:

- stable `RoomId` and `JoinCode`;
- player capacity;
- current player memberships;
- current client/connection memberships;
- logical `AuthorityId`;
- deterministic lifecycle events.

Transport addresses, IPs and ports are not Core room identity. They belong to a later transport/join descriptor.

## Player versus connected client

A player is a durable session slot. A connected client is a current network-facing participant binding.

```text
PlayerId      stable session identity
ConnectionId  transient connection identity
```

`PlayerMembership` retains the player while disconnected. `ClientMembership` exists only while a current connection is attached.

Display name and cosmetic profile fields are not identity and are intentionally absent from the generic lifecycle model.

## Client roles

Version 1 supports these infrastructure roles:

- `player` - requires a `PlayerId` and consumes player capacity;
- `shared-screen` - no player identity and no player capacity;
- `host` - a non-player coordination/client role where needed by a topology.

The logical `AuthorityId` is assigned to the session independently from these connections. Connecting a `host` client does not implicitly change authority.

This lets PartyBeam run a browser shared screen against a separate local authority/companion process without pretending that the screen is a scored player.

## Join and admission

`JoinPlayer` returns an explicit result rather than throwing for expected admission failures.

Generic rejection cases include:

- room closed;
- room full;
- player identity already exists and must use rejoin instead;
- connection identity already belongs to another client.

A new join never infers identity from a display name or transport object.

Non-player clients use `ConnectClient`. Passing the `player` role through that API is rejected because a player must have a stable `PlayerId`.

## Disconnect versus leave

This is a core compatibility rule from Państwa Miasta.

### Disconnect

`Disconnect(ConnectionId)` removes the transient client binding. For a player it keeps `PlayerMembership`, marks it disconnected and retains the capacity slot.

A dropped socket, missed heartbeat or sleeping phone therefore cannot silently erase the player or game-owned state.

### Leave

`LeavePlayer(PlayerId)` permanently removes the player membership and any current connection binding. It frees player capacity.

Expected lifecycle/policy code can decide when a leave is permitted; Core does not copy a concrete game's phase enum.

## Rejoin

`RejoinPlayer(PlayerId, ConnectionId)` binds a replacement connection to an existing player membership.

It guarantees:

- the same `PlayerId` remains the same player slot;
- no duplicate player is created;
- a stale still-connected binding for that player can be replaced;
- a connection already owned by another client cannot be stolen;
- capacity does not change during rejoin.

Reconnect credential validation is intentionally added in `[07]`; `[04]` implements only the session identity/lifecycle semantics required after identity has been authenticated.

## Authority

`AuthorityId` is supplied explicitly when the session is created. It is not derived from a `ConnectionId`, WebSocket or current client role.

Automatic authority/host migration is not a v0.1 contract. A later policy can change authority only after a concrete cross-game requirement proves how that should work.

## Deterministic lifecycle events

Successful state transitions append an in-memory lifecycle event with a monotonic local sequence number. Events contain only infrastructure identity/role information and no timestamps, networking objects or game payload.

The sequence is useful for deterministic orchestration/tests. It is **not** the wire-level authoritative snapshot sequence introduced by `[06]` and does not imply global message ordering.

Failed/idempotent operations do not append phantom lifecycle transitions.

## State projections and snapshots

A game may later produce separate public/shared-screen and private-player projections from one canonical game state. PartyGameKit owns targeting and snapshot ordering metadata, while the game owns payload content.

Those contracts are implemented in `[06]`, not in the basic membership lifecycle.

## Host loss and reconnect timing

Presence, heartbeat, host timeout, reconnect window and credential validation are implemented in `[07]`. The lifecycle introduced here deliberately has no clock and no transport callbacks.

## Validation targets

The model is considered reusable only when it supports both:

1. **Państwa Miasta behavior**: stable identity, join/leave, disconnect/rejoin, host-authoritative snapshots and stale-state rejection without moving Countries & Cities rules into PartyGameKit.
2. **Dungeon-style sample**: shared public state, private per-player state, authoritative actions and reconnect without adding dungeon concepts to generic packages.

If a later sample requires a new generic abstraction, it must be justified by a need that is actually cross-game rather than by renaming one game's domain model.
