# AGENTS.md

## Repository role

`Dihor.GameKit.Networking` is an independent, reusable communication/networking library for PartyBeam, Państwa Miasta and future unrelated products.

## Non-negotiable boundary

A consumer must be able to use this library without defining players, a lobby, PartySession/GameSession, TV/controller roles, score or game state.

Allowed responsibilities include:

- connection/peer identities;
- protocol envelopes;
- transports;
- discovery;
- reconnect/resume;
- heartbeat/timeout;
- replay/deduplication;
- generic ordering;
- monotonic timing synchronization and diagnostics;
- resource/backpressure policy.

Do not add:

- PartyBeam-specific roles/session semantics;
- PartyBeam Game Contract methods;
- `.partybeam` package/catalog concepts;
- PartyBeam UI;
- game rules or fairness policy.

`PartyBeam.GameSdk` is intentionally PartyBeam-specific and must remain a consumer-side sibling, never a dependency of this repository.

## Consumer-driven fixes

A PartyBeam defect may justify work here only if the defect is demonstrably generic networking behavior. Reproduce it at this layer, fix it without PartyBeam concepts, add regression tests, then let PartyBeam upgrade its pinned prerelease.

## Compatibility

Keep protocol/wire compatibility explicit and versioned. Do not rename preserved wire identifiers casually merely because historical names contain PartyGameKit vocabulary.

## Dependencies and licensing

No paid commercial dependency. Prefer permissive licenses and keep runtime dependencies justified.

## Validation

Maintain deterministic unit/integration/conformance tests across supported .NET/TypeScript/Dart surfaces. Public API boundary guards should prevent product/session vocabulary from leaking back into production packages.

## Workflow

Use focused issues/PRs. Keep public API documentation and migration notes aligned with breaking prerelease changes.
