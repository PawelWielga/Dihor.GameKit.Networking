# Roadmap

PartyGameKit is developed in the exact ordered backlog tracked by GitHub issue `#2`. Work follows `[NN]` prefixes, not GitHub issue numbers.

## Historical v0.1 foundation: `[01]`–`[14]`

The first implementation cycle successfully proved:

- a versioned C#/Dart/TypeScript wire contract;
- direct LAN WebSocket communication without cloud dependency;
- in-memory transport testing;
- UDP LAN discovery and direct descriptor fallback;
- heartbeat/reconnect mechanics;
- browser and Dart interoperability;
- game-oriented validation samples;
- reproducible prerelease packaging.

It also overreached architecturally by promoting player/session/authority/snapshot semantics into PartyGameKit base APIs.

Those issues remain useful implementation history, but the v0.1 product/session surface is not the architecture going forward.

## Corrected communication foundation: `[15]`–`[17]`

The boundary-correction phase establishes PartyGameKit `0.2` as communication infrastructure only.

### `[15]` Boundary decision

The architecture decision established that:

- PartyGameKit owns communication/networking, not PartyBeam/game runtime semantics;
- `ConnectionId` is transient transport identity;
- optional stable reconnect identity is neutral `PeerId`;
- optional routing isolation is technical `ChannelId`;
- player, role, room-session, authority and game-state semantics belong to consumers;
- connection descriptors and discovery describe technical connectivity;
- application/game payloads remain opaque to PartyGameKit.

Source of truth: [Communication boundary](communication-boundary.md).

### `[16]` Production API correction

The .NET foundation implements that decision:

- neutral `ConnectionId`, `PeerId`, `ChannelId` and `ConnectionDescriptor`;
- `ConnectionContinuityCoordinator` for neutral resume/reconnect;
- `IMessageTransport` and neutral transport lifecycle/events;
- protocol v2 connect/resume/heartbeat/disconnect control messages;
- opaque `application.message` data;
- direct LAN WebSocket transport with neutral handshake;
- UDP endpoint discovery;
- optional generic message ordering/deduplication utilities;
- removed player/host/shared-screen/authority/room-session/snapshot-projection APIs.

### `[17]` Consumer-facing alignment

The consumer-facing surfaces are aligned to the corrected boundary:

- `@partygamekit/client` uses protocol v2 and neutral peer/connection vocabulary;
- Dart validates the same protocol-v2 fixture set;
- C#, Dart and TypeScript consume one canonical `v2-*` contract;
- `samples/CommunicationDemo` verifies real LAN communication with generic peers, discovery, targeted/broadcast delivery and resume;
- package-only NuGet validation exercises opaque messages and neutral resume;
- migration documentation covers the deliberate v0.1 → v0.2 break;
- cross-repository validation confirms PartyBeam and Państwa Miasta keep product/game semantics above PartyGameKit;
- v0.1 game-oriented samples and fixture vectors were retired from the active v0.2 tree and remain available through Git history/tags.

## Corrected foundation invariant

After the boundary correction, PartyGameKit must continue to satisfy all of these:

- direct LAN connect/send/receive/disconnect/resume works without a cloud backend;
- LAN discovery and direct descriptor connection are independent;
- C#, Dart and TypeScript share the language-neutral communication contract;
- application payloads are opaque and consumer-owned;
- a consumer with **no concept of players** can use the library;
- PartyBeam owns TV/controller/player/party/authority/game behavior;
- Państwa Miasta owns its player/game/session/snapshot behavior;
- no base API requires player, host, shared-screen, scoring, lobby or game-session semantics.

Any later issue that violates these points is an architectural regression, not an extension.

## Additional transports

Connectivity choices are added behind the corrected boundary. They must not recreate a generic game-session runtime.

### `[18]` Optional backend-assisted SignalR transport — implemented in `0.2.0-preview.2`

SignalR/backend relay is available as another communication transport.

Implemented rules and validation:

- LAN remains usable with no backend deployed;
- `PartyGameKit.Transport.SignalR` provides listener/client communication over SignalR;
- `PartyGameKit.Transport.SignalR.Server` provides a minimal ASP.NET Core relay endpoint;
- backend routing uses opaque `ChannelId` and transient `ConnectionId` only;
- backend does not own PartyBeam parties, players, lobbies, authority or game state;
- the same protocol-v2 and opaque application payloads work over LAN and SignalR;
- targeted delivery, broadcast, disconnect and routing-scope isolation are integration-tested;
- protocol-v1 handshakes are rejected before an application connection is exposed;
- replacement relay connections resume the same neutral `PeerId` through the existing continuity layer;
- client/server cleanup and payload limits are validated;
- `CommunicationDemo` runs the same neutral scenario over both real transports.

See [SignalR relay](signalr-relay.md).

### `[19]` WebRTC DataChannel transport

Add low-latency peer communication behind the same neutral abstractions.

Rules:

- signaling is infrastructure only;
- PartyGameKit does not assume peers are phones, players, TV or authority;
- validate 30–60 Hz opaque message streams;
- bound buffering/backpressure and expose latency/jitter diagnostics;
- product state/roles remain opaque.

### `[20]` Automatic transport selection and fallback

Add deterministic communication-path selection after LAN, SignalR and WebRTC are independently reliable.

A strategy may prefer LAN, then WebRTC, then backend relay, but the decision is based on connectivity/capabilities, not product roles.

Automatic mode only answers: **which transport should carry messages now?** It must not decide whether a game pauses, whether a participant keeps a slot or who becomes authority.

## Explicitly consumer-owned

Do not move these into PartyGameKit base packages without a new communication-level justification:

- PartyBeam party/lobby lifecycle;
- TV/pilot/controller/player/spectator roles;
- player capacity/admission rules;
- game authority/host policy;
- automatic game-host migration policy;
- game start/pause/end policy;
- public/private/player game-state projections;
- game snapshot ownership/restoration;
- score, phases and commands;
- persistent accounts/matchmaking;
- database/Redis product session persistence;
- UI frameworks/components;
- product QR/join-code UX.

## Rule for future issues

A new abstraction belongs in PartyGameKit only when it answers a communication/networking problem that remains meaningful for an application with no players and no game session.

If the abstraction answers a product/game question, it belongs above PartyGameKit.
