# dihor_gamekit_networking

First-class Dart/Dart VM runtime for Dihor.GameKit.Networking.

The package builds on the language-neutral `dihor_gamekit_networking_protocol` package and adds real networking behavior without introducing player, lobby, host, round or game-state concepts.

## Current capabilities

The current `[27]` + `[28]` Dart runtime supports:

- a transport-neutral client abstraction;
- direct LAN WebSocket connectivity through `dart:io`;
- `ConnectionDescriptor` validation and consumption;
- protocol-v2 connect handshake;
- optional neutral stable `peerId` on initial connect;
- sending and receiving opaque `application.message` values;
- observable connection state, remote close and transport/protocol errors;
- configurable connection/handshake timeouts and message-size limit;
- neutral in-memory identity/resume-credential storage with a pluggable persistence interface;
- protocol-v2 heartbeat and explicit disconnect control messages;
- manual bounded reconnect/resume on a replacement connection;
- cancellation of connection/reconnect attempts without leaking late sockets.

The LAN implementation is intended for Dart VM and Flutter mobile/desktop platforms where `dart:io` WebSocket is available. Browser Dart needs a browser-specific transport implementation.

`[28]` adds neutral connection continuity: optional stable `PeerId`, resume credentials, heartbeat, explicit disconnect and bounded/cancellable reconnect. The library never puts application payloads into heartbeat and does not automatically decide when a Flutter app should reconnect.

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

## Reconnect and resume

A stable peer can keep a resume credential in a `DihorGameKitNetworkingIdentityStore`. The default `MemoryDihorGameKitNetworkingIdentityStore` survives transport replacement while the client/process remains alive; applications that need restart persistence can provide their own store implementation.

```dart
final store = MemoryDihorGameKitNetworkingIdentityStore();

final client = await DihorGameKitNetworkingClient.connectLan(
  descriptor,
  peerId: 'device-a',
  identityStore: store,
);

final replacement = await client.reconnect(
  policy: DihorGameKitNetworkingReconnectPolicy(
    maxAttempts: 3,
    delay: const Duration(milliseconds: 500),
  ),
);

print(replacement.connectionId); // new transient connection
```

`connectLan` also resumes automatically when the supplied identity store already contains a matching resume credential. This supports application-owned persistence without adding Flutter UI/lifecycle policy to the networking package.

Reconnect is **manual policy**: a network drop moves the client to `closed`; the consumer decides if and when to call `reconnect`. The reconnect operation itself is bounded by max attempts, transport/handshake timeouts and optional `DihorGameKitNetworkingCancellationSignal`.

Heartbeat carries only the optional stable `peerId`. It never carries transient application replay/state. A successful local send or resume still does not imply exactly-once delivery.

```dart
final cancellation = DihorGameKitNetworkingCancellationSignal();

final reconnect = client.reconnect(
  policy: DihorGameKitNetworkingReconnectPolicy(maxAttempts: 5),
  cancellation: cancellation,
);

// Consumer policy can cancel the attempt at any time.
cancellation.cancel();
```

Use `disconnect(reason: ...)` for an explicit protocol-v2 disconnect while keeping the resume credential, or pass `clearResumeCredential: true` when the old continuity identity must be retired.

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

Repository CI additionally starts the real .NET `LanWebSocketTransport` and connects this Dart runtime to it. The cross-runtime check validates protocol-v2 admission, heartbeat, forced transport drop, resume on a replacement connection, opaque application traffic after resume and explicit disconnect.

## Boundary

This package does not define:

- player identity or admission;
- host/controller/shared-screen roles;
- lobby or game-session lifecycle;
- game commands, rounds, scoring or state;
- Flutter UI lifecycle policy.

Those remain consumer responsibilities.
