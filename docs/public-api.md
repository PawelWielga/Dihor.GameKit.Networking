# Public API review for 0.2

Dihor.GameKit.Networking `0.2.0-preview.2` exposes communication/networking primitives only. The corrected boundary was established in `[15]`–`[17]`; `[18]` adds SignalR as another transport without changing ownership of product/game semantics.

The detailed ownership rationale is in [Communication boundary](communication-boundary.md).

## Core

The public Core surface is intentionally small:

- `ConnectionId` — transient identity of one active transport connection;
- `PeerId` — optional stable communication identity used for resume/reconnect;
- `ChannelId` — optional technical routing/discovery scope;
- `ConnectionDescriptor` — transport, endpoint, protocol version and optional channel;
- `ConnectionContinuityCoordinator` and related presence/result types — neutral peer continuity across replaced connections;
- `MessageSequence` / `SequenceGate` — optional monotonic ordering utility;
- `MessageIdDeduplicator` — bounded duplicate detection keyed by stable `PeerId` plus protocol `messageId`, with configurable capacity/retention and explicit peer cleanup.

Core does not expose `PlayerId`, `AuthorityId`, `ClientRole`, `RoomSession`, player membership/capacity/admission or game snapshot projections.

## Protocol

The public protocol surface uses neutral connection language:

- `ProtocolVersions`;
- `ProtocolEnvelope<TPayload>`;
- connect request/accepted/rejected payloads;
- resume request/accepted/rejected payloads;
- heartbeat/disconnect payloads;
- `ApplicationMessagePayload` for opaque consumer data;
- `Dihor.GameKit.NetworkingMessages.Create`;
- `ProtocolJson`;
- `ConnectionDescriptorCodec`;
- `DiscoveryEndpointAnnouncementCodec`.

Dihor.GameKit.Networking control messages and application messages are distinct. The library does not inspect the game/product meaning of `ApplicationMessagePayload.Data`.

Protocol remains version `2` in `0.2.0-preview.2`.

## Transport abstractions

The transport package exposes technology-neutral message transport concepts:

- `IMessageTransport`;
- `TransportEvent`;
- `TransportConnectionOpened`;
- `TransportConnectionClosed`;
- `TransportMessageReceived`;
- `TransportFaulted`;
- neutral transport error/close/exception types;
- `LatestValueReplayBuffer` for latest-value/coalescing replay across replacement client connections.

The abstraction deals only with `ConnectionId` plus opaque bytes. It does not require a player, room or game session.

## Connection runtime

`Dihor.GameKit.Networking.Runtime` exposes the reusable communication lifecycle that previously had to be rebuilt by each .NET consumer:

- `ConnectionHostRuntime` and neutral host events;
- `ConnectionClientRuntime`, state and application-message events;
- transport connector and resume-credential-store contracts;
- host/client timeout, heartbeat and reconnect options;
- `MonotonicTimingScheduler` and refresh diagnostics.

The runtime composes Core, Protocol and Transport.Abstractions. Its public API contains no product participant, role, lobby, party, game-session or projection type.

## Concrete transports

### In-memory

`InMemoryTransport` is the deterministic reference/test implementation of `IMessageTransport`.

### LAN WebSocket

The supported public LAN surface includes:

- `LanWebSocketTransport`;
- `LanWebSocketClient`;
- `LanWebSocketHostOptions`;
- `LanConnectionDescriptor`.

LAN handshake validation accepts only Dihor.GameKit.Networking connection control messages. Player capacity/admission and other product rules remain above the transport.

### SignalR relay

`Dihor.GameKit.Networking.Transport.SignalR` exposes:

- `SignalRRelayOptions`;
- `SignalRRelayTransport : IMessageTransport` for the listener/multi-connection side;
- `SignalRRelayClient` for a single remote peer;
- `SignalRRelayClientMessage` for opaque inbound data/close notification.

The transport validates the same protocol-v2 connect/resume handshake boundary as LAN before publishing `TransportConnectionOpened`.

`Dihor.GameKit.Networking.Transport.SignalR.Server` deliberately exposes only server setup/mapping extensions:

- `AddDihorGameKitNetworkingSignalRRelay(...)`;
- `MapDihorGameKitNetworkingSignalRRelay(...)`;
- `SignalRRelayServerOptions` for transport-level limits.

The actual Hub and routing registry remain internal. They are not an application session API.

### LAN discovery

The supported discovery surface includes:

- UDP advertiser/listener;
- `DiscoveredEndpointRegistry`;
- `LanDiscoveryBroadcastAddressResolver`;
- neutral `ConnectionDescriptor` announcements.

Discovery locates technical endpoints, not product lobbies or game sessions.

## TypeScript browser SDK

`@dihor/gamekit-networking` `0.2.0-preview.2` exposes:

- `PartyGameClient` with connect/disconnect;
- send/receive of opaque application messages;
- connection state;
- heartbeat;
- optional stable peer identity persistence;
- resume/reconnect;
- protocol-v2 parsing/serialization;
- `ConnectionDescriptor` JSON/URI parsing;
- `LatestValueReplayBuffer<TMessage>` and `ReplaySender<TMessage>` for reconnect-safe transient replay;
- `MessageIdDeduplicator` for bounded receiver-side duplicate detection across replacement connections.

The base SDK has no required player/shared-screen/host role and no public/private game-state projection model.

Preview.2 does not add a browser SignalR implementation; its version is aligned with the supported Dihor.GameKit.Networking compatibility line while the browser API stays protocol-v2 compatible with preview.1.

## Dart packages

`dihor_gamekit_networking_protocol` implements the protocol-v2 compatibility layer in Dart:

- envelope parsing, creation, serialization and version admission;
- `DihorGameKitNetworkingConnectionDescriptor`;
- discovery announcement parsing;
- neutral peer/resume fields;
- opaque application-message payload parsing;
- generic message sequence gating.

`dihor_gamekit_networking` in `clients/dart` is the first Dart runtime layer. Its current `[27]` + `[28]` public surface includes:

- `DihorGameKitNetworkingClientTransport` and neutral transport messages;
- `DihorGameKitNetworkingLanWebSocketTransport` for Dart VM / Flutter mobile and desktop;
- `DihorGameKitNetworkingClient.connectLan(...)`;
- protocol-v2 initial connect and resume handshakes;
- optional stable `PeerId` plus pluggable identity/resume-credential storage;
- heartbeat and explicit disconnect control messages;
- manual bounded/cancellable reconnect on a replacement connection;
- observable connection state and transport/protocol errors;
- opaque application-message send/receive before and after resume;
- configurable connect/handshake timeouts and message-size limit.

The Dart runtime still contains no player/session/game model. Discovery, automatic connectivity, SignalR, WebRTC and synchronized timing remain follow-up runtime work rather than being faked by the LAN transport.

## Retired v0.1 concepts

The following categories are intentionally absent from the active `0.2` public base API:

- player identity/membership/capacity;
- host/shared-screen/controller roles;
- room/lobby lifecycle;
- authority/coordinator policy;
- product invitation/join-code semantics;
- game state and score;
- public/private/player state projections;
- authoritative snapshot publication/restoration policy.

If a future proposal adds one of these concepts, it must demonstrate that the abstraction solves a communication problem for an application with no players and no game session. Otherwise it belongs in the consumer.

## Validation

The current API boundary is protected by:

- reflection/public-surface guard tests in the C# test suite;
- the neutral `CommunicationDemo` running the same scenario over LAN and SignalR;
- in-process SignalR integration tests for routing isolation, targeted/broadcast delivery, protocol mismatch, resume and cleanup;
- package-only consumer validation including both SignalR NuGet packages;
- shared protocol-v2 fixtures consumed by C#, Dart and TypeScript;
- cross-repository validation against PartyBeam and Państwa Miasta.

The former `SharedCounter` and `DungeonPrototype` implementations validated the historical v0.1 foundation, but were retired from the active source tree after their framework dependencies were intentionally removed. They are not compatibility requirements for `0.2`.
