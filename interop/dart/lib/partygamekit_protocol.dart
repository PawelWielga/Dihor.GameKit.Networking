import 'dart:convert';

const partyGameKitProtocolVersion = 1;

final class PartyGameKitEnvelope {
  PartyGameKitEnvelope({
    required this.type,
    required this.protocolVersion,
    required this.messageId,
    required this.payload,
    this.correlationId,
  });

  factory PartyGameKitEnvelope.parse(String source) {
    final json = _jsonObject(jsonDecode(source), 'envelope');
    final protocolVersion = _requiredInt(json, 'protocolVersion');
    if (protocolVersion != partyGameKitProtocolVersion) {
      throw FormatException(
        'Unsupported PartyGameKit protocol version: $protocolVersion.',
      );
    }

    return PartyGameKitEnvelope(
      type: _requiredString(json, 'type'),
      protocolVersion: protocolVersion,
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

final class PartyGameKitJoinDescriptor {
  PartyGameKitJoinDescriptor({
    required int protocolVersion,
    required String roomId,
    required String joinCode,
    required String transport,
    required String endpoint,
  })  : protocolVersion = protocolVersion,
        roomId = _normalizeRequired(roomId, 'roomId'),
        joinCode = _normalizeRequired(joinCode, 'joinCode').toUpperCase(),
        transport = _normalizeRequired(transport, 'transport').toLowerCase(),
        endpoint = _normalizeEndpoint(endpoint) {
    if (protocolVersion != partyGameKitProtocolVersion) {
      throw FormatException(
        'Unsupported PartyGameKit protocol version: $protocolVersion.',
      );
    }
  }

  factory PartyGameKitJoinDescriptor.parseJson(String source) {
    return PartyGameKitJoinDescriptor.fromJsonObject(
      _jsonObject(jsonDecode(source), 'join descriptor'),
    );
  }

  factory PartyGameKitJoinDescriptor.fromJsonObject(
    Map<String, Object?> json,
  ) {
    return PartyGameKitJoinDescriptor(
      protocolVersion: _requiredInt(json, 'protocolVersion'),
      roomId: _requiredString(json, 'roomId'),
      joinCode: _requiredString(json, 'joinCode'),
      transport: _requiredString(json, 'transport'),
      endpoint: _requiredString(json, 'endpoint'),
    );
  }

  factory PartyGameKitJoinDescriptor.parseUri(String source) {
    final uri = Uri.tryParse(source.trim());
    if (uri == null || uri.scheme != 'partygamekit' || uri.host != 'join') {
      throw const FormatException('Invalid PartyGameKit join URI.');
    }

    final query = _parseQuery(uri.query);

    String requiredSingle(String key) {
      final value = query[key];
      if (value == null || value.trim().isEmpty) {
        throw FormatException('Join URI must contain exactly one $key.');
      }
      return value;
    }

    final version = int.tryParse(requiredSingle('protocolVersion'));
    if (version == null) {
      throw const FormatException(
          'Join URI protocolVersion must be an integer.');
    }

    return PartyGameKitJoinDescriptor(
      protocolVersion: version,
      roomId: requiredSingle('roomId'),
      joinCode: requiredSingle('joinCode'),
      transport: requiredSingle('transport'),
      endpoint: requiredSingle('endpoint'),
    );
  }

  final int protocolVersion;
  final String roomId;
  final String joinCode;
  final String transport;
  final String endpoint;

  Map<String, Object?> toJsonObject() => <String, Object?>{
        'protocolVersion': protocolVersion,
        'roomId': roomId,
        'joinCode': joinCode,
        'transport': transport,
        'endpoint': endpoint,
      };

  String toJsonString() => jsonEncode(toJsonObject());

  String toUriString() {
    String encode(String value) => Uri.encodeComponent(value);

    return 'partygamekit://join?protocolVersion=$protocolVersion'
        '&roomId=${encode(roomId)}'
        '&joinCode=${encode(joinCode)}'
        '&transport=${encode(transport)}'
        '&endpoint=${encode(endpoint)}';
  }
}

final class PartyGameKitDiscoveryAnnouncement {
  PartyGameKitDiscoveryAnnouncement(this.descriptor);

  factory PartyGameKitDiscoveryAnnouncement.parse(String source) {
    final json = _jsonObject(jsonDecode(source), 'discovery announcement');
    if (_requiredString(json, 'type') != 'session.discovery.announce') {
      throw const FormatException('Invalid discovery announcement type.');
    }
    final protocolVersion = _requiredInt(json, 'protocolVersion');
    if (protocolVersion != partyGameKitProtocolVersion) {
      throw FormatException(
        'Unsupported PartyGameKit protocol version: $protocolVersion.',
      );
    }

    final descriptor = PartyGameKitJoinDescriptor.fromJsonObject(
      _jsonObject(json['descriptor'], 'descriptor'),
    );
    if (descriptor.protocolVersion != protocolVersion) {
      throw const FormatException(
        'Discovery and descriptor protocol versions must match.',
      );
    }
    return PartyGameKitDiscoveryAnnouncement(descriptor);
  }

  final PartyGameKitJoinDescriptor descriptor;
}

final class PartyGameKitSnapshotSequenceGate {
  int _lastAcceptedSequence = 0;

  int get lastAcceptedSequence => _lastAcceptedSequence;

  bool tryAccept(int sequence) {
    if (sequence <= 0) {
      throw ArgumentError.value(sequence, 'sequence', 'Must be positive.');
    }
    if (sequence <= _lastAcceptedSequence) {
      return false;
    }
    _lastAcceptedSequence = sequence;
    return true;
  }
}

Map<String, Object?> _jsonObject(Object? value, String name) {
  if (value is! Map) {
    throw FormatException('$name must be a JSON object.');
  }

  final result = <String, Object?>{};
  for (final entry in value.entries) {
    final key = entry.key;
    if (key is! String) {
      throw FormatException('$name contains a non-string key.');
    }
    result[key] = entry.value;
  }
  return result;
}

Map<String, String> _parseQuery(String query) {
  final result = <String, String>{};
  for (final segment in query.split('&')) {
    if (segment.isEmpty) continue;
    final separator = segment.indexOf('=');
    if (separator <= 0) {
      throw const FormatException('Invalid join URI query.');
    }

    final key = Uri.decodeComponent(segment.substring(0, separator));
    final value = Uri.decodeComponent(segment.substring(separator + 1));
    if (result.containsKey(key)) {
      throw FormatException("Duplicate join URI field '$key'.");
    }
    result[key] = value;
  }
  return result;
}

String _normalizeRequired(String value, String name) {
  final normalized = value.trim();
  if (normalized.isEmpty) {
    throw FormatException('$name must be a non-empty string.');
  }
  return normalized;
}

String _normalizeEndpoint(String value) {
  final normalized = _normalizeRequired(value, 'endpoint');
  final uri = Uri.tryParse(normalized);
  if (uri == null || !uri.hasScheme) {
    throw const FormatException('Join descriptor endpoint must be absolute.');
  }
  return uri.toString();
}

String _requiredString(Map<String, Object?> json, String key) {
  final value = json[key];
  if (value is! String || value.trim().isEmpty) {
    throw FormatException('$key must be a non-empty string.');
  }
  return value;
}

String? _optionalString(Map<String, Object?> json, String key) {
  final value = json[key];
  if (value == null) return null;
  if (value is! String || value.trim().isEmpty) {
    throw FormatException('$key must be a non-empty string when present.');
  }
  return value;
}

int _requiredInt(Map<String, Object?> json, String key) {
  final value = json[key];
  if (value is! int) {
    throw FormatException('$key must be an integer.');
  }
  return value;
}
