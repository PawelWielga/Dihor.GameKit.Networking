import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:dihor_gamekit_networking_protocol/dihor_gamekit_networking_protocol.dart';

import 'reconnect.dart';
import 'transport.dart';

final class DihorGameKitNetworkingLanWebSocketTransport
    implements DihorGameKitNetworkingClientTransport {
  DihorGameKitNetworkingLanWebSocketTransport._(
    this._socket,
    this.maxMessageBytes,
  ) {
    _subscription = _socket.listen(
      _handleData,
      onError: _handleError,
      onDone: _handleDone,
      cancelOnError: false,
    );
  }

  static const transportName = 'lan-websocket';
  static const defaultMaxMessageBytes = 256 * 1024;
  static const defaultConnectTimeout = Duration(seconds: 5);

  final WebSocket _socket;
  final int maxMessageBytes;
  final StreamController<DihorGameKitNetworkingTransportMessage>
      _messageController =
      StreamController<DihorGameKitNetworkingTransportMessage>();

  late final StreamSubscription<dynamic> _subscription;
  bool _closed = false;
  bool _messageControllerClosed = false;

  static Future<DihorGameKitNetworkingLanWebSocketTransport> connect(
    DihorGameKitNetworkingConnectionDescriptor descriptor, {
    Duration connectTimeout = defaultConnectTimeout,
    int maxMessageBytes = defaultMaxMessageBytes,
    DihorGameKitNetworkingCancellationSignal? cancellation,
  }) async {
    if (descriptor.protocolVersion != dihorGameKitNetworkingProtocolVersion) {
      throw FormatException(
        'Unsupported Dihor.GameKit.Networking protocol version: '
        '${descriptor.protocolVersion}.',
      );
    }
    if (descriptor.transport != transportName) {
      throw FormatException(
        'Connection descriptor transport must be $transportName.',
      );
    }
    if (connectTimeout <= Duration.zero) {
      throw ArgumentError.value(
        connectTimeout,
        'connectTimeout',
        'Must be positive.',
      );
    }
    if (maxMessageBytes <= 0) {
      throw ArgumentError.value(
        maxMessageBytes,
        'maxMessageBytes',
        'Must be positive.',
      );
    }

    final endpoint = Uri.parse(descriptor.endpoint);
    if (endpoint.scheme != 'ws' && endpoint.scheme != 'wss') {
      throw const FormatException(
        'LAN WebSocket endpoint must use ws or wss.',
      );
    }

    cancellation?.throwIfCancellationRequested();
    final socketFuture = WebSocket.connect(endpoint.toString());

    try {
      final socket = await Future.any<WebSocket>(<Future<WebSocket>>[
        socketFuture,
        Future<WebSocket>.delayed(
          connectTimeout,
          () => throw TimeoutException(
            'Timed out connecting to LAN WebSocket endpoint.',
            connectTimeout,
          ),
        ),
        if (cancellation != null)
          cancellation.whenCancelled.then<WebSocket>(
            (_) =>
                throw const DihorGameKitNetworkingOperationCancelledException(),
          ),
      ]);
      cancellation?.throwIfCancellationRequested();
      return DihorGameKitNetworkingLanWebSocketTransport._(
        socket,
        maxMessageBytes,
      );
    } on DihorGameKitNetworkingOperationCancelledException {
      unawaited(_closeLateSocket(socketFuture));
      rethrow;
    } catch (error) {
      unawaited(_closeLateSocket(socketFuture));
      throw DihorGameKitNetworkingTransportException(
        'Unable to connect to LAN WebSocket endpoint.',
        error,
      );
    }
  }

  static Future<void> _closeLateSocket(Future<WebSocket> socketFuture) async {
    try {
      final socket = await socketFuture;
      await socket.close(
        WebSocketStatus.goingAway,
        'connection-attempt-abandoned',
      );
    } catch (_) {
      // The original connection attempt failed, so there is nothing to close.
    }
  }

  @override
  Stream<DihorGameKitNetworkingTransportMessage> get messages =>
      _messageController.stream;

  @override
  bool get isOpen => !_closed && _socket.readyState == WebSocket.open;

  @override
  Future<void> send(
    List<int> payload, {
    DihorGameKitNetworkingTransportMessageType type =
        DihorGameKitNetworkingTransportMessageType.binary,
  }) async {
    if (!isOpen) {
      throw DihorGameKitNetworkingTransportException(
        'LAN WebSocket transport is not open.',
      );
    }
    if (payload.length > maxMessageBytes) {
      throw ArgumentError.value(
        payload.length,
        'payload',
        'Message exceeds the configured LAN message limit.',
      );
    }

    try {
      switch (type) {
        case DihorGameKitNetworkingTransportMessageType.text:
          _socket.add(utf8.decode(payload));
          break;
        case DihorGameKitNetworkingTransportMessageType.binary:
          _socket.add(payload);
          break;
      }
    } catch (error) {
      throw DihorGameKitNetworkingTransportException(
        'Unable to send LAN WebSocket message.',
        error,
      );
    }
  }

  @override
  Future<void> close({int? code, String? reason}) async {
    if (_closed) {
      await _closeMessageController();
      return;
    }

    _closed = true;
    try {
      await _socket.close(code ?? WebSocketStatus.normalClosure, reason);
    } catch (_) {
      // Closing is best effort. Transport errors remain observable on messages.
    } finally {
      await _subscription.cancel();
      await _closeMessageController();
    }
  }

  void _handleData(dynamic data) {
    late final List<int> payload;
    late final DihorGameKitNetworkingTransportMessageType type;

    if (data is String) {
      payload = utf8.encode(data);
      type = DihorGameKitNetworkingTransportMessageType.text;
    } else if (data is List<int>) {
      payload = data;
      type = DihorGameKitNetworkingTransportMessageType.binary;
    } else {
      _messageController.addError(
        DihorGameKitNetworkingTransportException(
          'Unsupported LAN WebSocket message type: ${data.runtimeType}.',
        ),
      );
      return;
    }

    if (payload.length > maxMessageBytes) {
      _messageController.addError(
        DihorGameKitNetworkingTransportException(
          'LAN WebSocket message exceeds the configured limit of '
          '$maxMessageBytes bytes.',
        ),
      );
      unawaited(close(
        code: WebSocketStatus.messageTooBig,
        reason: 'message-too-large',
      ));
      return;
    }

    _messageController.add(
      DihorGameKitNetworkingTransportMessage(payload, type: type),
    );
  }

  void _handleError(Object error, StackTrace stackTrace) {
    if (_messageControllerClosed) return;
    _messageController.addError(
      DihorGameKitNetworkingTransportException(
        'LAN WebSocket transport error.',
        error,
      ),
      stackTrace,
    );
  }

  void _handleDone() {
    _closed = true;
    unawaited(_closeMessageController());
  }

  Future<void> _closeMessageController() async {
    if (_messageControllerClosed) return;
    _messageControllerClosed = true;
    await _messageController.close();
  }
}
