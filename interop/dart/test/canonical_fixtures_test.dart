import 'dart:io';

import 'package:dihor_gamekit_networking_protocol/dihor_gamekit_networking_protocol.dart';
import 'package:test/test.dart';

void main() {
  group('canonical Dihor.GameKit.Networking v2 fixtures', () {
    test('all communication envelopes use the supported protocol version', () {
      const envelopeFixtures = <String>[
        'v2-connect-request.json',
        'v2-resume-request.json',
        'v2-heartbeat.json',
        'v2-application-message.json',
      ];

      for (final fixtureName in envelopeFixtures) {
        final envelope = DihorGameKitNetworkingEnvelope.parse(_fixture(fixtureName));
        expect(envelope.protocolVersion, dihorGameKitNetworkingProtocolVersion);
        expect(envelope.type, isNotEmpty);
        expect(envelope.messageId, isNotEmpty);
      }
    });

    test('connect and resume preserve neutral peer identity', () {
      final connect =
          DihorGameKitNetworkingEnvelope.parse(_fixture('v2-connect-request.json'));
      final resume =
          DihorGameKitNetworkingEnvelope.parse(_fixture('v2-resume-request.json'));

      expect(connect.type, 'connection.connect.request');
      expect(resume.type, 'connection.resume.request');
      expect(connect.payload['peerId'], 'peer-a');
      expect(resume.payload['peerId'], connect.payload['peerId']);
      expect(resume.payload['resumeToken'], 'resume-token');
      expect(connect.payload.containsKey('playerId'), isFalse);
      expect(connect.payload.containsKey('role'), isFalse);
    });

    test('application message payload remains consumer owned and opaque', () {
      final message = DihorGameKitNetworkingEnvelope.parse(
          _fixture('v2-application-message.json'));
      expect(message.type, 'application.message');
      expect(message.payload['applicationType'], isNotEmpty);
      expect(message.payload.containsKey('data'), isTrue);
    });

    test('connection descriptor matches canonical JSON and deterministic URI',
        () {
      final canonical = _fixture('v2-connection-descriptor.json');
      final descriptor =
          DihorGameKitNetworkingConnectionDescriptor.parseJson(canonical);

      expect(descriptor.protocolVersion, 2);
      expect(descriptor.transport, 'lan-websocket');
      expect(descriptor.endpoint, 'ws://192.168.1.10:45678/partygamekit');
      expect(descriptor.channelId, 'channel-a');
      expect(descriptor.toJsonString(), canonical);

      const expectedUri =
          'partygamekit://connect?protocolVersion=2&transport=lan-websocket'
          '&endpoint=ws%3A%2F%2F192.168.1.10%3A45678%2Fpartygamekit'
          '&channelId=channel-a';
      expect(descriptor.toUriString(), expectedUri);
      final fromUri =
          DihorGameKitNetworkingConnectionDescriptor.parseUri(expectedUri);
      expect(fromUri.toJsonString(), canonical);
    });

    test('descriptor parser preserves literal plus as data', () {
      final descriptor = DihorGameKitNetworkingConnectionDescriptor.parseUri(
        'partygamekit://connect?protocolVersion=2&transport=lan-websocket'
        '&endpoint=ws%3A%2F%2F127.0.0.1%3A5042%2Fpartygamekit'
        '&channelId=channel%2Ba',
      );
      expect(descriptor.channelId, 'channel+a');
    });

    test('discovery announcement carries only technical descriptor metadata',
        () {
      final announcement = DihorGameKitNetworkingDiscoveryAnnouncement.parse(
        _fixture('v2-discovery-announcement.json'),
      );

      expect(announcement.descriptor.protocolVersion, 2);
      expect(announcement.descriptor.transport, 'lan-websocket');
      expect(announcement.descriptor.channelId, 'channel-a');
    });

    test('generic sequence gate rejects stale or equal messages', () {
      final gate = DihorGameKitNetworkingMessageSequenceGate();
      expect(gate.tryAccept(42), isTrue);
      expect(gate.tryAccept(42), isFalse);
      expect(gate.tryAccept(41), isFalse);
      expect(gate.tryAccept(43), isTrue);
      expect(gate.lastAcceptedSequence, 43);
    });

    test('unsupported protocol version is rejected before payload use', () {
      final incompatible = _fixture('v2-resume-request.json').replaceFirst(
        '"protocolVersion":2',
        '"protocolVersion":999',
      );

      expect(
        () => DihorGameKitNetworkingEnvelope.parse(incompatible),
        throwsA(isA<FormatException>()),
      );
    });
  });
}

String _fixture(String name) {
  return File('../../protocol/fixtures/$name').readAsStringSync().trim();
}
