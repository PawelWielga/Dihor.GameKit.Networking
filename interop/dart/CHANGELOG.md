# Changelog

## 0.2.0-preview.1

- Moves Dart interoperability to PartyGameKit protocol v2.
- Replaces player/session vocabulary with neutral peer/connection semantics.
- Adds v2 connect/resume, heartbeat and opaque application-message fixture coverage.
- Replaces `JoinDescriptor` with technical `ConnectionDescriptor` JSON and URI support.
- Replaces snapshot-specific ordering terminology with a generic message sequence gate.
- Keeps the package protocol-only: no Dart transport, player model or game/session engine.

## 0.1.0-preview.1

- Adds PartyGameKit protocol v1 envelope parsing.
- Adds canonical join-descriptor JSON and URI support.
- Adds snapshot sequence gating and discovery-announcement parsing.
- Validates Dart behavior against the repository's shared canonical protocol fixtures.

This package covers the language-neutral protocol surface only. It does not provide a Dart LAN transport or game/session engine.
