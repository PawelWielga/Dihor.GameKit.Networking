# Changelog

All notable Dihor.GameKit.Networking changes are documented here. Package versions follow the policy in `docs/versioning.md`; the wire protocol has its own independent version.

## 0.2.0-preview.7 - 2026-09-20

Reusable connection lifecycle and reconnect-safe transient delivery on protocol v2.

### .NET runtime

- adds `Dihor.GameKit.Networking.Runtime` with transport-neutral host and client orchestration;
- owns connect/resume negotiation, rotated resume credentials, heartbeat, timeout, reconnect and neutral application-message dispatch above `IMessageTransport` / `IMessageTransportClient`;
- composes client reconnect with `AutomaticTransportSelector` without adding product roles or session policy;
- adds `MonotonicTimingScheduler` for per-peer immediate acquisition, periodic refresh and reconnect reset;
- validates the new runtime package through package-only .NET and Android client/host consumers.

### Reconnect delivery and cross-platform runtime

- adds bounded message-id deduplication keyed by stable peer identity;
- adds reconnect-safe latest-value replay for transient application messages whose older revisions become obsolete;
- adds the first-class Dart LAN WebSocket runtime with protocol-v2 connect and opaque application-message exchange;
- keeps wire protocol `2` unchanged and leaves players, parties, game sessions, authority and application payload meaning to consumers.

## 0.2.0-preview.5 - 2026-09-15

Transport-neutral synchronized monotonic timing on the existing protocol-v2 communication foundation.

### Timing model

- adds `MonotonicClock.TimestampMilliseconds` backed by `Stopwatch.GetTimestamp()` for .NET monotonic measurements;
- adds `MonotonicTimingSynchronizer` with bounded outstanding probes and bounded rolling sample history;
- estimates peer-minus-reference clock offset with four-timestamp NTP-style sampling;
- reports representative RTT, RTT jitter, offset spread and a conservative uncertainty metric;
- normalizes peer-local event timestamps into the reference monotonic clock domain without treating raw client timestamps as authoritative;
- uses the best half of bounded RTT samples and median-based filtering to reduce sensitivity to slower path outliers.

### Evidence validation and reconnect

- rejects unknown probes, invalid/non-finite timestamps, negative peer-processing intervals and excessive RTT samples;
- rejects timestamp evidence when synchronization is absent/stale, peer evidence is non-monotonic, or normalized events are too old/implausibly future;
- adds explicit `Reset(TimingResetReason)` with reconnect/transport-change reasons so a changed path must reacquire timing state;
- exposes generation, sample/probe counts, accepted/rejected counters, last rejection/reset and current model through structured diagnostics;
- fixes pending-probe ordering bookkeeping so internal tracking remains bounded even after many completed probes.

### TypeScript and validation

- adds equivalent browser `MonotonicTimingSynchronizer` and `monotonicNowMs()` based on `performance.now()`;
- adds deterministic .NET and TypeScript tests for known offsets, RTT/jitter variation, uncertainty, timestamp validation, reconnect invalidation, bounded storage and normalized ordering that differs from packet-arrival order;
- package-only NuGet validation exercises timing offset estimation and timestamp normalization through `Dihor.GameKit.Networking.Core`;
- existing LAN, SignalR, Auto, Dart and real Chromium WebRTC gates remain green;
- adds no external runtime dependency and keeps wire protocol `2` unchanged.

### Boundary

- Dihor.GameKit.Networking reports timing facts and uncertainty only; consumers decide whether timing quality is sufficient and what normalized event order means for a game/product;
- no player, controller, TV, winner, scoring or PartySession semantics are introduced.

## 0.2.0-preview.4 - 2026-09-13

Deterministic communication-path orchestration on the existing protocol-v2 and communication-only foundation.

### Automatic connectivity

- adds `ConnectivityMode.Auto` plus forced LAN, WebRTC and SignalR modes;
- adds a transport-neutral `IMessageTransportClient` contract for client-side send/receive/disposal without changing the host-side `IMessageTransport` contract;
- makes the existing .NET LAN WebSocket and SignalR relay clients implement that neutral client contract while retaining their concrete APIs;
- adds `AutomaticTransportSelector` with default `LAN -> WebRTC -> SignalR` candidate order;
- each candidate is considered at most once per selection operation and has an explicit bounded attempt budget;
- missing runtime candidates are reported as unavailable instead of being silently ignored;
- reconnect first retries the previously successful transport before deterministic fallback through the configured order;
- caller cancellation stops fallback immediately and abandoned late connections are disposed instead of leaking;
- connect/resume handshakes and application payloads remain opaque to the selector.

### Diagnostics and browser SDK

- adds structured diagnostics for every candidate considered, outcome, duration, error, selected transport and reconnect preference reuse;
- terminal no-path failures expose complete diagnostics through `ConnectivitySelectionException` in .NET and `ConnectivitySelectionError` in TypeScript;
- adds a generic TypeScript `AutomaticTransportSelector<TContext, TConnection>` with the same bounded fallback/reconnect policy so browser-native WebRTC can participate without introducing a second product/session model;
- transport changes do not alter stable protocol-v2 `PeerId` continuity or consumer-owned payload bytes.

### Validation

- adds .NET tests for LAN success, LAN -> WebRTC fallback, direct-path -> SignalR fallback, all-path failure, timeout, cancellation, late-success cleanup, reconnect preference, previous-path failure, forced transport modes and opaque identity/payload continuity;
- adds equivalent TypeScript selector coverage including abort cleanup for candidates that ignore `AbortSignal`;
- adds `samples/AutoConnectivityDemo`, where scenario code requests `Auto`, receives only `IMessageTransportClient`, preserves a stable `PeerId` and verifies byte-for-byte opaque payload delivery;
- CI runs the automatic connectivity sample alongside the existing LAN/SignalR communication sample and real Chromium WebRTC gate.

### Boundary and release

- automatic selection answers only which registered communication path should carry messages; it does not define players, hosts, authority, game sessions, state migration or PartyBeam recovery behavior;
- application-level failures do not silently trigger transport fallback;
- bumps .NET, TypeScript and Dart package surfaces to `0.2.0-preview.4` while keeping wire protocol `2`.

## 0.2.0-preview.3 - 2026-09-13

Compatible real-time transport expansion on the existing communication-only protocol-v2 foundation.

### Browser WebRTC DataChannel

- adds browser-native `WebRtcPeer` based on `RTCPeerConnection` / `RTCDataChannel` without a third-party WebRTC runtime;
- adds explicit `reliable` ordered and `low-latency` unordered/no-retransmit DataChannel profiles;
- keeps application payloads opaque and peer-to-peer after negotiation;
- bounds pending ICE candidates and DataChannel buffering instead of maintaining unbounded Dihor.GameKit.Networking send queues;
- reliable mode reports backpressure while low-latency mode can drop newest stale-prone payloads;
- adds RTT and RTT-variation diagnostics plus dropped-message counters.

### Signaling

- adds a neutral browser signaling abstraction separated from application payload transport;
- adds optional SignalR browser signaling through `@microsoft/signalr`;
- adds `AddDihorGameKitNetworkingWebRtcSignaling(...)` and `MapDihorGameKitNetworkingWebRtcSignaling(...)` to the ASP.NET Core server package;
- SignalR routes only SDP/ICE negotiation data between transient connections sharing the same technical `ChannelId`;
- cross-channel signaling is rejected and signaling payloads are bounded;
- stable `PeerId`, players, PartyBeam parties, product roles and game state remain outside the signaling backend.

### Validation

- adds in-process Kestrel/SignalR signaling tests for peer discovery, target routing, leave/disconnect cleanup, channel isolation and signal-size limits;
- adds browser SDK unit tests for reliability profiles, bounded buffering and signaling behavior;
- adds a real headless Chromium gate that establishes direct DataChannels, verifies bidirectional binary delivery and exercises an approximately 60 Hz opaque stream;
- the browser test verifies application traffic does not increase the signaling message count after negotiation;
- LAN WebSocket and SignalR relay validation remain unchanged and continue to run in the same CI pipeline.

### Dependency policy

- rejects SIPSorcery as a Dihor.GameKit.Networking dependency because its current non-standard license does not match the project's free-commercial-use dependency rule;
- does not adopt archived MixedReality-WebRTC or the WebRTCme desktop path;
- uses browser-native WebRTC, MIT-licensed `@microsoft/signalr`, and Apache-2.0 Playwright as dev-only real-browser test tooling.

### Packaging and release

- bumps .NET, TypeScript and Dart package surfaces to `0.2.0-preview.3` while keeping wire protocol `2`;
- CI and prerelease workflows both run the real Chromium WebRTC validation.

## 0.2.0-preview.2 - 2026-09-13

Compatible transport expansion on the corrected communication-only protocol-v2 foundation.

### SignalR relay transport

- adds `Dihor.GameKit.Networking.Transport.SignalR` with `SignalRRelayTransport : IMessageTransport` and `SignalRRelayClient`;
- adds `Dihor.GameKit.Networking.Transport.SignalR.Server` with minimal ASP.NET Core registration/mapping extensions;
- keeps the SignalR hub and relay registry internal implementation details;
- routes opaque bytes using technical `ChannelId` scopes and transient `ConnectionId` values only;
- preserves the LAN lifecycle rule that a connect/resume handshake is validated before `TransportConnectionOpened` is exposed;
- supports targeted listener-to-client delivery, broadcast, client-to-listener delivery, disconnect and cleanup;
- keeps stable `PeerId` continuity and resume tokens above the relay backend;
- rejects protocol-v1 handshakes before exposing a connection to the application;
- accounts for SignalR JSON/base64 framing while retaining the configured raw Dihor.GameKit.Networking payload limit;
- closes the physical SignalR connection when a client-side payload limit is violated so backend routing bindings are removed promptly.

### Validation

- adds in-process Kestrel/SignalR integration coverage for multiple clients, channel isolation, targeted/broadcast delivery and disconnect;
- verifies protocol mismatch behavior over SignalR;
- verifies a replacement SignalR connection resumes the same neutral peer without duplication;
- verifies local payload-limit rejection cleans up the relay binding;
- extends `samples/CommunicationDemo` so the same protocol-v2/application-message scenario runs over both LAN and SignalR;
- package-only validation includes the SignalR client and server NuGet packages;
- LAN remains fully usable without a backend.

### Packaging and release

- bumps the supported .NET, TypeScript and Dart package surfaces to `0.2.0-preview.2` while keeping wire protocol `2`;
- CI derives artifact names from central package metadata rather than hardcoding a prerelease suffix;
- prerelease packaging can substitute any `v0.2.0-preview.N` version into the package-only consumer.

### Documentation

- adds SignalR relay architecture, deployment/configuration and security assumptions;
- updates package boundaries, networking, compatibility, sample and README documentation without introducing PartyBeam/player/game-session semantics.

## 0.2.0-preview.1 - 2026-09-13

Breaking correction of the public architecture boundary. Dihor.GameKit.Networking is now a communication/networking library rather than a generic party/game-session runtime.

### Breaking changes

- wire protocol incremented from `1` to `2`;
- `PlayerId` was replaced by neutral `PeerId` for optional communication continuity;
- `RoomId`/`JoinCode`/`JoinDescriptor` were removed from the base connection model and replaced by optional `ChannelId` plus `ConnectionDescriptor`;
- `ClientRole`, `AuthorityId`, `RoomSession`, player membership/capacity/lifecycle and game snapshot projection APIs were removed from production packages;
- `SessionContinuityCoordinator` was replaced by neutral `ConnectionContinuityCoordinator`;
- `SnapshotSequence`/`SnapshotSequenceGate` became generic `MessageSequence`/`SequenceGate` utilities;
- `IGameTransport` became `IMessageTransport`;
- `PartyGameTransportException` became `TransportException`;
- `InMemoryGameTransport` became `InMemoryTransport`;
- `LanJoinDescriptor` became `LanConnectionDescriptor`;
- `DiscoveredSessionRegistry` became `DiscoveredEndpointRegistry`.

### Protocol v2

- connection lifecycle messages use `connection.connect.*`, `connection.resume.*`, `connection.heartbeat` and `connection.disconnect`;
- consumer data is carried as `application.message` with opaque consumer-owned data;
- LAN WebSocket handshake accepts connect/resume control messages only;
- LAN discovery advertises technical `ConnectionDescriptor` data rather than product/game sessions;
- protocol version mismatch remains deterministic;
- protocol-v1 canonical fixtures were removed after C#, Dart and TypeScript moved to the v2 fixture set.

### TypeScript and Dart

- `@dihor/gamekit-networking` moved to neutral connect/resume APIs with optional stable peer identity;
- browser role requirements (`player`, `shared-screen`) and snapshot projection handling were removed;
- browser descriptors now use `ConnectionDescriptor` / `dihor-gamekit-networking://connect`;
- the Dart interoperability package moved to protocol v2, neutral peer/connection vocabulary and connection descriptors;
- both language surfaces validate the same `protocol/fixtures/v2-*.json` contract as C#.

### Samples and package validation

- added `samples/CommunicationDemo`, a game-agnostic real-LAN reference sample covering UDP discovery, direct descriptor connection, two generic peers, opaque application messages, targeted delivery, broadcast and resume on a replacement connection;
- the neutral communication demo runs in CI and prerelease validation;
- `packaging/consumer` now restores generated NuGet packages only and verifies opaque message exchange plus neutral peer resume instead of merely checking that package types load;
- retired the v0.1 `SharedCounter` and `DungeonPrototype` source/tests from the active v0.2 tree rather than keeping non-compiling examples tied to removed session APIs; they remain available in Git history and the v0.1 tag.

### Cross-repository validation

- validated the corrected primitives against the current Państwa Miasta multiplayer/LAN structure without moving `PlayerProfile`, room policy or Countries & Cities game state into Dihor.GameKit.Networking;
- validated that PartyBeam can keep PartySession, TV/controller/player roles, technical-authority policy and game-state projections entirely above Dihor.GameKit.Networking;
- documented required consumer-side follow-up separately in `docs/cross-repo-validation.md`.

### Validation

- neutral peer continuity tests prove replacement connections resume the same logical peer without duplication;
- in-memory transport tests cover generic targeted and broadcast payloads;
- LAN WebSocket integration tests cover opaque bidirectional messaging and protocol mismatch rejection;
- UDP discovery tests cover endpoint discovery and direct-descriptor independence;
- public API guard tests prevent removed v0.1 product/session abstractions from returning to production assemblies;
- C#, Dart and TypeScript conformance tests target protocol v2.

### Migration

See `docs/migration-0.1-to-0.2.md`.

## 0.1.0-preview.1 - 2026-09-12

First packaged preview of the foundation validated by two mechanically different games.

### Added

- transport-independent room/session lifecycle with stable `PlayerId` and transient `ConnectionId`;
- versioned protocol-v1 envelopes, join/rejoin/leave/heartbeat messages and canonical cross-language fixtures;
- authoritative public/shared-screen and private-player snapshots with monotonic sequences;
- reconnect credentials, presence tracking and reconnect-window semantics;
- transport abstractions plus deterministic in-memory transport;
- direct LAN WebSocket transport and UDP LAN discovery;
- deterministic portable join descriptors suitable for QR/manual transfer;
- Dart protocol implementation tested against the same fixtures as C#;
- browser-first TypeScript client with join/rejoin, heartbeat, reconnect and projection filtering;
- Shared Counter end-to-end sample with two player browsers and a shared screen;
- Dungeon Prototype validation with character selection, turns/AP, movement, private inventory, combat and reconnect;
- package-consumer smoke test and reproducible prerelease artifact workflows.

### Validation outcome

The preview proved the networking mechanisms but also exposed that the generic package boundary was too broad. Player/session/authority/snapshot semantics are therefore historical v0.1 behavior, not the target architecture.

### Distribution

`0.1.0-preview.1` is intentionally a prerelease. CI produced NuGet, npm and Dart-source artifacts. It remains the legacy protocol-v1 line.
