param(
    [string]$Version = "0.2.0-preview.5",
    [string]$Destination = ".dihor-gamekit-networking/$Version",
    [string]$Repository = "PawelWielga/Dihor.GameKit.Networking"
)

$ErrorActionPreference = "Stop"

$tag = "v$Version"
$asset = "dihor-gamekit-networking-nuget-feed-$Version.zip"
$url = "https://github.com/$Repository/releases/download/$tag/$asset"
$archive = Join-Path $Destination $asset
$feed = Join-Path $Destination "feed"

New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Write-Host "Downloading Dihor.GameKit.Networking $Version from $url"
Invoke-WebRequest -Uri $url -OutFile $archive

if (Test-Path $feed) {
    Remove-Item -Recurse -Force $feed
}
New-Item -ItemType Directory -Path $feed -Force | Out-Null
Expand-Archive -Path $archive -DestinationPath $feed -Force

$config = Join-Path $feed "NuGet.Config"
Write-Host "NuGet config: $config"
Write-Host "Restore with: dotnet restore <solution-or-project> --configfile `"$config`""
