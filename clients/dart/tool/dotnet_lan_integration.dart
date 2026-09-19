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

  var sequence = 0;
  final client = await DihorGameKitNetworkingClient.connectLan(
    descriptor,
    peerId: 'dart-interop-peer',
    messageIdFactory: () => 'dart-interop-${++sequence}',
  );

  try {
    if (client.activeConnection?.peerId != 'dart-interop-peer') {
      throw StateError('Stable Dart peer identity was not preserved.');
    }

    final replyFuture = client.applicationMessages.first.timeout(
      const Duration(seconds: 5),
    );

    final messageId = await client.sendApplicationMessage(
      'interop.dart.ping',
      <String, Object?>{'value': 42, 'runtime': 'dart'},
    );

    final reply = await replyFuture;
    if (reply.applicationType != 'interop.dotnet.pong') {
      throw StateError(
        'Unexpected .NET application type: ${reply.applicationType}.',
      );
    }
    if (reply.correlationId != messageId) {
      throw StateError('Interop reply correlationId did not match Dart request.');
    }

    final data = reply.data;
    if (data is! Map ||
        data['value'] != 42 ||
        data['runtime'] != 'dotnet') {
      throw StateError('Opaque .NET application payload was not preserved.');
    }

    stdout.writeln('Dart LAN client -> .NET listener interop passed.');
  } finally {
    await client.close();
  }
}
