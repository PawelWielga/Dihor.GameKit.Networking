# Shared Counter reference sample

This sample is the smallest end-to-end PartyGameKit game-shaped application. The .NET host owns a counter per player. Browser/phone players can increment only through a game-owned command, while a shared TV/browser screen receives the public projection. Each player also receives a private projection containing their own count.

Nothing in this sample is part of `PartyGameKit.Core`: `sample.counter.increment`, counter state and the browser UI all stay under `samples/SharedCounter`.

## Architecture

```mermaid
flowchart LR
    P1[Phone / Player 1] -->|HTTP local UI| WEB[Vanilla browser app]
    P2[Phone / Player 2] -->|HTTP local UI| WEB
    TV[TV / shared browser] -->|HTTP local UI| WEB
    WEB -->|@partygamekit/client\njoin / rejoin / heartbeat| WS[LAN WebSocket transport]
    WS --> HOST[SharedCounterHost]
    HOST --> SESSION[RoomSession + continuity]
    HOST --> SNAP[AuthoritativeSnapshotPublisher]
    SNAP -->|public snapshots| TV
    SNAP -->|public + private snapshots| P1
    SNAP -->|public + private snapshots| P2
```

The HTTP server only serves local static assets and `/config.json`. Gameplay goes directly over PartyGameKit's LAN WebSocket transport. There is no cloud backend and no CDN.

## Run from a fresh clone

Prerequisites: .NET 10 SDK and Node.js 20+.

From the repository root, restore/build the browser SDK once:

```bash
cd clients/typescript
npm install
npm run build
cd ../..
```

Then start the sample. For phones on the same Wi-Fi/LAN, replace `192.168.1.50` with the host computer's LAN IPv4 address:

```bash
dotnet run --project samples/SharedCounter/SharedCounter.Host -- --host 192.168.1.50
```

Defaults are HTTP `8080` and WebSocket `5042`. They can be changed with `--http-port` and `--ws-port`.

The host prints three useful values:

- shared screen URL, for example `http://192.168.1.50:8080/?role=shared-screen`;
- player URL, for example `http://192.168.1.50:8080/?role=player`;
- the canonical `partygamekit://join?...` descriptor used by the browser client and suitable as a QR payload in a product UI.

Open the shared-screen URL on the TV/laptop. Open the player URL on at least two phones. For a one-machine demo, use separate browser profiles or one normal and one private/incognito profile because stable player identity is intentionally persisted per browser profile.

Press **+1** on either player. The TV receives the new public total and player list. Each player receives the same public state plus a private `yourCount` projection.

## Reconnect demo

After a player has joined, temporarily drop its connection or refresh the page. The TypeScript SDK keeps the stable `playerId` and reconnect token in browser `localStorage`. The new WebSocket sends `session.rejoin.request`; the host rebinds the existing player and publishes the current state. The player's previous count is preserved.

The sample uses a two-minute reconnect window. An explicit **leave** clears the reconnect credential and removes that player's sample state.

## Offline/LAN behavior

After NuGet/npm dependencies and the TypeScript build output are already available locally, the demo needs no Internet access. HTML, CSS, JavaScript and the PartyGameKit SDK are served from the repository by the local .NET process; gameplay remains on the LAN WebSocket connection.

## Automated integration test

`PartyGameKit.Sample.SharedCounter.Tests` starts the real `[08]` `LanWebSocketTransport` on loopback and connects one shared-screen plus two player clients simultaneously. It verifies public/private snapshots, a game-owned increment command and reconnect with the same player identity/current count.

Run the full repository suite with:

```bash
dotnet test PartyGameKit.slnx --configuration Release
```
