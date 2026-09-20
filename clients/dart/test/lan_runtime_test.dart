import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:dihor_gamekit_networking/dihor_gamekit_networking.dart';
import 'package:test/test.dart';

void main() {
  group('Dart LAN WebSocket runtime', () {
    test('raw transport sends and receives opaque binary payloads', () async {
      final server = await _TestLanServer.start((socket) async {
        final iterator = StreamIterator<dynamic>(socket);
        try {
          expect(await iterator.moveNext(), isTrue);
          final received = _bytes(iterator.current);
          expect(received, <int>[1, 2, 3, 4]);
          socket.add(<int>[4, 3, 2, 1]);
        } finally {
          await iterator.cancel();
        }
      });
      addTearDown(server.close);

      final transport =
          await DihorGameKitNetworkingLanWebSocketTransport.connect(
        server.descriptor,
      );
      addTearDown(transport.close);

      final response = transport.messages.first;
      await transport.send(<int>[1, 2, 3, 4]);

      expect((await response).payload, <int>[4, 3, 2, 1]);
      await server.done;
    });

    test('client performs protocol-v2 connect and opaque application exchange',
        () async {
      final server = await _TestLanServer.start((socket) async {
        final iterator = StreamIterator<dynamic>(socket);
        try {
          expect(await iterator.moveNext(), isTrue);
          final connect = DihorGameKitNetworkingEnvelope.parse(
            utf8.decode(_bytes(iterator.current)),
          );
          expect(
            connect.type,
            DihorGameKitNetworkingMessageTypes.connectRequest,
          );
          expect(connect.payload['peerId'], 'peer-dart');

          socket.add(
            utf8.encode(
              DihorGameKitNetworkingEnvelope.create(
                type: DihorGameKitNetworkingMessageTypes.connectAccepted,
                messageId: 'accepted-1',
                correlationId: connect.messageId,
                payload: <String, Object?>{
                  'connectionId': 'dotnet-connection-1',
                  'peerId': 'peer-dart',
                  'resumeToken': 'resume-dart-1',
                },
              ).toJsonString(),
            ),
          );

          expect(await iterator.moveNext(), isTrue);
          final application = DihorGameKitNetworkingEnvelope.parse(
            utf8.decode(_bytes(iterator.current)),
          );
          expect(
            application.type,
            DihorGameKitNetworkingMessageTypes.applicationMessage,
          );
          expect(application.payload['applicationType'], 'demo.dart.command');
          expect(
            application.payload['data'],
            <String, Object?>{'value': 42},
          );

          socket.add(
            utf8.encode(
              DihorGameKitNetworkingEnvelope.create(
                type: DihorGameKitNetworkingMessageTypes.applicationMessage,
                messageId: 'dotnet-reply-1',
                correlationId: application.messageId,
                payload: <String, Object?>{
                  'applicationType': 'demo.dotnet.reply',
                  'data': <String, Object?>{'ok': true},
                },
              ).toJsonString(),
            ),
          );
        } finally {
          await iterator.cancel();
        }
      });
      addTearDown(server.close);

      var sequence = 0;
      final client = await DihorGameKitNetworkingClient.connectLan(
        server.descriptor,
        peerId: 'peer-dart',
        messageIdFactory: () => 'dart-message-${++sequence}',
      );
      addTearDown(client.close);

      expect(client.state, DihorGameKitNetworkingConnectionState.connected);
      expect(client.activeConnection?.connectionId, 'dotnet-connection-1');
      expect(client.activeConnection?.peerId, 'peer-dart');
      expect(client.activeConnection?.resumeToken, 'resume-dart-1');

      final reply = client.applicationMessages.first;
      final sentMessageId = await client.sendApplicationMessage(
        'demo.dart.command',
        <String, Object?>{'value': 42},
      );

      final received = await reply;
      expect(sentMessageId, 'dart-message-2');
      expect(received.messageId, 'dotnet-reply-1');
      expect(received.correlationId, sentMessageId);
      expect(received.applicationType, 'demo.dotnet.reply');
      expect(received.data, <String, Object?>{'ok': true});
      await server.done;
    });

    test('connect rejection is surfaced to the caller', () async {
      final server = await _TestLanServer.start((socket) async {
        final iterator = StreamIterator<dynamic>(socket);
        try {
          expect(await iterator.moveNext(), isTrue);
          final connect = DihorGameKitNetworkingEnvelope.parse(
            utf8.decode(_bytes(iterator.current)),
          );

          socket.add(
            utf8.encode(
              DihorGameKitNetworkingEnvelope.create(
                type: DihorGameKitNetworkingMessageTypes.connectRejected,
                messageId: 'rejected-1',
                correlationId: connect.messageId,
                payload: <String, Object?>{
                  'code': 'invalid-request',
                  'reason': 'test rejection',
                },
              ).toJsonString(),
            ),
          );
        } finally {
          await iterator.cancel();
        }
      });
      addTearDown(server.close);

      await expectLater(
        DihorGameKitNetworkingClient.connectLan(server.descriptor),
        throwsA(
          isA<DihorGameKitNetworkingConnectionRejectedException>()
              .having((error) => error.code, 'code', 'invalid-request'),
        ),
      );
      await server.done;
    });

    test('remote close becomes observable connection state', () async {
      final releaseClose = Completer<void>();
      final server = await _TestLanServer.start((socket) async {
        final iterator = StreamIterator<dynamic>(socket);
        try {
          expect(await iterator.moveNext(), isTrue);
          final connect = DihorGameKitNetworkingEnvelope.parse(
            utf8.decode(_bytes(iterator.current)),
          );
          socket.add(
            utf8.encode(
              DihorGameKitNetworkingEnvelope.create(
                type: DihorGameKitNetworkingMessageTypes.connectAccepted,
                messageId: 'accepted-close',
                correlationId: connect.messageId,
                payload: <String, Object?>{
                  'connectionId': 'dotnet-connection-close',
                },
              ).toJsonString(),
            ),
          );

          await releaseClose.future;
          await socket.close(
            WebSocketStatus.goingAway,
            'server-going-away',
          );
        } finally {
          await iterator.cancel();
        }
      });
      addTearDown(server.close);

      final client =
          await DihorGameKitNetworkingClient.connectLan(server.descriptor);
      addTearDown(client.close);

      final closed = client.stateChanges.firstWhere(
        (state) => state == DihorGameKitNetworkingConnectionState.closed,
      );

      releaseClose.complete();

      expect(
        await closed,
        DihorGameKitNetworkingConnectionState.closed,
      );
      expect(client.activeConnection, isNull);
      await server.done;
    });

    test('rejects incompatible LAN descriptors before connecting', () async {
      final wrongTransport = DihorGameKitNetworkingConnectionDescriptor(
        protocolVersion: dihorGameKitNetworkingProtocolVersion,
        transport: 'signalr-relay',
        endpoint: 'ws://127.0.0.1:1/partygamekit',
      );
      final wrongVersion = DihorGameKitNetworkingConnectionDescriptor(
        protocolVersion: 999,
        transport: DihorGameKitNetworkingLanWebSocketTransport.transportName,
        endpoint: 'ws://127.0.0.1:1/partygamekit',
      );

      await expectLater(
        DihorGameKitNetworkingLanWebSocketTransport.connect(wrongTransport),
        throwsA(isA<FormatException>()),
      );
      await expectLater(
        DihorGameKitNetworkingLanWebSocketTransport.connect(wrongVersion),
        throwsA(isA<FormatException>()),
      );
    });
  });
}

List<int> _bytes(dynamic value) {
  if (value is String) return utf8.encode(value);
  if (value is List<int>) return value;
  throw StateError('Unexpected WebSocket test payload: ${value.runtimeType}.');
}

final class _TestLanServer {
  _TestLanServer._(
    this._server,
    this.descriptor,
    this.done,
  );

  final HttpServer _server;
  final DihorGameKitNetworkingConnectionDescriptor descriptor;
  final Future<void> done;

  static Future<_TestLanServer> start(
    Future<void> Function(WebSocket socket) scenario,
  ) async {
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    final done = Completer<void>();

    server.listen((request) async {
      try {
        if (request.uri.path != '/partygamekit') {
          request.response.statusCode = HttpStatus.notFound;
          await request.response.close();
          return;
        }

        final socket = await WebSocketTransformer.upgrade(request);
        await scenario(socket);
        if (!done.isCompleted) done.complete();
      } catch (error, stackTrace) {
        if (!done.isCompleted) done.completeError(error, stackTrace);
      }
    });

    return _TestLanServer._(
      server,
      DihorGameKitNetworkingConnectionDescriptor(
        protocolVersion: dihorGameKitNetworkingProtocolVersion,
        transport: DihorGameKitNetworkingLanWebSocketTransport.transportName,
        endpoint: 'ws://127.0.0.1:${server.port}/partygamekit',
        channelId: 'dart-test',
      ),
      done.future,
    );
  }

  Future<void> close() async {
    await _server.close(force: true);
  }
}
