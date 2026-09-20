import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:dihor_gamekit_networking/dihor_gamekit_networking.dart';
import 'package:test/test.dart';

void main() {
  group('Dart connection continuity', () {
    test('heartbeat is control-only and disconnect is explicit', () async {
      final heartbeat = Completer<DihorGameKitNetworkingEnvelope>();
      final disconnect = Completer<DihorGameKitNetworkingEnvelope>();

      final server = await _ContinuityServer.start((socket, connectionIndex) async {
        expect(connectionIndex, 1);
        final iterator = StreamIterator<dynamic>(socket);
        try {
          final connect = await _nextEnvelope(iterator);
          expect(
            connect.type,
            DihorGameKitNetworkingMessageTypes.connectRequest,
          );
          socket.add(
            _wire(
              DihorGameKitNetworkingEnvelope.create(
                type: DihorGameKitNetworkingMessageTypes.connectAccepted,
                messageId: 'accepted-heartbeat',
                correlationId: connect.messageId,
                payload: <String, Object?>{
                  'connectionId': 'connection-heartbeat',
                  'peerId': 'peer-heartbeat',
                  'resumeToken': 'resume-heartbeat',
                },
              ),
            ),
          );

          while (await iterator.moveNext()) {
            final message = DihorGameKitNetworkingEnvelope.parse(
              utf8.decode(_bytes(iterator.current)),
            );
            if (message.type ==
                DihorGameKitNetworkingMessageTypes.heartbeat) {
              if (!heartbeat.isCompleted) heartbeat.complete(message);
              continue;
            }
            if (message.type ==
                DihorGameKitNetworkingMessageTypes.disconnect) {
              if (!disconnect.isCompleted) disconnect.complete(message);
              return;
            }
          }
        } finally {
          await iterator.cancel();
        }
      });
      addTearDown(server.close);

      final store = MemoryDihorGameKitNetworkingIdentityStore();
      final client = await DihorGameKitNetworkingClient.connectLan(
        server.descriptor,
        peerId: 'peer-heartbeat',
        identityStore: store,
        heartbeatInterval: const Duration(milliseconds: 20),
      );
      addTearDown(client.close);

      final heartbeatMessage = await heartbeat.future.timeout(
        const Duration(seconds: 2),
      );
      expect(heartbeatMessage.payload, <String, Object?>{
        'peerId': 'peer-heartbeat',
      });
      expect(
        heartbeatMessage.payload.containsKey('applicationType'),
        isFalse,
      );
      expect(heartbeatMessage.payload.containsKey('data'), isFalse);

      await client.disconnect(reason: 'test-complete');

      final disconnectMessage = await disconnect.future.timeout(
        const Duration(seconds: 2),
      );
      expect(disconnectMessage.payload['reason'], 'test-complete');
      expect(client.state, DihorGameKitNetworkingConnectionState.closed);
      expect(
        store.getResumeCredential('lan-websocket:channel:continuity-test')
            ?.resumeToken,
        'resume-heartbeat',
      );
    });

    test('same peer resumes on replacement connection and keeps app traffic',
        () async {
      final dropFirst = Completer<void>();
      final firstClosed = Completer<void>();
      final resumed = Completer<void>();

      final server = await _ContinuityServer.start((socket, connectionIndex) async {
        final iterator = StreamIterator<dynamic>(socket);
        try {
          if (connectionIndex == 1) {
            final connect = await _nextEnvelope(iterator);
            expect(
              connect.type,
              DihorGameKitNetworkingMessageTypes.connectRequest,
            );
            expect(connect.payload['peerId'], 'peer-resume');
            socket.add(
              _wire(
                DihorGameKitNetworkingEnvelope.create(
                  type: DihorGameKitNetworkingMessageTypes.connectAccepted,
                  messageId: 'accepted-initial',
                  correlationId: connect.messageId,
                  payload: <String, Object?>{
                    'connectionId': 'connection-1',
                    'peerId': 'peer-resume',
                    'resumeToken': 'resume-token-1',
                  },
                ),
              ),
            );

            await dropFirst.future;
            await socket.close(
              WebSocketStatus.goingAway,
              'simulated-network-drop',
            );
            if (!firstClosed.isCompleted) firstClosed.complete();
            return;
          }

          expect(connectionIndex, 2);
          final resumeRequest = await _nextEnvelope(iterator);
          expect(
            resumeRequest.type,
            DihorGameKitNetworkingMessageTypes.resumeRequest,
          );
          expect(resumeRequest.payload['peerId'], 'peer-resume');
          expect(resumeRequest.payload['resumeToken'], 'resume-token-1');

          socket.add(
            _wire(
              DihorGameKitNetworkingEnvelope.create(
                type: DihorGameKitNetworkingMessageTypes.resumeAccepted,
                messageId: 'resume-accepted',
                correlationId: resumeRequest.messageId,
                payload: <String, Object?>{
                  'connectionId': 'connection-2',
                  'peerId': 'peer-resume',
                  'resumeToken': 'resume-token-2',
                },
              ),
            ),
          );
          if (!resumed.isCompleted) resumed.complete();

          DihorGameKitNetworkingEnvelope application;
          do {
            application = await _nextEnvelope(iterator);
          } while (application.type ==
              DihorGameKitNetworkingMessageTypes.heartbeat);

          expect(
            application.type,
            DihorGameKitNetworkingMessageTypes.applicationMessage,
          );
          expect(
            application.payload['applicationType'],
            'continuity.after-resume',
          );

          socket.add(
            _wire(
              DihorGameKitNetworkingEnvelope.create(
                type: DihorGameKitNetworkingMessageTypes.applicationMessage,
                messageId: 'after-resume-reply',
                correlationId: application.messageId,
                payload: <String, Object?>{
                  'applicationType': 'continuity.reply',
                  'data': <String, Object?>{'ok': true},
                },
              ),
            ),
          );
        } finally {
          await iterator.cancel();
        }
      });
      addTearDown(() async {
        if (!dropFirst.isCompleted) dropFirst.complete();
        await server.close();
      });

      final store = MemoryDihorGameKitNetworkingIdentityStore();
      var messageSequence = 0;
      final client = await DihorGameKitNetworkingClient.connectLan(
        server.descriptor,
        peerId: 'peer-resume',
        identityStore: store,
        heartbeatInterval: const Duration(seconds: 30),
        messageIdFactory: () => 'dart-continuity-${++messageSequence}',
      );
      addTearDown(client.close);

      final initialConnectionId = client.activeConnection!.connectionId;
      expect(initialConnectionId, 'connection-1');

      final closed = client.stateChanges.firstWhere(
        (state) => state == DihorGameKitNetworkingConnectionState.closed,
      );
      dropFirst.complete();
      await firstClosed.future.timeout(const Duration(seconds: 2));
      await closed.timeout(const Duration(seconds: 2));

      final replacement = await client.reconnect(
        policy: DihorGameKitNetworkingReconnectPolicy(
          maxAttempts: 2,
          delay: const Duration(milliseconds: 10),
        ),
      );
      await resumed.future.timeout(const Duration(seconds: 2));

      expect(replacement.peerId, 'peer-resume');
      expect(replacement.connectionId, 'connection-2');
      expect(replacement.connectionId, isNot(initialConnectionId));
      expect(client.stablePeerId, 'peer-resume');
      expect(
        store.getResumeCredential('lan-websocket:channel:continuity-test')
            ?.resumeToken,
        'resume-token-2',
      );

      final messages =
          StreamIterator<DihorGameKitNetworkingApplicationMessage>(
        client.applicationMessages,
      );
      addTearDown(messages.cancel);

      final sentMessageId = await client.sendApplicationMessage(
        'continuity.after-resume',
        <String, Object?>{'value': 42},
      );
      expect(await messages.moveNext(), isTrue);
      expect(messages.current.applicationType, 'continuity.reply');
      expect(messages.current.correlationId, sentMessageId);
      expect(messages.current.data, <String, Object?>{'ok': true});
    });

    test('resume rejection is deterministic and clears stale credential',
        () async {
      final dropFirst = Completer<void>();
      var connectionCount = 0;

      final server = await _ContinuityServer.start((socket, connectionIndex) async {
        connectionCount = connectionIndex;
        final iterator = StreamIterator<dynamic>(socket);
        try {
          if (connectionIndex == 1) {
            final connect = await _nextEnvelope(iterator);
            socket.add(
              _wire(
                DihorGameKitNetworkingEnvelope.create(
                  type: DihorGameKitNetworkingMessageTypes.connectAccepted,
                  messageId: 'accepted-reject',
                  correlationId: connect.messageId,
                  payload: <String, Object?>{
                    'connectionId': 'reject-connection-1',
                    'peerId': 'peer-reject',
                    'resumeToken': 'stale-token',
                  },
                ),
              ),
            );
            await dropFirst.future;
            await socket.close(
              WebSocketStatus.goingAway,
              'drop-before-reject',
            );
            return;
          }

          final resumeRequest = await _nextEnvelope(iterator);
          expect(
            resumeRequest.type,
            DihorGameKitNetworkingMessageTypes.resumeRequest,
          );
          socket.add(
            _wire(
              DihorGameKitNetworkingEnvelope.create(
                type: DihorGameKitNetworkingMessageTypes.resumeRejected,
                messageId: 'resume-rejected',
                correlationId: resumeRequest.messageId,
                payload: <String, Object?>{
                  'peerId': 'peer-reject',
                  'code': 'invalid-resume-credential',
                  'reason': 'credential rejected for test',
                },
              ),
            ),
          );
        } finally {
          await iterator.cancel();
        }
      });
      addTearDown(() async {
        if (!dropFirst.isCompleted) dropFirst.complete();
        await server.close();
      });

      final store = MemoryDihorGameKitNetworkingIdentityStore();
      final client = await DihorGameKitNetworkingClient.connectLan(
        server.descriptor,
        peerId: 'peer-reject',
        identityStore: store,
        heartbeatInterval: const Duration(seconds: 30),
      );
      addTearDown(client.close);

      final closed = client.stateChanges.firstWhere(
        (state) => state == DihorGameKitNetworkingConnectionState.closed,
      );
      dropFirst.complete();
      await closed.timeout(const Duration(seconds: 2));

      await expectLater(
        client.reconnect(
          policy: DihorGameKitNetworkingReconnectPolicy(
            maxAttempts: 3,
            delay: const Duration(milliseconds: 10),
          ),
        ),
        throwsA(
          isA<DihorGameKitNetworkingConnectionRejectedException>()
              .having((error) => error.isResume, 'isResume', isTrue)
              .having(
                (error) => error.code,
                'code',
                'invalid-resume-credential',
              ),
        ),
      );

      expect(connectionCount, 2);
      expect(
        store.getResumeCredential('lan-websocket:channel:continuity-test'),
        isNull,
      );
      expect(client.state, DihorGameKitNetworkingConnectionState.closed);
    });

    test('resume handshake timeout is bounded', () async {
      final dropFirst = Completer<void>();
      final releaseSecond = Completer<void>();
      final resumeSeen = Completer<void>();

      final server = await _ContinuityServer.start((socket, connectionIndex) async {
        final iterator = StreamIterator<dynamic>(socket);
        try {
          if (connectionIndex == 1) {
            final connect = await _nextEnvelope(iterator);
            socket.add(
              _wire(
                DihorGameKitNetworkingEnvelope.create(
                  type: DihorGameKitNetworkingMessageTypes.connectAccepted,
                  messageId: 'accepted-timeout',
                  correlationId: connect.messageId,
                  payload: <String, Object?>{
                    'connectionId': 'timeout-connection-1',
                    'peerId': 'peer-timeout',
                    'resumeToken': 'timeout-resume-token',
                  },
                ),
              ),
            );
            await dropFirst.future;
            await socket.close(
              WebSocketStatus.goingAway,
              'drop-before-resume-timeout',
            );
            return;
          }

          final resumeRequest = await _nextEnvelope(iterator);
          expect(
            resumeRequest.type,
            DihorGameKitNetworkingMessageTypes.resumeRequest,
          );
          if (!resumeSeen.isCompleted) resumeSeen.complete();
          await releaseSecond.future;
        } finally {
          await iterator.cancel();
        }
      });
      addTearDown(() async {
        if (!dropFirst.isCompleted) dropFirst.complete();
        if (!releaseSecond.isCompleted) releaseSecond.complete();
        await server.close();
      });

      final client = await DihorGameKitNetworkingClient.connectLan(
        server.descriptor,
        peerId: 'peer-timeout',
        handshakeTimeout: const Duration(milliseconds: 50),
        heartbeatInterval: const Duration(seconds: 30),
      );
      addTearDown(client.close);

      final closed = client.stateChanges.firstWhere(
        (state) => state == DihorGameKitNetworkingConnectionState.closed,
      );
      dropFirst.complete();
      await closed.timeout(const Duration(seconds: 2));

      await expectLater(
        client.reconnect(
          policy: DihorGameKitNetworkingReconnectPolicy(maxAttempts: 1),
        ),
        throwsA(
          isA<DihorGameKitNetworkingReconnectFailedException>()
              .having(
                (error) => error.lastError,
                'lastError',
                isA<TimeoutException>(),
              ),
        ),
      );
      await resumeSeen.future.timeout(const Duration(seconds: 2));
      expect(client.state, DihorGameKitNetworkingConnectionState.closed);

      if (!releaseSecond.isCompleted) releaseSecond.complete();
    });

    test('reconnect retries are bounded and delay can be cancelled', () async {
      final dropFirst = Completer<void>();

      final server = await _ContinuityServer.start((socket, connectionIndex) async {
        expect(connectionIndex, 1);
        final iterator = StreamIterator<dynamic>(socket);
        try {
          final connect = await _nextEnvelope(iterator);
          socket.add(
            _wire(
              DihorGameKitNetworkingEnvelope.create(
                type: DihorGameKitNetworkingMessageTypes.connectAccepted,
                messageId: 'accepted-bounded',
                correlationId: connect.messageId,
                payload: <String, Object?>{
                  'connectionId': 'bounded-connection-1',
                  'peerId': 'peer-bounded',
                  'resumeToken': 'bounded-resume-token',
                },
              ),
            ),
          );
          await dropFirst.future;
          await socket.close(
            WebSocketStatus.goingAway,
            'drop-before-bounded-retry',
          );
        } finally {
          await iterator.cancel();
        }
      });

      final store = MemoryDihorGameKitNetworkingIdentityStore();
      final client = await DihorGameKitNetworkingClient.connectLan(
        server.descriptor,
        peerId: 'peer-bounded',
        identityStore: store,
        connectTimeout: const Duration(milliseconds: 100),
        heartbeatInterval: const Duration(seconds: 30),
      );
      addTearDown(client.close);

      final closed = client.stateChanges.firstWhere(
        (state) => state == DihorGameKitNetworkingConnectionState.closed,
      );
      dropFirst.complete();
      await closed.timeout(const Duration(seconds: 2));
      await server.close();

      await expectLater(
        client.reconnect(
          policy: DihorGameKitNetworkingReconnectPolicy(
            maxAttempts: 2,
            delay: const Duration(milliseconds: 10),
          ),
        ),
        throwsA(
          isA<DihorGameKitNetworkingReconnectFailedException>()
              .having((error) => error.attempts, 'attempts', 2),
        ),
      );

      final cancellation = DihorGameKitNetworkingCancellationSignal();
      Timer(
        const Duration(milliseconds: 50),
        cancellation.cancel,
      );

      await expectLater(
        client.reconnect(
          policy: DihorGameKitNetworkingReconnectPolicy(
            maxAttempts: 5,
            delay: const Duration(seconds: 5),
          ),
          cancellation: cancellation,
        ),
        throwsA(
          isA<DihorGameKitNetworkingOperationCancelledException>(),
        ),
      );
      expect(client.state, DihorGameKitNetworkingConnectionState.closed);
    });
  });
}

Future<DihorGameKitNetworkingEnvelope> _nextEnvelope(
  StreamIterator<dynamic> iterator,
) async {
  expect(await iterator.moveNext(), isTrue);
  return DihorGameKitNetworkingEnvelope.parse(
    utf8.decode(_bytes(iterator.current)),
  );
}

List<int> _wire(DihorGameKitNetworkingEnvelope envelope) =>
    utf8.encode(envelope.toJsonString());

List<int> _bytes(dynamic value) {
  if (value is String) return utf8.encode(value);
  if (value is List<int>) return value;
  throw StateError('Unexpected WebSocket payload: ${value.runtimeType}.');
}

final class _ContinuityServer {
  _ContinuityServer._(
    this._server,
    this.descriptor,
    this._tasks,
  );

  final HttpServer _server;
  final DihorGameKitNetworkingConnectionDescriptor descriptor;
  final List<Future<void>> _tasks;

  static Future<_ContinuityServer> start(
    Future<void> Function(WebSocket socket, int connectionIndex) scenario,
  ) async {
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    final tasks = <Future<void>>[];
    var connectionIndex = 0;

    server.listen((request) {
      final task = () async {
        if (request.uri.path != '/partygamekit') {
          request.response.statusCode = HttpStatus.notFound;
          await request.response.close();
          return;
        }

        final socket = await WebSocketTransformer.upgrade(request);
        connectionIndex++;
        await scenario(socket, connectionIndex);
      }();
      tasks.add(task);
    });

    return _ContinuityServer._(
      server,
      DihorGameKitNetworkingConnectionDescriptor(
        protocolVersion: dihorGameKitNetworkingProtocolVersion,
        transport: DihorGameKitNetworkingLanWebSocketTransport.transportName,
        endpoint: 'ws://127.0.0.1:${server.port}/partygamekit',
        channelId: 'continuity-test',
      ),
      tasks,
    );
  }

  Future<void> close() async {
    await _server.close(force: true);
    if (_tasks.isNotEmpty) {
      await Future.wait<void>(_tasks);
    }
  }
}
