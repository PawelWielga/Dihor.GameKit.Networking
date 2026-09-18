import 'dart:convert';

const gameKitNetworkingProtocolVersion = 2;

final class GameKitNetworkingEnvelope {
  GameKitNetworkingEnvelope(
      {required this.type,
      required this.protocolVersion,
      required this.messageId,
      required this.payload,
      this.correlationId});

  factory GameKitNetworkingEnvelope.parse(String source) {
    final json = _jsonObject(jsonDecode(source), 'envelope');
    final version = _requiredInt(json, 'protocolVersion');
    if (version != gameKitNetworkingProtocolVersion) {
      throw FormatException(
          'Unsupported Dihor.GameKit.Networking protocol version: $version.');
    }
    return GameKitNetworkingEnvelope(
      type: _requiredString(json, 'type'),
      protocolVersion: version,
      messageId: _requiredString(json, 'messageId'),
      correlationId: _optionalString(json, 'correlationId'),
      payload: _jsonObject(json['payload'], 'payload'),
    );
  }

  final String type;
  final int protocolVersion;
  final String messageId;
  final String? correlationId;
  final Map<String, Object?> payload;
}

final class GameKitNetworkingConnectionDescriptor {
  GameKitNetworkingConnectionDescriptor(
      {required int protocolVersion,
      required String transport,
      required String endpoint,
      String? channelId})
      : protocolVersion = protocolVersion,
        transport = _normalizeRequired(transport, 'transport'),
        endpoint = _normalizeEndpoint(endpoint),
        channelId = _normalizeOptional(channelId, 'channelId') {
    if (protocolVersion <= 0)
      throw const FormatException('protocolVersion must be positive.');
  }

  factory GameKitNetworkingConnectionDescriptor.parseJson(String source) =>
      GameKitNetworkingConnectionDescriptor.fromJsonObject(
          _jsonObject(jsonDecode(source), 'connection descriptor'));

  factory GameKitNetworkingConnectionDescriptor.fromJsonObject(
          Map<String, Object?> json) =>
      GameKitNetworkingConnectionDescriptor(
        protocolVersion: _requiredInt(json, 'protocolVersion'),
        transport: _requiredString(json, 'transport'),
        endpoint: _requiredString(json, 'endpoint'),
        channelId: _optionalString(json, 'channelId'),
      );

  factory GameKitNetworkingConnectionDescriptor.parseUri(String source) {
    final uri = Uri.tryParse(source.trim());
    if (uri == null ||
        uri.scheme != 'dihor-gamekit-networking' ||
        uri.host != 'connect') {
      throw const FormatException(
          'Invalid Dihor.GameKit.Networking connection URI.');
    }
    final query = _parseQuery(uri.query);
    String required(String key) {
      final value = query[key];
      if (value == null || value.trim().isEmpty)
        throw FormatException('Connection URI is missing $key.');
      return value;
    }

    final version = int.tryParse(required('protocolVersion'));
    if (version == null)
      throw const FormatException(
          'Connection URI protocolVersion must be an integer.');
    return GameKitNetworkingConnectionDescriptor(
      protocolVersion: version,
      transport: required('transport'),
      endpoint: required('endpoint'),
      channelId: query['channelId'],
    );
  }

  final int protocolVersion;
  final String transport;
  final String endpoint;
  final String? channelId;

  Map<String, Object?> toJsonObject() => <String, Object?>{
        'protocolVersion': protocolVersion,
        'transport': transport,
        'endpoint': endpoint,
        if (channelId != null) 'channelId': channelId,
      };

  String toJsonString() => jsonEncode(toJsonObject());

  String toUriString() {
    String encode(String value) => Uri.encodeComponent(value);
    return 'dihor-gamekit-networking://connect?protocolVersion=$protocolVersion'
        '&transport=${encode(transport)}'
        '&endpoint=${encode(endpoint)}'
        '${channelId == null ? '' : '&channelId=${encode(channelId!)}'}';
  }
}

final class GameKitNetworkingDiscoveryAnnouncement {
  GameKitNetworkingDiscoveryAnnouncement(this.descriptor);

  factory GameKitNetworkingDiscoveryAnnouncement.parse(String source) {
    final json = _jsonObject(jsonDecode(source), 'discovery announcement');
    if (_requiredString(json, 'type') != 'connection.discovery.announce') {
      throw const FormatException('Invalid discovery announcement type.');
    }
    final version = _requiredInt(json, 'protocolVersion');
    if (version != gameKitNetworkingProtocolVersion)
      throw FormatException(
          'Unsupported Dihor.GameKit.Networking protocol version: $version.');
    final descriptor = GameKitNetworkingConnectionDescriptor.fromJsonObject(
        _jsonObject(json['descriptor'], 'descriptor'));
    if (descriptor.protocolVersion != version)
      throw const FormatException(
          'Discovery and descriptor protocol versions must match.');
    return GameKitNetworkingDiscoveryAnnouncement(descriptor);
  }

  final GameKitNetworkingConnectionDescriptor descriptor;
}

final class GameKitNetworkingMessageSequenceGate {
  int _lastAcceptedSequence = 0;
  int get lastAcceptedSequence => _lastAcceptedSequence;

  bool tryAccept(int sequence) {
    if (sequence <= 0)
      throw ArgumentError.value(sequence, 'sequence', 'Must be positive.');
    if (sequence <= _lastAcceptedSequence) return false;
    _lastAcceptedSequence = sequence;
    return true;
  }
}

Map<String, Object?> _jsonObject(Object? value, String name) {
  if (value is! Map) throw FormatException('$name must be a JSON object.');
  final result = <String, Object?>{};
  for (final entry in value.entries) {
    if (entry.key is! String)
      throw FormatException('$name contains a non-string key.');
    result[entry.key as String] = entry.value;
  }
  return result;
}

Map<String, String> _parseQuery(String query) {
  final result = <String, String>{};
  for (final segment in query.split('&')) {
    if (segment.isEmpty) continue;
    final separator = segment.indexOf('=');
    if (separator <= 0)
      throw const FormatException('Invalid connection URI query.');
    final key = Uri.decodeComponent(segment.substring(0, separator));
    final value = Uri.decodeComponent(segment.substring(separator + 1));
    if (result.containsKey(key))
      throw FormatException("Duplicate connection URI field '$key'.");
    result[key] = value;
  }
  return result;
}

String _normalizeRequired(String value, String name) {
  final normalized = value.trim();
  if (normalized.isEmpty)
    throw FormatException('$name must be a non-empty string.');
  return normalized;
}

String? _normalizeOptional(String? value, String name) =>
    value == null ? null : _normalizeRequired(value, name);

String _normalizeEndpoint(String value) {
  final normalized = _normalizeRequired(value, 'endpoint');
  final uri = Uri.tryParse(normalized);
  if (uri == null || !uri.hasScheme || uri.scheme == 'file') {
    throw const FormatException(
        'Connection descriptor endpoint must be an absolute non-file URI.');
  }
  return uri.toString();
}

String _requiredString(Map<String, Object?> json, String key) {
  final value = json[key];
  if (value is! String || value.trim().isEmpty)
    throw FormatException('$key must be a non-empty string.');
  return value;
}

String? _optionalString(Map<String, Object?> json, String key) {
  final value = json[key];
  if (value == null) return null;
  if (value is! String || value.trim().isEmpty)
    throw FormatException('$key must be a non-empty string when present.');
  return value;
}

int _requiredInt(Map<String, Object?> json, String key) {
  final value = json[key];
  if (value is! int) throw FormatException('$key must be an integer.');
  return value;
}
