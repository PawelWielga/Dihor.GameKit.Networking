# Transport abstraction

PartyGameKit transports move opaque bytes between current connections. They do not own player identity, game rules or reconnect semantics.

## Boundary

`IGameTransport` works with `ConnectionId` only:

```text
RoomSession
  PlayerId -> current ConnectionId
                  |
                  v
             IGameTransport
```

Routing to a player is therefore performed by session/application code by resolving the player's current connection. This is deliberate: a transport must never decide that a socket is the player.

The v0.1 contract exposes only behavior required by the current reference game and the upcoming LAN adapter:

- ordered connection/message event stream;
- targeted send to one connection;
- broadcast to currently open connections;
- explicit disconnect;
- explicit stop/disposal;
- cancellation tokens;
- technology-neutral errors/close reasons.

No socket, HTTP, SignalR, WebRTC, IP, port or platform type appears in the abstraction.

## Session scope

The initial transport instance is scoped to one logical hosted session. `BroadcastAsync` therefore means all currently open connections on that transport instance.

This matches the direct-LAN reference topology and avoids adding room/group APIs before the backend transport exists. If `[15]` proves that a backend adapter needs a different multiplexing layer, that layer can wrap/session-scope the same minimal transport contract instead of teaching Core about SignalR groups.

## Event model

A transport emits:

- `TransportConnectionOpened`;
- `TransportMessageReceived`;
- `TransportConnectionClosed`;
- `TransportFaulted` for asynchronous technology-neutral failures.

Successful operations preserve their observable order. The abstraction does not promise global ordering across unrelated physical transports.

Payloads are opaque `ReadOnlyMemory<byte>`. Protocol JSON and game payload meaning stay outside the transport.

## Errors

Expected transport failures use `PartyGameTransportException` containing a technology-neutral `TransportError` and `TransportErrorCode` such as connection-not-found or transport-closed.

Cancellation remains standard .NET cancellation and surfaces as `OperationCanceledException`.

Concrete adapters may log native exceptions internally, but socket/SignalR/WebRTC exception types must not escape through the shared contract.

## In-memory reference transport

`PartyGameKit.Transport.InMemory` is a deterministic reference implementation and test harness. It provides peers that can:

- open/close a connection;
- inject bytes into the transport event stream;
- receive targeted/broadcast bytes from the transport.

The implementation copies payload bytes at the boundary so later mutation of caller-owned buffers cannot alter queued messages.

It is intentionally not a network simulator. Latency, packet loss and reconnect timing belong to later targeted tests.

## Disposal

`StopAsync` is idempotent. It closes all current peers with `TransportStopped`, publishes those close events in connection-open order, then completes the event stream.

`DisposeAsync` delegates to `StopAsync`.

A peer disposal represents a remote close and publishes exactly one `RemoteClosed` event.
