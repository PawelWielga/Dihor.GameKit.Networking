# Versioning policy

PartyGameKit uses Semantic Versioning for distributed packages and a separate integer version for the wire protocol.

## Package versions

The first packaged release line is `0.1.0-preview.N`. It is intentionally pre-1.0: public APIs are usable but may still change when later transports prove a concrete cross-game need.

For the preview line:

- patch/prerelease increments may add compatible APIs, fixes and documentation;
- removing or changing a public API requires an explicit changelog entry and a new preview version;
- a stable `1.0.0` is not implied by completion of v0.1;
- all packages built from one repository release use the same base prerelease version where practical.

## Protocol versions

Package version and protocol version are independent. PartyGameKit `0.1.0-preview.1` implements wire protocol `1`.

A wire-incompatible change requires a new protocol version even when the package is still pre-1.0. Clients must reject unsupported protocol versions before using payload data. Additive game-owned payload changes do not change PartyGameKit's infrastructure protocol version unless they alter the generic envelope/session contract.

Canonical files under `protocol/fixtures/` are the cross-language compatibility vectors for C#, Dart and TypeScript.

## Compatibility promise for v0.1 previews

Within protocol v1, fixes should preserve the existing serialized field names, enum strings, join-descriptor form and snapshot-target semantics. If that cannot be done safely, the protocol version must be incremented instead of silently reinterpreting v1.

Game rules and opaque game-owned commands are versioned by each game, not by PartyGameKit Core.
