# Game session model

## Core entities

The initial model should stay deliberately small.

### Room

A room is the joinable multiplayer container.

Expected data may include:

- room identifier,
- short join code,
- current host,
- connected players,
- session status,
- optional transport/discovery metadata.

A room is infrastructure and should not contain game-specific rules.

### Player

A player needs a stable identity that survives temporary connection loss.

Connection identity and player identity should not be treated as the same thing.

This is important for phone sleep, browser refresh and reconnect.

Conceptually:

```text
PlayerId = stable identity inside the game session
ConnectionId = current network connection
```

A reconnect should attach a new connection to the existing player whenever ownership can be validated.

### Host

The host is the participant/device responsible for session coordination from the product perspective.

Host role and game authority may initially be the same device, but they should remain separate concepts because future cloud-authoritative games may use:

```text
TV = display/host
Backend = authority
```

The framework should support host transfer rather than making host identity immutable.

### GameSession

A game session represents the lifecycle of one playable match.

Possible lifecycle direction:

```text
Created
  ↓
Lobby
  ↓
Starting
  ↓
Running
  ↓
Finished
  ↓
Closed
```

Do not freeze this enum/API until the needs of at least two games are compared.

## Commands and events

Commands represent player/device intent.

Examples:

```text
JoinRoom
LeaveRoom
Ready
SubmitAnswer
VoteAnswer
Move
Attack
UseItem
```

Infrastructure-level commands can be standardized by PartyGameKit. Game-specific commands belong to the game.

Events represent accepted changes/facts.

Examples:

```text
PlayerJoined
PlayerDisconnected
HostChanged
GameStarted
PlayerMoved
AnswerSubmitted
```

The authority validates commands before producing state changes/events.

## State ownership

The authority owns canonical state.

Clients should not be trusted to set authoritative values such as:

- score,
- HP,
- item ownership,
- character position,
- combat result,
- generated loot.

Clients send intent and render the resulting state.

For simple games this still applies even when the authority is just the local TV/laptop.

## State projections

A single canonical state may produce different client projections.

```text
Canonical state
     │
     ├──► SharedScreenProjection
     │
     ├──► PlayerProjection(A)
     ├──► PlayerProjection(B)
     └──► PlayerProjection(C)
```

This is preferable to broadcasting one huge state object and relying on UI code to hide secrets.

### Shared-screen projection

Contains only data appropriate for the common display.

Examples:

- map,
- round number,
- timers,
- public scores,
- public actions,
- visible monsters,
- public vote results.

### Player projection

Contains data intended for one player.

Examples:

- inventory,
- cards,
- secret role,
- secret objective,
- private answer draft,
- available actions,
- cooldowns visible only to that player.

## Reconnect

Reconnect is a first-class use case, not an error edge case.

A reconnect flow should conceptually perform:

```text
new connection
    ↓
identify previous player
    ↓
attach connection
    ↓
restore current projection/state
    ↓
resume session
```

The transport implementation handles reconnect mechanics; Core defines the session/player semantics.

## Host disconnect

The framework should support policies rather than one hard-coded behavior.

Potential policies:

- pause and wait for host reconnect,
- transfer host automatically,
- transfer host by vote/selection,
- close the room,
- continue if authority is elsewhere.

The first implementation can support only the policy proven by Państwa Miasta, while leaving room for additional policies later.

## Shared-screen device as non-player

The TV should be able to join a session as a display/device without consuming a player slot.

This distinction is important because the shared screen may:

- display public state,
- own local authority,
- show QR/join code,
- have no score or character,
- remain connected while players come and go.

Therefore `Player` and `ConnectedClient`/`SessionParticipant` may eventually need to be separate concepts. This should be validated before introducing extra types into Core.

## Validation target

The model is considered genuinely reusable only when it supports at least:

1. **Państwa Miasta**: lobby, typed answers, rounds, voting/scoring, reconnect, host handling.
2. **A dungeon-style game**: shared map, turn/action state, characters, private inventory, movement/combat commands.

If both games fit without adding game-specific hacks to PartyGameKit.Core, the abstraction is probably moving in the right direction.
