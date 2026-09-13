# @partygamekit/client

Browser-first TypeScript client for PartyGameKit protocol v2 and the communication-only `0.2` boundary.

The SDK is intentionally neutral. It connects a generic peer to PartyGameKit transport infrastructure and does not define players, hosts, shared screens, lobbies, authority or game-state projections. PartyBeam, Państwa Miasta and other products build those concepts above this package.

## Minimal API

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
client.sendApplicationMessage("my-app.command", { value: 42 });
```

The client provides:

- neutral connect/disconnect state;
- optional stable `PeerId` persistence;
- resume credentials and automatic reconnect on a replacement WebSocket;
- protocol-v2 admission and compatibility checks;
- `partygamekit://connect` / JSON `ConnectionDescriptor` parsing;
- `application.message` delivery with consumer-owned data;
- heartbeat support;
- raw message events for frames outside PartyGameKit's protocol envelope.

The package has no runtime dependencies. The built-in browser transport supports `lan-websocket` descriptors using `ws://` or `wss://` endpoints.

## Ownership boundary

A product may decide that a peer is a TV, controller, player, spectator, server process or something else. Those meanings are not encoded by `@partygamekit/client`.

`0.1.0-preview.1` exposed `player` / `shared-screen`, join/rejoin and snapshot-projection APIs. Those preview APIs were intentionally removed for `0.2.0-preview.1`; see the repository migration guide for the compiler-level mapping.
