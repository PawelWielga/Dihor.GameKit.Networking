# WebRTC DataChannel transport

Issue `[19]` adds a low-latency peer-to-peer communication path for browser-based PartyGameKit consumers while preserving the communication-only boundary established in `[15]`–`[18]`.

## Decision

The first production WebRTC implementation uses the browser's native `RTCPeerConnection` and `RTCDataChannel` APIs in `@partygamekit/client`.

Signaling is a separate infrastructure concern. PartyGameKit will expose a small neutral signaling abstraction and an optional SignalR-backed implementation. SignalR carries only WebRTC negotiation data (SDP/ICE and transient routing metadata); application payloads move directly over the negotiated DataChannel.

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

PartyBeam's TV and phone clients are browser/PWA consumers, so native browser WebRTC covers the primary low-latency path without adding a native runtime dependency to PartyGameKit.

This also avoids forcing a desktop/server WebRTC implementation into the .NET package line when the .NET side is not required to be a DataChannel endpoint for the primary PartyBeam topology.

## Dependency/license review

PartyGameKit dependencies must permit free commercial use and must not require a paid commercial license, runtime royalty, subscription, per-seat fee or similar commercial-use payment. Prefer standard permissive open-source licenses such as MIT, Apache-2.0 and BSD.

For `[19]` the following options were reviewed on 2026-09-13:

- **SIPSorcery 10.0.16** — technically capable and actively maintained, but the current package uses a non-standard license containing additional geographic/use restrictions. It is not accepted for PartyGameKit.
- **Microsoft.MixedReality-WebRTC 2.0.2** — MIT, but deprecated/archived since 2022 and distributed with old platform-specific native binaries. It is not accepted as the new foundation.
- **WebRTCme 2.0.0** — MIT wrapper, but its desktop path depends on SIPSorcery and therefore does not remove the licensing concern.
- **Pion WebRTC** — active and MIT, but Go-based. Introducing a helper process/native bridge only to connect browser peers would add deployment complexity without improving the primary PartyBeam path. It remains a possible future non-browser adapter, not a dependency for the initial implementation.
- **Browser WebRTC APIs** — built into supported browsers and require no third-party WebRTC runtime package. Chosen for the initial transport.

`@microsoft/signalr` is acceptable for the optional browser signaling adapter under the MIT license. Browser integration tests may use Playwright under Apache-2.0.

## Public boundary

WebRTC APIs must remain communication-neutral. They may expose:

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

Use an ordered reliable DataChannel. Suitable for commands and state updates that must arrive.

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

Consumers can choose which messages belong on which profile. PartyGameKit does not decide that controller input, snapshots or any other product concept belongs on a specific channel.

## Buffering and backpressure

High-frequency WebRTC traffic must not create an unbounded user-space queue.

The browser adapter therefore:

1. sets a configurable maximum `RTCDataChannel.bufferedAmount` threshold;
2. rejects or drops a new send according to an explicit overflow policy when the threshold is exceeded;
3. never grows an unbounded pending-message array inside PartyGameKit;
4. exposes buffered amount / drop counters for diagnostics.

The default low-latency behavior is to drop the new stale-prone message rather than queue indefinitely. Reliable mode reports backpressure so the caller can retry or apply product-specific policy.

## Signaling abstraction

WebRTC negotiation is kept behind a neutral interface conceptually equivalent to:

```text
send(target, signal)
receive(source, signal)
```

Signals contain only WebRTC negotiation data and transient routing metadata. The signaling implementation does not inspect application payloads and does not own stable `PeerId` continuity.

The optional SignalR adapter introduced in this issue reuses the backend capability from `[18]`, but it is a signaling service, not an `IMessageTransport` application-data relay.

## Validation

Automated browser integration coverage must prove:

- two peers establish a direct DataChannel using signaling;
- bidirectional opaque binary delivery;
- reliable ordered behavior;
- unordered/no-retransmit profile creation;
- bounded backpressure behavior;
- clean close/disposal;
- failed negotiation/connection state is surfaced;
- a 30–60 Hz opaque stream runs without routing each application message through SignalR.

A manual diagnostic sample will report message count, drops, approximate round-trip latency and jitter on a normal home LAN.

## Non-goals for `[19]`

- native .NET WebRTC endpoint;
- TURN service implementation;
- persistent signaling/session storage;
- PartyBeam product topology or role semantics;
- automatic transport selection/fallback;
- treating SignalR as the high-frequency data path.
