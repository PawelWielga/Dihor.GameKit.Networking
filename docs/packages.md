# Package boundaries

PartyGameKit `0.2.0-preview.3` is a communication/networking library. The historical `0.1.0-preview.1` room/player/session surface has been removed from the active package line.

The authoritative ownership decision is [Communication boundary](communication-boundary.md).

## Package rule

A PartyGameKit package may depend on communication concepts. It must not require consumers to adopt players, hosts, shared screens, game sessions, authority policy, scoring or game-state projections.

## Current .NET packages

### `PartyGameKit.Core`

Contains small communication-neutral primitives:

- transient `ConnectionId`;
- optional stable `PeerId` used for connection continuity;
- optional technical `ChannelId`;
- `ConnectionDescriptor`;
- `ConnectionContinuityCoordinator` and peer-presence state;
- `MessageSequence` / `SequenceGate` generic ordering helpers.

It does not contain player membership, room lifecycle, product roles, authority or game snapshots.

### `PartyGameKit.Protocol`

Owns the language-neutral protocol-v2 communication contract:

- neutral envelopes, message IDs and correlation IDs;
- connect/resume/heartbeat/disconnect control messages;
- opaque `application.message` boundary;
- protocol compatibility validation;
- `ConnectionDescriptor` JSON/URI codecs;
- LAN discovery announcement codec.

### `PartyGameKit.Transport.Abstractions`

Owns technology-neutral connection/message events and operations:

- `IMessageTransport`;
- transient connection open/close lifecycle;
- receive events;
- targeted send;
- broadcast;
- transport fault reporting;
- cancellation/disposal.

### `PartyGameKit.Transport.InMemory`

Deterministic transport for tests, package consumers and applications that need an in-process implementation of the same message contract.

### `PartyGameKit.Transport.Lan`

Direct LAN WebSocket communication using the same neutral protocol/transport boundary. It validates connect/resume admission but does not own product/player admission or game-session rules.

### `PartyGameKit.Transport.SignalR`

Optional backend-assisted connectivity over SignalR.

It provides:

- `SignalRRelayTransport` as the listener/multi-connection implementation of `IMessageTransport`;
- `SignalRRelayClient` as the single-peer counterpart;
- the same protocol-v2 connect/resume handshake semantics as LAN;
- targeted delivery, broadcast and transport lifecycle events;
- opaque `ChannelId` routing scopes without product/session meaning.

The client package depends on the SignalR client stack but does not require an ASP.NET Core server reference.

### `PartyGameKit.Transport.SignalR.Server`

Minimal ASP.NET Core communication hosting support.

The public setup surface includes:

- `AddPartyGameKitSignalRRelay(...)` / `MapPartyGameKitSignalRRelay(...)` for opaque application-data relay;
- `AddPartyGameKitWebRtcSignaling(...)` / `MapPartyGameKitWebRtcSignaling(...)` for SDP/ICE signaling only.

The hubs and registries remain implementation details. Relay routing uses technical `ChannelId` and transient `ConnectionId`; WebRTC signaling uses technical `ChannelId` plus transient SignalR connection IDs. Neither endpoint models players, parties, lobbies, authority or game state.

See [SignalR relay](signalr-relay.md) and [WebRTC DataChannel](webrtc-datachannel.md).

### `PartyGameKit.Discovery.Lan`

Optional UDP discovery for technical `ConnectionDescriptor` endpoints. Discovery is convenience infrastructure only; a valid descriptor can always be supplied directly.

## Browser package

### `@partygamekit/client`

The `0.2.0-preview.3` browser client exposes two communication paths.

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

WebRTC application data does not flow through SignalR after negotiation. The SDK does not require `player`, `host`, `controller` or `shared-screen` roles and does not implement game-state projection policy.

The npm runtime dependency added for `[19]` is MIT-licensed `@microsoft/signalr`; WebRTC itself is provided by the browser runtime. Playwright is a dev-only Apache-2.0 dependency for real-browser CI validation.

## Dart package

### `partygamekit_protocol`

The Dart package is a thin implementation of the PartyGameKit v2 protocol and connection-descriptor contract.

`0.2.0-preview.3` keeps the same protocol-v2 wire contract. It deliberately does not claim a Dart WebRTC runtime, SignalR transport or game/session engine. Państwa Miasta keeps its player model, host-authoritative game engine, snapshots and lifecycle policy above the adapter boundary.

## Reference validation

`samples/CommunicationDemo` remains the active .NET v0.2 reference sample. It runs the same neutral communication scenario over direct LAN WebSocket and backend-assisted SignalR, covering two generic peers, opaque messages, targeted/broadcast delivery and resume on a replacement connection. LAN additionally validates UDP endpoint discovery and a serialized direct connection descriptor.

WebRTC is validated separately in the browser package through unit tests and a real Chromium integration test that establishes peer-to-peer DataChannels and exercises an approximately 60 Hz opaque stream without routing application traffic through signaling.

## Dependency direction

```text
        PartyBeam / Państwa Miasta / other apps
                         │
                         ▼
              application/game protocol
                         │
                         ▼
                  PartyGameKit 0.2
        ┌────────────────┼────────────────────────────┐
        ▼                ▼                            ▼
      Core            Protocol               Transport/browser APIs
        │                │               ┌─────────────┼──────────────┐
        │                │               ▼             ▼              ▼
        │                │          LAN WebSocket  SignalR relay  WebRTC browser
        │                │               │             │              │
        └────────────────┴──── optional discovery   backend      SDP/ICE signaling
```

No base package may depend upward on PartyBeam or concrete game semantics.

## Package-only and browser validation

CI builds NuGet packages first, restores `packaging/consumer` using only those generated artifacts and validates package boundaries. It also builds/tests the npm package and runs the real Chromium WebRTC test.

The combined gate verifies:

- opaque `application.message` exchange;
- neutral connection events;
- stable `PeerId` resume on a replacement `ConnectionId`;
- SignalR client/server package availability from the generated NuGet feed;
- WebRTC signaling isolation;
- real browser DataChannel establishment and high-frequency bounded messaging;
- absence of source-project dependencies in the NuGet consumer.
