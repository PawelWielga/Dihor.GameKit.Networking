# Versioning policy

PartyGameKit uses Semantic Versioning for distributed packages and a separate integer version for the wire protocol.

## Package versions

The historical first packaged line is `0.1.0-preview.1`. The corrected communication-only .NET line starts at `0.2.0-preview.1` because the public API break is deliberate and substantial.

For preview releases:

- compatible fixes/features may increment the prerelease suffix;
- removing or changing a public API requires an explicit changelog/migration entry and an appropriate SemVer prerelease version change;
- a stable `1.0.0` is not implied by completion of the current backlog;
- all language packages should use the same release version once `[17]` restores cross-language alignment.

## Protocol versions

Package version and protocol version are independent:

- `0.1.0-preview.1` used wire protocol `1` with room/player/session semantics;
- `0.2.0-preview.1` uses wire protocol `2` with neutral connection/resume/application-message semantics.

A wire-incompatible change requires a new protocol version even while package versions are pre-1.0. Clients must reject unsupported protocol versions before using payload data.

Consumer/application payloads are opaque to PartyGameKit and may be independently versioned by their owner without incrementing the PartyGameKit protocol unless the base communication envelope/control contract changes.

## Canonical fixtures

Protocol-v2 fixtures are prefixed `v2-` during the `[16]` -> `[17]` transition. Legacy v1 fixture files remain temporarily so the still-v1 TypeScript and Dart implementations continue to verify their historical contract until `[17]` migrates them.

After `[17]`, the supported C#, Dart and TypeScript implementations must consume the same protocol-v2 fixture set.

## Compatibility promise

PartyGameKit does not silently reinterpret protocol v1 as v2. A v2 transport rejects v1 handshakes deterministically.

Product/game rules and product identifiers are versioned by the consuming application, not by PartyGameKit Core.
