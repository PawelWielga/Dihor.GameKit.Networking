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
Phone B ─────┼──── local Wi-Fi ──── TV / laptop host
Phone C ─────┘
```

The host owns the game session and game authority.

Potential first implementation:

- host exposes a local endpoint,
- host creates a room code,
- QR code contains the host address and room information,
- phones connect directly,
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
- reconnect after a phone sleeps or changes network.

For the first version, a QR code containing the host address is an acceptable solution instead of automatic LAN discovery.

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

Pure browser clients are the preferred starting point because joining by QR without installation is a core product advantage.

Browser limitations mean automatic LAN discovery is less straightforward than in a native application. Native discovery mechanisms such as mDNS/UDP can be considered later for dedicated TV or desktop hosts.

The protocol and core model should not depend on whether the host is ultimately a browser, desktop app, Android TV app or another runtime.

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

It should not be required to simulate every local game session if the TV/host can safely own that responsibility.
