# @partygamekit/client

Browser-first TypeScript client for PartyGameKit protocol v2 and the communication-only `0.2` boundary.

The SDK is intentionally neutral. It connects generic peers and does not define players, hosts, shared screens, lobbies, authority or game-state projections. PartyBeam, Państwa Miasta and other products build those concepts above this package.

## Protocol-v2 WebSocket client

```ts
import {
  PartyGameClient,
  parseConnectionDescriptor,
} from "@partygamekit/client";

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
- raw message events for frames outside PartyGameKit's protocol envelope.

The built-in WebSocket path supports `lan-websocket` descriptors using `ws://` or `wss://` endpoints.

## WebRTC DataChannel

`0.2.0-preview.3` adds a browser-native peer-to-peer path based on `RTCPeerConnection` and `RTCDataChannel`.

The DataChannel carries opaque binary data directly between browser peers. Signaling is separate and is used only to exchange SDP/ICE negotiation data.

```ts
import {
  SignalRWebRtcSignalingClient,
  WebRtcPeer,
} from "@partygamekit/client";

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

A peer already present in the technical signaling channel can initiate toward a newly joined connection using `onPeerJoined(...)`. The product decides which transient connections should negotiate with each other; PartyGameKit does not impose a host/player/controller topology.

### Profiles

`reliable` creates an ordered reliable DataChannel. When the configured `bufferedAmount` limit would be exceeded, `send(...)` throws `WebRtcBackpressureError` rather than creating an unbounded PartyGameKit queue.

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

## SignalR signaling

The optional `SignalRWebRtcSignalingClient` uses the MIT-licensed `@microsoft/signalr` package. A compatible ASP.NET Core endpoint is provided by `PartyGameKit.Transport.SignalR.Server`:

```csharp
builder.Services.AddPartyGameKitWebRtcSignaling();

var app = builder.Build();
app.MapPartyGameKitWebRtcSignaling();
```

The signaling service stores only ephemeral technical channel membership and routes target SDP/ICE JSON. Normal WebRTC application payloads do **not** pass through SignalR.

For public deployments, use ordinary ASP.NET Core/SignalR authentication and TLS. `ChannelId` is routing metadata, not an authorization secret.

## Ownership boundary

A product may decide that a peer is a TV, controller, player, spectator, server process or something else. Those meanings are not encoded by `@partygamekit/client`.

`0.1.0-preview.1` exposed `player` / `shared-screen`, join/rejoin and snapshot-projection APIs. Those preview APIs were intentionally removed for `0.2`; see the repository migration guide for the compiler-level mapping.
