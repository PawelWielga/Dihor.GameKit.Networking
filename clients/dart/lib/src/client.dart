import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:dihor_gamekit_networking_protocol/dihor_gamekit_networking_protocol.dart';

import 'lan_websocket_transport.dart';
import 'transport.dart';

enum DihorGameKitNetworkingConnectionState {
  idle,
  connecting,
  connected,
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
    this.reason,
  });

  final String code;
  final String? reason;

  @override
  String toString() => reason == null
      ? 'DihorGameKitNetworkingConnectionRejectedException: $code'
      : 'DihorGameKitNetworkingConnectionRejectedException: $code ($reason)';
}

final class DihorGameKitNetworkingClient {
  DihorGameKitNetworkingClient._(
    this._transport,
    this._messageIdFactory,
    this._requestedPeerId,
  ) {
    _subscription = _transport.messages.listen(
      _handleTransportMessage,
      onError: _handleTransportError,
      onDone: _handleTransportDone,
      cancelOnError: false,
    );
  }

  static const defaultHandshakeTimeout = Duration(seconds: 5);

  final DihorGameKitNetworkingClientTransport _transport;
  final String Function() _messageIdFactory;
  final String? _requestedPeerId;

  final StreamController<DihorGameKitNetworkingEnvelope> _messageController =
      StreamController<DihorGameKitNetworkingEnvelope>();
  final StreamController<DihorGameKitNetworkingApplicationMessage>
      _applicationController =
      StreamController<DihorGameKitNetworkingApplicationMessage>();
  final StreamController<DihorGameKitNetworkingConnectionState>
      _stateController =
      StreamController<DihorGameKitNetworkingConnectionState>.broadcast();
  final StreamController<Object> _errorController =
      StreamController<Object>.broadcast();

  late final StreamSubscription<DihorGameKitNetworkingTransportMessage>
      _subscription;

  DihorGameKitNetworkingConnectionState _state =
      DihorGameKitNetworkingConnectionState.idle;
  DihorGameKitNetworkingConnectionInfo? _connection;
  Completer<DihorGameKitNetworkingConnectionInfo>? _pendingConnection;
  bool _controllersClosed = false;

  static Future<DihorGameKitNetworkingClient> connectLan(
    DihorGameKitNetworkingConnectionDescriptor descriptor, {
    String? peerId,
    Duration connectTimeout =
        DihorGameKitNetworkingLanWebSocketTransport.defaultConnectTimeout,
    Duration handshakeTimeout = defaultHandshakeTimeout,
    int maxMessageBytes =
        DihorGameKitNetworkingLanWebSocketTransport.defaultMaxMessageBytes,
    String Function()? messageIdFactory,
  }) async {
    final normalizedPeerId =
        peerId == null ? null : _required(peerId, 'peerId');
    if (handshakeTimeout <= Duration.zero) {
      throw ArgumentError.value(
        handshakeTimeout,
        'handshakeTimeout',
        'Must be positive.',
      );
    }

    final transport = await DihorGameKitNetworkingLanWebSocketTransport.connect(
      descriptor,
      connectTimeout: connectTimeout,
      maxMessageBytes: maxMessageBytes,
    );
    final client = DihorGameKitNetworkingClient._(
      transport,
      messageIdFactory ?? _defaultMessageIdFactory,
      normalizedPeerId,
    );

    try {
      await client._connect(handshakeTimeout);
      return client;
    } catch (_) {
      await client.close();
      rethrow;
    }
  }

  DihorGameKitNetworkingConnectionState get state => _state;

  DihorGameKitNetworkingConnectionInfo? get activeConnection => _connection;

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
    if (_state != DihorGameKitNetworkingConnectionState.connected ||
        !_transport.isOpen) {
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

  Future<void> close() async {
    if (_state == DihorGameKitNetworkingConnectionState.closed) return;

    _setState(DihorGameKitNetworkingConnectionState.closing);
    _pendingConnection?.completeError(
      StateError('Connection closed before handshake completed.'),
    );
    _pendingConnection = null;

    try {
      await _transport.close(reason: 'client-closed');
    } finally {
      await _subscription.cancel();
      _connection = null;
      _setState(DihorGameKitNetworkingConnectionState.closed);
      await _closeControllers();
    }
  }

  Future<void> _connect(Duration handshakeTimeout) async {
    _setState(DihorGameKitNetworkingConnectionState.connecting);
    final pending = Completer<DihorGameKitNetworkingConnectionInfo>();
    _pendingConnection = pending;

    final envelope = DihorGameKitNetworkingEnvelope.create(
      type: DihorGameKitNetworkingMessageTypes.connectRequest,
      messageId: _nextMessageId(),
      payload: <String, Object?>{
        if (_requestedPeerId != null) 'peerId': _requestedPeerId,
      },
    );

    await _sendEnvelope(envelope);

    try {
      await pending.future.timeout(
        handshakeTimeout,
        onTimeout: () => throw TimeoutException(
          'Timed out waiting for protocol-v2 connect response.',
          handshakeTimeout,
        ),
      );
    } finally {
      if (identical(_pendingConnection, pending)) {
        _pendingConnection = null;
      }
    }
  }

  Future<void> _sendEnvelope(DihorGameKitNetworkingEnvelope envelope) =>
      _transport.send(
        utf8.encode(envelope.toJsonString()),
        type: DihorGameKitNetworkingTransportMessageType.text,
      );

  void _handleTransportMessage(
    DihorGameKitNetworkingTransportMessage transportMessage,
  ) {
    try {
      final json = utf8.decode(transportMessage.payload);
      final envelope = DihorGameKitNetworkingEnvelope.parse(json);
      _messageController.add(envelope);

      switch (envelope.type) {
        case DihorGameKitNetworkingMessageTypes.connectAccepted:
          _acceptConnection(envelope);
          break;
        case DihorGameKitNetworkingMessageTypes.connectRejected:
          _rejectConnection(envelope);
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

    if (_requestedPeerId != null &&
        peerId != null &&
        peerId != _requestedPeerId) {
      throw FormatException(
        'Connection was accepted for a different peer identity.',
      );
    }

    final info = DihorGameKitNetworkingConnectionInfo(
      connectionId: connectionId,
      peerId: peerId,
      resumeToken: resumeToken,
    );
    _connection = info;
    _setState(DihorGameKitNetworkingConnectionState.connected);

    final pending = _pendingConnection;
    if (pending != null && !pending.isCompleted) {
      pending.complete(info);
    }
  }

  void _rejectConnection(DihorGameKitNetworkingEnvelope envelope) {
    final error = DihorGameKitNetworkingConnectionRejectedException(
      code: _payloadString(envelope.payload, 'code', required: true)!,
      reason: _payloadString(envelope.payload, 'reason'),
    );
    _errorController.add(error);

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
    if (!_controllersClosed) {
      _errorController.addError(error, stackTrace);
    }

    final pending = _pendingConnection;
    if (pending != null && !pending.isCompleted) {
      pending.completeError(error, stackTrace);
    }
  }

  void _handleTransportError(Object error, StackTrace stackTrace) {
    if (!_controllersClosed) {
      _errorController.addError(error, stackTrace);
    }

    final pending = _pendingConnection;
    if (pending != null && !pending.isCompleted) {
      pending.completeError(error, stackTrace);
    }
  }

  void _handleTransportDone() {
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
    _setState(DihorGameKitNetworkingConnectionState.closed);
    unawaited(_closeControllers());
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
    await Future.wait<void>([
      _messageController.close(),
      _applicationController.close(),
      _stateController.close(),
      _errorController.close(),
    ]);
  }

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
