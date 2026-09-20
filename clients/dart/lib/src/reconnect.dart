import 'dart:async';

final class DihorGameKitNetworkingCancellationSignal {
  final Completer<void> _cancelled = Completer<void>();

  bool get isCancellationRequested => _cancelled.isCompleted;

  Future<void> get whenCancelled => _cancelled.future;

  void cancel() {
    if (!_cancelled.isCompleted) {
      _cancelled.complete();
    }
  }

  void throwIfCancellationRequested() {
    if (isCancellationRequested) {
      throw const DihorGameKitNetworkingOperationCancelledException();
    }
  }
}

final class DihorGameKitNetworkingOperationCancelledException
    implements Exception {
  const DihorGameKitNetworkingOperationCancelledException();

  @override
  String toString() => 'DihorGameKitNetworkingOperationCancelledException';
}

final class DihorGameKitNetworkingReconnectPolicy {
  DihorGameKitNetworkingReconnectPolicy({
    this.maxAttempts = 3,
    this.delay = const Duration(milliseconds: 500),
  }) {
    if (maxAttempts <= 0) {
      throw ArgumentError.value(
        maxAttempts,
        'maxAttempts',
        'Must be positive.',
      );
    }
    if (delay.isNegative) {
      throw ArgumentError.value(
        delay,
        'delay',
        'Cannot be negative.',
      );
    }
  }

  final int maxAttempts;
  final Duration delay;
}

final class DihorGameKitNetworkingReconnectFailedException
    implements Exception {
  DihorGameKitNetworkingReconnectFailedException({
    required this.attempts,
    required this.lastError,
  });

  final int attempts;
  final Object lastError;

  @override
  String toString() => 'DihorGameKitNetworkingReconnectFailedException: '
      '$attempts reconnect attempts failed; last error: $lastError';
}
