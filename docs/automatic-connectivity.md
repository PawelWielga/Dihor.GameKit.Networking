# Automatic connectivity

PartyGameKit `0.2.0-preview.4` adds a communication-only automatic connectivity policy. It chooses which registered client transport should establish a connection without interpreting players, hosts, parties, game sessions, authority or application state.

## Default policy

`ConnectivityMode.Auto` uses this deterministic preference order by default:

1. `lan-websocket`;
2. `webrtc-datachannel`;
3. `signalr-relay`.

Only candidates registered by the current runtime can actually connect. A missing candidate is recorded as `Unavailable` and the selector continues to the next candidate. This matters because runtime capabilities are intentionally different: .NET has LAN and SignalR clients, while the browser SDK also has native WebRTC.

The order is configurable. Each attempt has an explicit timeout budget, defaulting to three seconds. A candidate can override that budget for its own attempt.

There is no retry loop inside one selection operation. Every candidate is considered at most once, in deterministic order.

## What causes fallback

Fallback is limited to connection establishment and reconnect attempts:

- candidate connection failure;
- candidate timeout;
- candidate unavailable in the current runtime.

PartyGameKit does **not** silently reinterpret an application-level failure, rejected application message or game error as a reason to switch transports. The consumer remains responsible for its own application semantics.

Caller cancellation stops the selection immediately and does not continue to another candidate. If a candidate ignores cancellation and succeeds later, PartyGameKit disposes that abandoned connection when the candidate supplies the required cleanup hook or implements the .NET client contract.

## Reconnect

`ReconnectAsync` / `reconnect(...)` remembers the most recently successful transport. In `Auto` mode it attempts that transport first before considering the normal configured order.

This prevents unnecessary migration when the previous path still works. If that path is no longer available, fallback proceeds deterministically through the remaining candidates.

The selector treats connect and resume handshakes as opaque values. Stable `PeerId` and resume-token semantics remain in the neutral protocol/continuity layer, so changing the transport does not change logical communication identity.

## Diagnostics

Every completed selection exposes structured diagnostics containing:

- every candidate considered in order;
- `Selected`, `Failed`, `TimedOut` or `Unavailable` outcome;
- elapsed attempt duration;
- failure/error text when applicable;
- selected transport;
- preferred transport during reconnect;
- whether reconnect reused the preferred transport.

If no candidate succeeds, `ConnectivitySelectionException` in .NET and `ConnectivitySelectionError` in TypeScript carry the complete attempt diagnostics.

## .NET

The transport-neutral client surface is `IMessageTransportClient`:

```csharp
var selector = new AutomaticTransportSelector(
    [
        new TransportClientCandidate(
            TransportIds.LanWebSocket,
            async (handshake, cancellationToken) =>
                await LanWebSocketClient.ConnectAsync(
                    lanEndpoint,
                    handshake,
                    cancellationToken: cancellationToken)),
        new TransportClientCandidate(
            TransportIds.SignalRRelay,
            async (handshake, cancellationToken) =>
                await SignalRRelayClient.ConnectAsync(
                    relayOptions,
                    handshake,
                    cancellationToken)),
    ]);

await using var client = await selector.ConnectAsync(
    connectHandshake,
    ConnectivityMode.Auto,
    cancellationToken);

await client.SendAsync(opaquePayload, cancellationToken);
```

A consumer can force `ConnectivityMode.Lan`, `ConnectivityMode.WebRtc` or `ConnectivityMode.SignalR`. Forcing an unregistered transport produces a normal structured terminal selection failure rather than silently selecting another path.

`LanWebSocketClient` and `SignalRRelayClient` implement `IMessageTransportClient` while retaining their transport-specific public APIs for callers that intentionally select them directly.

## TypeScript/browser

The browser SDK exposes the same orchestration policy as a generic selector. Candidates can wrap `PartyGameClient`, `WebRtcPeer`, SignalR relay integration or another communication adapter without teaching the selector product semantics.

```ts
const selector = new AutomaticTransportSelector<string, MyConnection>([
  {
    transportId: connectivityTransportIds.lanWebSocket,
    connect: (handshake, signal) => connectLan(handshake, signal),
    disposeLateConnection: (connection) => connection.close(),
  },
  {
    transportId: connectivityTransportIds.webRtcDataChannel,
    connect: (handshake, signal) => connectWebRtc(handshake, signal),
    disposeLateConnection: (connection) => connection.close(),
  },
  {
    transportId: connectivityTransportIds.signalRRelay,
    connect: (handshake, signal) => connectRelay(handshake, signal),
    disposeLateConnection: (connection) => connection.close(),
  },
]);

const selected = await selector.connect(connectHandshake, "auto", abortSignal);
```

The TypeScript selector is generic on connection/context types so it does not impose a second application/session model on the existing browser clients.

## Neutral validation sample

`samples/AutoConnectivityDemo` requests `ConnectivityMode.Auto`, receives an `IMessageTransportClient`, exchanges a protocol-v2 connection handshake and sends an opaque `application.message` payload. The scenario verifies that the returned application bytes are unchanged and that the stable `PeerId` remains intact.

The concrete LAN client appears only in the composition root where candidates are registered. The communication scenario itself does not depend on `LanWebSocketClient`, `SignalRRelayClient` or a product role.

CI runs this sample together with the existing LAN/SignalR communication demo and browser WebRTC validation.

## Boundary

Automatic connectivity answers one question only:

> Which available transport should carry messages now?

It does not decide who is a player, who is host/authority, whether a participant retains a slot, whether a game pauses or ends, how state migrates, or how PartyBeam should present fallback diagnostics. Those remain consumer responsibilities.
