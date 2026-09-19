# dihor_gamekit_networking_protocol

`dihor_gamekit_networking_protocol` is the small Dart implementation of Dihor.GameKit.Networking's language-neutral protocol v2 boundary.

It exists for Flutter/Dart consumers that need wire compatibility without importing a duplicate game/session runtime. It deliberately models communication primitives only.

It covers:

- protocol envelope/version admission;
- neutral stable `peerId` versus transient `connectionId` semantics;
- connect/resume and resume-token fields;
- opaque `application.message` payloads;
- generic message sequence gating;
- portable `ConnectionDescriptor` JSON and `partygamekit://connect` URI forms;
- LAN discovery announcement parsing.

It deliberately does **not** contain a Dart transport, Flutter UI, player model, party/session engine or game state. The separate `clients/dart` package (`dihor_gamekit_networking`) builds on this contract and provides the first-class Dart VM LAN WebSocket runtime.

## Preview consumption

The `0.2.0-preview.1` package remains `publish_to: none`. After the matching Git tag exists it can be consumed directly from this repository:

```yaml
dependencies:
  dihor_gamekit_networking_protocol:
    git:
      url: https://github.com/PawelWielga/Dihor.GameKit.Networking.git
      ref: v0.2.0-preview.1
      path: interop/dart
```

## Validation

Tests read the repository's `protocol/fixtures/v2-*.json` files directly, so C#, Dart and TypeScript validate the same communication contract.

From this directory:

```bash
dart pub get
dart format .
dart analyze
dart test
```

Państwa Miasta remains free to keep its own player identity, host-authoritative game model and game snapshots above these primitives. Dihor.GameKit.Networking does not require those concepts to move into this package.
