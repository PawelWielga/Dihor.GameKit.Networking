# Changelog

## 0.2.0-preview.2 - 2026-09-13

- Aligns the browser package version with the PartyGameKit preview.2 release line.
- Keeps protocol v2 and the preview.1 browser API unchanged.
- The new SignalR relay transport is provided by the .NET transport packages; this release does not claim a browser SignalR transport implementation.

## 0.2.0-preview.1 - 2026-09-13

- Moves the browser SDK to PartyGameKit protocol v2.
- Replaces player/shared-screen roles with neutral peer identity.
- Replaces join/rejoin vocabulary with connect/resume.
- Replaces room/join descriptors with technical `ConnectionDescriptor` JSON and `partygamekit://connect` URI forms.
- Replaces public/private game snapshot handling with opaque consumer-owned `application.message` delivery.
- Keeps heartbeat, reconnect and local identity persistence as communication concerns.
- Removes the v0.1 snapshot projection helper from the base SDK.

## 0.1.0-preview.1 - 2026-09-12

- PartyGameKit protocol-v1 browser models and validation.
- LAN WebSocket join/rejoin/leave flow.
- Stable player identity and reconnect credential persistence.
- Heartbeat and automatic reconnect.
- Shared-screen and player roles.
- Public/private snapshot filtering and per-target stale snapshot rejection.
- Canonical join descriptor JSON/URI compatibility with C# and Dart.
