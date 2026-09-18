# Versioning policy

Dihor.GameKit.Networking uses Semantic Versioning for distributed packages and a separate integer version for the wire protocol.

## Package versions

The historical first packaged line is `0.1.0-preview.1`. The corrected communication-only line starts at `0.2.0-preview.1` because the public API break is deliberate and substantial.

Compatible transport/capability expansions on that corrected boundary use later prerelease suffixes:

- `0.2.0-preview.2` adds optional SignalR relay connectivity;
- `0.2.0-preview.3` adds browser-native WebRTC DataChannels plus optional SignalR SDP/ICE signaling;
- `0.2.0-preview.4` adds bounded automatic transport selection/fallback and reconnect preference across registered communication paths;
- `0.2.0-preview.5` adds transport-neutral synchronized monotonic timing, RTT/jitter/uncertainty metrics and peer timestamp normalization;
- `0.2.0-preview.6` makes the direct LAN WebSocket client/host and UDP discovery package path consumable from `net10.0-android` API 28+ without an ASP.NET Core runtime dependency.

For preview releases:

- compatible fixes/features may increment the prerelease suffix;
- removing or changing a public API requires an explicit changelog/migration entry and an appropriate SemVer prerelease version change;
- a stable `1.0.0` is not implied by completion of the current backlog;
- the supported .NET, TypeScript and Dart package surfaces use the same release version for one Dihor.GameKit.Networking compatibility line even when runtime capabilities differ by platform.

The repository's default package version is declared in `Directory.Build.targets`. CI derives artifact names from that value rather than duplicating a specific prerelease suffix. The explicit versions in the TypeScript/Dart manifests and package-only consumer must match the same release line.

## Protocol versions

Package version and protocol version are independent:

- `0.1.0-preview.1` used wire protocol `1` with room/player/session semantics;
- `0.2.0-preview.1` uses wire protocol `2` with neutral connection/resume/application-message semantics;
- `0.2.0-preview.2` also uses wire protocol `2`; adding SignalR relay does not change the communication envelope;
- `0.2.0-preview.3` also uses wire protocol `2`; adding browser WebRTC and signaling does not change the protocol-v2 control/application envelope;
- `0.2.0-preview.4` also uses wire protocol `2`; automatic transport selection changes connection orchestration only and carries the same opaque protocol/application data;
- `0.2.0-preview.5` also uses wire protocol `2`; synchronized timing is an optional transport-neutral utility whose probe/reply data can travel inside existing consumer-owned payloads without changing the base envelope;
- `0.2.0-preview.6` also uses wire protocol `2`; replacing the LAN host implementation and expanding platform compatibility does not change connect/resume/application-message semantics.

A wire-incompatible change requires a new protocol version even while package versions are pre-1.0. Clients must reject unsupported protocol versions before using payload data.

Consumer/application payloads are opaque to Dihor.GameKit.Networking and may be independently versioned by their owner without incrementing the Dihor.GameKit.Networking protocol unless the base communication envelope/control contract changes.

## WebRTC and protocol versioning

The initial browser WebRTC path carries opaque binary application data directly between peers. Its SDP/ICE signaling is transport negotiation infrastructure, not a new Dihor.GameKit.Networking application wire protocol.

Therefore adding `WebRtcPeer`, DataChannel profiles, signaling adapters, buffering policy or diagnostics does not by itself increment protocol `2`.

If a consumer chooses to carry Dihor.GameKit.Networking protocol envelopes over a DataChannel, those envelopes still obey their own `protocolVersion` field. The WebRTC signaling layer must not silently reinterpret one Dihor.GameKit.Networking protocol version as another.

## Automatic connectivity and protocol versioning

`ConnectivityMode.Auto` selects among registered transport candidates before or during a neutral reconnect. It does not rewrite connect/resume handshakes or application payloads and therefore does not define another wire protocol.

Changing from LAN to WebRTC or SignalR may create a new transient `ConnectionId`, while stable logical continuity remains governed by protocol-v2 `PeerId`/resume semantics. The selector treats that handshake data as opaque input rather than adding product/session meaning.

## Monotonic timing and protocol versioning

`MonotonicTimingSynchronizer` consumes probe/reply timestamp evidence supplied by the caller. It does not reserve a new protocol-v2 control message or inspect transport payloads.

Consumers can carry timing probe identifiers and peer timestamps through their existing opaque application payloads or transport-specific adapter envelopes. A reconnect or transport replacement invalidates the timing model explicitly through `Reset(...)`, but does not change the protocol-v2 connection contract.

## Canonical fixtures

The active canonical fixture set is `protocol/fixtures/v2-*.json`.

C#, Dart and TypeScript protocol tests consume those vectors as the single source of truth for the Dihor.GameKit.Networking v2 wire contract. Protocol-v1 fixtures were retired from the active tree when `[17]` completed cross-language migration; the v1 contract remains available in Git history and the `0.1.0-preview.1` tag/release.

SignalR relay integration tests prove the same v2 connect/resume/application-message semantics over backend relay. WebRTC integration tests are separate because they validate native browser DataChannel behavior and signaling boundaries rather than modifying the canonical v2 fixtures.

## Runtime capability differences

Matching package versions do not imply every language/runtime implements every transport or optional utility.

For `0.2.0-preview.6`:

- .NET desktop/server provides LAN WebSocket, SignalR relay, the transport-neutral automatic client selector, optional WebRTC signaling hosting and `MonotonicTimingSynchronizer` in `Dihor.GameKit.Networking.Core`;
- .NET Android API 28+ can consume the direct LAN WebSocket client and host from `Dihor.GameKit.Networking.Transport.Lan` plus UDP discovery from `Dihor.GameKit.Networking.Discovery.Lan` without `Microsoft.AspNetCore.App`; package-only Android client/host builds are release gates;
- TypeScript/browser provides protocol-v2 WebSocket connectivity, native WebRTC DataChannels, the generic automatic selector and an equivalent `MonotonicTimingSynchronizer`/`monotonicNowMs()` timing surface;
- Dart provides protocol-v2 interoperability only and does not claim a LAN/SignalR/WebRTC transport runtime, automatic transport implementation or synchronized timing implementation.

Android package compatibility does not imply that Dihor.GameKit.Networking owns Android application permissions, Wi-Fi policy or product-specific interface selection. Those remain consumer/platform responsibilities. See `docs/android-lan.md`.

These differences must be explicit in compatibility/package documentation rather than hidden by pretending all runtimes have identical capabilities.

## Compatibility promise

Dihor.GameKit.Networking does not silently reinterpret protocol v1 as v2. A v2 LAN or SignalR listener rejects v1 handshakes deterministically.

Within a protocol version, changes must preserve the documented envelope/control-message contract. Product/game rules and product identifiers are versioned by the consuming application, not by Dihor.GameKit.Networking Core.
