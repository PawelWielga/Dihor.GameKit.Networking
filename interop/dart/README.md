# partygamekit_protocol

`partygamekit_protocol` is the small Dart implementation of PartyGameKit's language-neutral protocol v1 boundary. It is intended for Flutter/Dart consumers that need compatible infrastructure messages and portable join descriptors without copying protocol code from another game.

It models:

- protocol envelope/version admission;
- stable player versus transient connection identity fields;
- reconnect metadata;
- snapshot sequence ordering;
- portable `JoinDescriptor` JSON and URI forms;
- LAN discovery announcement descriptor parsing.

It deliberately does **not** contain a Dart transport, Flutter UI or game engine.

## Preview consumption

The `0.1.0-preview.1` package remains `publish_to: none`. After the matching Git tag exists it can be consumed directly from this repository:

```yaml
dependencies:
  partygamekit_protocol:
    git:
      url: https://github.com/PawelWielga/PartyGameKit.git
      ref: v0.1.0-preview.1
      path: interop/dart
```

## Validation

Tests read `../../protocol/fixtures/*.json` directly, so C#, Dart and TypeScript share one set of compatibility vectors.

From this directory:

```bash
dart pub get
dart format .
dart analyze
dart test
```

The existing Państwa Miasta application keeps its historic LAN protocol and game-specific snapshot schema. PartyGameKit compatibility is validated at the stable identity/reconnect/snapshot boundary rather than by replacing that application's wire protocol.
