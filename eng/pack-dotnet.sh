#!/usr/bin/env bash
set -euo pipefail

output_dir="${1:-artifacts/nuget}"
version="${2:-}"

projects=(
  "src/PartyGameKit.Core/PartyGameKit.Core.csproj"
  "src/PartyGameKit.Protocol/PartyGameKit.Protocol.csproj"
  "src/PartyGameKit.Transport.Abstractions/PartyGameKit.Transport.Abstractions.csproj"
  "src/PartyGameKit.Transport.InMemory/PartyGameKit.Transport.InMemory.csproj"
  "src/PartyGameKit.Transport.Lan/PartyGameKit.Transport.Lan.csproj"
  "src/PartyGameKit.Discovery.Lan/PartyGameKit.Discovery.Lan.csproj"
)

mkdir -p "$output_dir"

for project in "${projects[@]}"; do
  args=(
    dotnet pack "$project"
    --configuration Release
    --no-build
    --output "$output_dir"
  )

  if [[ -n "$version" ]]; then
    args+=("-p:Version=$version")
  fi

  "${args[@]}"
done
