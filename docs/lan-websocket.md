# Direct LAN WebSocket transport

`PartyGameKit.Transport.Lan` hosts a WebSocket listener directly on the local network and requires no cloud service.

## Runtime boundary

The listener uses ASP.NET Core Kestrel and therefore runs in a server-capable .NET process: desktop, native/TV runtime, companion process or server. A browser/PWA can connect as a client but cannot be the inbound WebSocket listener.

Application code can depend on the neutral `IMessageTransport`; Kestrel, WebSocket, IP address and port types remain in the concrete LAN package.

## Listener

`LanWebSocketTransport.StartAsync` accepts immutable `LanWebSocketHostOptions`:

- bind IP address;
- port (`0` asks Kestrel for a dynamic available port, useful for tests);
- WebSocket path;
- maximum complete message size;
- handshake timeout;
- WebSocket keep-alive interval.

After startup, `BoundPort` reports the actual port and `CreateClientUri(host)` builds a `ws://` endpoint for a chosen LAN address.

## Protocol-v2 handshake

Every newly accepted socket must send exactly one text JSON message first. It must be a valid protocol-v2 envelope of one of these types:

- `connection.connect.request`;
- `connection.resume.request`.

The transport validates message type, protocol version and payload shape before exposing the connection. A valid handshake becomes the first `TransportMessageReceived` immediately after `TransportConnectionOpened`.

The transport deliberately validates only communication syntax. It does not decide whether a peer is a player, whether a party is full, whether a product session exists or whether a resume credential is valid. The consumer can pass connect/resume data to its own orchestration and `ConnectionContinuityCoordinator` as needed.

Unsupported protocol versions are closed with WebSocket `PolicyViolation` and reason `protocol-version-mismatch`. Malformed or wrong-type handshakes use `invalid-handshake`. A handshake that does not arrive before the deadline uses `handshake-timeout`.

## Framing after handshake

After the handshake, `IMessageTransport` carries opaque bytes.

Host/listener-to-client sends use complete WebSocket binary messages. Clients may send complete text or binary messages; the transport emits their bytes without interpreting consumer payloads.

Fragmented WebSocket messages are reassembled before a transport event is emitted. A message exceeding `MaxMessageBytes` is closed with WebSocket `MessageTooBig` and reported as a technology-neutral transport fault.

`LanWebSocketClient` is a small .NET helper used by integration tests and native consumers.

## Connection descriptors

`LanConnectionDescriptor.Create(...)` builds a neutral `ConnectionDescriptor` containing:

- transport `lan-websocket`;
- `ws://` or `wss://` endpoint;
- PartyGameKit protocol version;
- optional `ChannelId` technical routing scope.

`ConnectionDescriptorCodec` serializes the descriptor to JSON or a portable `partygamekit://connect?...` form. Product invitation codes, party metadata and QR presentation belong above PartyGameKit.

## Reconnect

A replacement WebSocket always receives a new transient `ConnectionId`.

A typical resume flow is:

1. first socket closes and produces `TransportConnectionClosed`;
2. consumer calls `ConnectionContinuityCoordinator.MarkDisconnected(oldConnectionId)`;
3. replacement socket sends `connection.resume.request` with stable neutral `PeerId` and resume token;
4. transport emits a new `TransportConnectionOpened` and the resume request;
5. consumer calls `ConnectionContinuityCoordinator.Resume(peerId, token, newConnectionId)`;
6. on success, the same logical `PeerId` is bound to the replacement connection and the resume token is rotated;
7. the consumer independently decides whether any application state must be resent.

PartyGameKit does not restore game state, preserve player slots or migrate game authority.

## Shutdown

`StopAsync`/`DisposeAsync`:

- stops accepting new HTTP/WebSocket requests;
- closes active sockets;
- emits `TransportConnectionClosed(..., TransportStopped)` for active connections;
- stops/disposes Kestrel;
- completes the transport event stream.

## LAN security model

The default direct transport is intended for trusted/local networks.

Important limits:

- `ws://` has no transport encryption; use `wss://` only behind an appropriate secure setup;
- resume tokens are credentials and must not be logged/displayed publicly;
- protocol validation, message-size limits and handshake timeouts bound basic misuse but are not Internet-facing authentication/rate limiting;
- a `ChannelId` is routing metadata, not a secret;
- do not expose the listener directly to the public Internet without TLS, authentication and rate limiting designed for that environment.

SignalR/cloud authentication and WebRTC security are separate future transport concerns.
