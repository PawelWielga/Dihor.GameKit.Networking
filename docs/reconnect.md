# Presence, heartbeat and reconnect

PartyGameKit treats player identity, current connection and network presence as separate concerns.

## Presence

`SessionContinuityCoordinator` records a last-seen timestamp for every current connection. Timing is policy, not transport behavior:

- host timeout is configurable;
- client timeout is configurable;
- reconnect window is configurable;
- time comes from an injected clock in tests/applications.

A heartbeat refreshes only presence. Missing one heartbeat does not remove a player. A connection is considered lost only after the configured timeout is reached.

## Disconnect versus leave

A timeout or transport loss calls the existing session disconnect behavior:

```text
PlayerId stays in session
ConnectionId is detached
Presence = Disconnected
game-owned state stays intact
```

The reconnect window limits automatic resume. Expiring that window does **not** silently turn disconnect into leave and does not delete the player. Permanent removal remains an explicit `LeavePlayer` operation or a future game/application policy.

## Resume credential

A successful player join receives an opaque reconnect token. The coordinator stores only a SHA-256 fingerprint of that token and compares reconnect attempts with `CryptographicOperations.FixedTimeEquals`.

The token is independent of the transport connection. A successful resume can therefore bind a new `ConnectionId` to the same stable `PlayerId`.

`JoinAcceptedPayload.reconnectToken` is optional at the protocol type level because non-player clients do not need one. Player joins handled by the continuity coordinator issue one.

## Resume result

Resume failures are explicit and transport-neutral:

- room closed;
- invalid resume identity/credential;
- reconnect window expired;
- new connection already in use.

Successful resume returns the newest public and player-targeted snapshot currently retained by the authoritative publisher. Historical snapshots are not replayed.

## Host loss

A timed-out client with role `host` produces a `HostLost` timeout result. PartyGameKit does not change `AuthorityId` or elect a new host in v0.1.

This keeps host-loss detection separate from automatic host migration, which remains experimental in the Państwa Miasta reference implementation.

## Wire messages

Protocol v1 gains additive messages/fields:

- `session.heartbeat` with `roomId` and `lastSeenSnapshotSequence`;
- optional `reconnectToken` on `session.join.accepted`;
- `session.rejoin.rejected` with stable string rejection codes.

`session.rejoin.request` remains the handshake request defined in `[03]`: stable `PlayerId`, opaque reconnect token and the last snapshot sequence seen by the client.

The accepted response remains `session.rejoin.accepted`; application code then sends the newest applicable `state.snapshot` from `[06]`.

## Out of scope

- Android foreground/background service behavior;
- LAN address discovery;
- WebSocket framing;
- automatic host migration;
- deleting disconnected players automatically;
- cloud account identity.
