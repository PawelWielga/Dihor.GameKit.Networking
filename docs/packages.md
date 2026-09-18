# Package boundaries

Dihor.GameKit.Networking `0.2.0-preview.5` is a communication/networking library. The historical `0.1.0-preview.1` room/player/session surface has been removed from the active package line.

The authoritative ownership decision is [Communication boundary](communication-boundary.md).

## Package rule

A Dihor.GameKit.Networking package may depend on communication concepts. It must not require consumers to adopt players, hosts, shared screens, game sessions, authority policy, scoring or game-state projections.

## Current .NET packages

### `Dihor.GameKit.Networking.Core`

Contains small communication-neutral primitives:

- transient `ConnectionId`;
- optional stable `PeerId` used for connection continuity;
- optional technical `ChannelId`;
- `ConnectionDescriptor`;
- `ConnectionContinuityCoordinator` and peer-presence state;
- `MessageSequence` / `SequenceGate` generic ordering helpers;
- `MonotonicClock` and `MonotonicTimingSynchronizer` for bounded peer/reference clock-offset, RTT, jitter, uncertainty and timestamp normalization.

The timing API reports communication evidence only. It does not choose reaction winners, scoring or acceptable quality thresholds.

It does not contain player membership, room lifecycle, product roles, authority policy or game snapshots.

### `Dihor.GameKit.Networking.Protocol`

Owns the language-neutral protocol-v2 communication contract:

- neutral envelopes, message IDs and correlation IDs;
- connect/resume/heartbeat/disconnect control messages;
- opaque `application.message` boundary;
- protocol compatibility validation;
- `ConnectionDescriptor` JSON/URI codecs;
- LAN discovery announcement codec.

Synchronized timing does not add a protocol-v2 control message. Probe/reply data remains caller/adapter-owned payload data.

### `Dihor.GameKit.Networking.Transport.Abstractions`

Owns technology-neutral host/client communication contracts and orchestration:

- `IMessageTransport` for listener/multi-connection transports;
- `IMessageTransportClient` for a single connected client path;
- transient connection open/close lifecycle;
- receive events;
- targeted send and broadcast on the listener side;
- client send/receive/disposal;
- transport fault reporting;
- `ConnectivityMode` / `AutomaticTransportSelector`;
- deterministic candidate ordering, timeout budgets and reconnect preference;
- structured automatic-connectivity diagnostics;
- cancellation and late-connection cleanup.

The selector works on registered communication candidates only. It does not own product roles, sessions, game state or application-failure policy.

### `Dihor.GameKit.Networking.Transport.InMemory`

Deterministic transport for tests, package consumers and applications that need an in-process implementation of the same message contract.

### `Dihor.GameKit.Networking.Transport.Lan`

Direct LAN WebSocket communication using the same neutral protocol/transport boundary. It validates connect/resume admission but does not own product/player admission or game-session rules.

`LanWebSocketClient` implements `IMessageTransportClient`, so it can be registered as an automatic-connectivity candidate without hiding its concrete LAN API from callers that intentionally force LAN.

### `Dihor.GameKit.Networking.Transport.SignalR`

Optional backend-assisted connectivity over SignalR.

It provides:

- `SignalRRelayTransport` as the listener/multi-connection implementation of `IMessageTransport`;
- `SignalRRelayClient` as the single-peer counterpart and `IMessageTransportClient` implementation;
- the same protocol-v2 connect/resume handshake semantics as LAN;
- targeted delivery, broadcast and transport lifecycle events;
- opaque `ChannelId` routing scopes without product/session meaning.

The client package depends on the SignalR client stack but does not require an ASP.NET Core server reference.

### `Dihor.GameKit.Networking.Transport.SignalR.Server`

Minimal ASP.NET Core communication hosting support.

The public setup surface includes:

- `AddDihorGameKitNetworkingSignalRRelay(...)` / `MapDihorGameKitNetworkingSignalRRelay(...)` for opaque application-data relay;
- `AddDihorGameKitNetworkingWebRtcSignaling(...)` / `MapDihorGameKitNetworkingWebRtcSignaling(...)` for SDP/ICE signaling only.

The hubs and registries remain implementation details. Relay routing uses technical `ChannelId` and transient `ConnectionId`; WebRTC signaling uses technical `ChannelId` plus transient SignalR connection IDs. Neither endpoint models players, parties, lobbies, authority or game state.

See [SignalR relay](signalr-relay.md), [WebRTC DataChannel](webrtc-datachannel.md), [Automatic connectivity](automatic-connectivity.md) and [Synchronized monotonic timing](monotonic-timing.md).

### `Dihor.GameKit.Networking.Discovery.Lan`

Optional UDP discovery for technical `ConnectionDescriptor` endpoints. Discovery is convenience infrastructure only; a valid descriptor can always be supplied directly.

## Browser package

### `@dihor/gamekit-networking`

The `0.2.0-preview.5` browser client exposes protocol-v2 WebSocket, native WebRTC, generic transport-selection and monotonic-timing capabilities.

Protocol-v2 WebSocket capabilities:

- connect/disconnect;
- send/receive opaque application messages;
- connection state and heartbeat;
- optional stable peer identity;
- resume/reconnect;
- protocol compatibility;
- `ConnectionDescriptor` parsing.

Browser-native WebRTC capabilities:

- `WebRtcPeer` using the runtime's `RTCPeerConnection` / `RTCDataChannel`;
- reliable ordered and low-latency unordered/no-retransmit profiles;
- bounded `bufferedAmount` policy with explicit reject/drop behavior;
- bounded pre-description ICE candidate buffering;
- direct opaque binary peer-to-peer payloads;
- communication diagnostics including RTT samples and dropped-message count;
- neutral `WebRtcSignalingChannel` abstraction;
- optional `SignalRWebRtcSignalingClient` for SDP/ICE routing.

Automatic selection capabilities:

- generic `AutomaticTransportSelector<TContext, TConnection>`;
- default transport IDs/order for LAN, WebRTC and SignalR;
- forced transport modes;
- per-attempt timeout budgets;
- previous-path preference on reconnect;
- structured attempt diagnostics;
- abort and late-success cleanup hooks.

Monotonic timing capabilities:

- `monotonicNowMs()` backed by `performance.now()`;
- `MonotonicTimingSynchronizer` using the same bounded sample/filter policy as .NET;
- current offset, RTT, jitter and uncertainty diagnostics;
- peer-event normalization into the reference clock domain;
- rejection of invalid/stale/non-monotonic timestamp evidence;
- explicit reset/reacquisition after reconnect or transport replacement.

The TypeScript selector and timing utility do not pretend all browser transports share one connection model or that timing quality determines product/game policy.

WebRTC application data does not flow through SignalR after negotiation. The SDK does not require `player`, `host`, `controller` or `shared-screen` roles and does not implement game-state projection policy.

The npm runtime dependency added for `[19]` is MIT-licensed `@microsoft/signalr`; WebRTC itself is provided by the browser runtime. Playwright is a dev-only Apache-2.0 dependency for real-browser CI validation. `[20]` and `[21]` add no new external dependency.

## Dart package

### `dihor_gamekit_networking_protocol`

The Dart package is a thin implementation of the Dihor.GameKit.Networking v2 protocol and connection-descriptor contract.

`0.2.0-preview.5` keeps the same protocol-v2 wire contract. It deliberately does not claim a Dart LAN, SignalR, WebRTC, automatic transport or synchronized timing runtime and does not contain a game/session engine. Państwa Miasta keeps its player model, host-authoritative game engine, snapshots and lifecycle policy above the adapter boundary.

## Reference validation

`samples/CommunicationDemo` remains the listener-side .NET v0.2 reference sample. It runs the same neutral communication scenario over direct LAN WebSocket and backend-assisted SignalR, covering two generic peers, opaque messages, targeted/broadcast delivery and resume on a replacement connection. LAN additionally validates UDP endpoint discovery and a serialized direct connection descriptor.

`samples/AutoConnectivityDemo` validates the client-side automatic orchestration boundary. The scenario asks for `ConnectivityMode.Auto`, receives only `IMessageTransportClient`, preserves a stable `PeerId` and verifies byte-for-byte opaque payload delivery. The concrete LAN client appears only where the candidate is registered.

Monotonic timing is validated with deterministic .NET and TypeScript tests covering known offsets, RTT variation, uncertainty, stale/non-monotonic/implausible evidence, reconnect reset, bounded storage and normalized ordering that differs from packet arrival.

WebRTC is validated separately in the browser package through unit tests and a real Chromium integration test that establishes peer-to-peer DataChannels and exercises an approximately 60 Hz opaque stream without routing application traffic through signaling.

## Dependency direction

```text
        PartyBeam / Państwa Miasta / other apps
                         │
                         ▼
              application/game protocol
                         │
                         ▼
                  Dihor.GameKit.Networking 0.2
        ┌────────────────┼────────────────────────────┐
        ▼                ▼                            ▼
      Core            Protocol               Transport/browser APIs
        │                │               ┌─────────────┼──────────────┐
        │                │               ▼             ▼              ▼
        │                │          LAN WebSocket  SignalR relay  WebRTC browser
        │                │               │             │              │
        └────────────────┴──── optional discovery   backend      SDP/ICE signaling
                                           │
                                  automatic selector
                         timing stays transport-neutral
```

No base package may depend upward on PartyBeam or concrete game semantics.

## Package-only and browser validation

CI builds NuGet packages first, restores `packaging/consumer` using only those generated artifacts and validates package boundaries. It also builds/tests the npm package and runs the real Chromium WebRTC test.

The combined gate verifies:

- opaque `application.message` exchange;
- neutral connection events;
- stable `PeerId` resume on a replacement `ConnectionId`;
- package-only monotonic timing offset/normalization through `Dihor.GameKit.Networking.Core`;
- SignalR client/server package availability from the generated NuGet feed;
- bounded/deterministic automatic transport selection and cancellation cleanup;
- neutral Auto sample behavior;
- deterministic .NET/TypeScript timing tests and bounded timing storage;
- WebRTC signaling isolation;
- real browser DataChannel establishment and high-frequency bounded messaging;
- absence of source-project dependencies in the NuGet consumer.
