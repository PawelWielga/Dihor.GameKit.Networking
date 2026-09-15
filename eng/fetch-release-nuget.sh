#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.2.0-preview.5}"
DESTINATION="${2:-.partygamekit/$VERSION}"
REPOSITORY="${PARTYGAMEKIT_REPOSITORY:-PawelWielga/PartyGameKit}"
TAG="v${VERSION}"
ASSET="partygamekit-nuget-feed-${VERSION}.zip"
URL="https://github.com/${REPOSITORY}/releases/download/${TAG}/${ASSET}"

mkdir -p "$DESTINATION"
ARCHIVE="$DESTINATION/$ASSET"

echo "Downloading PartyGameKit ${VERSION} from ${URL}"
curl --fail --location --retry 3 --output "$ARCHIVE" "$URL"
rm -rf "$DESTINATION/feed"
mkdir -p "$DESTINATION/feed"
unzip -q "$ARCHIVE" -d "$DESTINATION/feed"

echo "NuGet config: $DESTINATION/feed/NuGet.Config"
echo "Restore with: dotnet restore <solution-or-project> --configfile \"$DESTINATION/feed/NuGet.Config\""
