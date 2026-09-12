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
    required this.protocolVersion,
    required this.roomId,
    required this.joinCode,
    required this.transport,
    required this.endpoint,
  }) {
    if (protocolVersion != partyGameKitProtocolVersion) {
      throw FormatException(
        'Unsupported PartyGameKit protocol version: $protocolVersion.',
      );
    }
    if (roomId.trim().isEmpty ||
        joinCode.trim().isEmpty ||
        transport.trim().isEmpty ||
        endpoint.trim().isEmpty) {
      throw const FormatException('Join descriptor fields cannot be empty.');
    }
    final endpointUri = Uri.tryParse(endpoint);
    if (endpointUri == null || !endpointUri.hasScheme) {
      throw const FormatException('Join descriptor endpoint must be absolute.');
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

    String requiredSingle(String key) {
      final values = uri.queryParametersAll[key];
      if (values == null ||
          values.length != 1 ||
          values.single.trim().isEmpty) {
        throw FormatException('Join URI must contain exactly one $key.');
      }
      return values.single;
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
    String encode(String value) => Uri.encodeQueryComponent(value);

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
