# Versioning policy

PartyGameKit uses Semantic Versioning for distributed packages and a separate integer version for the wire protocol.

## Package versions

The historical first packaged line is `0.1.0-preview.1`. The corrected communication-only line starts at `0.2.0-preview.1` because the public API break is deliberate and substantial.

For preview releases:

- compatible fixes/features may increment the prerelease suffix;
- removing or changing a public API requires an explicit changelog/migration entry and an appropriate SemVer prerelease version change;
- a stable `1.0.0` is not implied by completion of the current backlog;
- the supported .NET, TypeScript and Dart surfaces use the same release version for one PartyGameKit compatibility line.

## Protocol versions

Package version and protocol version are independent:

- `0.1.0-preview.1` used wire protocol `1` with room/player/session semantics;
- `0.2.0-preview.1` uses wire protocol `2` with neutral connection/resume/application-message semantics.

A wire-incompatible change requires a new protocol version even while package versions are pre-1.0. Clients must reject unsupported protocol versions before using payload data.

Consumer/application payloads are opaque to PartyGameKit and may be independently versioned by their owner without incrementing the PartyGameKit protocol unless the base communication envelope/control contract changes.

## Canonical fixtures

The active canonical fixture set is `protocol/fixtures/v2-*.json`.

C#, Dart and TypeScript tests consume those vectors as the single source of truth for the PartyGameKit v2 wire contract. Protocol-v1 fixtures were retired from the active tree when `[17]` completed cross-language migration; the v1 contract remains available in Git history and the `0.1.0-preview.1` tag/release.

## Compatibility promise

PartyGameKit does not silently reinterpret protocol v1 as v2. A v2 transport rejects v1 handshakes deterministically.

Within a protocol version, changes must preserve the documented envelope/control-message contract. Product/game rules and product identifiers are versioned by the consuming application, not by PartyGameKit Core.
