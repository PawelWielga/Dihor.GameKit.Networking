# Migration from 0.1 preview to 0.2 preview

`0.2.0-preview.1` is intentionally source- and wire-incompatible with `0.1.0-preview.1`.

The change fixes an ownership error in the first preview: Dihor.GameKit.Networking no longer models a generic game/party session. It provides communication primitives; PartyBeam, Państwa Miasta and other consumers own participant roles, product sessions, authority and game state.

## Package and protocol versions

| Line | .NET package | Wire protocol | Meaning |
| --- | --- | ---: | --- |
| historical | `0.1.0-preview.1` | 1 | room/player/session-oriented preview |
| corrected | `0.2.0-preview.1` | 2 | communication-only preview |

Protocol v1 and v2 are not wire compatible. A LAN v2 listener rejects a v1 handshake with `protocol-version-mismatch`.

## Identity migration

### Before

```csharp
var roomId = new RoomId("party-1");
var playerId = new PlayerId("player-1");
var authorityId = new AuthorityId("host-1");
```

### After

```csharp
var connectionId = new ConnectionId("connection-1");
var peerId = new PeerId("peer-1"); // only when stable resume identity is needed
var channelId = new ChannelId("party-routing-scope"); // optional technical routing only
```

Do not translate every old type one-for-one. Product identifiers remain in the consumer. For example, a PartyBeam participant/player id can be mapped to `PeerId` when reconnect requires stable communication identity, but Dihor.GameKit.Networking does not know that the peer is a player.

## Session and roles

Remove dependencies on:

- `RoomSession`;
- `ClientRole`;
- `AuthorityId`;
- player membership/capacity/admission APIs;
- Dihor.GameKit.Networking leave/game-session lifecycle APIs.

Define those concepts in the consuming application where they actually belong.

## Connection descriptors

### Before

```csharp
var descriptor = LanJoinDescriptor.Create(
    roomId,
    joinCode,
    "192.168.1.10",
    45678);
```

### After

```csharp
var descriptor = LanConnectionDescriptor.Create(
    "192.168.1.10",
    45678,
    channelId);

var portable = ConnectionDescriptorCodec.SerializeText(descriptor);
```

A `ConnectionDescriptor` contains only technical connection information: transport, endpoint, protocol version and optional routing channel. Product invitation codes and QR UX wrap this descriptor outside Dihor.GameKit.Networking.

## Transport API

Rename usages:

| v0.1 | v0.2 |
| --- | --- |
| `IGameTransport` | `IMessageTransport` |
| `PartyGameTransportException` | `TransportException` |
| `InMemoryGameTransport` | `InMemoryTransport` |
| `LanJoinDescriptor` | `LanConnectionDescriptor` |
| `DiscoveredSessionRegistry` | `DiscoveredEndpointRegistry` |

Transport events still use transient `ConnectionId` and carry opaque `ReadOnlyMemory<byte>` payloads.

## Protocol messages

Replace v1 session messages with v2 communication control messages:

| v0.1 | v0.2 |
| --- | --- |
| `session.join.request` | `connection.connect.request` |
| join accepted/rejected | connection accepted/rejected |
| `session.rejoin.request` | `connection.resume.request` |
| rejoin accepted/rejected | resume accepted/rejected |
| `session.heartbeat` | `connection.heartbeat` |
| `session.leave` / disconnected | `connection.disconnect` and transport close events |
| `state.snapshot` | consumer-owned `application.message` payload |

Dihor.GameKit.Networking protocol v2 does not define player capacity, room state, authority or game snapshots.

## Reconnect

### Before

Reconnect was coupled to player/session state and snapshot restoration.

### After

```csharp
var continuity = new ConnectionContinuityCoordinator(
    new ConnectionContinuityOptions(
        peerTimeout: TimeSpan.FromSeconds(30),
        reconnectWindow: TimeSpan.FromMinutes(2)));

var registered = continuity.Register(peerId, connectionId);
// persist registered.ResumeToken on the reconnecting client

continuity.MarkDisconnected(connectionId);

var resumed = continuity.Resume(
    peerId,
    registered.ResumeToken!,
    replacementConnectionId);
```

Successful resume only rebinds communication identity. The consumer decides whether to restore a player slot, send current game state or apply any other product policy.

## Ordering and snapshots

Remove base-package usages of:

- `StateSnapshot<T>`;
- `SnapshotTarget` / `SnapshotAudience`;
- public/private projection types;
- `AuthoritativeSnapshotPublisher`.

If consumer messages need monotonic deduplication, use the neutral `MessageSequence` and `SequenceGate` utility or keep sequencing entirely in the consumer protocol.

## Discovery

LAN discovery now returns `DiscoveredEndpoint` objects with technical `ConnectionDescriptor` data. Product session metadata should be advertised by the application separately if required.

Direct connection remains independent from UDP discovery: a valid serialized `ConnectionDescriptor` is sufficient.

## TypeScript browser SDK

The browser API changed at the same boundary.

| v0.1 | v0.2 |
| --- | --- |
| `new PartyGameClient({ role: "player" })` | `new PartyGameClient()` |
| `join(...)` | `connect(...)` |
| `JoinDescriptor` / `dihor-gamekit-networking://join` | `ConnectionDescriptor` / `dihor-gamekit-networking://connect` |
| `stablePlayerId` | neutral peer identity managed by `PeerIdentityStore` |
| join/rejoin messages | connect/resume messages |
| `snapshot` event | `applicationMessage` event |
| public/private snapshot filtering | consumer-owned application payload/schema |

Do not recreate a `role` option in a wrapper around the SDK just to preserve the old Dihor.GameKit.Networking API. If PartyBeam needs TV/controller/player roles, define them in PartyBeam and associate them with its own peer/session model.

## Dart interoperability

The Dart package now validates protocol v2 fixtures and exposes communication-neutral names:

- `Dihor.GameKit.NetworkingConnectionDescriptor` instead of a join/room descriptor;
- stable peer/connection fields instead of player identity;
- connect/resume envelopes;
- opaque application messages;
- generic message sequence gating rather than game-snapshot sequencing.

It remains intentionally protocol-only. Państwa Miasta can keep its player identity, host-authoritative game state and snapshot schema in its own code.

## Samples

`samples/CommunicationDemo` is the v0.2 reference sample. It contains no game/player role model and verifies the real LAN transport, UDP discovery, multiple peers, opaque message exchange, targeted/broadcast delivery and resume.

`SharedCounter` and `DungeonPrototype` are historical/game-oriented consumers. Their player, authority and game-state concepts are application-owned examples, not Dihor.GameKit.Networking abstractions.

## Package-only validation

`packaging/consumer` restores only generated `0.2` NuGet packages. It verifies:

- an opaque `application.message` can travel peer → transport and transport → peer;
- `ConnectionContinuityCoordinator` can resume one stable `PeerId` on a replacement `ConnectionId` without creating a duplicate logical peer;
- no source-project reference or party-game/session abstraction is required.
