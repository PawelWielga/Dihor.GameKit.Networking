# PartyGameKit

PartyGameKit is a reusable **multiplayer communication/networking library** extracted from networking behavior proven in [Państwa Miasta](https://github.com/PawelWielga/panstwa-miasta).

It is infrastructure below [PartyBeam](https://github.com/PawelWielga/PartyBeam), Państwa Miasta and future multiplayer products. It does not require consumers to adopt a player/host/shared-screen model.

```text
PartyBeam / Państwa Miasta / future multiplayer products
                │
                │ players, roles, parties, game sessions,
                │ authority policy, state and game rules
                ▼
          PartyGameKit
      communication/networking
                │
        transports + discovery
                │
       LAN / SignalR / WebRTC
```

## 0.2 communication boundary

`0.1.0-preview.1` proved the LAN transport, discovery and reconnect approach, but also exposed product concepts such as players, client roles, room sessions, authority and public/private game-state projections.

`0.2.0-preview.1` corrected that boundary. `0.2.0-preview.2` added optional backend-assisted SignalR relay connectivity. `0.2.0-preview.3` added browser-native WebRTC DataChannel communication with optional SignalR signaling. `0.2.0-preview.4` adds deterministic automatic transport selection/fallback without changing protocol v2 or reintroducing product/session semantics.

Protocol v2 and the base APIs use communication-neutral concepts:

- transient `ConnectionId`;
- optional stable `PeerId` for resume/reconnect;
- optional technical `ChannelId` for routing scope;
- `ConnectionDescriptor` for transport/endpoint discovery and direct connection;
- connect/resume/heartbeat/disconnect control messages;
- opaque `application.message` payloads owned by the consumer;
- transport-neutral send/receive, targeted delivery and broadcast;
- deterministic connection continuity and generic ordering helpers;
- direct LAN WebSocket transport and optional UDP LAN discovery;
- optional backend-assisted SignalR relay transport;
- browser-native WebRTC DataChannels for direct low-latency peer communication;
- deterministic automatic client transport selection with bounded fallback and structured diagnostics.

The migration from v0.1 is intentionally breaking. See [Migration 0.1 → 0.2](docs/migration-0.1-to-0.2.md).

## What PartyGameKit does not own

PartyGameKit does **not** decide:

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

- `PartyGameKit.Core` — neutral identity, connection continuity and ordering primitives;
- `PartyGameKit.Protocol` — protocol v2 envelopes, connection descriptors and codecs;
- `PartyGameKit.Transport.Abstractions` — transport-neutral host and client message contracts plus automatic connectivity orchestration;
- `PartyGameKit.Transport.InMemory` — deterministic reference/test transport;
- `PartyGameKit.Transport.Lan` — direct LAN WebSocket transport;
- `PartyGameKit.Transport.SignalR` — optional backend-assisted SignalR relay client/listener transport;
- `PartyGameKit.Transport.SignalR.Server` — ASP.NET Core endpoints for opaque SignalR relay traffic and optional WebRTC SDP/ICE signaling;
- `PartyGameKit.Discovery.Lan` — optional UDP LAN discovery.

Browser consumers use `@partygamekit/client`, which includes protocol-v2 WebSocket connectivity, native browser WebRTC DataChannels and generic automatic transport selection. Flutter/Dart consumers can use the small `interop/dart` protocol package when they need canonical protocol compatibility without a duplicated game/session engine.

## Cross-language contract

Protocol v2 uses canonical fixtures shared by C#, Dart and TypeScript:

```text
protocol/fixtures/v2-*.json
          │
    ┌─────┼─────┐
    ▼     ▼     ▼
   C#    Dart   TypeScript
```

PartyGameKit control messages are distinct from `application.message`; the library carries application data without understanding its game/product meaning.

## LAN, backend relay and WebRTC

Direct LAN WebSocket transport works without Internet or a cloud backend. UDP discovery is optional convenience infrastructure, not a prerequisite for connecting.

A serialized `ConnectionDescriptor` can be passed directly through any product-owned invitation flow, including QR, deep links, manual codes or another backend.

When peers cannot communicate through the direct LAN listener path, `PartyGameKit.Transport.SignalR` can route the same protocol-v2 and application payloads through an optional backend relay. The relay owns only transient connections and opaque `ChannelId` routing scopes.

For latency-sensitive browser-to-browser traffic, `@partygamekit/client` provides native WebRTC DataChannels. SignalR may be used to exchange SDP/ICE negotiation data, but once the DataChannel is established, application payloads travel directly peer-to-peer rather than through the backend.

WebRTC supports explicit `reliable` and `low-latency` profiles plus bounded buffering/backpressure and communication diagnostics. PartyGameKit does not decide whether those peers are TVs, controllers, players or anything else.

See [SignalR relay](docs/signalr-relay.md) and [WebRTC DataChannel](docs/webrtc-datachannel.md). LAN mode remains fully usable without a SignalR server deployed.

## Automatic connectivity

`ConnectivityMode.Auto` chooses among transport candidates registered by the current runtime. The default order is:

1. LAN WebSocket;
2. WebRTC DataChannel;
3. SignalR relay.

Selection is deterministic and bounded. Every candidate is attempted at most once per selection operation with an explicit timeout budget. Failures, timeouts and unavailable runtime candidates are exposed in structured diagnostics instead of being hidden.

Reconnect in `Auto` mode first retries the previously successful transport. If it no longer works, fallback continues through the configured order. Callers can still force LAN, WebRTC or SignalR for tests and product requirements.

Automatic fallback applies to connection establishment/reconnect only. PartyGameKit does not silently interpret application-level failures as a reason to change transport. Connect/resume handshakes, stable `PeerId` continuity and application payload bytes stay outside the selector's interpretation.

See [Automatic connectivity](docs/automatic-connectivity.md) for policy, diagnostics, cancellation behavior and .NET/TypeScript examples.

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
dotnet run --project samples/CommunicationDemo/PartyGameKit.Sample.CommunicationDemo.csproj
```

Run a single path with `-- lan` or `-- signalr`.

`samples/AutoConnectivityDemo` validates the high-level client path. Scenario code requests `ConnectivityMode.Auto`, receives only `IMessageTransportClient`, verifies stable peer identity and exchanges the same opaque application bytes without depending on a concrete client class:

```bash
dotnet run --project samples/AutoConnectivityDemo/PartyGameKit.Sample.AutoConnectivityDemo.csproj
```

Real WebRTC validation runs separately in Chromium from `clients/typescript/test/browser-webrtc.mjs`. It establishes direct reliable and low-latency DataChannels and sends an approximately 60 Hz opaque stream while verifying that application traffic does not continue through signaling.

The former `SharedCounter` and `DungeonPrototype` samples belonged to the historical v0.1 session-oriented API. They were retired from the active v0.2 tree during the boundary correction and remain available through Git history and the v0.1 tag.

## TypeScript

Protocol-v2 WebSocket path:

```ts
import {
  PartyGameClient,
  parseConnectionDescriptor,
} from "@partygamekit/client";

const client = new PartyGameClient();
await client.connect(parseConnectionDescriptor(connectionPayload));
client.send("my-product.command", { value: 42 });
```

WebRTC path:

```ts
import {
  SignalRWebRtcSignalingClient,
  WebRtcPeer,
} from "@partygamekit/client";

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

The browser SDK can persist neutral peer identity for protocol-v2 reconnect, but it does not assign a product role to that peer. WebRTC signaling identifiers are transient transport infrastructure and are not `PeerId` values.

## Dependency policy

External dependencies must permit free commercial use. PartyGameKit does not adopt dependencies that require a paid commercial license, runtime royalty, subscription or per-seat fee. Standard permissive open-source licenses such as MIT, Apache-2.0 and BSD are preferred.

The WebRTC implementation intentionally uses native browser APIs instead of adding a native WebRTC runtime with unsuitable licensing or maintenance characteristics. `@microsoft/signalr` is used for optional browser signaling; Playwright is dev-only real-browser test tooling.

## Developer setup

Requirements:

- .NET 10 SDK;
- Node.js 22 for the TypeScript SDK;
- Dart stable for Dart conformance tests;
- Chromium installed by Playwright for the WebRTC integration gate.

Build/test the repository and run the neutral demos:

```bash
dotnet restore PartyGameKit.slnx
dotnet build PartyGameKit.slnx --configuration Release --no-restore
dotnet test PartyGameKit.slnx --configuration Release --no-build
dotnet run --project samples/CommunicationDemo/PartyGameKit.Sample.CommunicationDemo.csproj --configuration Release --no-build
dotnet run --project samples/AutoConnectivityDemo/PartyGameKit.Sample.AutoConnectivityDemo.csproj --configuration Release --no-build
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

CI also packs all .NET packages and runs `packaging/consumer` from those generated NuGet artifacts only. The package-only consumer verifies opaque message exchange, neutral peer resume and availability of the SignalR client/server packages without source-project references.

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
- [LAN discovery](docs/discovery.md)
- [Compatibility matrix](docs/compatibility.md)
- [Versioning](docs/versioning.md)
- [Extraction from Państwa Miasta](docs/extraction-from-panstwa-miasta.md)
- [Roadmap](docs/roadmap.md)
- [Changelog](CHANGELOG.md)

## Design invariant

Future transports may change **how** messages move. They must not change **what a player, host, TV, party or game means**, because those concepts belong to the consumer, not PartyGameKit.
