# LAN discovery and connection descriptors

LAN discovery remains PartyGameKit infrastructure, but it discovers **technical connection endpoints/services**, not product game sessions.

See [Communication boundary](communication-boundary.md).

## Separation of concerns

```text
LAN discovery
    │ finds technical endpoint/descriptor
    ▼
connection descriptor
    │
    ▼
transport connect
    │
    ▼
opaque consumer messages
```

Discovery never carries authoritative game state and is not required for direct connection.

## Target discovery data

A PartyGameKit discovery announcement may contain only communication metadata such as:

- protocol version;
- transport kind;
- endpoint/address/port;
- optional neutral routing scope;
- expiry/refresh identity required to deduplicate advertisements;
- transport capability metadata when justified.

Product metadata such as party name, player count, game phase, selected game, join policy or player capacity belongs to the consumer. A consumer may advertise that separately or wrap PartyGameKit discovery data in its own product discovery layer.

## Connection descriptor

The current `JoinDescriptor`/`LanJoinDescriptor` capability remains useful but will be generalized to a technical `ConnectionDescriptor`-style model.

It should contain only information required to establish communication. Product join codes, invitation UX and QR rendering are consumer responsibilities.

A PartyBeam invite may therefore conceptually contain:

```text
PartyBeam invite metadata
└── PartyGameKit ConnectionDescriptor
```

## UDP implementation

The current UDP advertiser/listener/broadcast-address resolver are valid LAN infrastructure and remain behind the discovery package.

`DiscoveredSessionRegistry` is scheduled for neutral naming because discovery does not imply a game/session.

## Refresh, dedupe and expiry

Reusable behavior remains:

- repeated advertisement refreshes the same discovered endpoint/service;
- stable technical identity prevents duplicate entries;
- stale advertisements expire;
- discovery can be disabled/blocked without breaking direct connection.

## Failure behavior

Discovery is convenience, not a prerequisite. A valid connection descriptor must still allow direct connection when UDP broadcast is unavailable.

## Historical v0.1 vocabulary

Existing preview payloads use room/session/join-code terminology. Issue `[16]` will replace that vocabulary in production contracts and `[17]` will align TypeScript/Dart fixtures and samples.