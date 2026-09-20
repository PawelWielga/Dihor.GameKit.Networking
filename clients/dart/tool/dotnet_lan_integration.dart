import 'dart:async';
import 'dart:io';

import 'package:dihor_gamekit_networking/dihor_gamekit_networking.dart';

Future<void> main(List<String> args) async {
  if (args.length != 1 || args.single.trim().isEmpty) {
    throw ArgumentError('Expected one .NET descriptor file path.');
  }

  final descriptorText = await File(args.single).readAsString();
  final descriptor = DihorGameKitNetworkingConnectionDescriptor.parseUri(
    descriptorText.trim(),
  );

  final identityStore = MemoryDihorGameKitNetworkingIdentityStore();
  var sequence = 0;
  final client = await DihorGameKitNetworkingClient.connectLan(
    descriptor,
    peerId: 'dart-interop-peer',
    identityStore: identityStore,
    heartbeatInterval: const Duration(milliseconds: 50),
    messageIdFactory: () => 'dart-interop-${++sequence}',
  );

  final applications =
      StreamIterator<DihorGameKitNetworkingApplicationMessage>(
    client.applicationMessages,
  );

  try {
    if (client.activeConnection?.peerId != 'dart-interop-peer') {
      throw StateError('Stable Dart peer identity was not preserved.');
    }
    final initialConnectionId = client.activeConnection!.connectionId;

    final closed = client.stateChanges.firstWhere(
      (state) => state == DihorGameKitNetworkingConnectionState.closed,
    );

    final beforeMessageId = await client.sendApplicationMessage(
      'interop.dart.before-resume',
      <String, Object?>{'value': 42, 'runtime': 'dart'},
    );
    final beforeReply = await _nextApplication(applications);
    _validateReply(
      beforeReply,
      expectedType: 'interop.dotnet.before-resume',
      expectedCorrelationId: beforeMessageId,
    );

    await closed.timeout(const Duration(seconds: 5));

    final replacement = await client.reconnect(
      policy: DihorGameKitNetworkingReconnectPolicy(
        maxAttempts: 3,
        delay: const Duration(milliseconds: 100),
      ),
    );

    if (replacement.peerId != 'dart-interop-peer') {
      throw StateError('Resume changed the stable Dart PeerId.');
    }
    if (replacement.connectionId == initialConnectionId) {
      throw StateError('Resume reused the original transient ConnectionId.');
    }

    final storedCredential = identityStore.getResumeCredential(
      'lan-websocket:channel:dart-lan-interop',
    );
    if (storedCredential == null ||
        storedCredential.peerId != 'dart-interop-peer' ||
        storedCredential.resumeToken.trim().isEmpty) {
      throw StateError('Rotated Dart resume credential was not stored.');
    }

    final afterMessageId = await client.sendApplicationMessage(
      'interop.dart.after-resume',
      <String, Object?>{'value': 42, 'runtime': 'dart'},
    );
    final afterReply = await _nextApplication(applications);
    _validateReply(
      afterReply,
      expectedType: 'interop.dotnet.after-resume',
      expectedCorrelationId: afterMessageId,
    );

    await client.disconnect(reason: 'interop-complete');
    if (client.state != DihorGameKitNetworkingConnectionState.closed) {
      throw StateError('Explicit Dart disconnect did not close the client.');
    }

    stdout.writeln(
      'Dart LAN client -> .NET listener connect/heartbeat/resume interop passed.',
    );
  } finally {
    await applications.cancel();
    await client.close();
  }
}

Future<DihorGameKitNetworkingApplicationMessage> _nextApplication(
  StreamIterator<DihorGameKitNetworkingApplicationMessage> applications,
) async {
  final moved = await applications.moveNext().timeout(
    const Duration(seconds: 5),
  );
  if (!moved) {
    throw StateError('Dart application stream closed before .NET reply.');
  }
  return applications.current;
}

void _validateReply(
  DihorGameKitNetworkingApplicationMessage reply, {
  required String expectedType,
  required String expectedCorrelationId,
}) {
  if (reply.applicationType != expectedType) {
    throw StateError(
      'Unexpected .NET application type: ${reply.applicationType}.',
    );
  }
  if (reply.correlationId != expectedCorrelationId) {
    throw StateError(
      'Interop reply correlationId did not match Dart request.',
    );
  }

  final data = reply.data;
  if (data is! Map || data['value'] != 42 || data['runtime'] != 'dotnet') {
    throw StateError('Opaque .NET application payload was not preserved.');
  }
}
