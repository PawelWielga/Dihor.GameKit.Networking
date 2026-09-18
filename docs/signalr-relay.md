# SignalR relay transport

Dihor.GameKit.Networking `0.2.0-preview.2` adds optional backend-assisted connectivity as another communication transport. It does not add a cloud-owned party, lobby or game-session runtime.

## Boundary

The relay understands only communication concepts:

- a backend SignalR connection;
- a Dihor.GameKit.Networking `ConnectionId` assigned to a remote peer connection;
- an optional technical `ChannelId` used to isolate relay traffic;
- opaque byte payloads;
- targeted delivery, broadcast and disconnect lifecycle.

The relay does not interpret or own:

- players or player capacity;
- TV/controller/shared-screen roles;
- PartyBeam parties or game sessions;
- authority/game-master policy;
- product join codes;
- game state, commands, snapshots or scoring.

Product code may map one of its own identifiers to a `ChannelId`, but Dihor.GameKit.Networking treats that value only as a routing scope.

## Packages

Use the client/listener transport package in applications that connect to a relay:

```xml
<PackageReference Include="Dihor.GameKit.Networking.Transport.SignalR" Version="0.2.0-preview.2" />
```

Use the server package only in the ASP.NET Core process that exposes the relay endpoint:

```xml
<PackageReference Include="Dihor.GameKit.Networking.Transport.SignalR.Server" Version="0.2.0-preview.2" />
```

The split is deliberate: a normal transport consumer does not need a server-side ASP.NET Core framework reference.

## Server setup

Register and map the relay in an ASP.NET Core application:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDihorGameKitNetworkingSignalRRelay(options =>
{
    options.MaxMessageBytes = 256 * 1024;
});

var app = builder.Build();

app.MapDihorGameKitNetworkingSignalRRelay("/dihor-gamekit-networking-relay");

app.Run();
```

`MaxMessageBytes` is the Dihor.GameKit.Networking raw payload limit. The server internally allows enough SignalR JSON/base64 framing overhead for a payload at that limit, then validates the decoded byte array against the configured raw limit.

The public server surface intentionally consists of registration/mapping/options. The Hub and ephemeral routing registry are internal implementation details.

## Listener transport

A consumer that owns the communication-listener side creates an ordinary `IMessageTransport`:

```csharp
var options = new SignalRRelayOptions(
    new Uri("https://relay.example.com/dihor-gamekit-networking-relay"),
    new ChannelId("opaque-routing-scope"));

await using IMessageTransport transport =
    await SignalRRelayTransport.StartAsync(options, cancellationToken);
```

`SignalRRelayTransport` exposes remote clients through the same transport events and operations used by the LAN path:

- `TransportConnectionOpened`;
- `TransportMessageReceived`;
- `TransportConnectionClosed`;
- `TransportFaulted`;
- targeted `SendAsync`;
- `BroadcastAsync`;
- `DisconnectAsync`;
- `StopAsync`/disposal.

## Remote client

A single peer connects with the same protocol-v2 connect or resume handshake it would use for LAN:

```csharp
var options = new SignalRRelayOptions(
    new Uri("https://relay.example.com/dihor-gamekit-networking-relay"),
    new ChannelId("opaque-routing-scope"));

await using var client = await SignalRRelayClient.ConnectAsync(
    options,
    connectOrResumeHandshakeJson,
    cancellationToken);

await client.SendAsync(applicationBytes, cancellationToken);
var inbound = await client.ReceiveAsync(cancellationToken);
```

The backend does not parse the Dihor.GameKit.Networking handshake. `SignalRRelayTransport` validates protocol-v2 connect/resume before it exposes `TransportConnectionOpened`, matching the LAN lifecycle contract.

## Authentication and connection customization

`SignalRRelayOptions.ConfigureConnection` exposes the underlying SignalR HTTP connection options so the host application can configure authentication or HTTP behavior without Dihor.GameKit.Networking defining an account model.

For example, a product may provide an access token through the normal SignalR client option:

```csharp
var options = new SignalRRelayOptions(endpoint, channelId)
{
    ConfigureConnection = http =>
    {
        http.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
    },
};
```

Authentication and authorization policy belong to the deployed backend/product. Dihor.GameKit.Networking does not mint accounts, product join tokens or globally meaningful party codes.

## Relay routing

A relay endpoint is identified by a technical `ChannelId`. One `SignalRRelayTransport` owns the listener registration for that channel at a time. Remote clients attach to the same channel and receive an opaque transient transport `ConnectionId`.

The server keeps only in-memory routing bindings required to deliver traffic. It does not persist application sessions or participant records.

Independent channels are isolated: traffic for channel A is not delivered to channel B.

`ChannelId` is not an authorization secret. A production server must authorize access independently when channels should not be publicly joinable.

## Lifecycle and resume

1. `SignalRRelayTransport` connects to the backend and registers its channel as the current listener.
2. `SignalRRelayClient` connects and attaches to that channel with a protocol-v2 connect/resume handshake.
3. The relay assigns a transient `ConnectionId` and forwards the handshake to the listener transport.
4. The listener transport validates the handshake before publishing the connection.
5. Client bytes become `TransportMessageReceived` events on the listener.
6. Listener `SendAsync` targets one relay client; `BroadcastAsync` targets all clients in its channel.
7. Disconnects and physical SignalR loss remove relay bindings and become transport-close events.
8. `ConnectionContinuityCoordinator` may bind a replacement `ConnectionId` back to the same stable `PeerId` when a valid resume credential is presented.

The relay itself never stores `PeerId` or resume credentials. A replacement physical connection gets a new transient `ConnectionId`; neutral continuity is re-established above the relay with the normal Dihor.GameKit.Networking protocol-v2 resume flow.

The implementation does not enable SignalR automatic reconnect as a substitute for Dihor.GameKit.Networking continuity. Transport recovery must not silently invent application/product identity.

## Backend availability and failure

SignalR is optional. No LAN project depends on the SignalR package, and no direct-LAN flow requires Internet or a deployed relay.

If the relay connection disappears unexpectedly:

- active relay-backed `ConnectionId` values close with transport failure semantics;
- a `TransportFaulted` event is published;
- the relay transport becomes unavailable for sends;
- the consumer may establish a new transport and use protocol-v2 resume when appropriate.

A relay restart loses the in-memory routing registry by design. This is not data loss from Dihor.GameKit.Networking's perspective because the relay does not own product/session state. Consumers that need durable party/game state must persist it in their own application layer.

## Payload limits and cleanup

Both listener and client options expose `MaxMessageBytes` (default 256 KiB).

- outbound Dihor.GameKit.Networking messages larger than the configured local limit are rejected before send;
- server methods validate decoded raw payload size;
- if a client receives a payload larger than its own local limit, it reports `message-too-large` and closes the physical SignalR connection;
- physical disconnect removes the ephemeral backend binding and the listener receives a close event.

This keeps relay state bounded by live communication connections rather than stale logical participants.

## Security assumptions

The relay package provides transport infrastructure, not a complete Internet-facing security product. A production deployment should:

- use TLS (`https`, and therefore secure SignalR WebSocket transport) outside trusted development networks;
- authenticate callers before exposing relay methods publicly;
- authorize which listener/client may use a routing scope;
- treat `ChannelId` as routing metadata, never as a bearer secret;
- enforce appropriate ASP.NET Core request/rate/concurrency limits for the deployment;
- keep Dihor.GameKit.Networking payload limits appropriate for expected traffic;
- validate/decode application payloads in the consumer, because they remain opaque to the relay;
- avoid placing secrets in application payloads unless the product's own threat model and encryption/authentication scheme allow it.

The package does not currently provide database/Redis-backed relay state, distributed channel coordination, quotas, account management or matchmaking. Those are separate deployment/product concerns and must not be inferred from the transport API.

## Validation

Integration tests run a real in-process ASP.NET Core/Kestrel SignalR endpoint and prove:

- multiple generic clients;
- listener/client connection and disconnect events;
- client-to-listener opaque payloads;
- targeted listener-to-client delivery;
- channel-local broadcast;
- channel isolation;
- protocol-v1 mismatch rejection before connection exposure;
- replacement transport connections participate in the same neutral protocol-v2 resume flow;
- client-side payload-limit rejection cleans the backend binding;
- clean server/client disposal.

`samples/CommunicationDemo` runs the same consumer-owned application-message scenario over direct LAN WebSocket and SignalR without changing the application payload schema.
