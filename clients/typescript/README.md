# @dihor/gamekit-networking

Browser-first TypeScript client for Dihor.GameKit.Networking protocol v2 and the communication-only `0.2` boundary.

The SDK is intentionally neutral. It connects generic peers and does not define players, hosts, shared screens, lobbies, authority or game-state projections. PartyBeam, Państwa Miasta and other products build those concepts above this package.

## Protocol-v2 WebSocket client

```ts
import {
  PartyGameClient,
  parseConnectionDescriptor,
} from "@dihor/gamekit-networking";

const client = new PartyGameClient();

client.on("applicationMessage", (message) => {
  console.log(message.applicationType, message.data);
});

await client.connect(parseConnectionDescriptor(connectionPayload));
client.send("my-app.command", { value: 42 });
```

The protocol-v2 client provides:

- neutral connect/disconnect state;
- optional stable `PeerId` persistence;
- resume credentials and automatic reconnect on a replacement WebSocket;
- protocol-v2 admission and compatibility checks;
- `partygamekit://connect` / JSON `ConnectionDescriptor` parsing;
- `application.message` delivery with consumer-owned data;
- heartbeat support;
- raw message events for frames outside Dihor.GameKit.Networking's protocol envelope.

The built-in WebSocket path supports `lan-websocket` descriptors using `ws://` or `wss://` endpoints.

## Transient latest-value replay

`LatestValueReplayBuffer<TMessage>` keeps at most one newest replayable value per caller-owned key and scope/epoch. It is separate from heartbeat and from the automatic transport selector.

For protocol-v2 WebSocket traffic, stage the already serialized `application.message` so a reconnect retry reuses the same `messageId`:

```ts
const replay = new LatestValueReplayBuffer<string>();

const wire = serializeMessage(createMessage(
  messageTypes.applicationMessage,
  "draft-value-7",
  { applicationType: "my-app.draft", data: { value: "latest" } },
));

await replay.stageLatest("draft", "interaction-42", wire);

const sender = {
  send: (message: string) => client.sendRaw(message),
};

await replay.bindSender(sender);
```

Call `replay.unbindSender(sender)` as soon as reconnect begins. Values staged while unbound do not touch the stale connection. Binding a replacement sender immediately replays the newest still-valid value.

A locally successful `send` is not treated as a receiver acknowledgement, so the latest value remains staged until `clearLatest` / `invalidateScope` retires it. A new logical value should use a new `messageId`; replay of that same staged value should reuse its existing serialized message. This is not exactly-once delivery. Receiver-side bounded deduplication is tracked separately in issue [25].

## WebRTC DataChannel

`0.2.0-preview.3` adds a browser-native peer-to-peer path based on `RTCPeerConnection` and `RTCDataChannel`.

The DataChannel carries opaque binary data directly between browser peers. Signaling is separate and is used only to exchange SDP/ICE negotiation data.

```ts
import {
  SignalRWebRtcSignalingClient,
  WebRtcPeer,
} from "@dihor/gamekit-networking";

const signaling = new SignalRWebRtcSignalingClient({
  endpoint: "https://example.test/partygamekit-webrtc-signaling",
  channelId: "technical-scope-a",
});

const registration = await signaling.connect();

for (const remoteConnectionId of registration.existingConnectionIds) {
  const peer = new WebRtcPeer(signaling.createChannel(remoteConnectionId), {
    initiator: true,
    profile: "low-latency",
  });

  peer.onMessage((bytes) => {
    console.log("peer payload", bytes);
  });

  await peer.connect();
  peer.send(new Uint8Array([1, 2, 3]));
}
```

A peer already present in the technical signaling channel can initiate toward a newly joined connection using `onPeerJoined(...)`. The product decides which transient connections should negotiate with each other; Dihor.GameKit.Networking does not impose a host/player/controller topology.

### Profiles

`reliable` creates an ordered reliable DataChannel. When the configured `bufferedAmount` limit would be exceeded, `send(...)` throws `WebRtcBackpressureError` rather than creating an unbounded Dihor.GameKit.Networking queue.

`low-latency` creates an unordered DataChannel with `maxRetransmits: 0`. Its default overflow policy drops the newest payload and increments `droppedMessageCount`, which is useful for high-frequency data where stale queued messages are worse than a dropped update.

Both profiles can be configured through `WebRtcPeerOptions`.

### Diagnostics

`sampleDiagnostics()` reports communication-only information:

- current peer state;
- DataChannel buffered bytes;
- dropped-message count;
- observed WebRTC candidate-pair round-trip time when available;
- variation from the previous RTT sample.

No game/player semantics are attached to these diagnostics.

## Automatic connectivity

`0.2.0-preview.4` adds `AutomaticTransportSelector<TContext, TConnection>` for deterministic runtime-specific path selection. The built-in transport identifiers use this default order:

1. `lan-websocket`;
2. `webrtc-datachannel`;
3. `signalr-relay`.

The selector is generic because the concrete browser connection objects are intentionally different. Register candidates in the composition root and keep application code dependent only on the selected connection abstraction you choose.

```ts
import {
  AutomaticTransportSelector,
  connectivityTransportIds,
} from "@dihor/gamekit-networking";

const selector = new AutomaticTransportSelector([
  {
    transportId: connectivityTransportIds.lanWebSocket,
    connect: (context, signal) => connectLan(context, signal),
    disposeLateConnection: (connection) => connection.close(),
  },
  {
    transportId: connectivityTransportIds.webRtcDataChannel,
    connect: (context, signal) => connectWebRtc(context, signal),
    disposeLateConnection: (connection) => connection.close(),
  },
  {
    transportId: connectivityTransportIds.signalRRelay,
    connect: (context, signal) => connectRelay(context, signal),
    disposeLateConnection: (connection) => connection.close(),
  },
], {
  attemptTimeoutMs: 3_000,
});

const selected = await selector.connect(connectContext, "auto", abortSignal);
console.log(selected.transportId, selected.diagnostics.attempts);
```

Each candidate is attempted at most once per selection call. Timeout/failure/unavailable outcomes are observable. `reconnect(...)` prefers the previously successful path first. Forced `"lan"`, `"webrtc"` and `"signalr"` modes never silently select another transport.

Caller cancellation stops fallback immediately. If a candidate ignores `AbortSignal` and later succeeds, its `disposeLateConnection` hook is used to clean up the abandoned connection.

The selector does not turn application-level errors into transport fallback and does not interpret the context or payloads it is given.

## SignalR signaling

The optional `SignalRWebRtcSignalingClient` uses the MIT-licensed `@microsoft/signalr` package. A compatible ASP.NET Core endpoint is provided by `Dihor.GameKit.Networking.Transport.SignalR.Server`:

```csharp
builder.Services.AddDihorGameKitNetworkingWebRtcSignaling();

var app = builder.Build();
app.MapDihorGameKitNetworkingWebRtcSignaling();
```

The signaling service stores only ephemeral technical channel membership and routes target SDP/ICE JSON. Normal WebRTC application payloads do **not** pass through SignalR.

For public deployments, use ordinary ASP.NET Core/SignalR authentication and TLS. `ChannelId` is routing metadata, not an authorization secret.

## Ownership boundary

A product may decide that a peer is a TV, controller, player, spectator, server process or something else. Those meanings are not encoded by `@dihor/gamekit-networking`.

`0.1.0-preview.1` exposed `player` / `shared-screen`, join/rejoin and snapshot-projection APIs. Those preview APIs were intentionally removed for `0.2`; see the repository migration guide for the compiler-level mapping.
