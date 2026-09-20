final class DihorGameKitNetworkingResumeCredential {
  DihorGameKitNetworkingResumeCredential({
    required String scope,
    required String peerId,
    required String resumeToken,
  })  : scope = _required(scope, 'scope'),
        peerId = _required(peerId, 'peerId'),
        resumeToken = _required(resumeToken, 'resumeToken');

  final String scope;
  final String peerId;
  final String resumeToken;
}

abstract interface class DihorGameKitNetworkingIdentityStore {
  String? getPeerId();

  void setPeerId(String peerId);

  DihorGameKitNetworkingResumeCredential? getResumeCredential(String scope);

  void setResumeCredential(DihorGameKitNetworkingResumeCredential credential);

  void clearResumeCredential(String scope);
}

final class MemoryDihorGameKitNetworkingIdentityStore
    implements DihorGameKitNetworkingIdentityStore {
  String? _peerId;
  final Map<String, DihorGameKitNetworkingResumeCredential> _resume =
      <String, DihorGameKitNetworkingResumeCredential>{};

  @override
  String? getPeerId() => _peerId;

  @override
  void setPeerId(String peerId) {
    _peerId = _required(peerId, 'peerId');
  }

  @override
  DihorGameKitNetworkingResumeCredential? getResumeCredential(String scope) =>
      _resume[_required(scope, 'scope')];

  @override
  void setResumeCredential(
    DihorGameKitNetworkingResumeCredential credential,
  ) {
    _resume[credential.scope] = credential;
  }

  @override
  void clearResumeCredential(String scope) {
    _resume.remove(_required(scope, 'scope'));
  }
}

String _required(String value, String name) {
  final normalized = value.trim();
  if (normalized.isEmpty) {
    throw ArgumentError.value(value, name, 'Must be non-empty.');
  }
  return normalized;
}
