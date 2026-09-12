# Dungeon Prototype validation sample

This sample exists to challenge PartyGameKit with mechanics that are deliberately different from Państwa Miasta and the Shared Counter sample. It is a tiny dungeon game, not a reusable dungeon framework and not a commercial game.

The prototype includes two or more players, character selection, a shared map, turns with action points, movement, a key/inventory item, one enemy/combat interaction, private player state, an authoritative local host and reconnect during an active turn.

All dungeon concepts stay under `samples/DungeonPrototype`. PartyGameKit Core does **not** gain `Monster`, `Loot`, `Tile`, `Attack`, `Character`, `Inventory` or other game-domain APIs.

## Architecture

```mermaid
flowchart LR
    P1[Phone / Player 1] --> WEB[Vanilla browser UI]
    P2[Phone / Player 2] --> WEB
    TV[TV / shared screen] --> WEB
    WEB -->|@partygamekit/client| WS[LAN WebSocket transport]
    WS --> HOST[DungeonPrototypeHost]
    HOST --> SESSION[RoomSession + continuity]
    HOST --> GAME[Dungeon-owned rules/state]
    GAME --> SNAP[AuthoritativeSnapshotPublisher]
    SNAP -->|public map/turn/enemy| TV
    SNAP -->|public + private inventory/turn| P1
    SNAP -->|public + private inventory/turn| P2
```

The browser SDK owns transport/session concerns. The sample owns commands such as `sample.dungeon.move` and every dungeon rule.

## Run locally

Prerequisites: .NET 10 SDK and Node.js 20+.

Build the browser SDK once from the repository root:

```bash
cd clients/typescript
npm install
npm run build
cd ../..
```

Start the sample, replacing the address with the host computer's LAN IPv4 address when testing from phones:

```bash
dotnet run --project samples/DungeonPrototype/DungeonPrototype.Host -- --host 192.168.1.50
```

Defaults are HTTP `8081` and WebSocket `5043`; override them with `--http-port` and `--ws-port`.

Open the printed shared-screen URL on the TV/laptop and the player URL on at least two phones or separate browser profiles. Each player chooses Scout or Guardian. Once every joined player has selected a character and at least two players are present, the first player's turn begins with two action points.

The map is 4×2. Moving one orthogonal square costs one AP. The key at `(1,0)` is picked up automatically and appears only in that player's private inventory. The slime at `(3,0)` can be attacked from an adjacent square; attacking costs one AP. Running out of AP advances the turn automatically, and **End turn** advances it early.

## Reconnect validation

Refresh or temporarily disconnect the active player's browser after the player has picked up the key. The TypeScript SDK reuses the stable `playerId` and reconnect credential from local storage. The host rebinds the player to a new WebSocket and republishes the current authoritative snapshot. Character selection, position, inventory, active turn and remaining AP are not reconstructed by the browser; they come back from host state.

## Offline behavior

After NuGet/npm dependencies and the TypeScript SDK build output are already present locally, the sample does not need Internet access. Static UI assets are served by the local .NET process and gameplay stays on LAN WebSocket.

## Automated validation

`PartyGameKit.Sample.DungeonPrototype.Tests` runs the real LAN transport with one shared-screen client and two players. The test selects Scout/Guardian, starts a turn, moves onto the key, verifies private inventory state, changes turns, disconnects/rejoins the active player and attacks the enemy after restoring the same authoritative state.

Run all repository tests with:

```bash
dotnet test PartyGameKit.slnx --configuration Release
```

## Framework impact

This prototype required **no PartyGameKit public API change**. The existing generic room/session lifecycle, opaque game-owned commands, public/private projections, LAN transport and continuity/reconnect semantics were sufficient. This is the intended result of `[13]`: the second game's mechanics remain an adapter/application concern rather than expanding Core with domain concepts.
