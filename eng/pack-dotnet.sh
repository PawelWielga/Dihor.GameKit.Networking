#!/usr/bin/env bash
set -euo pipefail

output_dir="${1:-artifacts/nuget}"
version="${2:-}"

projects=(
  "src/Dihor.GameKit.Networking.Core/Dihor.GameKit.Networking.Core.csproj"
  "src/Dihor.GameKit.Networking.Protocol/Dihor.GameKit.Networking.Protocol.csproj"
  "src/Dihor.GameKit.Networking.Runtime/Dihor.GameKit.Networking.Runtime.csproj"
  "src/Dihor.GameKit.Networking.Transport.Abstractions/Dihor.GameKit.Networking.Transport.Abstractions.csproj"
  "src/Dihor.GameKit.Networking.Transport.InMemory/Dihor.GameKit.Networking.Transport.InMemory.csproj"
  "src/Dihor.GameKit.Networking.Transport.Lan/Dihor.GameKit.Networking.Transport.Lan.csproj"
  "src/Dihor.GameKit.Networking.Transport.SignalR/Dihor.GameKit.Networking.Transport.SignalR.csproj"
  "src/Dihor.GameKit.Networking.Transport.SignalR.Server/Dihor.GameKit.Networking.Transport.SignalR.Server.csproj"
  "src/Dihor.GameKit.Networking.Discovery.Lan/Dihor.GameKit.Networking.Discovery.Lan.csproj"
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
