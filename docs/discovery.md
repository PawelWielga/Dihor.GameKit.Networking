# LAN discovery and join descriptors

Discovery is a convenience layer, not a gameplay transport. PartyGameKit can connect directly from a valid join descriptor even when UDP discovery is blocked or disabled.

## JoinDescriptor

`JoinDescriptor` is the portable connection contract. Core owns only generic data:

- stable `RoomId`;
- `JoinCode`;
- transport name;
- absolute endpoint URI as data;
- protocol version.

Core does not import WebSocket, IP address, port or UDP types. `PartyGameKit.Transport.Lan` provides `LanJoinDescriptor.Create(...)` and `GetEndpointUri(...)` for the `lan-websocket` transport.

The descriptor has three deterministic representations:

- canonical compact JSON for cross-language fixtures;
- `partygamekit://join?...` URI for QR/deep-link payloads;
- text form, currently identical to the URI form. The parser also accepts canonical JSON for paste/manual workflows.

QR image rendering belongs to product/UI code. PartyGameKit provides only the payload.

## UDP discovery

`PartyGameKit.Discovery.Lan` implements periodic IPv4 UDP announcements. The wire packet contains only:

- discovery message type and protocol version;
- the portable join descriptor.

No game state, commands, answers, scores or private player data travel over discovery.

The default discovery port is `45678` and the default announce interval is one second. The advertiser computes actual IPv4 subnet broadcast addresses from interface netmasks where available and keeps `255.255.255.255` as a fallback. Tests can supply explicit unicast targets such as loopback.

The listener maintains `DiscoveredSessionRegistry`, keyed by stable `RoomId`. Repeated announcements refresh `LastSeenAt` rather than creating duplicates. The default TTL is three seconds; expired sessions are removed independently of the WebSocket transport.

## Manual/direct fallback

A manual workflow may construct the same LAN descriptor from known room identity, join code, host/address, port and path. It then connects through `[08]` exactly like a discovered descriptor.

This is deliberately independent of discovery health. UDP broadcast may be filtered by guest Wi-Fi, AP isolation, OS permissions or local firewall rules while a direct WebSocket endpoint remains reachable.

## Security

Discovery advertisements are unauthenticated hints on a trusted LAN. A malicious local peer can spoof them. The authoritative WebSocket/session handshake remains responsible for validating protocol, room, capacity and reconnect credentials.

Do not place reconnect tokens, private state or secrets in discovery announcements or QR descriptors.
