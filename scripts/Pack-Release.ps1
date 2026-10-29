# Publishes NapPilot for one runtime and packs it with Velopack: an installer, a portable zip and the update feed.
# Windows runtimes pack on Windows, macOS runtimes on a Mac (Velopack builds the .app and .pkg there). Used locally and by
# .github/workflows/release.yml. Needs the vpk tool: dotnet tool install -g vpk --version 1.2.161
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64', 'osx-x64', 'osx-arm64')][string]$Runtime,
    [Parameter(Mandatory)][string]$Version,
    [string]$OutputDir = 'releases',
    [string]$ReleaseNotes
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root "publish/$Runtime"
$assets = Join-Path $root 'src/PowerPlan.App/Assets'
$isMac = $Runtime.StartsWith('osx')

if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
dotnet publish (Join-Path $root 'src/PowerPlan.App') -c Release -r $Runtime --self-contained "-p:Version=$Version" -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Runtime" }

# The channel is the runtime, so each install only ever updates to a build for its own OS and CPU.
$pack = @(
    'pack', '--packId', 'NapPilot', '--packVersion', $Version, '--packDir', $publish, '--packTitle', 'NapPilot',
    '--packAuthors', 'NapPilot', '--channel', $Runtime, '--runtime', $Runtime, '--outputDir', (Join-Path $root $OutputDir),
    '--mainExe', $(if ($isMac) { 'NapPilot' } else { 'NapPilot.exe' }),
    '--icon', (Join-Path $assets $(if ($isMac) { 'nappilot.icns' } else { 'nappilot.ico' }))
)
if ($isMac) { $pack += '--bundleId', 'com.nappilot.app' }
else { $pack += '--shortcuts', 'Desktop,StartMenuRoot' }
if ($ReleaseNotes) { $pack += '--releaseNotes', $ReleaseNotes }

vpk @pack
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed for $Runtime" }
