# Durable prerelease distribution

Dihor.GameKit.Networking prereleases are distributed through **GitHub Releases**. GitHub Actions run artifacts are validation outputs only and are not a supported dependency source for downstream repositories.

The current release line is `0.2.0-preview.8`, tagged `v0.2.0-preview.8`.

## Release contents

A Dihor.GameKit.Networking prerelease contains:

- individual `.nupkg` and `.snupkg` files for all .NET packages;
- `dihor-gamekit-networking-nuget-feed-<version>.zip`, a self-contained local NuGet source for downstream .NET restores;
- the `@dihor/gamekit-networking` npm tarball;
- the Dart protocol source archive;
- `SHA256SUMS.txt` for release-asset verification.

The NuGet feed ZIP contains:

```text
NuGet.Config
packages/
  Dihor.GameKit.Networking.Core.<version>.nupkg
  Dihor.GameKit.Networking.Protocol.<version>.nupkg
  Dihor.GameKit.Networking.Transport.Abstractions.<version>.nupkg
  Dihor.GameKit.Networking.Transport.InMemory.<version>.nupkg
  Dihor.GameKit.Networking.Transport.Lan.<version>.nupkg
  Dihor.GameKit.Networking.Transport.SignalR.<version>.nupkg
  Dihor.GameKit.Networking.Transport.SignalR.Server.<version>.nupkg
  Dihor.GameKit.Networking.Discovery.Lan.<version>.nupkg
```

`NuGet.Config` adds the extracted `packages/` directory plus NuGet.org for Dihor.GameKit.Networking's external dependencies. Dihor.GameKit.Networking package versions therefore come from the durable GitHub Release asset rather than a transient workflow artifact.

## Linux / CI consumption

From a checkout that contains the Dihor.GameKit.Networking helper script:

```bash
bash eng/fetch-release-nuget.sh 0.2.0-preview.8

dotnet restore YourSolution.sln \
  --configfile .dihor-gamekit-networking/0.2.0-preview.8/feed/NuGet.Config
```

A downstream repository does not need to copy the helper. It can download the stable release URL directly:

```bash
VERSION=0.2.0-preview.8
TAG="v${VERSION}"
ASSET="dihor-gamekit-networking-nuget-feed-${VERSION}.zip"
mkdir -p .dihor-gamekit-networking/${VERSION}
curl --fail --location --retry 3 \
  --output ".dihor-gamekit-networking/${VERSION}/${ASSET}" \
  "https://github.com/PawelWielga/Dihor.GameKit.Networking/releases/download/${TAG}/${ASSET}"
unzip -q ".dihor-gamekit-networking/${VERSION}/${ASSET}" \
  -d ".dihor-gamekit-networking/${VERSION}/feed"

dotnet restore YourSolution.sln \
  --configfile ".dihor-gamekit-networking/${VERSION}/feed/NuGet.Config"
```

In GitHub Actions this requires no Dihor.GameKit.Networking-specific secret because the repository/release is public.

## Windows / PowerShell consumption

```powershell
./eng/fetch-release-nuget.ps1 -Version 0.2.0-preview.8

dotnet restore YourSolution.sln `
  --configfile .dihor-gamekit-networking/0.2.0-preview.8/feed/NuGet.Config
```

A downstream repository can use the same `Invoke-WebRequest`/`Expand-Archive` logic directly if it does not vendor the helper.

## PackageReference example

Once the release feed has been downloaded, projects use normal NuGet references:

```xml
<ItemGroup>
  <PackageReference Include="Dihor.GameKit.Networking.Core" Version="0.2.0-preview.8" />
  <PackageReference Include="Dihor.GameKit.Networking.Protocol" Version="0.2.0-preview.8" />
  <PackageReference Include="Dihor.GameKit.Networking.Transport.Abstractions" Version="0.2.0-preview.8" />
  <PackageReference Include="Dihor.GameKit.Networking.Transport.Lan" Version="0.2.0-preview.8" />
</ItemGroup>
```

Add SignalR/Discovery packages only when the consumer needs those concrete capabilities.

## Release integrity and licensing

Dihor.GameKit.Networking is licensed under the MIT License. NuGet packages declare `MIT` through `PackageLicenseExpression`; the npm package declares `"license": "MIT"`; source artifacts include the license text.

Every release publishes `SHA256SUMS.txt`. Consumers that pin release assets in automated supply-chain workflows can verify the downloaded ZIP/package against those hashes.

The project dependency policy remains separate: Dihor.GameKit.Networking itself only adopts external dependencies that permit free commercial use.

## Publication gate

`.github/workflows/prerelease.yml` rebuilds and retests the repository before creating/updating a prerelease. It then:

1. validates that central .NET, TypeScript, Dart and release-manifest versions match;
2. validates MIT package metadata;
3. packs and smoke-tests NuGet packages as a fresh consumer;
4. runs Dart and TypeScript tests plus real Chromium WebRTC validation;
5. creates the durable NuGet-feed ZIP and checksums;
6. creates/updates the GitHub prerelease and assets;
7. downloads the NuGet-feed ZIP back from the GitHub Release into a clean directory;
8. restores, builds and runs the package-only consumer from that downloaded release source.

A release workflow is not considered successful until the durable GitHub Release asset itself has passed the downstream restore smoke test.
