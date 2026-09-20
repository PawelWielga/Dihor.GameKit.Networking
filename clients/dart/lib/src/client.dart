import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:dihor_gamekit_networking_protocol/dihor_gamekit_networking_protocol.dart';

import 'identity.dart';
import 'lan_websocket_transport.dart';
import 'reconnect.dart';
import 'transport.dart';

enum DihorGameKitNetworkingConnectionState {
  idle,
  connecting,
  connected,
  reconnecting,
  closing,
  closed,
}

final class DihorGameKitNetworkingConnectionInfo {
  const DihorGameKitNetworkingConnectionInfo({
    required this.connectionId,
    this.peerId,
    this.resumeToken,
  });

  final String connectionId;
  final String? peerId;
  final String? resumeToken;
}

final class DihorGameKitNetworkingApplicationMessage {
  const DihorGameKitNetworkingApplicationMessage({
    required this.messageId,
    required this.applicationType,
    required this.data,
    this.correlationId,
  });

  final String messageId;
  final String applicationType;
  final Object? data;
  final String? correlationId;
}

final class DihorGameKitNetworkingConnectionRejectedException
    implements Exception {
  DihorGameKitNetworkingConnectionRejectedException({
    required this.code,
    required this.isResume,
    this.reason,
  });

  final String code;
  final String? reason;
  final bool isResume;

  @override
  String toString() {
    final operation = isResume ? 'resume' : 'connect';
    return reason == null
        ? 'DihorGameKitNetworkingConnectionRejectedException: '
            '$operation rejected with $code'
        : 'DihorGameKitNetworkingConnectionRejectedException: '
            '$operation rejected with $code ($reason)';
  }
}

final class DihorGameKitNetworkingClient {
  DihorGameKitNetworkingClient._({
    required DihorGameKitNetworkingConnectionDescriptor descriptor,
    required DihorGameKitNetworkingIdentityStore identityStore,
    required String Function() messageIdFactory,
    required String? peerId,
    required Duration connectTimeout,
    required Duration handshakeTimeout,
    required Duration heartbeatInterval,
    required int maxMessageBytes,
  })  : _descriptor = descriptor,
        _identityStore = identityStore,
        _messageIdFactory = messageIdFactory,
        _peerId = peerId,
        _connectTimeout = connectTimeout,
        _handshakeTimeout = handshakeTimeout,
        _heartbeatInterval = heartbeatInterval,
        _maxMessageBytes = maxMessageBytes;

  static const defaultHandshakeTimeout = Duration(seconds: 5);
  static const defaultHeartbeatInterval = Duration(seconds: 10);

  final DihorGameKitNetworkingConnectionDescriptor _descriptor;
  final DihorGameKitNetworkingIdentityStore _identityStore;
  final String Function() _messageIdFactory;
  final Duration _connectTimeout;
  final Duration _handshakeTimeout;
  final Duration _heartbeatInterval;
  final int _maxMessageBytes;

  final StreamController<DihorGameKitNetworkingEnvelope> _messageController =
      StreamController<DihorGameKitNetworkingEnvelope>.broadcast();
  final StreamController<DihorGameKitNetworkingApplicationMessage>
      _applicationController =
      StreamController<DihorGameKitNetworkingApplicationMessage>();
  final StreamController<DihorGameKitNetworkingConnectionState>
      _stateController =
      StreamController<DihorGameKitNetworkingConnectionState>.broadcast();
  final StreamController<Object> _errorController = StreamController<Object>();

  DihorGameKitNetworkingClientTransport? _transport;
  StreamSubscription<DihorGameKitNetworkingTransportMessage>? _subscription;
  DihorGameKitNetworkingConnectionState _state =
      DihorGameKitNetworkingConnectionState.idle;
  DihorGameKitNetworkingConnectionInfo? _connection;
  Completer<DihorGameKitNetworkingConnectionInfo>? _pendingConnection;
  bool _pendingResume = false;
  String? _peerId;
  Timer? _heartbeatTimer;
  int _transportGeneration = 0;
  bool _controllersClosed = false;
  bool _disposed = false;

  static Future<DihorGameKitNetworkingClient> connectLan(
    DihorGameKitNetworkingConnectionDescriptor descriptor, {
    String? peerId,
    DihorGameKitNetworkingIdentityStore? identityStore,
    Duration connectTimeout =
        DihorGameKitNetworkingLanWebSocketTransport.defaultConnectTimeout,
    Duration handshakeTimeout = defaultHandshakeTimeout,
    Duration heartbeatInterval = defaultHeartbeatInterval,
    int maxMessageBytes =
        DihorGameKitNetworkingLanWebSocketTransport.defaultMaxMessageBytes,
    String Function()? messageIdFactory,
    DihorGameKitNetworkingCancellationSignal? cancellation,
  }) async {
    if (handshakeTimeout <= Duration.zero) {
      throw ArgumentError.value(
        handshakeTimeout,
        'handshakeTimeout',
        'Must be positive.',
      );
    }
    if (heartbeatInterval <= Duration.zero) {
      throw ArgumentError.value(
        heartbeatInterval,
        'heartbeatInterval',
        'Must be positive.',
      );
    }

    final store =
        identityStore ?? MemoryDihorGameKitNetworkingIdentityStore();
    final configuredPeerId =
        peerId == null ? null : _required(peerId, 'peerId');
    if (configuredPeerId != null) {
      store.setPeerId(configuredPeerId);
    }
    final resolvedPeerId = configuredPeerId ?? store.getPeerId();

    final client = DihorGameKitNetworkingClient._(
      descriptor: descriptor,
      identityStore: store,
      messageIdFactory: messageIdFactory ?? _defaultMessageIdFactory,
      peerId: resolvedPeerId,
      connectTimeout: connectTimeout,
      handshakeTimeout: handshakeTimeout,
      heartbeatInterval: heartbeatInterval,
      maxMessageBytes: maxMessageBytes,
    );

    final storedCredential =
        store.getResumeCredential(_resumeScope(descriptor));
    final shouldResume = resolvedPeerId != null &&
        storedCredential != null &&
        storedCredential.peerId == resolvedPeerId;

    try {
      await client._openAndHandshake(
        resume: shouldResume,
        cancellation: cancellation,
      );
      return client;
    } catch (_) {
      await client.close();
      rethrow;
    }
  }

  DihorGameKitNetworkingConnectionState get state => _state;

  DihorGameKitNetworkingConnectionInfo? get activeConnection => _connection;

  String? get stablePeerId => _peerId;

  Stream<DihorGameKitNetworkingConnectionState> get stateChanges =>
      _stateController.stream;

  Stream<DihorGameKitNetworkingEnvelope> get messages =>
      _messageController.stream;

  Stream<DihorGameKitNetworkingApplicationMessage> get applicationMessages =>
      _applicationController.stream;

  Stream<Object> get errors => _errorController.stream;

  Future<String> sendApplicationMessage(
    String applicationType,
    Object? data, {
    String? correlationId,
  }) async {
    _throwIfDisposed();
    final transport = _transport;
    if (_state != DihorGameKitNetworkingConnectionState.connected ||
        transport == null ||
        !transport.isOpen) {
      throw StateError('Client is not connected.');
    }

    final messageId = _nextMessageId();
    final envelope = DihorGameKitNetworkingEnvelope.create(
      type: DihorGameKitNetworkingMessageTypes.applicationMessage,
      messageId: messageId,
      correlationId: correlationId,
      payload: <String, Object?>{
        'applicationType': _required(applicationType, 'applicationType'),
        'data': data,
      },
    );

    await _sendEnvelope(envelope);
    return messageId;
  }

  Future<DihorGameKitNetworkingConnectionInfo> reconnect({
    DihorGameKitNetworkingReconnectPolicy? policy,
    DihorGameKitNetworkingCancellationSignal? cancellation,
  }) async {
    _throwIfDisposed();
    if (_state == DihorGameKitNetworkingConnectionState.connected ||
        _state == DihorGameKitNetworkingConnectionState.connecting ||
        _state == DihorGameKitNetworkingConnectionState.reconnecting) {
      throw StateError('Client is already connected or connecting.');
    }

    final peerId = _peerId ?? _identityStore.getPeerId();
    if (peerId == null || peerId.trim().isEmpty) {
      throw StateError('Anonymous connections cannot resume.');
    }
    _peerId = peerId.trim();

    final credential =
        _identityStore.getResumeCredential(_resumeScope(_descriptor));
    if (credential == null || credential.peerId != _peerId) {
      throw StateError('No matching resume credential is available.');
    }

    final reconnectPolicy =
        policy ?? DihorGameKitNetworkingReconnectPolicy();
    _setState(DihorGameKitNetworkingConnectionState.reconnecting);

    Object? lastError;
    for (var attempt = 1; attempt <= reconnectPolicy.maxAttempts; attempt++) {
      cancellation?.throwIfCancellationRequested();

      if (attempt > 1 && reconnectPolicy.delay > Duration.zero) {
        await _delayWithCancellation(reconnectPolicy.delay, cancellation);
      }

      try {
        return await _openAndHandshake(
          resume: true,
          cancellation: cancellation,
        );
      } on DihorGameKitNetworkingOperationCancelledException {
        _setState(DihorGameKitNetworkingConnectionState.closed);
        rethrow;
      } on DihorGameKitNetworkingConnectionRejectedException {
        _setState(DihorGameKitNetworkingConnectionState.closed);
        rethrow;
      } catch (error) {
        lastError = error;
        _reportError(error);
        await _disposeCurrentTransport();
      }
    }

    _setState(DihorGameKitNetworkingConnectionState.closed);
    throw DihorGameKitNetworkingReconnectFailedException(
      attempts: reconnectPolicy.maxAttempts,
      lastError: lastError ??
          StateError('Reconnect failed without a transport error.'),
    );
  }

  Future<void> disconnect({
    String? reason,
    bool clearResumeCredential = false,
  }) async {
    _throwIfDisposed();

    final normalizedReason =
        reason == null ? null : _required(reason, 'reason');
    final transport = _transport;

    _stopHeartbeat();
    _setState(DihorGameKitNetworkingConnectionState.closing);

    if (transport != null &&
        transport.isOpen &&
        _connection != null) {
      try {
        await _sendEnvelope(
          DihorGameKitNetworkingEnvelope.create(
            type: DihorGameKitNetworkingMessageTypes.disconnect,
            messageId: _nextMessageId(),
            payload: <String, Object?>{
              if (normalizedReason != null) 'reason': normalizedReason,
            },
          ),
        );
      } catch (error) {
        _reportError(error);
      }
    }

    await _disposeCurrentTransport(reason: 'client-disconnect');
    _connection = null;

    if (clearResumeCredential) {
      _identityStore.clearResumeCredential(_resumeScope(_descriptor));
    }

    _setState(DihorGameKitNetworkingConnectionState.closed);
  }

  Future<void> close({bool clearResumeCredential = false}) async {
    if (_disposed) return;

    try {
      await disconnect(
        reason: 'client-closed',
        clearResumeCredential: clearResumeCredential,
      );
    } finally {
      _disposed = true;
      _stopHeartbeat();
      await _closeControllers();
    }
  }

  Future<DihorGameKitNetworkingConnectionInfo> _openAndHandshake({
    required bool resume,
    DihorGameKitNetworkingCancellationSignal? cancellation,
  }) async {
    _throwIfDisposed();
    cancellation?.throwIfCancellationRequested();

    await _disposeCurrentTransport();

    final transport = await DihorGameKitNetworkingLanWebSocketTransport.connect(
      _descriptor,
      connectTimeout: _connectTimeout,
      maxMessageBytes: _maxMessageBytes,
      cancellation: cancellation,
    );
    _bindTransport(transport);

    final pending = Completer<DihorGameKitNetworkingConnectionInfo>();
    _pendingConnection = pending;
    _pendingResume = resume;

    try {
      cancellation?.throwIfCancellationRequested();
      _connection = null;
      _setState(
        resume
            ? DihorGameKitNetworkingConnectionState.reconnecting
            : DihorGameKitNetworkingConnectionState.connecting,
      );

      await _sendEnvelope(
        resume ? _createResumeRequest() : _createConnectRequest(),
      );

      final connection = await _waitForHandshake(
        pending.future,
        cancellation,
      );
      return connection;
    } catch (_) {
      if (identical(_pendingConnection, pending)) {
        _pendingConnection = null;
      }
      await _disposeCurrentTransport();
      rethrow;
    } finally {
      if (identical(_pendingConnection, pending)) {
        _pendingConnection = null;
      }
      _pendingResume = false;
    }
  }

  DihorGameKitNetworkingEnvelope _createConnectRequest() =>
      DihorGameKitNetworkingEnvelope.create(
        type: DihorGameKitNetworkingMessageTypes.connectRequest,
        messageId: _nextMessageId(),
        payload: <String, Object?>{
          if (_peerId != null) 'peerId': _peerId,
        },
      );

  DihorGameKitNetworkingEnvelope _createResumeRequest() {
    final peerId = _peerId;
    if (peerId == null) {
      throw StateError('Anonymous connections cannot resume.');
    }

    final credential =
        _identityStore.getResumeCredential(_resumeScope(_descriptor));
    if (credential == null || credential.peerId != peerId) {
      throw StateError('No matching resume credential is available.');
    }

    return DihorGameKitNetworkingEnvelope.create(
      type: DihorGameKitNetworkingMessageTypes.resumeRequest,
      messageId: _nextMessageId(),
      payload: <String, Object?>{
        'peerId': peerId,
        'resumeToken': credential.resumeToken,
      },
    );
  }

  Future<DihorGameKitNetworkingConnectionInfo> _waitForHandshake(
    Future<DihorGameKitNetworkingConnectionInfo> handshake,
    DihorGameKitNetworkingCancellationSignal? cancellation,
  ) async {
    return Future.any<DihorGameKitNetworkingConnectionInfo>(
      <Future<DihorGameKitNetworkingConnectionInfo>>[
        handshake,
        Future<DihorGameKitNetworkingConnectionInfo>.delayed(
          _handshakeTimeout,
          () => throw TimeoutException(
            'Timed out waiting for protocol-v2 connection response.',
            _handshakeTimeout,
          ),
        ),
        if (cancellation != null)
          cancellation.whenCancelled
              .then<DihorGameKitNetworkingConnectionInfo>(
            (_) => throw const DihorGameKitNetworkingOperationCancelledException(),
          ),
      ],
    );
  }

  void _bindTransport(DihorGameKitNetworkingClientTransport transport) {
    final generation = ++_transportGeneration;
    _transport = transport;
    _subscription = transport.messages.listen(
      (message) {
        if (generation == _transportGeneration) {
          _handleTransportMessage(message);
        }
      },
      onError: (Object error, StackTrace stackTrace) {
        if (generation == _transportGeneration) {
          _handleTransportError(error, stackTrace);
        }
      },
      onDone: () {
        if (generation == _transportGeneration) {
          _handleTransportDone();
        }
      },
      cancelOnError: false,
    );
  }

  Future<void> _disposeCurrentTransport({String? reason}) async {
    _stopHeartbeat();

    final transport = _transport;
    final subscription = _subscription;
    _transport = null;
    _subscription = null;
    _transportGeneration++;

    if (subscription != null) {
      await subscription.cancel();
    }
    if (transport != null) {
      await transport.close(reason: reason);
    }
  }

  Future<void> _sendEnvelope(DihorGameKitNetworkingEnvelope envelope) {
    final transport = _transport;
    if (transport == null || !transport.isOpen) {
      throw DihorGameKitNetworkingTransportException(
        'Transport is not open.',
      );
    }

    return transport.send(
      utf8.encode(envelope.toJsonString()),
      type: DihorGameKitNetworkingTransportMessageType.text,
    );
  }

  void _handleTransportMessage(
    DihorGameKitNetworkingTransportMessage transportMessage,
  ) {
    try {
      final json = utf8.decode(transportMessage.payload);
      final envelope = DihorGameKitNetworkingEnvelope.parse(json);
      _messageController.add(envelope);

      switch (envelope.type) {
        case DihorGameKitNetworkingMessageTypes.connectAccepted:
          if (_pendingResume) {
            throw const FormatException(
              'Received connect accepted while resume was pending.',
            );
          }
          _acceptConnection(envelope);
          break;
        case DihorGameKitNetworkingMessageTypes.resumeAccepted:
          if (!_pendingResume) {
            throw const FormatException(
              'Received resume accepted without a pending resume.',
            );
          }
          _acceptResume(envelope);
          break;
        case DihorGameKitNetworkingMessageTypes.connectRejected:
          _rejectConnection(envelope, isResume: false);
          break;
        case DihorGameKitNetworkingMessageTypes.resumeRejected:
          _rejectConnection(envelope, isResume: true);
          break;
        case DihorGameKitNetworkingMessageTypes.applicationMessage:
          _publishApplicationMessage(envelope);
          break;
      }
    } catch (error, stackTrace) {
      _handleProtocolError(error, stackTrace);
    }
  }

  void _acceptConnection(DihorGameKitNetworkingEnvelope envelope) {
    final connectionId = _payloadString(
      envelope.payload,
      'connectionId',
      required: true,
    )!;
    final peerId = _payloadString(envelope.payload, 'peerId');
    final resumeToken = _payloadString(envelope.payload, 'resumeToken');

    if (_peerId != null && peerId != null && peerId != _peerId) {
      throw const FormatException(
        'Connection was accepted for a different peer identity.',
      );
    }

    if (peerId != null) {
      _peerId = peerId;
      _identityStore.setPeerId(peerId);
    }
    _storeResumeCredential(peerId, resumeToken);

    _completeConnection(
      DihorGameKitNetworkingConnectionInfo(
        connectionId: connectionId,
        peerId: peerId,
        resumeToken: resumeToken,
      ),
    );
  }

  void _acceptResume(DihorGameKitNetworkingEnvelope envelope) {
    final connectionId = _payloadString(
      envelope.payload,
      'connectionId',
      required: true,
    )!;
    final peerId = _payloadString(
      envelope.payload,
      'peerId',
      required: true,
    )!;
    final resumeToken = _payloadString(envelope.payload, 'resumeToken');

    if (_peerId == null || peerId != _peerId) {
      throw const FormatException(
        'Resume was accepted for an unexpected peer identity.',
      );
    }

    _identityStore.setPeerId(peerId);
    _storeResumeCredential(peerId, resumeToken);

    _completeConnection(
      DihorGameKitNetworkingConnectionInfo(
        connectionId: connectionId,
        peerId: peerId,
        resumeToken: resumeToken,
      ),
    );
  }

  void _completeConnection(DihorGameKitNetworkingConnectionInfo info) {
    _connection = info;
    _setState(DihorGameKitNetworkingConnectionState.connected);
    _startHeartbeat();

    final pending = _pendingConnection;
    if (pending != null && !pending.isCompleted) {
      pending.complete(info);
    }
  }

  void _rejectConnection(
    DihorGameKitNetworkingEnvelope envelope, {
    required bool isResume,
  }) {
    final error = DihorGameKitNetworkingConnectionRejectedException(
      code: _payloadString(envelope.payload, 'code', required: true)!,
      reason: _payloadString(envelope.payload, 'reason'),
      isResume: isResume,
    );

    if (isResume) {
      _identityStore.clearResumeCredential(_resumeScope(_descriptor));
    }
    _reportError(error);

    final pending = _pendingConnection;
    if (pending != null && !pending.isCompleted) {
      pending.completeError(error);
    }
  }

  void _publishApplicationMessage(
    DihorGameKitNetworkingEnvelope envelope,
  ) {
    final applicationType = _payloadString(
      envelope.payload,
      'applicationType',
      required: true,
    )!;
    if (!envelope.payload.containsKey('data')) {
      throw const FormatException('Application message data is required.');
    }

    _applicationController.add(
      DihorGameKitNetworkingApplicationMessage(
        messageId: envelope.messageId,
        applicationType: applicationType,
        data: envelope.payload['data'],
        correlationId: envelope.correlationId,
      ),
    );
  }

  void _handleProtocolError(Object error, StackTrace stackTrace) {
    _reportError(error);

    final pending = _pendingConnection;
    if (pending != null && !pending.isCompleted) {
      pending.completeError(error, stackTrace);
    }
  }

  void _handleTransportError(Object error, StackTrace stackTrace) {
    _reportError(error);

    final pending = _pendingConnection;
    if (pending != null && !pending.isCompleted) {
      pending.completeError(error, stackTrace);
    }
  }

  void _handleTransportDone() {
    _stopHeartbeat();
    _transport = null;
    _subscription = null;
    _transportGeneration++;
    _connection = null;

    final pending = _pendingConnection;
    if (pending != null && !pending.isCompleted) {
      pending.completeError(
        DihorGameKitNetworkingTransportException(
          'Transport closed before protocol handshake completed.',
        ),
      );
    }
    _pendingConnection = null;

    if (!_disposed) {
      _setState(DihorGameKitNetworkingConnectionState.closed);
    }
  }

  void _startHeartbeat() {
    _stopHeartbeat();
    _heartbeatTimer = Timer.periodic(
      _heartbeatInterval,
      (_) => unawaited(_sendHeartbeat()),
    );
  }

  Future<void> _sendHeartbeat() async {
    final transport = _transport;
    if (_state != DihorGameKitNetworkingConnectionState.connected ||
        transport == null ||
        !transport.isOpen) {
      return;
    }

    try {
      await _sendEnvelope(
        DihorGameKitNetworkingEnvelope.create(
          type: DihorGameKitNetworkingMessageTypes.heartbeat,
          messageId: _nextMessageId(),
          payload: <String, Object?>{
            if (_peerId != null) 'peerId': _peerId,
          },
        ),
      );
    } catch (error) {
      _reportError(error);
    }
  }

  void _stopHeartbeat() {
    _heartbeatTimer?.cancel();
    _heartbeatTimer = null;
  }

  void _storeResumeCredential(String? peerId, String? resumeToken) {
    if (peerId == null || resumeToken == null) return;

    _identityStore.setResumeCredential(
      DihorGameKitNetworkingResumeCredential(
        scope: _resumeScope(_descriptor),
        peerId: peerId,
        resumeToken: resumeToken,
      ),
    );
  }

  void _reportError(Object error) {
    if (!_controllersClosed) {
      _errorController.add(error);
    }
  }

  void _setState(DihorGameKitNetworkingConnectionState value) {
    if (_state == value) return;
    _state = value;
    if (!_controllersClosed) {
      _stateController.add(value);
    }
  }

  String _nextMessageId() {
    final provided = _messageIdFactory();
    final normalized = provided.trim();
    if (normalized.isEmpty) {
      throw StateError('messageIdFactory returned an empty identifier.');
    }
    return normalized;
  }

  Future<void> _closeControllers() async {
    if (_controllersClosed) return;
    _controllersClosed = true;
    await Future.wait<void>(<Future<void>>[
      _messageController.close(),
      _applicationController.close(),
      _stateController.close(),
      _errorController.close(),
    ]);
  }

  void _throwIfDisposed() {
    if (_disposed) {
      throw StateError('Client has been disposed.');
    }
  }

  static Future<void> _delayWithCancellation(
    Duration delay,
    DihorGameKitNetworkingCancellationSignal? cancellation,
  ) async {
    if (cancellation == null) {
      await Future<void>.delayed(delay);
      return;
    }

    await Future.any<void>(<Future<void>>[
      Future<void>.delayed(delay),
      cancellation.whenCancelled.then<void>(
        (_) => throw const DihorGameKitNetworkingOperationCancelledException(),
      ),
    ]);
  }

  static String _resumeScope(
    DihorGameKitNetworkingConnectionDescriptor descriptor,
  ) =>
      descriptor.channelId == null
          ? '${descriptor.transport}:${descriptor.endpoint}'
          : '${descriptor.transport}:channel:${descriptor.channelId}';

  static String _required(String value, String name) {
    final normalized = value.trim();
    if (normalized.isEmpty) {
      throw ArgumentError.value(value, name, 'Must be non-empty.');
    }
    return normalized;
  }

  static String? _payloadString(
    Map<String, Object?> payload,
    String key, {
    bool required = false,
  }) {
    final value = payload[key];
    if (value == null && !required) return null;
    if (value is! String || value.trim().isEmpty) {
      throw FormatException(
        required
            ? '$key must be a non-empty string.'
            : '$key must be a non-empty string when present.',
      );
    }
    return value.trim();
  }

  static String _defaultMessageIdFactory() =>
      'dart-${DateTime.now().microsecondsSinceEpoch}-'
      '${Random.secure().nextInt(0x7fffffff)}';
}
