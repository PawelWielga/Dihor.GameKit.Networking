# Networking

Dihor.GameKit.Networking networking is intentionally product-neutral. It connects peers and transports messages; it does not model players, parties or game sessions.

See [Communication boundary](communication-boundary.md).

## Core communication flow

```text
consumer application
       │ opaque application data
       ▼
Dihor.GameKit.Networking communication layer
       │
       ├── protocol v2 over LAN WebSocket
       ├── protocol v2 over SignalR relay
       └── direct browser WebRTC DataChannel
```

WebRTC signaling is infrastructure only. SDP/ICE may pass through the optional SignalR signaling endpoint, but normal DataChannel application traffic is peer-to-peer after negotiation.

## Transport abstraction

A transport exposes only communication concerns:

- connection open/close/error events;
- incoming opaque payloads;
- targeted send;
- broadcast/multicast where supported;
- disconnect/stop/disposal;
- cancellation;
- technology-neutral errors/diagnostics.

`IMessageTransport` is the listener-side .NET abstraction used by direct LAN and SignalR relay transports. A transport may have a technology-specific single-peer client counterpart, such as `LanWebSocketClient` or `SignalRRelayClient`, without changing the application protocol.

The initial WebRTC implementation is browser-native in `@dihor/gamekit-networking`; it is not presented as a .NET `IMessageTransport` implementation. That keeps the public surface honest about runtime capabilities.

`ConnectionId` identifies a transient network connection. It is not a player id.

## Logical peer identity

Reconnect may require an optional stable `PeerId` that outlives one connection.

```text
PeerId P1
  ├── ConnectionId C1   (old/disconnected)
  └── ConnectionId C2   (replacement/resumed)
```

Dihor.GameKit.Networking may validate a resume credential and rebind C2 to P1. The consumer decides whether P1 represents a player, TV, controller, server or something else.

The same continuity coordinator can be used when the physical protocol-v2 path is LAN or SignalR. The relay backend itself never owns `PeerId` or resume credentials.

WebRTC signaling connection IDs are separate transient routing identifiers and must not be treated as `PeerId` values.

## Routing scope

Some communication paths require an opaque routing scope to isolate delivery. `ChannelId` is communication-only and does not imply lobby/game lifecycle, capacity or authority.

For SignalR relay, the backend registry maps a `ChannelId` to one active listener and transient clients. For WebRTC signaling, it scopes which transient signaling connections may exchange SDP/ICE. Neither registry is a PartyBeam party/session store.

## Application messages

Application payloads are opaque to Dihor.GameKit.Networking.

A consumer may transport:

- a Countries & Cities answer command;
- a PartyBeam controller action;
- a game snapshot;
- a collaborative document operation;
- any other consumer-defined payload.

None of those schemas become Dihor.GameKit.Networking base API.

LAN WebSocket and SignalR relay carry protocol-v2 `application.message` payloads. Browser WebRTC DataChannels carry opaque binary consumer data directly. A consumer may serialize the same logical schema for all three paths, but Dihor.GameKit.Networking does not require a game-specific schema.

## Transient latest-value replay

`LatestValueReplayBuffer` is an optional layer above a connected sender. It is intended for transient state where only the newest value remains useful.

Staging while disconnected only updates the buffer. Binding a replacement sender after connect/resume immediately replays the newest value for each key. Scope/epoch replacement and explicit clear/invalidate prevent stale buffered state from leaking into a new logical context.

Replay stays separate from heartbeat and concrete transports, so the same buffered application message can survive an automatic LAN -> WebRTC -> SignalR path change.

Replay is not exactly-once delivery. The same staged `messageId` may be observed more than once after an ambiguous disconnect. `MessageIdDeduplicator` optionally rejects duplicate `(PeerId, messageId)` pairs across replacement connections and transport fallback while keeping memory bounded by capacity and retention.

See [Transient latest-value replay](transient-replay.md) and [Message-id deduplication](message-id-deduplication.md).

## Connection health

Heartbeat/timeout reports connectivity. It does not carry transient application replay state and does not trigger player leave, game pause or authority migration.

Consumers subscribe to communication state and apply their own product policies.

For WebRTC, `sampleDiagnostics()` exposes communication-only values such as candidate-pair RTT, RTT variation, buffered bytes and dropped-message count.

## LAN first

Direct LAN WebSocket remains the backend-free listener transport and works without Internet/cloud after local dependencies are available.

The listening side requires a server-capable runtime. This is a transport constraint, not a generic host role.

UDP LAN discovery is optional and remains independent from backend-assisted connectivity.

## Optional SignalR relay

`Dihor.GameKit.Networking.Transport.SignalR` and `Dihor.GameKit.Networking.Transport.SignalR.Server` provide an optional path for peers that cannot use direct LAN communication or are on different networks.

The relay:

- carries the same protocol-v2 handshake and opaque application payloads;
- uses technical `ChannelId` routing only;
- exposes targeted send and broadcast through `IMessageTransport`;
- removes transient bindings when clients/listeners disconnect;
- leaves stable `PeerId` continuity to the Dihor.GameKit.Networking protocol/core layer;
- does not implement PartyBeam parties, player admission, product join codes or game state.

SignalR deployment is optional. See [SignalR relay](signalr-relay.md) for server configuration and security assumptions.

## Browser WebRTC DataChannel

`@dihor/gamekit-networking` provides direct browser-to-browser communication through native `RTCPeerConnection` / `RTCDataChannel`.

The library exposes explicit communication profiles:

- `reliable` — ordered, fully reliable DataChannel;
- `low-latency` — unordered DataChannel with `maxRetransmits: 0`.

Dihor.GameKit.Networking does not decide which product messages belong on which profile.

Buffering is deliberately bounded. Reliable mode reports backpressure when a configured limit would be exceeded; low-latency mode can drop the newest payload rather than allowing stale data to accumulate in an unbounded Dihor.GameKit.Networking queue.

See [WebRTC DataChannel](webrtc-datachannel.md).

## WebRTC signaling

The optional ASP.NET Core signaling endpoint is separate from the SignalR application-data relay.

It carries only:

- transient signaling connection IDs;
- technical `ChannelId` scope;
- SDP offer/answer data;
- ICE candidates.

It rejects cross-channel targets and applies a signal-size limit. The backend does not inspect or relay normal WebRTC application payloads.

A deployed TURN service may still be required for some Internet/NAT topologies. Implementing or operating TURN is outside `[19]`.

## Automatic transport selection

Automatic selection/fallback belongs to `[20]`. Until then LAN, SignalR relay and WebRTC are explicit communication choices.

Future selection logic may choose a path based on connectivity/capability. It must not decide product lifecycle, player admission, authority or game policy.
