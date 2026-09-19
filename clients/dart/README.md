# dihor_gamekit_networking

First-class Dart/Dart VM runtime for Dihor.GameKit.Networking.

The package builds on the language-neutral `dihor_gamekit_networking_protocol` package and adds real networking behavior without introducing player, lobby, host, round or game-state concepts.

## Current capabilities

The `[27]` runtime supports:

- a transport-neutral client abstraction;
- direct LAN WebSocket connectivity through `dart:io`;
- `ConnectionDescriptor` validation and consumption;
- protocol-v2 connect handshake;
- optional neutral stable `peerId` on initial connect;
- sending and receiving opaque `application.message` values;
- observable connection state, remote close and transport/protocol errors;
- configurable connection/handshake timeouts and message-size limit.

The LAN implementation is intended for Dart VM and Flutter mobile/desktop platforms where `dart:io` WebSocket is available. Browser Dart needs a browser-specific transport implementation.

Reconnect/resume, heartbeat and stable identity persistence are intentionally tracked separately in `[28]` / issue #57.

## Connect over LAN

```dart
import 'package:dihor_gamekit_networking/dihor_gamekit_networking.dart';

final descriptor =
    DihorGameKitNetworkingConnectionDescriptor.parseUri(connectionUri);

final client = await DihorGameKitNetworkingClient.connectLan(
  descriptor,
  peerId: 'device-a',
);

client.applicationMessages.listen((message) {
  print('${message.applicationType}: ${message.data}');
});

await client.sendApplicationMessage(
  'consumer.command',
  <String, Object?>{'value': 42},
);

await client.close();
```

The application payload remains consumer-owned. The runtime validates only the Dihor.GameKit.Networking envelope and transport semantics.

## Raw transport

Consumers that need a lower-level adapter can use `DihorGameKitNetworkingLanWebSocketTransport` through the neutral `DihorGameKitNetworkingClientTransport` interface.

The initial protocol handshake is sent as a WebSocket text message for compatibility with the .NET LAN listener. Normal opaque transport messages may be text or binary; the high-level client serializes protocol-v2 envelopes as UTF-8 text.

## Validation

From this directory:

```bash
dart pub get
dart format .
dart analyze
dart test
```

Repository CI additionally starts the real .NET `LanWebSocketTransport` and connects this Dart runtime to it. The cross-runtime check validates protocol-v2 admission and opaque application message round-trip.

## Boundary

This package does not define:

- player identity or admission;
- host/controller/shared-screen roles;
- lobby or game-session lifecycle;
- game commands, rounds, scoring or state;
- Flutter UI lifecycle policy.

Those remain consumer responsibilities.
