# Changelog

## 0.2.0-preview.7

- Aligns the Dart LAN runtime with the preview.7 compatibility line.
- Added stable Dart peer identity and resume-credential storage.
- Added protocol-v2 resume, heartbeat and explicit disconnect control flow.
- Added manual bounded/cancellable reconnect with replacement ConnectionId semantics.
- Added reconnect timeout/cancellation/network-drop tests and real Dart-to-.NET resume interoperability.
- Keeps protocol v2 wire compatibility unchanged.

## 0.2.0-preview.6

- Added the first Dart runtime package for Dihor.GameKit.Networking.
- Added transport-neutral client abstractions.
- Added direct LAN WebSocket connectivity on Dart VM / Flutter mobile and desktop.
- Added protocol-v2 connect handshake and opaque application message send/receive.
- Added real Dart-to-.NET LAN interoperability validation.
