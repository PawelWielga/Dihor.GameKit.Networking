# Public API review for 0.2

PartyGameKit `0.2.0-preview.1` exposes communication/networking primitives only. This document records the result of the boundary correction that began in `[15]` and was implemented across `[16]`–`[17]`.

The detailed ownership rationale is in [Communication boundary](communication-boundary.md).

## Core

The public Core surface is intentionally small:

- `ConnectionId` — transient identity of one active transport connection;
- `PeerId` — optional stable communication identity used for resume/reconnect;
- `ChannelId` — optional technical routing/discovery scope;
- `ConnectionDescriptor` — transport, endpoint, protocol version and optional channel;
- `ConnectionContinuityCoordinator` and related presence/result types — neutral peer continuity across replaced connections;
- `MessageSequence` / `SequenceGate` — optional generic ordering/deduplication utility.

Core does not expose `PlayerId`, `AuthorityId`, `ClientRole`, `RoomSession`, player membership/capacity/admission or game snapshot projections.

## Protocol

The public protocol surface uses neutral connection language:

- `ProtocolVersions`;
- `ProtocolEnvelope<TPayload>`;
- connect request/accepted/rejected payloads;
- resume request/accepted/rejected payloads;
- heartbeat/disconnect payloads;
- `ApplicationMessagePayload` for opaque consumer data;
- `PartyGameKitMessages.Create`;
- `ProtocolJson`;
- `ConnectionDescriptorCodec`;
- `DiscoveryEndpointAnnouncementCodec`.

PartyGameKit control messages and application messages are distinct. The library does not inspect the game/product meaning of `ApplicationMessagePayload.Data`.

## Transport abstractions

The transport package exposes technology-neutral message transport concepts:

- `IMessageTransport`;
- `TransportEvent`;
- `TransportConnectionOpened`;
- `TransportConnectionClosed`;
- `TransportMessageReceived`;
- `TransportFaulted`;
- neutral transport error/close/exception types.

The abstraction deals only with `ConnectionId` plus opaque bytes. It does not require a player, room or game session.

## Concrete transports

### In-memory

`InMemoryTransport` is the deterministic reference/test implementation of `IMessageTransport`.

### LAN WebSocket

The supported public LAN surface includes:

- `LanWebSocketTransport`;
- `LanWebSocketClient`;
- `LanWebSocketHostOptions`;
- `LanConnectionDescriptor`.

LAN handshake validation accepts only PartyGameKit connection control messages. Player capacity/admission and other product rules remain above the transport.

### LAN discovery

The supported discovery surface includes:

- UDP advertiser/listener;
- `DiscoveredEndpointRegistry`;
- `LanDiscoveryBroadcastAddressResolver`;
- neutral `ConnectionDescriptor` announcements.

Discovery locates technical endpoints, not product lobbies or game sessions.

## TypeScript browser SDK

`@partygamekit/client` `0.2.0-preview.1` exposes:

- `PartyGameClient` with connect/disconnect;
- send/receive of opaque application messages;
- connection state;
- heartbeat;
- optional stable peer identity persistence;
- resume/reconnect;
- protocol-v2 parsing/serialization;
- `ConnectionDescriptor` JSON/URI parsing.

The base SDK has no required player/shared-screen/host role and no public/private game-state projection model.

## Dart interoperability package

`partygamekit_protocol` implements the protocol-v2 compatibility layer in Dart:

- envelope parsing/version admission;
- `PartyGameKitConnectionDescriptor`;
- discovery announcement parsing;
- neutral peer/resume fields;
- opaque application-message payload parsing;
- generic message sequence gating.

It deliberately does not duplicate a game/session engine or Dart transport runtime.

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
- the neutral `CommunicationDemo` using the real LAN transport;
- package-only consumer validation;
- shared protocol-v2 fixtures consumed by C#, Dart and TypeScript;
- cross-repository validation against PartyBeam and Państwa Miasta.

The former `SharedCounter` and `DungeonPrototype` implementations validated the historical v0.1 foundation, but were retired from the active source tree after their framework dependencies were intentionally removed. They are not compatibility requirements for `0.2`.
