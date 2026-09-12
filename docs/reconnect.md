# Reconnect and connection health

## Boundary

Reconnect remains a PartyGameKit responsibility only as **communication continuity**. Product/player/game recovery policy belongs to consumers.

See [Communication boundary](communication-boundary.md).

## Target model

```text
ConnectionId = transient network connection
PeerId       = optional stable logical communication identity
```

A reconnect credential/token proves that a replacement connection may resume the same `PeerId`.

PartyGameKit may:

- record last-seen/heartbeat timestamps;
- report connection/peer timeout;
- maintain a configurable reconnect window;
- validate a resume credential;
- replace the old connection binding with a new `ConnectionId`;
- avoid duplicate logical peers on successful resume;
- expose deterministic resume failure reasons.

PartyGameKit must not:

- decide that the peer is a player;
- decide that disconnect means player leave;
- decide whether a player slot remains occupied;
- decide whether a game pauses/ends;
- detect a special `HostLost` product condition;
- restore a required game snapshot;
- assign or migrate game authority.

## Heartbeat timeout

A heartbeat timeout is a connectivity fact, not a product lifecycle transition.

Conceptually:

```text
connected -> heartbeat overdue -> connectivity timeout
```

The consumer receives that information and decides what it means for its party/game/session.

## Resume flow

A neutral resume flow is:

1. client reconnects over a replacement transport connection;
2. client presents stable `PeerId` plus resume credential;
3. PartyGameKit validates the credential and reconnect window;
4. old connection binding is replaced by the new `ConnectionId`;
5. PartyGameKit reports resumed communication;
6. consumer decides whether/how to restore application state.

Application state restoration may be implemented by the consumer sending its latest snapshot/state as an ordinary opaque message after resume.

## Historical v0.1 implementation

The current `SessionContinuityCoordinator<TPublicState,TPrivateState>` couples valid reconnect mechanics to:

- `PlayerId`;
- `ClientRole`;
- host-specific timeout semantics;
- `RoomSession`;
- public/private snapshot restoration.

Issue `[16]` will split/generalize this implementation. The historical behavior remains useful test evidence, but the product semantics are not part of the target API.