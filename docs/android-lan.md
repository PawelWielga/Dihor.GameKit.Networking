# Android LAN transport

Dihor.GameKit.Networking `0.2.0-preview.7` supports the direct LAN WebSocket and connection-runtime path from `net10.0-android` applications without requiring the `Microsoft.AspNetCore.App` runtime pack.

The supported PartyBeam consumer baseline is:

- target framework: `net10.0-android`;
- minimum Android API: 28 (`SupportedOSPlatformVersion` `28.0`);
- protocol: Dihor.GameKit.Networking protocol v2;
- transport package: `Dihor.GameKit.Networking.Transport.Lan`;
- optional discovery package: `Dihor.GameKit.Networking.Discovery.Lan`.

The package remains communication-only. Android support does not introduce TV, controller, player, party or game-session semantics.

## Why preview.6 changes the LAN host

Before `0.2.0-preview.6`, `Dihor.GameKit.Networking.Transport.Lan` hosted WebSockets through ASP.NET Core/Kestrel and therefore declared a `Microsoft.AspNetCore.App` framework reference. That framework reference made the package unsuitable for Android consumers even though the client itself used portable `ClientWebSocket` APIs.

Starting with `0.2.0-preview.6`, the LAN host uses `TcpListener` plus the .NET WebSocket implementation over the accepted network stream. The public `LanWebSocketTransport`, `LanWebSocketClient`, `LanWebSocketHostOptions` and `LanConnectionDescriptor` model remains the same communication boundary, while the package no longer depends on ASP.NET Core.

## Android client

Reference the LAN package from the Android application:

```xml
<ItemGroup>
  <PackageReference Include="Dihor.GameKit.Networking.Transport.Lan" Version="0.2.0-preview.7" />
  <PackageReference Include="Dihor.GameKit.Networking.Runtime" Version="0.2.0-preview.7" />
</ItemGroup>
```

Connect with the descriptor endpoint and the normal protocol-v2 connect or resume handshake:

```csharp
var endpoint = LanConnectionDescriptor.GetEndpointUri(descriptor);
await using var client = await LanWebSocketClient.ConnectAsync(
    endpoint,
    handshakeJson,
    cancellationToken: cancellationToken);
```

`handshakeJson` remains a normal Dihor.GameKit.Networking protocol-v2 `connect.request` or `resume.request`. Stable `PeerId` and resume-token ownership remains in Dihor.GameKit.Networking Core/Protocol; product identity stays in the consumer.

## Android host

Reference the same LAN transport package. A host can bind directly to a local interface without Kestrel:

```csharp
await using var host = await LanWebSocketTransport.StartAsync(
    new LanWebSocketHostOptions(IPAddress.Any, port: 0),
    cancellationToken: cancellationToken);

var descriptor = host.CreateConnectionDescriptor(advertisedHost);
```

Passing port `0` lets the operating system choose an available port. `CreateConnectionDescriptor` uses the actual bound port, configured WebSocket path and protocol-v2 descriptor contract. The `advertisedHost` must be an address or host name reachable by the other devices on the LAN; Dihor.GameKit.Networking intentionally does not guess which interface a product wants to advertise.

The transport continues to expose the same `IMessageTransport` event/send/broadcast/disconnect surface. Connect, opaque `application.message`, heartbeat and resume/reconnect behavior therefore stays above the concrete socket implementation.

## LAN discovery on Android

`Dihor.GameKit.Networking.Discovery.Lan` remains based on UDP LAN sockets and can be referenced by the `net10.0-android` host/client path. The application is responsible for Android manifest permissions and any device/vendor-specific Wi-Fi restrictions required by its target SDK and deployment environment.

Discovery is optional. A valid `ConnectionDescriptor` can still be transferred through QR, manual entry or another product-owned mechanism and connected directly when discovery is unavailable.

## Package-only compatibility guard

CI and prerelease publishing restore and build two independent package consumers:

- `packaging/android-client`, which references the published LAN client path;
- `packaging/android-host`, which references the LAN host and LAN discovery packages.

Both target `net10.0-android` with Android API 28 as the minimum. They restore from the generated NuGet artifacts rather than source project references, so a future accidental framework/runtime dependency in a package is caught before publishing.

## Durable prerelease feed

The supported prerelease is `v0.2.0-preview.7`. The GitHub Release contains the individual NuGet packages and `dihor-gamekit-networking-nuget-feed-0.2.0-preview.7.zip`, which includes the complete package set and a ready `NuGet.Config`.

Use the durable GitHub Release/feed for downstream repositories. GitHub Actions artifacts are validation outputs and are not the supported long-term dependency source.
