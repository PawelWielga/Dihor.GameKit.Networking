# Architecture

## Purpose

PartyGameKit is intended to provide reusable multiplayer infrastructure for games where one device acts as a shared screen and multiple phones act as controllers or private player screens.

The most important architectural rule is that **game logic must be transport-agnostic**.

A game should not know whether players are connected through local WebSocket, SignalR, WebRTC, a relay or a future transport implementation.

## High-level model

```text
                         Optional backend
                    room discovery / signaling
                              │
                              │
Phones ───── transport ───── Shared screen / host
                              │
                              ▼
                        Game authority
                              │
                              ▼
                         Game logic
```

The shared screen will commonly be the game authority in local and hybrid modes, but authority is deliberately treated as a separate concern from transport.

## Layers

### PartyGameKit.Core

Contains reusable domain concepts only.

Expected responsibilities:

- `Room`
- `Player`
- `PlayerId`
- `GameSession`
- session lifecycle
- host ownership and host transfer
- connection/disconnection state
- commands
- events
- shared/public state contracts
- private player state contracts
- authority contracts

Core must not reference SignalR, WebRTC, HTTP, sockets, React or a concrete game.

### PartyGameKit.Transport.Abstractions

Defines communication contracts used by higher layers.

Example direction:

```csharp
public interface IGameTransport
{
    Task SendToPlayerAsync<T>(
        PlayerId playerId,
        T message,
        CancellationToken cancellationToken = default);

    Task BroadcastAsync<T>(
        T message,
        CancellationToken cancellationToken = default);
}
```

The exact API should be proven by real games before being stabilized.

### Transport implementations

Potential packages:

- `PartyGameKit.Transport.Lan`
- `PartyGameKit.Transport.SignalR`
- `PartyGameKit.Transport.WebRtc`

Transport packages should implement common abstractions and must not leak transport-specific types into game code.

### PartyGameKit.AspNetCore

Optional server integration for applications that use a backend.

Potential responsibilities:

- dependency-injection registration,
- endpoints for room creation/join,
- SignalR hubs,
- signaling support,
- cloud room registry,
- relay/fallback integration.

### Browser client

A TypeScript client should mirror the protocol concepts required by TV and phone applications.

Potential packages:

```text
@partygamekit/client
@partygamekit/react
```

The browser client should not own game rules. It should provide session connectivity, protocol handling and reusable UI hooks/components where useful.

## Command/event flow

Network messages should be modeled around intent and resulting facts.

```text
Controller
    │
    ▼
Command
    │
    ▼
Game authority
    │
    ▼
Game logic
    │
    ▼
Event / state update
    │
    ├────► TV
    └────► phones
```

Examples from different games:

```text
SubmitAnswerCommand
VoteAnswerCommand
MoveCommand
AttackCommand
UseItemCommand
```

Possible resulting events:

```text
AnswerSubmitted
VoteRegistered
PlayerMoved
MonsterDamaged
ItemUsed
```

PartyGameKit should understand how to route a command/event, but not what a dungeon attack or a submitted city answer means.

## Authority

Transport and game authority are separate concerns.

Possible authority modes:

### Local authority

```text
TV / laptop = authority
```

The host calculates movement, combat, random results, scoring and game state.

### Cloud authority

```text
Backend = authority
```

Useful later for games that require stronger server-side validation or remote multiplayer.

### Backend-assisted local authority

```text
Backend = discovery/signaling
TV      = authority
```

This is expected to be the default architecture for many PartyGameKit games.

A possible abstraction is:

```csharp
public interface IGameAuthority
{
    bool IsAuthority { get; }
}
```

The final contract should be kept minimal until validated by implementations.

## Public and private state

Shared-screen games need first-class support for different views of one game.

Conceptually:

```text
Game state
├── Public state
└── Player state
    ├── Player A private state
    ├── Player B private state
    └── Player C private state
```

The TV receives public state.

A phone receives public information that it needs plus the private state belonging to that player.

Examples of private state:

- cards,
- inventory,
- secret roles,
- secret objectives,
- answers being typed,
- hidden choices.

The framework must make it difficult to accidentally broadcast private player data to every client.

## Reuse boundary

The first reusable code should come from functionality already proven in Państwa Miasta:

- room lifecycle,
- joining,
- player identity,
- host role,
- reconnect,
- host transfer,
- broadcast/routing,
- session lifecycle.

A second game with different mechanics should then validate the abstraction.

The framework should not be generalized from one game alone.

## Package strategy

Initially, projects may use project references while the API is changing quickly.

NuGet/npm packages should be published only after the same core contracts have survived use in at least two games with meaningfully different mechanics.

This reduces the risk of freezing game-specific assumptions into a public package API.
