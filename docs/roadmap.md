# Roadmap

This roadmap is intentionally incremental. PartyGameKit should be extracted from real needs, not designed as a complete framework up front.

## Phase 0: Document and compare

Status: current.

Goals:

- document the architecture,
- define what belongs in Core and what does not,
- inspect the existing Państwa Miasta networking/session code,
- identify functionality already proven in production/use,
- avoid committing to package APIs too early.

Deliverable:

- agreed architecture and extraction plan.

## Phase 1: Small reusable core

Extract only the pieces that are clearly shared:

- `PlayerId`,
- room/session identity,
- player lifecycle,
- host lifecycle,
- join/leave,
- disconnect/reconnect semantics,
- host transfer semantics,
- transport abstractions,
- basic command/event envelopes if real code proves they are useful.

Use project references while the API is unstable.

Success criterion:

> Państwa Miasta behaves the same after moving the reusable infrastructure behind PartyGameKit abstractions.

## Phase 2: Big-screen mode for Państwa Miasta

Use PartyGameKit to separate the shared screen from player controllers.

TV/browser responsibilities:

- lobby and QR/join code,
- current round and letter,
- timer,
- public answers after reveal,
- voting/scoring presentation,
- final results.

Phone responsibilities:

- player identity,
- answer entry,
- ready/finish actions,
- voting,
- private information.

Success criterion:

> The game can be played naturally with one shared screen and multiple phones without passing a host phone around.

This phase validates public/private client projections.

## Phase 3: LAN transport

Implement an initial local transport.

First practical target:

- TV/laptop is host and authority,
- local WebSocket communication,
- QR code contains enough information to connect,
- no cloud backend required for gameplay,
- reconnect is supported within reasonable local constraints.

Do not block this phase on automatic LAN discovery.

Success criterion:

> A complete supported game can start and finish with Internet unavailable after the required local assets/application are available.

## Phase 4: Second game / dungeon prototype

Build a small game with very different requirements from Państwa Miasta.

Prototype scope:

- 2+ players,
- shared tile/map display,
- character selection,
- movement/action points,
- one enemy/combat flow,
- one private inventory or hidden-state feature,
- reconnect.

The goal is not a finished commercial game. The goal is to break bad abstractions.

Success criterion:

> The dungeon prototype uses the same PartyGameKit core without introducing concepts such as monster, loot or movement into the generic library.

## Phase 5: Stabilize packages

Only after two games validate the API:

Potential NuGet packages:

```text
PartyGameKit.Core
PartyGameKit.Transport.Abstractions
PartyGameKit.Transport.Lan
PartyGameKit.Transport.SignalR
PartyGameKit.AspNetCore
```

Potential npm packages:

```text
@partygamekit/client
@partygamekit/react
```

Tasks:

- semantic versioning,
- API documentation,
- package metadata,
- automated build/test,
- compatibility policy,
- sample applications.

## Phase 6: Backend-assisted rooms

Add optional cloud services for:

- globally usable join codes,
- room discovery,
- signaling,
- remote players,
- reconnect coordination,
- fallback/relay.

SignalR is the first candidate for backend session traffic.

Success criterion:

> The same game can switch between local and backend-assisted operation without changing game rules.

## Phase 7: WebRTC / low-latency transport

Add a direct transport path for games that need frequent input.

Targets:

- WebRTC DataChannel,
- sequence-numbered input,
- timestamps,
- stale-input rejection,
- appropriate reliable/unreliable delivery modes,
- fallback when peer-to-peer setup fails.

Validation game should be intentionally latency-sensitive, for example:

- Pong,
- micro racing,
- gyro steering,
- simple real-time arena movement.

Success criterion:

> 30–60 Hz controller input is usable on a normal home network without making the backend process every input frame.

## Phase 8: Automatic networking

Introduce a high-level mode such as:

```text
NetworkingMode.Auto
```

Possible strategy:

```text
LAN direct
   ↓ fail
WebRTC direct
   ↓ fail
Cloud fallback
```

This should be implemented only after all underlying modes are individually reliable.

## Later ideas

Not required for the initial framework:

- native Android/Google TV host,
- desktop host application,
- mDNS/UDP LAN discovery,
- persistent accounts,
- matchmaking,
- game catalog/platform shell,
- Party Night playlists spanning multiple games,
- spectator clients,
- dedicated server authority,
- analytics/telemetry,
- developer SDK/templates.

These should remain outside the early core until there is a concrete product requirement.
