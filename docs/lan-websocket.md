# Direct LAN WebSocket transport

`PartyGameKit.Transport.Lan` is the first real PartyGameKit transport. It hosts a WebSocket listener directly on the local network and requires no cloud service for gameplay.

## Runtime boundary

The listener uses ASP.NET Core Kestrel and therefore runs in a server-capable .NET process: desktop, native/TV host, companion process or server. A browser/PWA can connect as a client but cannot be the inbound WebSocket listener.

Core/session/game code still depends only on `IGameTransport`. Kestrel, WebSocket, IP address and port types live exclusively in the concrete LAN package.

## Listener

`LanWebSocketTransport.StartAsync` accepts immutable `LanWebSocketHostOptions`:

- bind IP address;
- port (`0` asks Kestrel for an available dynamic port, useful for tests);
- WebSocket path;
- maximum complete message size;
- handshake timeout;
- WebSocket keep-alive interval.

After startup, `BoundPort` reports the actual bound port and `CreateClientUri(host)` builds a `ws://` URI for a chosen host address.

## Handshake

Every newly accepted socket must send exactly one **text** JSON message first. The message must be a valid protocol-v1:

- `session.join.request`, or
- `session.rejoin.request`.

The transport validates `type`, `protocolVersion` and the full PartyGameKit protocol envelope before exposing the connection to the application. A valid handshake becomes the first `TransportMessageReceived` event immediately after `TransportConnectionOpened`, so the application/session layer still owns join/rejoin policy.

Unsupported protocol versions are closed with WebSocket `PolicyViolation` and reason `protocol-version-mismatch`. Malformed or wrong-type handshakes use `invalid-handshake`. A handshake that does not arrive before the configured deadline uses `handshake-timeout`.

This deliberately does not make the transport decide whether a room exists, is full, or whether a reconnect credential is valid. Those are `[04]`/`[07]` session concerns.

## Framing after handshake

After handshake, `IGameTransport` continues to carry opaque bytes. Host-to-client sends use complete WebSocket **binary** messages. Clients may send complete text or binary messages; the transport emits their bytes without interpreting game payloads.

Fragmented WebSocket messages are reassembled before a transport event is emitted. A message exceeding `MaxMessageBytes` is closed with WebSocket `MessageTooBig` and reported as a technology-neutral transport fault.

`LanWebSocketClient` is a small .NET client helper for integration tests and native consumers. It is not a replacement for the TypeScript browser SDK planned in `[11]`.

## Reconnect

The transport itself never equates a socket with a player. A reconnect is a new socket and therefore a new `ConnectionId`:

1. the old socket closes and produces `TransportConnectionClosed`;
2. the application calls `[07]` continuity disconnect handling;
3. a new socket sends `session.rejoin.request` with stable `PlayerId` + reconnect token;
4. the transport publishes the new connection and request;
5. the continuity coordinator validates the credential/window and rebinds the existing player;
6. the application sends `session.rejoin.accepted` plus the newest `[06]` player snapshot.

No history replay or automatic host migration is introduced here.

## Shutdown

`StopAsync`/`DisposeAsync`:

- stops accepting new HTTP/WebSocket requests;
- closes every current WebSocket;
- emits one `TransportConnectionClosed(..., TransportStopped)` for each active connection;
- stops and disposes Kestrel;
- completes the transport event stream.

Loopback tests verify that disposal releases the listener port.

## LAN-only security model

The v0.1 direct transport is intentionally `ws://`, not Internet-facing TLS infrastructure.

Assumptions and limits:

- use it only on a trusted/local network;
- room join codes are routing/convenience values, not strong secrets;
- reconnect tokens are the credential for resuming an existing player and must not be logged or displayed publicly;
- the host validates protocol version before exposing a connection;
- complete-message size and handshake-time limits bound trivial resource abuse;
- no claim is made that `Origin` proves identity; native clients may not send it and browsers can run from different local origins;
- do not expose this listener directly to the public Internet without a secure deployment layer, authentication/rate limiting and TLS termination designed for that environment.

SignalR/cloud authentication and WebRTC security belong to their later transports rather than being smuggled into the LAN contract.
