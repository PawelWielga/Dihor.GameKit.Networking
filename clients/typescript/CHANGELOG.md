# Changelog

## 0.2.0-preview.5 - 2026-09-15

- Adds `MonotonicTimingSynchronizer` for bounded peer/reference monotonic clock-offset estimation.
- Adds RTT, RTT-jitter and uncertainty metrics using the same deterministic filter policy as .NET.
- Adds peer-local timestamp normalization into the reference clock domain.
- Rejects unknown probes, invalid/stale/non-monotonic evidence, old events and implausibly future timestamps.
- Adds explicit reconnect/transport-change reset so stale timing models are not silently reused.
- Adds `monotonicNowMs()` backed by `performance.now()` rather than wall-clock time.
- Keeps timing transport-neutral and protocol v2 unchanged; consumers decide how probe/reply data is carried.
- Adds no new runtime dependency.

## 0.2.0-preview.4 - 2026-09-13

- Adds generic `AutomaticTransportSelector` with `auto`, forced LAN, forced WebRTC and forced SignalR modes.
- Uses deterministic bounded candidate order with per-attempt timeout overrides.
- Reconnect prefers the previously successful transport before falling back through the configured order.
- Exposes structured attempt diagnostics including selected, failed, timed-out and unavailable candidates.
- Stops fallback immediately on caller cancellation and can dispose connections that succeed after timeout/cancellation.
- Keeps selector context and connection types generic so browser WebRTC/LAN/relay adapters remain communication concerns rather than product/session abstractions.
- Keeps protocol v2 and consumer-owned payloads unchanged when the selected transport changes.

## 0.2.0-preview.3 - 2026-09-13

- Adds browser-native `WebRtcPeer` using `RTCPeerConnection` and `RTCDataChannel`.
- Adds explicit `reliable` ordered and `low-latency` unordered/no-retransmit profiles.
- Adds bounded DataChannel buffering/backpressure with explicit reject/drop behavior instead of an unbounded PartyGameKit queue.
- Bounds ICE candidates received before the remote description is available.
- Adds communication diagnostics for buffered bytes, dropped-message count, candidate-pair RTT and RTT variation.
- Adds neutral `WebRtcSignalingChannel` plus optional `SignalRWebRtcSignalingClient` for SDP/ICE routing only.
- Adds MIT-licensed `@microsoft/signalr` as the browser signaling runtime dependency.
- Adds Apache-2.0 Playwright as dev-only tooling for real Chromium DataChannel validation.
- Keeps protocol v2 unchanged; WebRTC application bytes remain consumer-owned and opaque.
- Does not define players, controllers, TVs, product sessions, authority or automatic transport fallback.

## 0.2.0-preview.2 - 2026-09-13

- Aligns the browser package version with the PartyGameKit preview.2 release line.
- Keeps protocol v2 and the preview.1 browser API unchanged.
- The SignalR relay transport is provided by the .NET transport packages; this release does not claim a browser SignalR relay transport implementation.

## 0.2.0-preview.1 - 2026-09-13

- Moves the browser SDK to PartyGameKit protocol v2.
- Replaces player/shared-screen roles with neutral peer identity.
- Replaces join/rejoin vocabulary with connect/resume.
- Replaces room/join descriptors with technical `ConnectionDescriptor` JSON and `partygamekit://connect` URI forms.
- Replaces public/private game snapshot handling with opaque consumer-owned `application.message` delivery.
- Keeps heartbeat, reconnect and local identity persistence as communication concerns.
- Removes the v0.1 snapshot projection helper from the base SDK.

## 0.1.0-preview.1 - 2026-09-12

- PartyGameKit protocol-v1 browser models and validation.
- LAN WebSocket join/rejoin/leave flow.
- Stable player identity and reconnect credential persistence.
- Heartbeat and automatic reconnect.
- Shared-screen and player roles.
- Public/private snapshot filtering and per-target stale snapshot rejection.
- Canonical join descriptor JSON/URI compatibility with C# and Dart.
