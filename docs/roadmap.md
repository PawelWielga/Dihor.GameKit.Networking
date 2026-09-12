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
- shared-screen and dungeon-style consumer samples;
- reproducible prerelease packaging.

It also overreached architecturally by promoting player/session/authority/snapshot semantics into PartyGameKit base APIs.

Those historical issues remain closed implementation history. They are not the target architecture going forward.

## Boundary correction

### `[15]` Re-establish PartyGameKit as a reusable communication library

Status: architecture/documentation phase.

Decide and document:

- PartyGameKit owns communication/networking, not PartyBeam/game runtime semantics;
- transient `ConnectionId` remains;
- stable reconnect identity becomes neutral `PeerId`/logical client identity;
- `RoomId` survives only if justified as a neutral routing scope;
- `PlayerId`, `ClientRole`, `AuthorityId`, player capacity/admission and `RoomSession` move out/generalize;
- reconnect remains only as neutral communication continuity;
- game snapshot/public/private projection semantics move out;
- generic ordering/dedupe may remain as an optional utility;
- connection descriptors and discovery become technical rather than product/session concepts;
- TypeScript/Dart base APIs must become communication-neutral;
- breaking prerelease changes are acceptable.

Source of truth: [Communication boundary](communication-boundary.md).

### `[16]` Refactor Core and protocol to remove product/session semantics

Implement `[15]` in production code.

Required outcomes:

- neutral communication identities;
- transport abstractions no longer depend on game/session Core semantics;
- `IGameTransport` and related game-specific vocabulary are generalized;
- neutral connection handshake and resume control protocol;
- opaque application messages;
- player/host/shared-screen/authority/room-session APIs removed from base packages;
- session continuity split into neutral health/resume components;
- game snapshot/projection APIs removed from base packages;
- LAN discovery and connection descriptors neutralized;
- tests prove generic peers can connect, exchange opaque messages and resume without product semantics;
- protocol fixtures updated;
- migration notes/package version updated.

Do not add SignalR/WebRTC/fallback yet.

### `[17]` Align SDKs, samples, docs and interoperability

Finish the breaking correction across all consumer-facing surfaces:

- TypeScript browser SDK uses connect/send/receive/resume vocabulary;
- Dart package implements the same neutral communication protocol;
- canonical fixtures pass in C#, Dart and TypeScript;
- add/convert a neutral end-to-end communication sample;
- retain game-oriented samples only as consumers that own their own Player/TV/Authority/GameState types;
- validate mapping from Państwa Miasta networking behavior to the corrected primitives;
- validate PartyBeam can own TV/pilot/controller/player/party/authority semantics entirely above PartyGameKit;
- produce the next prerelease artifacts and migration guide.

## Corrected foundation completion criteria

After `[17]`:

- PartyGameKit has a tested language-neutral communication contract;
- direct LAN connect/send/receive/disconnect/reconnect works without cloud backend;
- LAN discovery and direct descriptor connection remain available;
- C#, Dart and TypeScript interoperability is validated;
- application payloads remain opaque and consumer-owned;
- a consumer with **no concept of players** can use the library;
- PartyBeam defines product roles/party/authority behavior above PartyGameKit;
- Państwa Miasta keeps its game engine/player/session/snapshot semantics above PartyGameKit;
- no base API requires player, host, shared-screen, scoring, lobby or game-session semantics.

## Additional transports

Only after `[15]`–`[17]` are complete.

### `[18]` Optional backend-assisted SignalR transport

Add SignalR/backend relay as another communication transport.

Rules:

- LAN remains usable without backend;
- backend routing scopes are opaque communication identifiers;
- backend does not own PartyBeam parties, players, lobbies, authority or game state;
- the same opaque application payloads work over LAN and SignalR.

### `[19]` WebRTC DataChannel transport

Add low-latency peer communication behind the same neutral abstractions.

Rules:

- signaling is infrastructure only;
- PartyGameKit does not assume peers are phones, players, TV or authority;
- validate 30–60 Hz opaque message streams;
- bound buffering/backpressure and expose latency/jitter diagnostics.

### `[20]` Automatic transport selection and fallback

Add deterministic communication-path selection after LAN, SignalR and WebRTC are independently reliable.

A reasonable strategy may prefer LAN, then WebRTC, then backend relay, but the decision is based on connectivity/capabilities, not product roles.

Automatic mode only answers: **which transport should carry messages now?** It must not decide whether a game pauses, whether a participant keeps a slot or who becomes authority.

## Explicitly deferred or consumer-owned

Do not move these into PartyGameKit base packages without a new communication-level justification:

- PartyBeam party/lobby lifecycle;
- TV/pilot/controller/player/spectator roles;
- player capacity/admission rules;
- game authority/host policy;
- automatic game-host migration;
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