# Networking

## Goal

PartyGameKit should support the same game code in multiple networking environments:

1. local LAN only,
2. backend/cloud assisted,
3. hybrid direct connection with backend signaling/fallback.

The user should eventually be able to choose `Auto` and let the framework select the best available path.

## Principle

Networking is infrastructure. It must not define game rules.

Game code should issue commands and events through abstractions rather than calling SignalR hubs, WebRTC APIs or sockets directly.

## Mode 1: LAN only

Typical topology:

```text
Phone A ─────┐
Phone B ─────┼──── local Wi-Fi ──── local authority host
Phone C ─────┘                            │
                                          └──── shared screen
```

For the first direct-LAN implementation, the authority host must run in a runtime that can listen for inbound connections, for example a .NET/native desktop, TV or companion process. A pure browser/PWA cannot expose the required HTTP/WebSocket listener because browser WebSocket APIs are client-only.

The shared screen may run in the same server-capable process or may be a separate browser client connected to it. PartyGameKit must not make display role, host role and authority identity synonymous.

Potential first implementation:

- a server-capable local host exposes a local endpoint,
- host creates a room code,
- QR/manual join data contains the host address and room information,
- phones and browser/shared-screen clients connect directly,
- WebSocket is sufficient for the initial implementation.

Example join target:

```text
http://192.168.1.42:7421/join/X7K2
```

Advantages:

- no backend required for the active session,
- very low infrastructure cost,
- low latency,
- can work without Internet access.

Challenges:

- host discovery,
- changing local IP addresses,
- client isolation on some Wi-Fi networks,
- browser HTTPS/security restrictions,
- reconnect after a phone sleeps or changes network,
- providing a server-capable local host when the visible shared screen is a pure browser.

For the first version, direct connection data in a QR/manual join descriptor is an acceptable fallback even when automatic LAN discovery is unavailable.

A backend-free topology where a pure browser itself accepts direct peer connections is not part of the initial WebSocket LAN transport. That requires a browser-capable peer transport such as WebRTC and belongs to the later low-latency/direct-transport phase.

## Mode 2: Cloud/backend

Typical topology:

```text
Phone A ─────┐
Phone B ─────┼──── backend ──── TV
Phone C ─────┘
```

SignalR is a natural transport for room/session traffic and games where input latency does not need to be extremely low.

This mode is appropriate for:

- Państwa Miasta,
- quizzes,
- voting,
- turn-based games,
- remote players,
- fallback when direct connectivity fails.

The backend may be only a transport/session coordinator, or it may later become the game authority for selected games.

## Mode 3: Hybrid

Expected long-term default:

```text
                 Backend
          discovery / signaling
                  │
                  ▼
Phone ───── direct connection ───── TV
```

The backend helps devices find each other and establish the session, but high-frequency game traffic can travel directly when possible.

WebRTC DataChannel is a candidate for this path.

This is particularly useful for games such as:

- racing,
- Pong-style games,
- real-time movement,
- gyro steering,
- rapid button input.

The phone should send **input**, not authoritative world position.

Example:

```text
steering = -0.72
accelerate = 1
brake = 0
sequence = 3812
timestamp = ...
```

The game authority simulates the actual game state.

## Auto transport

Long-term target:

```text
Auto
 │
 ├─ try direct LAN
 │      └─ success → use LAN
 │
 ├─ try direct WebRTC
 │      └─ success → use WebRTC
 │
 └─ fallback to cloud / SignalR relay
```

A possible consumer API:

```csharp
builder.Services.AddPartyGameKit(options =>
{
    options.Networking.Mode = NetworkingMode.Auto;
});
```

The exact API is intentionally not fixed yet.

## Reliability requirements

Transport implementations should eventually provide enough metadata and behavior to support:

- connection identity,
- reconnect,
- message ordering where required,
- sequence numbers for real-time input,
- timestamps,
- stale-input rejection,
- duplicate-command protection where required,
- graceful host disconnect handling,
- host transfer where supported.

For high-frequency real-time input, it must be possible to prefer freshness over guaranteed delivery. Old steering input is usually worse than dropping it.

## Browser versus native constraints

Pure browser clients are preferred for phones and shared-screen rendering where possible because joining by QR without installation is a core product advantage.

Browsers can initiate WebSocket connections but cannot act as raw HTTP/WebSocket listeners. Therefore the initial backend-free LAN WebSocket mode requires a server-capable local host. A browser shared screen can connect to that host, including a companion process on the same machine, without moving game rules into networking code.

Browser limitations also mean automatic LAN discovery is less straightforward than in a native application. Native discovery mechanisms such as mDNS/UDP can be used by a dedicated TV, desktop or companion host, while browser clients can use a portable join descriptor/QR or backend-assisted discovery later.

The protocol and core model must not depend on whether a client UI is a browser, desktop app, Android TV app or another runtime, and must not assume that the shared-screen client is necessarily the network listener or authority.

## Backend philosophy

The backend should be optional for local gameplay, not mandatory by architectural accident.

Where a backend exists, its responsibilities may include:

- room registry,
- short join codes,
- signaling,
- NAT traversal support,
- reconnect coordination,
- relay/fallback,
- optional persistence,
- optional server-authoritative games.

It should not be required to simulate every local game session if a local authority process can safely own that responsibility.
