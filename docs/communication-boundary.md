# Communication boundary

This document is the architecture decision for issue `[15]`. It supersedes the original v0.1 assumption that Dihor.GameKit.Networking should own a generic room/player/game-session runtime.

## Decision

Dihor.GameKit.Networking is a **reusable communication/networking library**. It is infrastructure below PartyBeam, Państwa Miasta and future multiplayer products.

```text
PartyBeam / Państwa Miasta / future multiplayer products
                │
                │ product roles, players, parties, game sessions,
                │ authority policy, state and game rules
                ▼
          Dihor.GameKit.Networking
      communication/networking
                │
        transports + discovery
                │
     LAN / SignalR / WebRTC
```

Dihor.GameKit.Networking owns connection mechanics. Consumers own the meaning of connected participants.

A consumer must be able to use Dihor.GameKit.Networking without defining a `Player`, `Host`, `SharedScreen`, party, lobby, score or game state.

## Rules

Dihor.GameKit.Networking may own:

- transient transport connection identity;
- a neutral stable logical client/peer identity required only for resume/reconnect;
- transport-neutral connect/disconnect/send/receive primitives;
- targeted send and broadcast/multicast where transport-independent;
- connection health, heartbeat, timeout and reconnect mechanics;
- versioned protocol envelopes and compatibility rules;
- opaque application payload transport;
- generic ordering/deduplication metadata and helpers when they do not imply game state;
- portable technical connection descriptors;
- LAN discovery;
- LAN WebSocket transport;
- diagnostics, cancellation and cleanup;
- cross-language C#/TypeScript/Dart compatibility for the communication contract.

Dihor.GameKit.Networking must not own:

- `Player` or player membership;
- player capacity/admission policy;
- lobby or game-session lifecycle;
- `Host`, `SharedScreen`, `Controller`, `Spectator` or similar product roles;
- product/game authority policy;
- game start/pause/end decisions;
- score or game state;
- public/shared-screen/private-player projection policy;
- product join UX or globally meaningful party codes;
- concrete game commands, phases or domain models.

## Identity decision

The minimum communication model is:

```text
ConnectionId   transient transport connection
PeerId         optional stable logical communication identity used for resume
```

`PeerId` is neutral. It does not mean player, account, controller, TV, host or spectator. A consumer may map its own participant identity to a `PeerId`, or may use Dihor.GameKit.Networking without stable peer identity when reconnect is not required.

Current `PlayerId` is therefore **RENAME/GENERALIZE** to `PeerId` (or the final equivalent chosen in `[16]`). Current `ConnectionId` is **KEEP**, but it should move to the lowest coherent communication package so transport abstractions do not depend on a game/session Core package.

`AuthorityId` is **MOVE OUT**. Authority is consumer policy.

## Routing scope decision

A neutral routing scope may be retained only if a transport needs it to isolate message delivery. The target name should be communication-oriented, for example `ChannelId` or `ScopeId`.

A routing scope:

- may identify which connected peers receive a message;
- may be advertised by discovery or encoded in a connection descriptor when technically required;
- must not imply a lobby, party or active game;
- must not own capacity, players, roles, authority, lifecycle or score.

Current `RoomId` is therefore **RENAME/GENERALIZE** only if such a routing scope is still required after `[16]`; otherwise it should be removed from the base protocol.

## Connection descriptor decision

The current `JoinDescriptor` mixes technical connection data with product concepts (`RoomId`, `JoinCode`). The capability is valid but the model is not.

Target: a neutral `ConnectionDescriptor` containing only the data required to establish communication, such as:

- transport kind;
- endpoint;
- protocol version;
- optional routing scope;
- optional transport-specific metadata.

A PartyBeam join code, QR presentation, party name or game identifier belongs above Dihor.GameKit.Networking. A product may embed or wrap a Dihor.GameKit.Networking connection descriptor in its own invite payload.

## Reconnect decision

Reconnect/resume remains a Dihor.GameKit.Networking responsibility when implemented as communication continuity:

1. a stable neutral `PeerId` may outlive one network connection;
2. a reconnect credential/token proves that a replacement connection may resume that peer identity;
3. heartbeat loss marks connectivity state only;
4. Dihor.GameKit.Networking does not decide whether a disconnected peer still occupies a player slot or whether a game pauses/ends;
5. successful resume rebinds the new `ConnectionId` to the same `PeerId` and does not create a duplicate logical peer.

Current `SessionContinuityCoordinator<TPublicState,TPrivateState>` mixes valid reconnect mechanics with player roles and snapshot restoration. It must be split in `[16]`.

## Snapshot/ordering decision

Game/session snapshot publication does **not** belong in the base communication library.

Current public/private snapshot projection types, authority-bound snapshot publisher and player-targeted snapshot restoration are **MOVE OUT**.

The small monotonic ordering primitive is useful outside games, so a neutral sequence gate can remain as an **OPTIONAL UTILITY** if it is renamed/generalized and has no room/player/authority semantics.

Target examples:

- generic message sequence metadata;
- `SequenceGate` / deduplication helper;
- no `SnapshotAudience.Public`, `SnapshotTarget.ForPlayer`, `AuthorityId` or player projection model in base packages.

Consumers such as Państwa Miasta may continue to own authoritative snapshots and use Dihor.GameKit.Networking only to carry those opaque payloads.

## Protocol decision

The base wire contract has two layers:

1. **Dihor.GameKit.Networking control protocol** for communication lifecycle and compatibility;
2. **opaque application messages** whose schema and semantics are consumer-owned.

Keep/generalize:

- protocol version;
- envelope type/message id/correlation metadata;
- connection handshake/version validation;
- heartbeat/connectivity messages;
- neutral resume request/accept/reject;
- opaque application payload messages.

Move out/remove from the base protocol:

- `session.join.*` semantics tied to rooms/roles/players;
- player admission and room-full rejection;
- `session.leave` as a product/player lifecycle event;
- `authorityId`;
- `state.snapshot` as a required Dihor.GameKit.Networking message;
- public/private/player projection metadata.

The target vocabulary should use communication terms such as connect, peer, connection, resume, channel/scope and application message.

## Public abstraction classification

| Current public abstraction/package | Decision | Target / reason |
| --- | --- | --- |
| `Dihor.GameKit.Networking.Core` package | **RENAME/GENERALIZE** | Do not preserve the package solely for compatibility. Split/collapse it around communication identities and reconnect utilities, or remove it if responsibilities fit Protocol/Transport packages better. |
| `ConnectionId` | **KEEP** | Pure transport identity. Move to the lowest coherent communication package. |
| `PlayerId` | **RENAME/GENERALIZE** | Replace with neutral `PeerId`/logical client identity used only for reconnect/routing. |
| `RoomId` | **RENAME/GENERALIZE** | Keep only as a neutral `ChannelId`/`ScopeId` if routing isolation requires it. |
| `AuthorityId` | **MOVE OUT** | Game/product authority policy belongs to consumers. |
| `JoinCode` | **MOVE OUT** | Product invitation/admission UX. A transport descriptor may contain only technical data. |
| `ClientRole` (`Host`, `Player`, `SharedScreen`) | **MOVE OUT** | Product topology/roles belong to PartyBeam or a game. |
| `RoomSession`, `RoomSessionState` | **MOVE OUT** | Product/game session lifecycle. |
| player/client membership and lifecycle result/event types | **MOVE OUT** | Player admission, capacity, leave and role semantics are consumer concerns. |
| `JoinDescriptor` | **RENAME/GENERALIZE** | Become technical `ConnectionDescriptor`; remove room/join-code semantics. |
| `SessionContinuityOptions` | **RENAME/GENERALIZE** | Split into neutral connection-health/resume options; remove host timeout semantics. |
| `ConnectionPresence` | **KEEP/GENERALIZE** | Connectivity timestamps are communication concerns; remove role/player coupling. |
| `SessionContinuityCoordinator<...>` | **RENAME/GENERALIZE** | Split into connection health + peer resume coordination; remove player/session/snapshot policy. |
| reconnect token/credential handling | **KEEP** | Communication resume security, expressed against neutral `PeerId`. |
| `PresenceTimeoutKind.HostLost` | **MOVE OUT** | Host meaning is product policy. Generic connection/peer timeout remains. |
| `SnapshotSequence` / `SnapshotSequenceGate` | **OPTIONAL UTILITY** | Generalize to message sequencing/deduplication without snapshot/game semantics. |
| `SnapshotAudience`, `SnapshotTarget` | **MOVE OUT** | Public/player projection policy is consumer-specific. |
| `PublicStateProjection`, `PlayerStateProjection` | **MOVE OUT** | Consumer state model. |
| `StateSnapshot`, `PublishedSnapshotSet`, `AuthoritativeSnapshotPublisher` | **MOVE OUT** | Authority/game-state replication policy belongs above Dihor.GameKit.Networking. |
| `Dihor.GameKit.Networking.Protocol` package | **KEEP/GENERALIZE** | Keep as language-neutral communication protocol; remove application/session semantics. |
| `ProtocolEnvelope<TPayload>` | **KEEP** | Neutral versioned envelope. |
| `ProtocolVersions` | **KEEP** | Wire compatibility. |
| current `ProtocolMessageTypes` | **RENAME/GENERALIZE** | Replace session/player/snapshot message set with neutral connection/control/application message types. |
| join/accepted/rejected payloads | **RENAME/GENERALIZE** | Connection handshake only; no role/player/capacity/authority semantics. |
| leave/disconnected payloads | **RENAME/GENERALIZE** | Transport/connection close semantics only. |
| heartbeat payload | **RENAME/GENERALIZE** | Keep health data; remove room/snapshot coupling. |
| rejoin payloads | **RENAME/GENERALIZE** | Neutral peer resume. |
| state snapshot payload | **MOVE OUT** | Consumer application message. |
| `GameKitNetworkingMessages.Create` | **KEEP/GENERALIZE** | Generic envelope factory; rename if needed to avoid game-specific naming. |
| `JoinDescriptorCodec` | **RENAME/GENERALIZE** | Codec for neutral connection descriptor. |
| `Dihor.GameKit.Networking.Transport.Abstractions` | **KEEP** | Core responsibility, but remove dependency on product/session Core. |
| `IGameTransport` | **RENAME/GENERALIZE** | Rename to neutral `ITransport`/`IMessageTransport`. |
| transport events/errors/close reasons | **KEEP** | Communication concerns. |
| `Dihor.GameKit.Networking.Transport.InMemory` | **KEEP** | Deterministic reference/test transport. Rename game-specific public types if present. |
| `Dihor.GameKit.Networking.Transport.Lan` | **KEEP** | Direct LAN WebSocket implementation. |
| `LanWebSocketTransport`, client and options | **KEEP** | Concrete communication transport. Ensure API accepts neutral descriptors/control messages. |
| `LanJoinDescriptor` | **RENAME/GENERALIZE** | LAN-specific connection descriptor, not product join/session object. |
| `Dihor.GameKit.Networking.Discovery.Lan` | **KEEP/GENERALIZE** | Discovery remains useful, but advertises connection endpoints/scopes rather than game sessions. |
| `DiscoveredSessionRegistry` | **RENAME/GENERALIZE** | Rename to discovered endpoint/service/connection registry. |
| UDP advertiser/listener and broadcast resolver | **KEEP/GENERALIZE** | Keep mechanics; neutralize payload vocabulary. |
| `@dihor/gamekit-networking` package | **KEEP/GENERALIZE** | Base browser SDK becomes connect/send/receive/resume client with opaque payloads. |
| TypeScript player/shared-screen roles, player identity, projections | **MOVE OUT/GENERALIZE** | Replace with neutral peer identity and application messages. |
| TypeScript snapshot gate | **OPTIONAL UTILITY** | Keep only as generic sequence/dedupe utility. |
| `dihor_gamekit_networking_protocol` Dart package | **KEEP/GENERALIZE** | Thin communication protocol/descriptor compatibility layer. |
| Dart player/role/session/snapshot protocol models | **MOVE OUT/GENERALIZE** | Align with neutral protocol; game snapshot behavior stays in Państwa Miasta. |
| SharedCounter and DungeonPrototype samples | **KEEP AS CONSUMERS** | Samples may define Player/Authority/GameState locally but must not define generic API. Add a neutral communication sample in `[17]`. |

## Package direction after refactor

The exact names are finalized in `[16]`, but dependencies should move toward:

```text
communication identities / protocol primitives
                 │
       ┌─────────┴─────────┐
       ▼                   ▼
transport abstractions   protocol/control
       │                   │
       ├── in-memory       ├── connection descriptor
       └── LAN WebSocket   └── resume/versioning
                 │
                 ▼
             discovery
```

No low-level package should depend on a package whose responsibility is player/session/game orchestration.

## Consumer ownership

### PartyBeam owns

- party lifecycle and invitation UX;
- TV/pilot/controller/player concepts;
- participant limits and game requirements;
- authority/coordinator policy;
- game catalog/modules;
- shared/private display policy;
- product join codes and warnings;
- start/interrupt/end game actions.

### Państwa Miasta owns

- `Player` semantics and stable game participant identity mapping;
- host-authoritative `CountriesCitiesGameEngine`;
- lobby/game capacity and leave rules;
- game snapshot schema and restoration policy;
- categories, answers, voting, scoring and phases.

It may map its participant identity to Dihor.GameKit.Networking `PeerId` and transport its snapshots/commands as opaque application messages.

## Migration from `0.1.0-preview.1`

Breaking changes are preferred over compatibility shims that preserve the wrong ownership model.

Expected migration:

- `PlayerId` -> neutral `PeerId` only for communication continuity;
- `RoomId` -> optional neutral routing `ChannelId`/`ScopeId` or consumer-owned ID;
- `JoinDescriptor` -> `ConnectionDescriptor`;
- `IGameTransport` -> neutral transport interface;
- remove `ClientRole`, `AuthorityId`, `RoomSession` and player lifecycle APIs from base packages;
- move snapshot/public/private projection logic to consumers; retain only generic sequence helper if useful;
- replace session join/rejoin protocol messages with connection handshake/resume control messages;
- browser/Dart SDK users handle PartyBeam/game roles and state above the base SDK.

Do not add obsolete adapters that silently keep the old API as the preferred path. If a short-lived compatibility type is necessary to make migration diagnosable, it must be clearly obsolete and removed before stable `1.0`.

## Refactoring checklist for `[16]`

1. Introduce neutral communication identities (`ConnectionId`, optional stable `PeerId`, optional routing scope).
2. Move transport abstractions off `Dihor.GameKit.Networking.Core`; rename `IGameTransport` and other game-specific transport vocabulary.
3. Replace `JoinDescriptor`/`LanJoinDescriptor` with technical connection descriptors.
4. Replace protocol `session.join.*`/`session.rejoin.*` with neutral handshake/resume control messages.
5. Remove `ClientRole`, `AuthorityId`, `RoomSession`, capacity/admission/player lifecycle APIs from base production packages.
6. Split `SessionContinuityCoordinator` into neutral connection-health and resume mechanics; remove host/player/snapshot policy.
7. Remove snapshot projection/public-private APIs from base production packages. Retain only a neutral sequence/deduplication helper if still justified.
8. Neutralize LAN discovery names/payloads so discovery finds connection endpoints/services, not game sessions.
9. Update LAN and in-memory transports to compile against the neutral abstractions without reintroducing session types.
10. Replace canonical fixtures with neutral connection/control/application-message fixtures while preserving explicit protocol version mismatch behavior.
11. Add tests proving generic peers can exchange opaque payloads, reconnect without duplicate logical peer identity and time out without triggering product leave semantics.
12. Add API guard tests/search checks ensuring base production packages do not expose required `player`, `host`, `shared-screen`, authority or game-session vocabulary.
13. Update package version for the breaking prerelease API and add migration notes.
14. Do not start SignalR, WebRTC or auto-fallback until `[16]` and `[17]` are complete.

## Acceptance invariant

After `[16]`, this should be valid application code conceptually:

```text
connect client A
connect client B
send opaque bytes/message A -> B
broadcast opaque message
replace A's network connection and resume the same PeerId
```

No step should require creating a player, room lobby, host, shared screen, authority or game state.