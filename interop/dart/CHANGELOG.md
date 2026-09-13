# Changelog

## 0.2.0-preview.4

- Aligns the Dart protocol package version with the PartyGameKit preview.4 release line.
- Keeps the protocol-v2 envelope, connection descriptor and canonical fixture contract unchanged.
- Automatic transport selection is a runtime orchestration capability in the .NET and TypeScript surfaces; this Dart package remains protocol-only.
- The package does not claim a Dart LAN, SignalR, WebRTC or automatic transport runtime.

## 0.2.0-preview.3

- Aligns the Dart protocol package version with the PartyGameKit preview.3 release line.
- Keeps the protocol-v2 envelope, connection descriptor and canonical fixture contract unchanged.
- The package remains protocol-only and does not claim a Dart WebRTC runtime or SignalR transport.
- Browser-native WebRTC and optional SignalR SDP/ICE signaling are runtime capabilities outside the Dart protocol package.

## 0.2.0-preview.2

- Aligns the Dart protocol package version with the PartyGameKit preview.2 release line.
- Keeps the protocol-v2 envelope, connection descriptor and fixture contract unchanged from preview.1.
- The package remains protocol-only; the SignalR relay transport is implemented in the .NET transport packages and is not duplicated in Dart.

## 0.2.0-preview.1

- Moves Dart interoperability to PartyGameKit protocol v2.
- Replaces player/session vocabulary with neutral peer/connection semantics.
- Adds v2 connect/resume, heartbeat and opaque application-message fixture coverage.
- Replaces `JoinDescriptor` with technical `ConnectionDescriptor` JSON and URI support.
- Replaces snapshot-specific ordering terminology with a generic message sequence gate.
- Keeps the package protocol-only: no Dart transport, player model or game/session engine.

## 0.1.0-preview.1

- Adds PartyGameKit protocol v1 envelope parsing.
- Adds canonical join-descriptor JSON/URI support.
- Adds snapshot sequence gating and discovery-announcement parsing.
- Validates Dart behavior against the repository's shared canonical protocol fixtures.

This package covers the language-neutral protocol surface only. It does not provide a Dart LAN/SignalR/WebRTC transport or game/session engine.
