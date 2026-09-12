# PartyGameKit Dart protocol conformance

This directory is the minimum Dart implementation required by the v0.1 cross-language decision.

It is intentionally **not** a Dart port of PartyGameKit Core and does not contain any game engine. It only models the stable language-neutral boundary needed to prove that Dart can consume PartyGameKit's canonical v1 JSON fixtures:

- protocol envelope/version admission;
- stable player versus transient connection identity fields;
- reconnect metadata;
- snapshot sequence ordering;
- portable `JoinDescriptor` JSON and URI forms;
- LAN discovery announcement descriptor parsing.

Tests read `../../protocol/fixtures/*.json` directly. No fixture copy lives in this package, so C# and Dart conformance cannot silently drift to different vectors.

Run from this directory:

```bash
dart pub get
dart format --set-exit-if-changed .
dart analyze
dart test
```

The existing `PawelWielga/panstwa-miasta` application keeps its own historic LAN protocol and game-specific snapshot schema. Its compatibility PR validates the reusable behavior against this contract without moving Countries & Cities rules into PartyGameKit.
