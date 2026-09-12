# SignalR backend transport

`PartyGameKit.Transport.SignalR` is an optional backend-assisted transport. It does not replace the LAN transport and it does not move game rules into PartyGameKit infrastructure.

## What the backend does

The SignalR backend is a routing layer:

1. A game host registers a `RoomId` and globally unique `JoinCode` in `SignalRRoomRegistry`.
2. A remote client connects to the PartyGameKit SignalR hub.
3. The first payload must be a normal PartyGameKit `session.join.request` or `session.rejoin.request` envelope.
4. The registry uses that infrastructure envelope to select a room.
5. The connection is bound to that room's `SignalRRoomTransport`.
6. Later payloads are forwarded as opaque bytes. Game-owned commands and state are not interpreted by the backend router.

This keeps signaling/routing concerns out of `PartyGameKit.Core` and preserves the same protocol/session rules used over LAN.

## Host setup

Add PartyGameKit SignalR services before building the ASP.NET Core application:

```csharp
builder.Services.AddPartyGameKitSignalR();
```

Map the hub and register each hosted room:

```csharp
var app = builder.Build();
app.MapPartyGameKitSignalR("/partygamekit");

var registry = app.Services.GetRequiredService<SignalRRoomRegistry>();
var transport = registry.RegisterRoom(roomId, joinCode);
```

The `SignalRRoomTransport` is an `IGameTransport`, so game/session code should consume it through the same transport abstraction as LAN or the in-memory transport.

A SignalR join descriptor uses:

```json
{
  "protocolVersion": 1,
  "roomId": "shared-counter-room",
  "joinCode": "COUNT1",
  "transport": "signalr",
  "endpoint": "https://games.example.com/partygamekit"
}
```

The endpoint is the public HTTP(S) SignalR hub URL. Browser clients should normally use HTTPS in production.

## Global join codes

Within one backend registry, `JoinCode` values are unique across rooms. Attempting to register a second active room with the same code fails.

For a single backend instance this gives deterministic global routing. A horizontally scaled deployment must put room/join-code ownership in shared infrastructure or use sticky/partitioned routing so all nodes agree on which host owns a code. The in-memory registry in this package is intentionally not a distributed database.

## Reconnect semantics

SignalR transport reconnection does not define PartyGameKit player identity.

A reconnect creates a new transport connection. The client then sends the existing PartyGameKit `session.rejoin.request` with:

- stable `PlayerId`,
- opaque reconnect token,
- last seen snapshot sequence.

`SessionContinuityCoordinator` remains authoritative for the reconnect window and identity validation. The browser adapter deliberately does not enable SignalR automatic reconnect, because silently creating a new SignalR connection without the PartyGameKit rejoin handshake would split transport state from session state.

## Shared Counter

The existing Shared Counter sample can use either transport without changing its game command or snapshot model.

LAN, still the default:

```bash
npm install --prefix clients/typescript --no-audit --no-fund
npm run build --prefix clients/typescript
dotnet run --project samples/SharedCounter/SharedCounter.Host -- \
  --host 192.168.1.20 \
  --transport lan
```

SignalR:

```bash
npm install --prefix clients/typescript --no-audit --no-fund
npm run build --prefix clients/typescript
dotnet run --project samples/SharedCounter/SharedCounter.Host -- \
  --host games.example.com \
  --http-port 8080 \
  --transport signalr
```

For a real internet deployment, put the ASP.NET Core host behind HTTPS and advertise the public hostname/port actually reachable by clients. If TLS terminates at a reverse proxy, configure forwarded headers and proxy WebSocket/SignalR traffic according to the hosting platform.

The sample serves the official browser build of `@microsoft/signalr` from local `node_modules`; it does not require a CDN at play time.

## Deployment expectations

The package does not provision infrastructure. A production backend is responsible for:

- a publicly reachable ASP.NET Core endpoint,
- HTTPS/TLS,
- firewall/security-group rules,
- reverse-proxy configuration where applicable,
- process supervision and logs,
- scaling strategy if more than one backend instance is used,
- persistence/distributed room routing if room ownership must survive process restarts or span nodes.

The current registry is process-local. If the backend process stops, its room registrations disappear. Game state persistence is a game/application concern unless a later PartyGameKit feature explicitly defines otherwise.

## LAN remains independent

`PartyGameKit.Transport.Lan` does not reference the SignalR package or require this backend. A LAN host can continue to advertise a `lan-websocket` join descriptor and run fully offline.

SignalR is therefore an additional connectivity option, not a mandatory dependency for local party games.
