# Public API classification

This review supersedes the original v0.1 preview API review. It records the boundary decision required by issue `[15]` before production API changes are made in `[16]`.

The detailed rationale is in [Communication boundary](communication-boundary.md).

## Classification meanings

- **KEEP**: communication concern; public concept remains valid, possibly moved to a better package.
- **MOVE OUT**: product/game/session concern; remove from base PartyGameKit production API.
- **RENAME/GENERALIZE**: useful capability with vocabulary or semantics that are too product-specific.
- **OPTIONAL UTILITY**: useful generic helper, but not part of the required communication model.

## Core

| Current API | Decision | Target |
| --- | --- | --- |
| `ConnectionId` | **KEEP** | Transient transport connection identity. Move to the lowest coherent communication package. |
| `PlayerId` | **RENAME/GENERALIZE** | Optional stable neutral `PeerId`/logical client identity used for reconnect/routing only. |
| `RoomId` | **RENAME/GENERALIZE** | Optional technical `ChannelId`/`ScopeId` only if routing isolation requires it; otherwise consumer-owned. |
| `AuthorityId` | **MOVE OUT** | Consumer authority/coordinator policy. |
| `JoinCode` | **MOVE OUT** | Product invitation/admission UX. |
| `ClientRole` | **MOVE OUT** | `Host`, `Player`, `SharedScreen` are product roles. |
| `RoomSession` / `RoomSessionState` | **MOVE OUT** | Product/game session lifecycle. |
| `PlayerPresence` | **MOVE OUT/GENERALIZE** | Player presence moves out; neutral peer/connection health may remain. |
| membership/result/lifecycle event types | **MOVE OUT** | Player admission, capacity, leave and role semantics belong to consumers. |
| `JoinDescriptor` | **RENAME/GENERALIZE** | Neutral technical `ConnectionDescriptor`, without room/join-code semantics. |
| `SessionContinuityOptions` | **RENAME/GENERALIZE** | Neutral connection-health/resume options; remove host-specific policy. |
| `ConnectionPresence` | **KEEP/GENERALIZE** | Connectivity timestamp/status without role/player coupling. |
| `PresenceTimeoutKind.HostLost` | **MOVE OUT** | Host loss is product semantics. Generic timeout remains. |
| `SessionContinuityCoordinator<TPublicState,TPrivateState>` | **RENAME/GENERALIZE** | Split into neutral health + resume coordination; remove player/session/snapshot coupling. |
| reconnect credential/token logic | **KEEP** | Neutral peer resume security. |
| `SnapshotSequence` | **OPTIONAL UTILITY** | Generalize to message sequence metadata if useful. |
| `SnapshotSequenceGate` | **OPTIONAL UTILITY** | General sequence/deduplication helper. |
| `SnapshotAudience` / `SnapshotTarget` | **MOVE OUT** | Public/player projection policy. |
| `PublicStateProjection` / `PlayerStateProjection` | **MOVE OUT** | Consumer state model. |
| `StateSnapshot` / `PublishedSnapshotSet` | **MOVE OUT** | Consumer game/application state replication. |
| `AuthoritativeSnapshotPublisher` | **MOVE OUT** | Authority and game-state publication policy. |

`PartyGameKit.Core` itself is **RENAME/GENERALIZE** as a package boundary. `[16]` may split, collapse or remove it rather than keep an incoherent package for compatibility.

## Protocol

| Current API | Decision | Target |
| --- | --- | --- |
| `ProtocolVersions` | **KEEP** | Communication wire compatibility. |
| `ProtocolEnvelope<TPayload>` | **KEEP** | Neutral versioned envelope. |
| `ProtocolMessageTypes` | **RENAME/GENERALIZE** | Neutral connection/control/application message types. |
| `JoinRequestPayload` | **RENAME/GENERALIZE** | Connection handshake; no player role, capacity or authority semantics. |
| `JoinAcceptedPayload` | **RENAME/GENERALIZE** | Connection accepted; neutral `ConnectionId` and optional `PeerId`. |
| `JoinRejectedPayload` / room-full rejection | **RENAME/GENERALIZE** | Protocol/connection rejection only. Product admission errors move out. |
| `LeavePayload` | **RENAME/GENERALIZE** | Connection close/disconnect semantics only. |
| `DisconnectedPayload` | **RENAME/GENERALIZE** | Neutral connectivity event. |
| `HeartbeatPayload` | **RENAME/GENERALIZE** | Keep heartbeat; remove room/snapshot fields. |
| rejoin request/accept/reject payloads | **RENAME/GENERALIZE** | Neutral peer resume. |
| `StateSnapshotPayload<TState>` | **MOVE OUT** | Consumer application message. |
| `PartyGameKitMessages.Create` | **KEEP/GENERALIZE** | Generic envelope factory. |
| `JoinDescriptorCodec` | **RENAME/GENERALIZE** | Connection descriptor codec. |

The protocol must clearly separate PartyGameKit control messages from opaque consumer/application messages.

## Transport abstractions

| Current API | Decision | Target |
| --- | --- | --- |
| `IGameTransport` | **RENAME/GENERALIZE** | Neutral `ITransport`/`IMessageTransport`. |
| `TransportEvent` | **KEEP** | Communication lifecycle. |
| `TransportConnectionOpened` | **KEEP** | Communication lifecycle. |
| `TransportConnectionClosed` | **KEEP** | Communication lifecycle. |
| `TransportMessageReceived` | **KEEP** | Opaque message delivery. |
| `TransportFaulted` | **KEEP** | Communication diagnostics. |
| `TransportError` / codes | **KEEP** | Technology-neutral errors. |
| `TransportCloseReason` | **KEEP** | Technology-neutral connection close semantics. |
| `PartyGameTransportException` | **RENAME/GENERALIZE** | Neutral transport exception name. |

The transport abstraction should not depend on a session/game Core package simply to share `ConnectionId`.

## Concrete transports

### In-memory

**KEEP.** Deterministic test/reference transport. Neutralize any public game-specific naming discovered during `[16]`.

### LAN WebSocket

| Current API | Decision | Target |
| --- | --- | --- |
| `LanWebSocketTransport` | **KEEP** | Direct LAN transport. |
| `LanWebSocketClient` | **KEEP** | Direct LAN client. |
| `LanWebSocketHostOptions` | **KEEP** | Transport configuration. |
| `LanJoinDescriptor` | **RENAME/GENERALIZE** | LAN connection descriptor. |

### LAN discovery

| Current API | Decision | Target |
| --- | --- | --- |
| UDP advertiser/listener | **KEEP/GENERALIZE** | Advertise/discover technical endpoints/services. |
| `LanDiscoveryBroadcastAddressResolver` | **KEEP** | Transport/discovery utility. |
| `DiscoveredSessionRegistry` | **RENAME/GENERALIZE** | Discovered endpoint/service registry. |

Discovery must not imply a game session or product lobby.

## TypeScript browser SDK

`@partygamekit/client` is **KEEP/GENERALIZE**.

Keep/generalize:

- connect/disconnect;
- send/receive;
- WebSocket transport;
- connection state;
- heartbeat;
- resume/reconnect;
- neutral peer identity persistence where configured;
- protocol versioning;
- connection descriptor parsing;
- optional generic sequence/dedupe utility.

Move out:

- player/shared-screen/host role APIs;
- player identity as product identity;
- room admission semantics;
- authority/game ownership;
- public/private player projections and required snapshot restoration.

## Dart interoperability package

`partygamekit_protocol` is **KEEP/GENERALIZE** as a thin implementation of the communication wire contract.

Player, host, lobby, game-state and snapshot semantics stay in Państwa Miasta. Dart fixtures will be aligned to the neutral protocol in `[17]`.

## Samples

`SharedCounter` and `DungeonPrototype` are **KEEP AS CONSUMERS**. Their product/game concepts are allowed locally but must not shape the required base API.

Issue `[17]` will add/convert a neutral communication sample proving generic clients can exchange opaque messages, reconnect and use LAN discovery/direct descriptors without players or game sessions.

## Result

The original v0.1 review was wrong to conclude that generic packages should own room/player/authority/session/snapshot semantics merely because they were shared by two game-like samples.

The corrected rule is stricter: a concept belongs in base PartyGameKit only when it is required to communicate independently of product/game meaning.

The implementation checklist for `[16]` is maintained in [Communication boundary](communication-boundary.md).