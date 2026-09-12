# @partygamekit/client

Browser-first TypeScript client for PartyGameKit protocol v1. It is intended for PartyBeam phone controllers and shared TV/browser screens, but contains no PartyBeam game logic and no React dependency.

## Minimal API

```ts
import { PartyGameClient, parseJoinDescriptor } from "@partygamekit/client";

const client = new PartyGameClient({ role: "player" });
client.on("snapshot", (snapshot) => {
  // Public snapshots and this player's private snapshots arrive here.
});

await client.join(parseJoinDescriptor(scannedQrPayload));
```

Use `role: "shared-screen"` for a browser/TV that should receive only public projections. Player identity and reconnect credentials are kept in `localStorage` when it is available; reconnect uses a new WebSocket while preserving the stable player identity. `leave()` sends the protocol leave message and closes intentionally.

The package has no runtime dependencies. It accepts the canonical `partygamekit://join` URI or JSON join descriptor and connects to `lan-websocket` `ws://`/`wss://` endpoints. Host-to-client binary JSON frames are decoded automatically. Non-protocol frames are surfaced through the `message` event for game-specific payloads.
