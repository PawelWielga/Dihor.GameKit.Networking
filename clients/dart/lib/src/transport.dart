import 'dart:typed_data';

enum DihorGameKitNetworkingTransportMessageType {
  text,
  binary,
}

final class DihorGameKitNetworkingTransportMessage {
  DihorGameKitNetworkingTransportMessage(
    List<int> payload, {
    required this.type,
  }) : payload = Uint8List.fromList(payload);

  final Uint8List payload;
  final DihorGameKitNetworkingTransportMessageType type;
}

abstract interface class DihorGameKitNetworkingClientTransport {
  Stream<DihorGameKitNetworkingTransportMessage> get messages;

  bool get isOpen;

  Future<void> send(
    List<int> payload, {
    DihorGameKitNetworkingTransportMessageType type =
        DihorGameKitNetworkingTransportMessageType.binary,
  });

  Future<void> close({int? code, String? reason});
}

final class DihorGameKitNetworkingTransportException implements Exception {
  DihorGameKitNetworkingTransportException(this.message, [this.cause]);

  final String message;
  final Object? cause;

  @override
  String toString() => cause == null
      ? 'DihorGameKitNetworkingTransportException: $message'
      : 'DihorGameKitNetworkingTransportException: $message ($cause)';
}
