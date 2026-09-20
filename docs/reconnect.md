# Reconnect and connection health

## Boundary

Reconnect remains a Dihor.GameKit.Networking responsibility only as **communication continuity**. Product/player/game recovery policy belongs to consumers.

See [Communication boundary](communication-boundary.md).

## Target model

```text
ConnectionId = transient network connection
PeerId       = optional stable logical communication identity
```

A reconnect credential/token proves that a replacement connection may resume the same `PeerId`.

Dihor.GameKit.Networking may:

- record last-seen/heartbeat timestamps;
- report connection/peer timeout;
- maintain a configurable reconnect window;
- validate a resume credential;
- replace the old connection binding with a new `ConnectionId`;
- avoid duplicate logical peers on successful resume;
- expose deterministic resume failure reasons.

Dihor.GameKit.Networking must not:

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
3. Dihor.GameKit.Networking validates the credential and reconnect window;
4. old connection binding is replaced by the new `ConnectionId`;
5. Dihor.GameKit.Networking reports resumed communication;
6. consumer decides whether/how to restore application state.

Application state restoration may be implemented by the consumer sending its latest snapshot/state as an ordinary opaque message after resume.

## .NET runtime orchestration

`ConnectionHostRuntime` applies `ConnectionContinuityCoordinator` to an `IMessageTransport`. It validates protocol-v2 connect/resume/heartbeat/disconnect messages, rotates resume tokens, sweeps heartbeat and reconnect deadlines, deduplicates application messages by stable peer and emits neutral connection events.

`ConnectionClientRuntime` accepts an `IConnectionTransportConnector` and `IConnectionResumeCredentialStore`. It chooses connect or resume, validates the handshake response, persists rotated credentials, sends heartbeats and reconnects after transport closure. `AutomaticTransportConnector` composes this lifecycle with `AutomaticTransportSelector` so a reconnect can prefer the previous path and then follow configured fallback order.

Consumers still decide what a connected, disconnected or resumed `PeerId` means to their product model.

## Historical v0.1 implementation

The current `SessionContinuityCoordinator<TPublicState,TPrivateState>` couples valid reconnect mechanics to:

- `PlayerId`;
- `ClientRole`;
- host-specific timeout semantics;
- `RoomSession`;
- public/private snapshot restoration.

Issue `[16]` will split/generalize this implementation. The historical behavior remains useful test evidence, but the product semantics are not part of the target API.
