# Game/session model is consumer-owned

PartyGameKit `0.2` does not own a generic player/room/game-session runtime. See [Communication boundary](communication-boundary.md).

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

There is no generic `RoomSession` abstraction in the `0.2` package line.

## What PartyGameKit provides underneath

A consumer may build its own session model above:

```text
ConnectionId   transient transport connection
PeerId         optional stable communication identity for resume
ChannelId      optional technical routing isolation
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
PartySession
├── TV/shared screen
├── controllers/participants
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

It may map a reconnecting game participant to a PartyGameKit `PeerId` at the adapter boundary and carry commands/snapshots as opaque application messages.

## Historical v0.1 API

`0.1.0-preview.1` contained `RoomSession`, `PlayerMembership`, `ClientRole`, `AuthorityId` and related lifecycle APIs. They were removed/generalized in the breaking `0.2` correction.

The historical source remains available through Git history and the v0.1 tag. It must not be used as the target architecture for new consumers.
