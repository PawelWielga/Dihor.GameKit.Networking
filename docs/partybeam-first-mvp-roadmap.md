# PartyBeam First MVP networking roadmap

## Role

Dihor.GameKit.Networking supplies reusable communication mechanics below PartyBeam. It must stay product-neutral.

For PartyBeam First MVP it owns:

- LAN discovery/connectivity;
- stable peer identity and reconnect/resume mechanics;
- transport-neutral application messages;
- bounded replay/deduplication primitives already consumed by PartyBeam;
- synchronized monotonic timing and uncertainty;
- Android-compatible LAN runtime;
- TypeScript/browser and .NET surfaces required by PartyBeam.

It does **not** own PartySession, GameSession, players, readiness, winners, scores or game fairness policy.

Status snapshot: 2026-09-21.

## Current PartyBeam baseline

PartyBeam.Platform currently consumes the `0.2.0-preview.8` line during its web-host MVP work.

The synchronized monotonic timing work required by Reflex is already implemented. The core PartyBeam communication/timing dependency is therefore not waiting for a new broad Networking feature.

## Ordered work for PartyBeam First MVP

### NET-MVP-01 - keep preview.8 as the integration baseline

1. Treat `0.2.0-preview.8` as the PartyBeam baseline unless an E2E blocker proves otherwise.
2. Do not change PartyBeam to another networking stack.
3. Do not duplicate reconnect/discovery/timing behavior in PartyBeam or games.
4. Keep protocol/package compatibility identifiers stable.

DONE when current PartyBeam local validation and package restore continue to use the pinned verified artifacts.

### NET-MVP-02 - validate through real PartyBeam scenarios

Networking acceptance for the ecosystem is obtained primarily through PartyBeam E2E:

- Android phone discovers/joins shared-screen host;
- reconnect preserves stable peer continuity while connection id changes;
- browser/web-host PartySession remains reachable;
- controller application messages reach PartyBeam authority;
- timing probes produce usable normalized time + uncertainty for Reflex;
- reconnect invalidates/reacquires stale timing;
- WAN removal does not break intended LAN-only prepared play.

Do not add PartyBeam-specific concepts merely to make these scenarios easier.

### NET-MVP-03 - fix only concrete First MVP defects

If PartyBeam/Reflex/Grimcellar exposes a generic networking defect:

1. reproduce it at this layer;
2. search existing issues first;
3. create/update one focused issue;
4. fix it product-neutrally;
5. add regression tests in every affected runtime;
6. publish the smallest required new prerelease, e.g. preview.9;
7. update PartyBeam's checksum/version pin;
8. rerun the failed cross-repository scenario.

Do not hide a networking defect with a PartyBeam-specific parallel transport.

### NET-MVP-04 - keep Dart work separate from PartyBeam critical path

PR #62 / issue #57 improves Dart connection continuity for Flutter consumers.

It may be completed independently, but it does not block PartyBeam First MVP because PartyBeam's required shared-screen/controller path currently depends on .NET/TypeScript/Android runtime surfaces rather than the Flutter Dart runtime.

Do not delay PartyBeam E2E waiting for Dart parity unless PartyBeam itself starts consuming that runtime.

### NET-MVP-05 - defer capability expansion until after First MVP

The following classes of work are valuable but are not PartyBeam First MVP prerequisites unless real testing turns one into a blocker:

- reliable ACK/retry delivery (#70);
- quality-aware transport policy (#71);
- network condition simulator (#72);
- unified observability snapshots (#73);
- broader cross-runtime conformance (#74);
- multi-endpoint offers (#76);
- rate/resource limiting expansion (#77);
- IPv6/mDNS provider expansion (#78);
- diagnostics CLI (#79);
- WebTransport/QUIC (#80);
- batching/compression experiments (#81);
- other competitive/library-hardening work not required by current PartyBeam acceptance.

## First MVP DONE criteria for this repository

Networking is sufficient for PartyBeam First MVP when:

- PartyBeam's pinned prerelease restores/verifies reproducibly;
- LAN discovery/connect/reconnect works on the real PartyBeam targets;
- synchronized monotonic timing supports Reflex fairness;
- no known generic networking correctness defect blocks Platform/Reflex/Grimcellar E2E;
- games remain unaware of concrete LAN/WebSocket/SignalR/WebRTC transport details.

## Relationship to other products

`panstwa-miasta` may continue to motivate Dart/Flutter and other reusable Networking improvements, but it is a separate product and is not a PartyBeam launch-game acceptance requirement.

## Maintenance rule

If a PartyBeam blocker requires Networking work, link the focused Networking issue from `PartyBeam.Platform/docs/ecosystem-first-mvp-execution.md` and keep the solution generic.
