# Dihor.GameKit.Networking

Dihor.GameKit.Networking is a reusable **multiplayer communication/networking library** extracted from networking behavior proven in [Państwa Miasta](https://github.com/PawelWielga/panstwa-miasta).

It is infrastructure below [PartyBeam](https://github.com/PawelWielga/PartyBeam), Państwa Miasta and future multiplayer products. It does not require consumers to adopt a player/host/shared-screen model.

```text
PartyBeam / Państwa Miasta / future multiplayer products
                │
                │ players, roles, parties, game sessions,
                │ authority policy, state and game rules
                ▼
          Dihor.GameKit.Networking
      communication/networking
                │
        transports + discovery
                │
       LAN / SignalR / WebRTC
```

## 0.2 communication boundary

`0.1.0-preview.1` proved the LAN transport, discovery and reconnect approach, but also exposed product concepts such as players, client roles, room sessions, authority and public/private game-state projections.

`0.2.0-preview.1` corrected that boundary. `0.2.0-preview.2` added optional backend-assisted SignalR relay connectivity. `0.2.0-preview.3` added browser-native WebRTC DataChannel communication with optional SignalR signaling. `0.2.0-preview.4` added deterministic automatic transport selection/fallback. `0.2.0-preview.5` adds synchronized monotonic timing, RTT/jitter and uncertainty metrics without changing protocol v2 or reintroducing product/session semantics.

Repository/package/API naming changed to `Dihor.GameKit.Networking`, but protocol-v2 wire identifiers remain stable for compatibility. Existing `partygamekit://connect`, `/partygamekit`, `/partygamekit-relay`, `/partygamekit-webrtc-signaling` and `PartyGameKit.WebRtc*` signaling method names are intentionally preserved until an explicit protocol-versioned migration.

Protocol v2 and the base APIs use communication-neutral concepts:

- transient `ConnectionId`;
- optional stable `PeerId` for resume/reconnect;
- optional technical `ChannelId` for routing scope;
- `ConnectionDescriptor` for transport/endpoint discovery and direct connection;
- connect/resume/heartbeat/disconnect control messages;
- opaque `application.message` payloads owned by the consumer;
- optional reconnect-safe latest-value replay for transient application messages, separate from heartbeat;
- transport-neutral send/receive, targeted delivery and broadcast;
- deterministic connection continuity and generic ordering helpers;
- direct LAN WebSocket transport and optional UDP LAN discovery;
- optional backend-assisted SignalR relay transport;
- browser-native WebRTC DataChannels for direct low-latency peer communication;
- deterministic automatic client transport selection with bounded fallback and structured diagnostics;
- transport-neutral monotonic clock synchronization with bounded RTT/jitter/uncertainty estimation and timestamp normalization.

The migration from v0.1 is intentionally breaking. See [Migration 0.1 → 0.2](docs/migration-0.1-to-0.2.md).

## What Dihor.GameKit.Networking does not own

Dihor.GameKit.Networking does **not** decide:

- who is a player;
- host, TV, controller, spectator or other product roles;
- player capacity/admission;
- lobby, party or game-session lifecycle;
- authority/coordinator policy;
- game start/pause/end behavior;
- score or game state;
- public/private/shared-screen projections;
- PartyBeam UX or game catalog behavior;
- game commands, phases or rules.

A consumer with no concept of players can use the library successfully.

## Packages

The .NET prerelease is split by communication responsibility:

- `Dihor.GameKit.Networking.Core` — neutral identity, connection continuity, ordering and monotonic timing primitives;
- `Dihor.GameKit.Networking.Protocol` — protocol v2 envelopes, connection descriptors and codecs;
- `Dihor.GameKit.Networking.Transport.Abstractions` — transport-neutral host and client message contracts plus automatic connectivity orchestration;
- `Dihor.GameKit.Networking.Transport.InMemory` — deterministic reference/test transport;
- `Dihor.GameKit.Networking.Transport.Lan` — direct LAN WebSocket transport;
- `Dihor.GameKit.Networking.Transport.SignalR` — optional backend-assisted SignalR relay client/listener transport;
- `Dihor.GameKit.Networking.Transport.SignalR.Server` — ASP.NET Core endpoints for opaque SignalR relay traffic and optional WebRTC SDP/ICE signaling;
- `Dihor.GameKit.Networking.Discovery.Lan` — optional UDP LAN discovery.

Browser consumers use `@dihor/gamekit-networking`, which includes protocol-v2 WebSocket connectivity, native browser WebRTC DataChannels, generic automatic transport selection and synchronized monotonic timing helpers. Flutter/Dart consumers can use the small `interop/dart` protocol package when they need canonical protocol compatibility without a duplicated game/session engine.

## Cross-language contract

Protocol v2 uses canonical fixtures shared by C#, Dart and TypeScript:

```text
protocol/fixtures/v2-*.json
          │
    ┌─────┼─────┐
    ▼     ▼     ▼
   C#    Dart   TypeScript
```

Dihor.GameKit.Networking control messages are distinct from `application.message`; the library carries application data without understanding its game/product meaning.

## LAN, backend relay and WebRTC

Direct LAN WebSocket transport works without Internet or a cloud backend. UDP discovery is optional convenience infrastructure, not a prerequisite for connecting.

A serialized `ConnectionDescriptor` can be passed directly through any product-owned invitation flow, including QR, deep links, manual codes or another backend.

When peers cannot communicate through the direct LAN listener path, `Dihor.GameKit.Networking.Transport.SignalR` can route the same protocol-v2 and application payloads through an optional backend relay. The relay owns only transient connections and opaque `ChannelId` routing scopes.

For latency-sensitive browser-to-browser traffic, `@dihor/gamekit-networking` provides native WebRTC DataChannels. SignalR may be used to exchange SDP/ICE negotiation data, but once the DataChannel is established, application payloads travel directly peer-to-peer rather than through the backend.

WebRTC supports explicit `reliable` and `low-latency` profiles plus bounded buffering/backpressure and communication diagnostics. Dihor.GameKit.Networking does not decide whether those peers are TVs, controllers, players or anything else.

See [SignalR relay](docs/signalr-relay.md) and [WebRTC DataChannel](docs/webrtc-datachannel.md). LAN mode remains fully usable without a SignalR server deployed.

## Automatic connectivity

`ConnectivityMode.Auto` chooses among transport candidates registered by the current runtime. The default order is:

1. LAN WebSocket;
2. WebRTC DataChannel;
3. SignalR relay.

Selection is deterministic and bounded. Every candidate is attempted at most once per selection operation with an explicit timeout budget. Failures, timeouts and unavailable runtime candidates are exposed in structured diagnostics instead of being hidden.

Reconnect in `Auto` mode first retries the previously successful transport. If it no longer works, fallback continues through the configured order. Callers can still force LAN, WebRTC or SignalR for tests and product requirements.

Automatic fallback applies to connection establishment/reconnect only. Dihor.GameKit.Networking does not silently interpret application-level failures as a reason to change transport. Connect/resume handshakes, stable `PeerId` continuity and application payload bytes stay outside the selector's interpretation.

See [Automatic connectivity](docs/automatic-connectivity.md) for policy, diagnostics, cancellation behavior and .NET/TypeScript examples.

## Transient latest-value replay

`LatestValueReplayBuffer` provides optional reconnect-safe delivery for transient application values whose older revisions become obsolete. Each caller-owned key stores only the newest staged value, and a caller-owned scope/epoch prevents stale data from replaying into a new logical context.

Replay stays on the ordinary application-data path. `connection.heartbeat` remains liveness-only, and changing transport does not move replay state into LAN, WebRTC or SignalR implementations. The newest staged value remains retained even after a locally successful send, because that is not a receiver ACK; `ClearLatest` / scope invalidation explicitly retire replay state.

Latest-value replay is not exactly-once delivery. A new logical value gets a new protocol `messageId`; replay of that same staged value reuses the same serialized message so receiver-side bounded deduplication can recognize duplicates.

`MessageIdDeduplicator` provides that receiver-side protection with a bounded `(PeerId, messageId)` window that survives replacement `ConnectionId` values and transport fallback. Capacity and retention are configurable, and `ForgetPeer` releases state when stable peer continuity ends.

See [Transient latest-value replay](docs/transient-replay.md) and [Message-id deduplication](docs/message-id-deduplication.md).

## Synchronized monotonic timing

`MonotonicTimingSynchronizer` lets a reference side estimate peer-to-reference monotonic clock offset from bounded probe/reply samples. The model exposes RTT, jitter and uncertainty and can normalize a peer-local event timestamp into the reference clock domain.

The synchronizer rejects unknown probes, invalid timing evidence, stale models, non-monotonic event timestamps, excessively old events and implausibly future events. A reconnect or transport replacement must reset the model and reacquire samples rather than trusting an old path estimate.

The timing primitive does not decide reaction order, winners, scoring or what uncertainty is acceptable for a game. Those remain consumer policy.

See [Synchronized monotonic timing](docs/monotonic-timing.md) for formulas, guarantees, reset behavior and .NET/TypeScript examples.

## Neutral reference validation

`samples/CommunicationDemo` exercises the same communication-only scenario over the two .NET listener transports:

- direct Kestrel/WebSocket LAN transport;
- UDP discovery plus direct descriptor connection for LAN;
- backend-assisted SignalR relay transport;
- two generic peers;
- opaque peer-to-host application messages;
- targeted and broadcast delivery;
- disconnect and resume of the same `PeerId` on a replacement `ConnectionId`.

Run both paths from the repository root:

```bash
dotnet run --project samples/CommunicationDemo/Dihor.GameKit.Networking.Sample.CommunicationDemo.csproj
```

Run a single path with `-- lan` or `-- signalr`.

`samples/AutoConnectivityDemo` validates the high-level client path. Scenario code requests `ConnectivityMode.Auto`, receives only `IMessageTransportClient`, verifies stable peer identity and exchanges the same opaque application bytes without depending on a concrete client class:

```bash
dotnet run --project samples/AutoConnectivityDemo/Dihor.GameKit.Networking.Sample.AutoConnectivityDemo.csproj
```

Real WebRTC validation runs separately in Chromium from `clients/typescript/test/browser-webrtc.mjs`. It establishes direct reliable and low-latency DataChannels and sends an approximately 60 Hz opaque stream while verifying that application traffic does not continue through signaling.

The former `SharedCounter` and `DungeonPrototype` samples belonged to the historical v0.1 session-oriented API. They were retired from the active v0.2 tree during the boundary correction and remain available through Git history and the v0.1 tag.

## TypeScript

Protocol-v2 WebSocket path:

```ts
import {
  PartyGameClient,
  parseConnectionDescriptor,
} from "@dihor/gamekit-networking";

const client = new PartyGameClient();
await client.connect(parseConnectionDescriptor(connectionPayload));
client.send("my-product.command", { value: 42 });
```

WebRTC path:

```ts
import {
  SignalRWebRtcSignalingClient,
  WebRtcPeer,
} from "@dihor/gamekit-networking";

const signaling = new SignalRWebRtcSignalingClient({
  endpoint: "https://example.test/partygamekit-webrtc-signaling",
  channelId: "scope-a",
});

const registration = await signaling.connect();
const target = registration.existingConnectionIds[0];
if (target !== undefined) {
  const peer = new WebRtcPeer(signaling.createChannel(target), {
    initiator: true,
    profile: "low-latency",
  });
  await peer.connect();
  peer.send(new Uint8Array([1, 2, 3]));
}
```

Automatic selection is exposed through generic `AutomaticTransportSelector<TContext, TConnection>` and `connectivityTransportIds`. Runtime adapters remain explicit composition-root concerns, while the selection policy stays free of product roles and session rules.

Timing support is exposed through `MonotonicTimingSynchronizer` and `monotonicNowMs()`. The TypeScript timing estimator follows the same bounded sample/filter/reset behavior as the .NET Core implementation.

The browser SDK can persist neutral peer identity for protocol-v2 reconnect, but it does not assign a product role to that peer. WebRTC signaling identifiers are transient transport infrastructure and are not `PeerId` values.

## Dependency policy

External dependencies must permit free commercial use. Dihor.GameKit.Networking does not adopt dependencies that require a paid commercial license, runtime royalty, subscription or per-seat fee. Standard permissive open-source licenses such as MIT, Apache-2.0 and BSD are preferred.

The WebRTC implementation intentionally uses native browser APIs instead of adding a native WebRTC runtime with unsuitable licensing or maintenance characteristics. `@microsoft/signalr` is used for optional browser signaling; Playwright is dev-only real-browser test tooling. Monotonic timing adds no external runtime dependency.

## Developer setup

Requirements:

- .NET 10 SDK;
- Node.js 22 for the TypeScript SDK;
- Dart stable for Dart conformance tests;
- Chromium installed by Playwright for the WebRTC integration gate.

Build/test the repository and run the neutral demos:

```bash
dotnet restore Dihor.GameKit.Networking.slnx
dotnet build Dihor.GameKit.Networking.slnx --configuration Release --no-restore
dotnet test Dihor.GameKit.Networking.slnx --configuration Release --no-build
dotnet run --project samples/CommunicationDemo/Dihor.GameKit.Networking.Sample.CommunicationDemo.csproj --configuration Release --no-build
dotnet run --project samples/AutoConnectivityDemo/Dihor.GameKit.Networking.Sample.AutoConnectivityDemo.csproj --configuration Release --no-build
```

TypeScript and real browser WebRTC:

```bash
cd clients/typescript
npm install --no-audit --no-fund
npm test
npx playwright install --with-deps chromium
npm run test:browser
```

Dart:

```bash
cd interop/dart
dart pub get
dart format --output=none --set-exit-if-changed .
dart analyze
dart test
```

CI also packs all .NET packages and runs `packaging/consumer` from those generated NuGet artifacts only. The package-only consumer verifies opaque message exchange, neutral peer resume, monotonic timing normalization and availability of the SignalR client/server packages without source-project references.

## Documentation

- [Communication boundary](docs/communication-boundary.md)
- [Architecture](docs/architecture.md)
- [Migration 0.1 → 0.2](docs/migration-0.1-to-0.2.md)
- [Cross-repository validation](docs/cross-repo-validation.md)
- [Package boundaries](docs/packages.md)
- [Public API review](docs/public-api.md)
- [Protocol](docs/protocol.md)
- [Networking](docs/networking.md)
- [LAN WebSocket](docs/lan-websocket.md)
- [SignalR relay](docs/signalr-relay.md)
- [WebRTC DataChannel](docs/webrtc-datachannel.md)
- [Automatic connectivity](docs/automatic-connectivity.md)
- [Synchronized monotonic timing](docs/monotonic-timing.md)
- [Transient latest-value replay](docs/transient-replay.md)
- [LAN discovery](docs/discovery.md)
- [Compatibility matrix](docs/compatibility.md)
- [Versioning](docs/versioning.md)
- [Extraction from Państwa Miasta](docs/extraction-from-panstwa-miasta.md)
- [Roadmap](docs/roadmap.md)
- [Changelog](CHANGELOG.md)

## Design invariant

Future transports may change **how** messages move. They must not change **what a player, host, TV, party or game means**, because those concepts belong to the consumer, not Dihor.GameKit.Networking.
