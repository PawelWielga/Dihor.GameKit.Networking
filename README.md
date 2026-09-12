# PartyGameKit

PartyGameKit is a reusable multiplayer foundation for **shared-screen party games**.

The primary interaction model is:

- a TV, laptop or browser is the shared game screen,
- players join from their phones,
- phones act as controllers and private player screens,
- the same game can run locally over LAN or use an optional backend/cloud path,
- game logic must not depend on SignalR, WebRTC, sockets or any other concrete transport.

The first real-world source of requirements is the existing **Państwa Miasta** game. A second, mechanically different game such as a dungeon crawler will be used to validate that the extracted API is genuinely reusable and not just a renamed copy of one game's networking layer.

## Goals

PartyGameKit should eventually provide reusable building blocks for:

- rooms and join codes,
- players and host lifecycle,
- connect, disconnect and reconnect,
- host transfer,
- game-session lifecycle,
- commands and events,
- public and private player state,
- TV/shared-screen clients,
- phone/controller clients,
- LAN networking,
- backend-assisted networking,
- WebRTC/direct peer transport where useful,
- automatic transport selection and fallback.

## Non-goals

The core library should **not** contain game-specific concepts such as countries, cities, answers, dungeon rooms, monsters, loot or combat rules.

PartyGameKit is also not intended to become a full general-purpose MMO/network engine. The initial focus is couch/party games with one shared screen and multiple personal controllers.

## Design principle

Game code should work with concepts such as `GameSession`, `Player`, `Command`, `Event` and `GameState`.

It should not need to know whether a message is delivered through LAN WebSocket, SignalR, WebRTC or a future transport.

```text
Game logic
   │
   ├── Session / Room
   │
   └── Transport abstraction
          ├── LAN
          ├── SignalR / cloud
          └── WebRTC
```

## Planned stack

### Core and server

- C#
- .NET 10
- ASP.NET Core
- SignalR where a backend transport is appropriate
- NuGet packages once the API has been validated by at least two games

### Browser clients

- TypeScript
- React
- PWA
- WebRTC DataChannel where direct low-latency communication is useful
- Phaser or another dedicated renderer for games that need a real-time 2D scene

## Planned repository shape

```text
PartyGameKit/
├── src/
│   ├── PartyGameKit.Core/
│   ├── PartyGameKit.Transport.Abstractions/
│   ├── PartyGameKit.Transport.Lan/
│   ├── PartyGameKit.Transport.SignalR/
│   ├── PartyGameKit.Transport.WebRtc/
│   └── PartyGameKit.AspNetCore/
├── web/
│   ├── @partygamekit/client/
│   └── @partygamekit/react/
├── samples/
│   ├── SimpleLobby/
│   └── SimpleDungeon/
└── docs/
```

This structure is a direction, not a commitment to create every package immediately. The project should grow from proven requirements rather than speculative abstractions.

## Documentation

- [Architecture](docs/architecture.md)
- [Networking](docs/networking.md)
- [Game session model](docs/game-session-model.md)
- [Roadmap](docs/roadmap.md)

## Current status

The repository is at the architecture/bootstrap stage. The next major step is to identify the reusable room/player/reconnect/host functionality already proven in Państwa Miasta and extract only that first slice.
