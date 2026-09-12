# Game/session model is consumer-owned

This document supersedes the historical v0.1 PartyGameKit game-session model.

Issue `[15]` established that PartyGameKit is a communication/networking library and must not own a generic player/room/game-session runtime. See [Communication boundary](communication-boundary.md).

## Decision

The following concepts belong to PartyBeam, Państwa Miasta or another consumer, not PartyGameKit base packages:

- player membership;
- player capacity/admission;
- lobby/open/closed lifecycle;
- host/shared-screen/controller/spectator roles;
- authority/coordinator policy;
- explicit player leave semantics;
- whether a disconnected player keeps a slot;
- game start/pause/end rules;
- score/game state;
- public/private/player projections.

There is therefore no target generic `RoomSession` abstraction in PartyGameKit.

## What PartyGameKit provides underneath

A consumer may build its session model above:

```text
ConnectionId   transient transport connection
PeerId         optional stable communication identity for resume
Channel/Scope  optional transport routing isolation, if needed
```

and communication operations such as:

- connect/disconnect;
- send/receive;
- targeted/broadcast delivery;
- heartbeat/timeout;
- neutral resume/reconnect;
- LAN discovery and connection descriptors.

Those primitives do not decide participant meaning.

## PartyBeam example

PartyBeam may define its own model such as:

```text
Party
├── TV/coordinator
├── pilot
├── players/controllers
├── selected game
└── product lifecycle/policies
```

PartyBeam can map those participants to PartyGameKit peers/connections without PartyGameKit knowing their roles.

## Państwa Miasta example

Państwa Miasta keeps:

- its stable game participant/player identity;
- lobby and capacity rules;
- host-authoritative game engine;
- disconnect-vs-leave policy;
- game snapshots and restore behavior.

It may map one game participant to a PartyGameKit `PeerId` for reconnect and carry commands/snapshots as opaque application messages.

## Historical v0.1 API

`0.1.0-preview.1` contains `RoomSession`, `PlayerMembership`, `ClientRole`, `AuthorityId` and related lifecycle APIs. These are implementation history and are scheduled for removal/generalization in `[16]`.

Do not treat them as the intended stable PartyGameKit architecture.