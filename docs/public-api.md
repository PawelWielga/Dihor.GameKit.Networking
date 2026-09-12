# Public API review for v0.1 preview

This review records the supported generic surface after validation against Państwa Miasta, Shared Counter and Dungeon Prototype. The API remains prerelease, but every public concept below has a concrete cross-game use.

## Core

- identity values: `RoomId`, `PlayerId`, `ConnectionId`, `AuthorityId`, `JoinCode`;
- client roles: host, player and shared screen;
- `JoinDescriptor` as transport/join metadata rather than game state;
- `RoomSession` and lifecycle result/event types;
- `AuthoritativeSnapshotPublisher<TPublicState,TPrivateState>`, snapshot target/sequence/projection types and `SnapshotSequenceGate`;
- `SessionContinuityCoordinator<TPublicState,TPrivateState>`, configurable presence/reconnect options and resume results.

These types contain no transport implementation and no game-domain rules.

## Protocol

- `ProtocolEnvelope<TPayload>`, protocol version/message type constants and infrastructure payloads;
- JSON serialization/validation through `ProtocolJson`;
- canonical join/discovery codecs through `JoinDescriptorCodec` and `DiscoveryAnnouncementCodec`.

Game commands remain opaque. The protocol package does not define category, answer, counter, movement, attack, inventory or scoring messages.

## Transport abstractions

- `IGameTransport`;
- transport connection/message/fault events;
- technology-neutral close reasons and errors.

The abstraction intentionally exposes complete message payloads and connection identities only. It does not expose WebSocket, HTTP, SignalR or WebRTC types.

## Concrete transports/discovery

- `InMemoryGameTransport` and its peer helper for deterministic testing;
- `LanWebSocketTransport`, `LanWebSocketHostOptions` and `LanWebSocketClient` for direct LAN;
- UDP LAN advertiser/listener/registry types under `PartyGameKit.Discovery.Lan`.

These live in separate packages and do not expand Core.

## Browser and Dart APIs

`@partygamekit/client` exports the browser client, protocol models/codecs, join-descriptor helpers, identity stores and snapshot gate. It deliberately has no React or PartyBeam component API.

`partygamekit_protocol` exports only the Dart protocol/join-descriptor compatibility layer. A Dart transport and Flutter UI are not claimed by v0.1.

## Review result

No public API from the two validation games needed to move into generic packages. In particular, there are no public generic concepts named or equivalent to `Category`, `Answer`, `Counter`, `Monster`, `Loot`, `Tile`, `Attack`, `Inventory`, `Character` or `ActionPoint`.

The package split in `docs/packages.md` is therefore the v0.1 preview boundary. Later transport issues may add packages, but should not change game/session rules merely because a new networking technology is introduced.
