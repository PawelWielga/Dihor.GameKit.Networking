# Cross-repository validation for the 0.2 communication boundary

This document validates the corrected Dihor.GameKit.Networking `0.2` boundary against the current `main` branches of its two intended consumers:

- `PawelWielga/panstwa-miasta`
- `PawelWielga/PartyBeam`

The purpose is not to make either consumer adopt Dihor.GameKit.Networking's product model. The purpose is to prove that Dihor.GameKit.Networking can sit below their existing product/game concepts without owning them.

## Validation result

The corrected boundary is sufficient for both consumers.

No player, TV/controller, PartySession, GameSession, authority-policy or game-state abstraction needs to be added back to Dihor.GameKit.Networking.

The reusable mapping is:

| Consumer concept | Dihor.GameKit.Networking primitive | Ownership |
| --- | --- | --- |
| live socket / transport connection | `ConnectionId` | Dihor.GameKit.Networking |
| stable reconnecting device/client identity, when useful | `PeerId` | Dihor.GameKit.Networking communication identity only |
| reconnect credential | resume token + `ConnectionContinuityCoordinator` | Dihor.GameKit.Networking |
| transport endpoint | `ConnectionDescriptor` | Dihor.GameKit.Networking |
| optional technical routing scope | `ChannelId` | Dihor.GameKit.Networking |
| product/game command or state payload | `application.message` data | consumer |
| player/profile/role | none | consumer |
| lobby/party/game session | none | consumer |
| authority/game-state policy | none | consumer |

## Państwa Miasta

### Current application model

The current Flutter application already separates a substantial product/game layer from its physical LAN socket implementation.

Its multiplayer domain contains application-owned concepts such as:

- `PlayerProfile` with `id`, name, colour and emoji;
- `MultiplayerRole.host` / `MultiplayerRole.client`;
- room identity and room membership;
- game-specific multiplayer messages;
- reconnect credentials associated with the existing player identity;
- application-specific game/session recovery state.

The current local LAN implementation owns WebSocket hosting/joining, room URLs, host-session checks, admission policy, reconnect-token verification and message delivery. This is the layer that can progressively delegate reusable networking mechanics to Dihor.GameKit.Networking.

### Correct Dihor.GameKit.Networking mapping

A future integration should keep these concepts in Państwa Miasta:

- `PlayerProfile` and its presentation fields;
- player admission and duplicate-player policy;
- host/client product meaning;
- room code UX and room membership;
- `Countries & Cities` commands, answers, categories, rounds, scoring and reviews;
- authoritative game state and game snapshots;
- unfinished-game persistence and product-level restoration policy.

Dihor.GameKit.Networking can provide the lower-level pieces:

1. Each active transport connection receives a transient `ConnectionId`.
2. When stable communication identity is useful, the application's existing stable player/client identifier can be mapped at the adapter boundary to a `PeerId`.
3. The existing reconnect credential maps to Dihor.GameKit.Networking's opaque resume-token mechanism.
4. Replacing a WebSocket maps to `ConnectionContinuityCoordinator.Resume`; this rebinds communication identity only.
5. Existing game messages remain application-owned and can be carried as `application.message` payloads.
6. Existing game snapshot sequence/recovery semantics remain above Dihor.GameKit.Networking. The neutral `SequenceGate` may be reused only where it matches the application's needs.
7. LAN endpoint discovery can use `ConnectionDescriptor` / `UdpLanDiscovery*`; the room code and invitation UI remain application data around that descriptor.

### Migration strategy

A full wire-format replacement is not required as one atomic change.

Państwa Miasta currently has a mature LAN protocol and can introduce Dihor.GameKit.Networking behind an adapter boundary. The safe migration path is:

1. keep the existing game/domain message classes unchanged;
2. isolate the current socket lifecycle behind the existing multiplayer transport abstraction;
3. replace socket/discovery/reconnect mechanics with Dihor.GameKit.Networking primitives incrementally;
4. serialize existing application messages into opaque `application.message` data when adopting protocol v2;
5. remove duplicated networking/reconnect code only after equivalent behavior is covered by existing app tests.

There is no reason to move `PlayerProfile`, room lifecycle or Countries & Cities state into Dihor.GameKit.Networking to complete this integration.

## PartyBeam

### Current product model

PartyBeam's current architecture documentation correctly places the following in PartyBeam itself:

- the installed TV/big-screen shell;
- Android phone/controller shell and browser fallback;
- game catalog and module delivery;
- PartySession UX and lifecycle;
- TV/controller/player product roles;
- GameSession state and scoring;
- product-level join/reconnect presentation;
- game-specific public/private/shared-screen projections.

Those responsibilities should remain there.

Some current PartyBeam documentation still reflects the historical Dihor.GameKit.Networking v0.1 boundary and describes Dihor.GameKit.Networking as owning generic rooms, players, technical authority and session lifecycle. That wording is stale relative to Dihor.GameKit.Networking `0.2` and should be corrected in a dedicated PartyBeam change rather than by reintroducing those abstractions here.

### Correct Dihor.GameKit.Networking mapping

PartyBeam can build its full product model above Dihor.GameKit.Networking as follows:

1. PartyBeam assigns product meaning to a connected device: TV, Android controller, browser controller, spectator or another future role.
2. Dihor.GameKit.Networking only sees the connection and, if enabled, its stable neutral `PeerId`.
3. PartyBeam owns `PartySession`, participant/player records, readiness, selected game and `GameSession` lifecycle.
4. PartyBeam owns technical-authority policy as a product/runtime decision. Dihor.GameKit.Networking transports messages to whichever process PartyBeam selects; it does not choose the authority.
5. PartyBeam invitation/QR metadata may wrap a Dihor.GameKit.Networking `ConnectionDescriptor` with product-specific session/join information.
6. PartyBeam/game-module commands and state are carried through `application.message` without Dihor.GameKit.Networking interpreting them.
7. Public/private/TV state projections are produced by PartyBeam or the selected game module, then sent to the appropriate connections chosen by the product layer.

This leaves Dihor.GameKit.Networking reusable for applications that have no TV, controller, player or party concept at all.

## Required consumer-side follow-up

Dihor.GameKit.Networking `[17]` does not require a source-code change in either consumer repository to validate the boundary.

The explicit follow-up is:

- **PartyBeam:** update stale architecture wording that still assigns rooms/players/session lifecycle/technical authority ownership to Dihor.GameKit.Networking before implementation starts depending on that old boundary.
- **Państwa Miasta:** when migration work is scheduled, implement it as an adapter/integration change in that repository and preserve its application/game semantics above Dihor.GameKit.Networking.

Any such changes should be reviewed and merged in their own repositories. They must not be hidden as compatibility shims inside Dihor.GameKit.Networking.

## Acceptance conclusion

The current consumers demonstrate that the `0.2` boundary is deliberately smaller than either product's multiplayer model, which is the intended result:

```text
PartyBeam product/runtime       Państwa Miasta game/runtime
          │                               │
          └──────── consumer policy ──────┘
                          │
                          ▼
                  Dihor.GameKit.Networking 0.2
                communication only
                          │
                  LAN / future transports
```

The next Dihor.GameKit.Networking transport work can therefore add alternative connectivity without adding product/session semantics back into the library.
