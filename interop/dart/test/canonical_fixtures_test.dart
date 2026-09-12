import 'dart:io';

import 'package:partygamekit_protocol_conformance/partygamekit_protocol.dart';
import 'package:test/test.dart';

void main() {
  group('canonical PartyGameKit v1 fixtures', () {
    test('all infrastructure envelopes use the supported protocol version', () {
      const envelopeFixtures = <String>[
        'join-success.json',
        'join-rejected.json',
        'rejoin.json',
        'rejoin-rejected.json',
        'heartbeat.json',
        'snapshot-public.json',
        'snapshot-player.json',
      ];

      for (final fixtureName in envelopeFixtures) {
        final envelope = PartyGameKitEnvelope.parse(_fixture(fixtureName));
        expect(envelope.protocolVersion, partyGameKitProtocolVersion);
        expect(envelope.type, isNotEmpty);
        expect(envelope.messageId, isNotEmpty);
      }
    });

    test('join and rejoin preserve stable player identity across connections',
        () {
      final join = PartyGameKitEnvelope.parse(_fixture('join-success.json'));
      final rejoin = PartyGameKitEnvelope.parse(_fixture('rejoin.json'));

      expect(join.type, 'session.join.accepted');
      expect(rejoin.type, 'session.rejoin.request');
      expect(join.payload['playerId'], 'player-001');
      expect(rejoin.payload['playerId'], join.payload['playerId']);
      expect(join.payload['connectionId'], 'connection-002');
      expect(join.payload['connectionId'], isNot(join.payload['playerId']));
      expect(join.payload['reconnectToken'], 'resume-token-001');
      expect(rejoin.payload['reconnectToken'], 'opaque-reconnect-token');
      expect(rejoin.payload['lastSeenSnapshotSequence'], 41);
    });

    test(
        'snapshot target stays generic and stale or equal sequences are rejected',
        () {
      final publicSnapshot =
          PartyGameKitEnvelope.parse(_fixture('snapshot-public.json'));
      final playerSnapshot =
          PartyGameKitEnvelope.parse(_fixture('snapshot-player.json'));

      expect(publicSnapshot.type, 'state.snapshot');
      expect(playerSnapshot.type, 'state.snapshot');
      expect(publicSnapshot.payload['sequence'], 42);
      expect(playerSnapshot.payload['sequence'], 42);

      final publicTarget = publicSnapshot.payload['target'] as Map;
      final playerTarget = playerSnapshot.payload['target'] as Map;
      expect(publicTarget['kind'], 'public');
      expect(playerTarget['kind'], 'player');
      expect(playerTarget['playerId'], 'player-001');

      final gate = PartyGameKitSnapshotSequenceGate();
      expect(gate.tryAccept(42), isTrue);
      expect(gate.tryAccept(42), isFalse);
      expect(gate.tryAccept(41), isFalse);
      expect(gate.tryAccept(43), isTrue);
      expect(gate.lastAcceptedSequence, 43);
    });

    test('join descriptor matches canonical JSON and deterministic QR URI', () {
      final canonical = _fixture('join-descriptor.json');
      final descriptor = PartyGameKitJoinDescriptor.parseJson(canonical);

      expect(descriptor.protocolVersion, 1);
      expect(descriptor.roomId, 'room-001');
      expect(descriptor.joinCode, 'ROOM42');
      expect(descriptor.transport, 'lan-websocket');
      expect(descriptor.endpoint, 'ws://192.168.1.20:5042/partygamekit');
      expect(descriptor.toJsonString(), canonical);

      const expectedUri =
          'partygamekit://join?protocolVersion=1&roomId=room-001'
          '&joinCode=ROOM42&transport=lan-websocket'
          '&endpoint=ws%3A%2F%2F192.168.1.20%3A5042%2Fpartygamekit';
      expect(descriptor.toUriString(), expectedUri);
      final fromUri = PartyGameKitJoinDescriptor.parseUri(expectedUri);
      expect(fromUri.toJsonString(), canonical);
    });

    test('descriptor canonicalization matches C# percent escaping', () {
      final descriptor = PartyGameKitJoinDescriptor(
        protocolVersion: 1,
        roomId: ' room 001 ',
        joinCode: ' room 42 ',
        transport: ' LAN WebSocket ',
        endpoint: ' ws://192.168.1.20:5042/partygamekit ',
      );

      expect(descriptor.roomId, 'room 001');
      expect(descriptor.joinCode, 'ROOM 42');
      expect(descriptor.transport, 'lan websocket');
      expect(descriptor.endpoint, 'ws://192.168.1.20:5042/partygamekit');
      expect(
        descriptor.toUriString(),
        'partygamekit://join?protocolVersion=1&roomId=room%20001'
        '&joinCode=ROOM%2042&transport=lan%20websocket'
        '&endpoint=ws%3A%2F%2F192.168.1.20%3A5042%2Fpartygamekit',
      );

      final roundTrip = PartyGameKitJoinDescriptor.parseUri(
        descriptor.toUriString(),
      );
      expect(roundTrip.roomId, descriptor.roomId);
      expect(roundTrip.joinCode, descriptor.joinCode);
      expect(roundTrip.transport, descriptor.transport);
      expect(roundTrip.endpoint, descriptor.endpoint);
    });

    test('URI parser preserves literal plus as data', () {
      final descriptor = PartyGameKitJoinDescriptor.parseUri(
        'partygamekit://join?protocolVersion=1&roomId=room%2B001'
        '&joinCode=ROOM42&transport=lan-websocket'
        '&endpoint=ws%3A%2F%2F127.0.0.1%3A5042%2Fpartygamekit',
      );

      expect(descriptor.roomId, 'room+001');
    });

    test('discovery announcement carries only the portable descriptor', () {
      final announcement = PartyGameKitDiscoveryAnnouncement.parse(
        _fixture('discovery-announcement.json'),
      );

      expect(announcement.descriptor.roomId, 'room-001');
      expect(announcement.descriptor.joinCode, 'ROOM42');
      expect(announcement.descriptor.transport, 'lan-websocket');
    });

    test('unsupported protocol version is rejected before payload use', () {
      final incompatible = _fixture('rejoin.json').replaceFirst(
        '"protocolVersion":1',
        '"protocolVersion":999',
      );

      expect(
        () => PartyGameKitEnvelope.parse(incompatible),
        throwsA(isA<FormatException>()),
      );
    });
  });
}

String _fixture(String name) {
  return File('../../protocol/fixtures/$name').readAsStringSync().trim();
}
