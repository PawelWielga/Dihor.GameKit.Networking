# WebRTC DataChannel transport

Issue `[19]` adds a low-latency peer-to-peer communication path for browser-based Dihor.GameKit.Networking consumers while preserving the communication-only boundary established in `[15]`–`[18]`.

## Decision

The first production WebRTC implementation uses the browser's native `RTCPeerConnection` and `RTCDataChannel` APIs in `@dihor/gamekit-networking`.

Signaling is a separate infrastructure concern. Dihor.GameKit.Networking exposes a small neutral signaling abstraction and an optional SignalR-backed implementation. SignalR carries only WebRTC negotiation data (SDP/ICE and transient routing metadata); application payloads move directly over the negotiated DataChannel.

```text
browser peer A                         browser peer B
     │                                      │
     │     SDP / ICE only                   │
     ├──────────── signaling ────────────────┤
     │        optional SignalR backend      │
     │                                      │
     ╰══════ RTCDataChannel (P2P) ══════════╯
                 application bytes
```

The backend must not become a data relay for the normal WebRTC path. If direct WebRTC cannot be established, the failure is surfaced to callers; automatic fallback belongs to `[20]`.

## Why browser-native WebRTC

PartyBeam's TV and phone clients are browser/PWA consumers, so native browser WebRTC covers the primary low-latency path without adding a native runtime dependency to Dihor.GameKit.Networking.

This also avoids forcing a desktop/server WebRTC implementation into the .NET package line when the .NET side is not required to be a DataChannel endpoint for the primary PartyBeam topology.

## Dependency/license review

Dihor.GameKit.Networking dependencies must permit free commercial use and must not require a paid commercial license, runtime royalty, subscription, per-seat fee or similar commercial-use payment. Prefer standard permissive open-source licenses such as MIT, Apache-2.0 and BSD.

For `[19]` the following options were reviewed on 2026-09-13:

- **SIPSorcery 10.0.16** — technically capable and actively maintained, but the current package uses a non-standard license containing additional geographic/use restrictions. It is not accepted for Dihor.GameKit.Networking.
- **Microsoft.MixedReality-WebRTC 2.0.2** — MIT, but deprecated/archived since 2022 and distributed with old platform-specific native binaries. It is not accepted as the new foundation.
- **WebRTCme 2.0.0** — MIT wrapper, but its desktop path depends on SIPSorcery and therefore does not remove the licensing concern.
- **Pion WebRTC** — active and MIT, but Go-based. Introducing a helper process/native bridge only to connect browser peers would add deployment complexity without improving the primary PartyBeam path. It remains a possible future non-browser adapter, not a dependency for the initial implementation.
- **Browser WebRTC APIs** — built into supported browsers and require no third-party WebRTC runtime package. Chosen for the initial transport.

`@microsoft/signalr` is acceptable for the optional browser signaling adapter under the MIT license. Browser integration tests use Playwright under Apache-2.0.

## Public boundary

WebRTC APIs remain communication-neutral. They may expose:

- transient peer/connection identifiers required to route signaling;
- offer/answer/ICE signaling payloads;
- DataChannel reliability/ordering configuration;
- connection/data-channel state;
- opaque binary or application-message payloads;
- bounded buffering/backpressure state;
- communication diagnostics such as observed round-trip latency and jitter.

They must not expose or require:

- `Player`, controller, TV/shared-screen or spectator roles;
- host/game-master/authority semantics;
- PartyBeam party/lobby lifecycle;
- game state, score, movement or input schemas;
- product join codes;
- automatic LAN/WebRTC/SignalR fallback policy.

## DataChannel profiles

The browser SDK supports explicit transport profiles rather than game-oriented modes.

### `reliable`

Use an ordered reliable DataChannel. Suitable for consumer data that must arrive in order.

Equivalent browser configuration:

```text
ordered = true
maxRetransmits = unset
maxPacketLifeTime = unset
```

### `low-latency`

Use an unordered partially reliable channel for high-frequency data where stale information is less useful than fresh information.

The initial profile uses:

```text
ordered = false
maxRetransmits = 0
```

Consumers choose which messages belong on which profile. Dihor.GameKit.Networking does not decide that controller input, snapshots or any other product concept belongs on a specific channel.

## Buffering and backpressure

High-frequency WebRTC traffic must not create an unbounded user-space queue.

The browser adapter therefore:

1. applies a configurable maximum `RTCDataChannel.bufferedAmount` threshold before each send;
2. rejects or drops a new send according to an explicit overflow policy when the threshold would be exceeded;
3. never grows an unbounded pending application-message array inside Dihor.GameKit.Networking;
4. bounds ICE candidates received before a remote description is available;
5. exposes buffered amount / drop counters for diagnostics.

The default low-latency behavior is to drop the newest stale-prone message rather than queue indefinitely. Reliable mode reports backpressure so the caller can retry or apply product-specific policy.

## Signaling abstraction

WebRTC negotiation is kept behind a neutral interface conceptually equivalent to:

```text
send(target, signal)
receive(source, signal)
```

Signals contain only WebRTC negotiation data and transient routing metadata. The signaling implementation does not inspect application payloads and does not own stable `PeerId` continuity.

The optional SignalR adapter reuses the backend capability from `[18]`, but it is a signaling service, not an `IMessageTransport` application-data relay.

### Early-signal race handling

A newly joined signaling peer may send an offer before the existing peer has finished reacting to its `PeerJoined` notification and subscribed a `WebRtcPeer` handler.

`SignalRWebRtcSignalingClient` therefore temporarily buffers early signals per remote transient connection. That buffer is bounded by `maxPendingSignalsPerPeer` (default `64`).

If the bound is exceeded, Dihor.GameKit.Networking discards that incomplete negotiation buffer and the later channel subscription fails deterministically instead of silently losing an arbitrary subset or growing memory without limit.

When a handler subscribes normally, buffered signals are replayed in arrival order. Peer-leave and client disposal clear the associated pending state.

## Failure and disposal semantics

`WebRtcPeer.connect()` resolves only when the DataChannel opens.

It rejects when:

- the underlying `RTCPeerConnection` reaches `failed`;
- signaling send/apply fails;
- pending ICE limits are exceeded;
- bounded early signaling overflow is surfaced by the signaling adapter;
- the peer or connection is closed before the DataChannel opens.

This avoids leaving consumers with permanently pending connection promises. After an already-open peer fails/closes, state-change listeners still receive the new communication state.

Automatic fallback after failure is intentionally deferred to `[20]`.

## Automated validation

CI validates `[19]` at several levels:

- TypeScript unit tests cover DataChannel profile configuration, bounded backpressure, early disposal and connection failure;
- signaling unit tests cover bounded early-signal buffering and targeted serialization;
- .NET Kestrel/SignalR integration tests cover signaling peer discovery, target routing, leave/disconnect behavior, technical-channel isolation and signal-size limits;
- a real headless Chromium test creates two `WebRtcPeer` instances and negotiates native `RTCPeerConnection` / `RTCDataChannel` connections;
- reliable mode is tested with bidirectional opaque binary delivery;
- low-latency mode sends `60` opaque messages approximately every `16 ms` (about `60 Hz`);
- the browser test records delivery count, observed one-way process-to-process timing and inter-sample variation;
- the browser test verifies the signaling-message count stops growing once negotiation is complete, proving normal application traffic is not routed through signaling.

## Manual latency/jitter diagnostic test

The same real-browser validation can be run manually on a developer machine. It is deliberately product-neutral and does not require PartyBeam/game concepts.

From the repository root:

```bash
cd clients/typescript
npm install --no-audit --no-fund
npx playwright install chromium
npm run test:browser
```

The command prints a summary similar to:

```text
WebRTC browser validation: 60/60 messages, 61.2 Hz, 0.42 ms average one-way observation, 0.11 ms inter-sample variation.
```

Interpretation:

- `received/sent` reports delivery observed by the low-latency channel test;
- `Hz` verifies the test actually exercised the intended 30–60 Hz class of traffic (the automated gate accepts the practical scheduler range used by CI);
- `average one-way observation` is local test-process timing from send timestamp to receive callback, not a synchronized-network benchmark;
- `inter-sample variation` is a simple jitter indicator over those observations;
- `sampleDiagnostics()` separately exposes browser WebRTC candidate-pair RTT when the browser provides it.

For a home-LAN manual check with two real devices, a consuming test page may instantiate the same `WebRtcPeer` APIs on each device using the SignalR signaling endpoint. The diagnostic values should be recorded from both peers; Dihor.GameKit.Networking intentionally does not impose a pass/fail latency threshold because device/browser/Wi-Fi conditions are environment-dependent.

The acceptance requirement is that application payloads use the direct DataChannel and do not require the backend to process every 30–60 Hz message.

## Non-goals for `[19]`

- native .NET WebRTC endpoint;
- native Dart WebRTC endpoint;
- TURN service implementation/operation;
- persistent signaling/session storage;
- PartyBeam product topology or role semantics;
- automatic transport selection/fallback;
- treating SignalR as the high-frequency data path.
