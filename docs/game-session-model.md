# Game session model

This document describes the smallest session model justified by the current Państwa Miasta multiplayer behavior and the concrete PartyBeam shared-screen requirement. It deliberately avoids freezing game lifecycle concepts that belong to individual games.

See [Extraction from Państwa Miasta](extraction-from-panstwa-miasta.md) for the evidence and cross-language boundary.

## Room and session

A `Room` is the joinable multiplayer container. A session is the logical multiplayer lifetime inside that room.

PartyGameKit may own infrastructure data such as:

- stable room/session identity;
- capacity/admission policy;
- current participants and their presence;
- logical host/authority identity;
- whether the session is accepting joins or closed.

Transport addresses, IPs and ports are not Core room identity. They belong to a transport/join descriptor.

PartyGameKit does not define a universal game-phase enum such as `Lobby -> Starting -> Running -> Finished`. Państwa Miasta has its own phases and a dungeon game will have different ones. The game owns that state inside its authoritative payload.

## Player

A player has a stable identity that survives temporary network loss.

```text
PlayerId      stable session identity
ConnectionId  transient network connection
```

This distinction is mandatory, not optional. The reference implementation already replaces an old connection for the same `playerId` without duplicating the player and keeps disconnected players in an active match so their game state can be restored.

Display name and cosmetic profile fields are not identity. Two connected players may have the same display name and must remain distinct.

## Connected client and role

Not every connected client is a player.

PartyBeam requires a shared TV/browser that can render public state without consuming player capacity, score, character ownership or other player semantics.

The protocol therefore needs a small role concept, for example:

```text
host/authority endpoint (where applicable)
player
shared-screen
```

The final serialized names are defined by `[03]`; the important rule is that `ClientRole` and `PlayerId` are not the same abstraction.

A shared-screen connection can exist without a `PlayerId` when it does not represent a player.

## Host and authority

`Host`, `authority` and current network connection are separate concepts.

The initial LAN implementation will commonly have one server-capable local process coordinate the room and own canonical game state. That does not justify storing authority as a property of a WebSocket object.

A connection can be replaced during reconnect while the logical participant/authority identity remains stable.

Future cloud-authoritative games may place authority on a backend while the TV remains only a shared-screen client. Core must not need redesign for that topology.

## Join and admission

Join is an explicit lifecycle operation with an explicit result.

At minimum the lifecycle must support deterministic outcomes such as:

- accepted;
- room not found/closed;
- room full;
- incompatible protocol;
- invalid/unauthorized resume identity.

The game may add its own admission rules outside Core, but generic capacity and session availability belong to the reusable session model.

A successful join must not infer player identity from a display name or from the connection handle.

## Disconnect versus leave

This is one of the most important semantics extracted from Państwa Miasta.

### Disconnect

A temporary transport loss means:

- the active connection is gone or unhealthy;
- the participant may be marked disconnected;
- game-owned state remains intact;
- capacity/session retention follows the current lifecycle policy;
- the player may reconnect with the same stable `PlayerId`.

### Leave

Leave is an explicit permanent lifecycle action/policy. It can remove the participant from the room and free capacity where appropriate.

The reference game already behaves differently in its lobby and active round: leaving a lobby frees a slot, while a network loss during an active game preserves the known player's state. PartyGameKit should expose the generic distinction without copying the game's phase enum.

## Reconnect

Reconnect is a first-class flow:

```text
new transport connection
        ↓
resume request with stable identity + proof
        ↓
validate existing session/player
        ↓
bind new ConnectionId
        ↓
mark participant connected
        ↓
deliver current authoritative projection/snapshot
```

A reconnect must not create a second player for the same `PlayerId`.

The reconnect credential is separate from the public player identity and is never part of a shared player profile. Persistence of that credential is a client SDK/platform concern.

## Presence and timeout

Heartbeat/presence timestamps are infrastructure state, not game state.

The reference implementation proves the need for configurable:

- heartbeat interval;
- host/client timeout;
- reconnect window;
- disconnected-player retention/grace policy.

Missing one heartbeat cannot be treated as permanent leave. Timeout policy is configurable and deterministic so tests can use controllable time.

## Game-owned commands and state

PartyGameKit standardizes infrastructure messages such as join/rejoin/leave/heartbeat and snapshot delivery.

Concrete game actions remain game-owned. PartyGameKit does not define a typed hierarchy containing commands such as answer submission, movement, attack or voting.

The authority receives a game-owned payload, applies game rules and publishes an authoritative state/projection payload through PartyGameKit.

## State projections

A game may produce separate projections from one canonical state:

```text
canonical game state
     │
     ├── public/shared-screen payload
     ├── private payload for Player A
     └── private payload for Player B
```

PartyGameKit owns generic targeting and snapshot metadata. It does not know the content of these payloads.

This keeps hidden information out of public broadcasts without putting concepts such as inventory, cards or draft answers into Core.

## Snapshot ordering

The authority publishes monotonically increasing snapshot sequence numbers.

A client applies a snapshot only when its sequence is newer than the latest one already applied. Equal or older snapshots are ignored.

A late join or reconnect receives the newest authoritative projection directly; replaying the entire history is not required for v0.1.

## Host loss

The production reference currently supports host-loss detection, preserving the client's latest snapshot and attempting reconnect to the known host. It does **not** support automatic host migration as a production path.

Therefore v0.1 requires deterministic host-loss representation and reconnect behavior, but does not promise automatic promotion/transfer.

Host migration can be revisited after it is proven by a concrete cross-game requirement. Experimental code in Państwa Miasta is not sufficient justification for a public framework API.

## Shared-screen device as non-player

A TV/browser shared screen must be able to join without pretending to be a scored player.

It may:

- display public state;
- show connection/join information;
- remain connected while players come and go;
- receive no private player projection;
- run separately from the server-capable LAN host process.

For the first direct LAN WebSocket transport, a pure browser is a client, not the inbound network listener. The listener/authority may be a local companion process on the same machine.

## Validation targets

The model is considered reusable only when it supports both:

1. **Państwa Miasta behavior**: stable identity, join/leave, disconnect/rejoin, host-authoritative snapshots and stale-state rejection without moving Countries & Cities rules into PartyGameKit.
2. **Dungeon-style sample**: shared public state, private per-player state, authoritative actions and reconnect without adding dungeon concepts to generic packages.

If a later sample requires a new generic abstraction, it must be justified by a need that is actually cross-game rather than by renaming one game's domain model.
