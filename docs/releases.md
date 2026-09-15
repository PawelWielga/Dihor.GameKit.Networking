# Durable prerelease distribution

PartyGameKit prereleases are distributed through **GitHub Releases**. GitHub Actions run artifacts are validation outputs only and are not a supported dependency source for downstream repositories.

The current release line is `0.2.0-preview.5`, tagged `v0.2.0-preview.5`.

## Release contents

A PartyGameKit prerelease contains:

- individual `.nupkg` and `.snupkg` files for all .NET packages;
- `partygamekit-nuget-feed-<version>.zip`, a self-contained local NuGet source for downstream .NET restores;
- the `@partygamekit/client` npm tarball;
- the Dart protocol source archive;
- `SHA256SUMS.txt` for release-asset verification.

The NuGet feed ZIP contains:

```text
NuGet.Config
packages/
  PartyGameKit.Core.<version>.nupkg
  PartyGameKit.Protocol.<version>.nupkg
  PartyGameKit.Transport.Abstractions.<version>.nupkg
  PartyGameKit.Transport.InMemory.<version>.nupkg
  PartyGameKit.Transport.Lan.<version>.nupkg
  PartyGameKit.Transport.SignalR.<version>.nupkg
  PartyGameKit.Transport.SignalR.Server.<version>.nupkg
  PartyGameKit.Discovery.Lan.<version>.nupkg
```

`NuGet.Config` adds the extracted `packages/` directory plus NuGet.org for PartyGameKit's external dependencies. PartyGameKit package versions therefore come from the durable GitHub Release asset rather than a transient workflow artifact.

## Linux / CI consumption

From a checkout that contains the PartyGameKit helper script:

```bash
bash eng/fetch-release-nuget.sh 0.2.0-preview.5

dotnet restore YourSolution.sln \
  --configfile .partygamekit/0.2.0-preview.5/feed/NuGet.Config
```

A downstream repository does not need to copy the helper. It can download the stable release URL directly:

```bash
VERSION=0.2.0-preview.5
TAG="v${VERSION}"
ASSET="partygamekit-nuget-feed-${VERSION}.zip"
mkdir -p .partygamekit/${VERSION}
curl --fail --location --retry 3 \
  --output ".partygamekit/${VERSION}/${ASSET}" \
  "https://github.com/PawelWielga/PartyGameKit/releases/download/${TAG}/${ASSET}"
unzip -q ".partygamekit/${VERSION}/${ASSET}" \
  -d ".partygamekit/${VERSION}/feed"

dotnet restore YourSolution.sln \
  --configfile ".partygamekit/${VERSION}/feed/NuGet.Config"
```

In GitHub Actions this requires no PartyGameKit-specific secret because the repository/release is public.

## Windows / PowerShell consumption

```powershell
./eng/fetch-release-nuget.ps1 -Version 0.2.0-preview.5

dotnet restore YourSolution.sln `
  --configfile .partygamekit/0.2.0-preview.5/feed/NuGet.Config
```

A downstream repository can use the same `Invoke-WebRequest`/`Expand-Archive` logic directly if it does not vendor the helper.

## PackageReference example

Once the release feed has been downloaded, projects use normal NuGet references:

```xml
<ItemGroup>
  <PackageReference Include="PartyGameKit.Core" Version="0.2.0-preview.5" />
  <PackageReference Include="PartyGameKit.Protocol" Version="0.2.0-preview.5" />
  <PackageReference Include="PartyGameKit.Transport.Abstractions" Version="0.2.0-preview.5" />
  <PackageReference Include="PartyGameKit.Transport.Lan" Version="0.2.0-preview.5" />
</ItemGroup>
```

Add SignalR/Discovery packages only when the consumer needs those concrete capabilities.

## Release integrity and licensing

PartyGameKit is licensed under the MIT License. NuGet packages declare `MIT` through `PackageLicenseExpression`; the npm package declares `"license": "MIT"`; source artifacts include the license text.

Every release publishes `SHA256SUMS.txt`. Consumers that pin release assets in automated supply-chain workflows can verify the downloaded ZIP/package against those hashes.

The project dependency policy remains separate: PartyGameKit itself only adopts external dependencies that permit free commercial use.

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
