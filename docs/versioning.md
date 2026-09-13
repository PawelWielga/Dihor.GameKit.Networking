# Versioning policy

PartyGameKit uses Semantic Versioning for distributed packages and a separate integer version for the wire protocol.

## Package versions

The historical first packaged line is `0.1.0-preview.1`. The corrected communication-only line starts at `0.2.0-preview.1` because the public API break is deliberate and substantial.

`0.2.0-preview.2` is the first compatible transport expansion on that corrected boundary. It adds optional SignalR relay connectivity without changing the protocol-v2 wire contract.

For preview releases:

- compatible fixes/features may increment the prerelease suffix;
- removing or changing a public API requires an explicit changelog/migration entry and an appropriate SemVer prerelease version change;
- a stable `1.0.0` is not implied by completion of the current backlog;
- the supported .NET, TypeScript and Dart surfaces use the same release version for one PartyGameKit compatibility line.

The repository's default package version is declared in `Directory.Build.targets`. CI derives artifact names from that value rather than duplicating a specific prerelease suffix. The explicit versions in the TypeScript/Dart manifests and package-only consumer must match the same release line.

## Protocol versions

Package version and protocol version are independent:

- `0.1.0-preview.1` used wire protocol `1` with room/player/session semantics;
- `0.2.0-preview.1` uses wire protocol `2` with neutral connection/resume/application-message semantics;
- `0.2.0-preview.2` also uses wire protocol `2`; adding a transport does not change the communication envelope.

A wire-incompatible change requires a new protocol version even while package versions are pre-1.0. Clients must reject unsupported protocol versions before using payload data.

Consumer/application payloads are opaque to PartyGameKit and may be independently versioned by their owner without incrementing the PartyGameKit protocol unless the base communication envelope/control contract changes.

## Canonical fixtures

The active canonical fixture set is `protocol/fixtures/v2-*.json`.

C#, Dart and TypeScript tests consume those vectors as the single source of truth for the PartyGameKit v2 wire contract. Protocol-v1 fixtures were retired from the active tree when `[17]` completed cross-language migration; the v1 contract remains available in Git history and the `0.1.0-preview.1` tag/release.

SignalR integration tests add transport-level proof that the same v2 connect/resume and opaque application-message semantics survive backend relay without changing the canonical fixtures.

## Compatibility promise

PartyGameKit does not silently reinterpret protocol v1 as v2. A v2 LAN or SignalR listener rejects v1 handshakes deterministically.

Within a protocol version, changes must preserve the documented envelope/control-message contract. Product/game rules and product identifiers are versioned by the consuming application, not by PartyGameKit Core.
