# Turns NapPilot's dry-run mode on or off. Restart the app afterwards; settings are read at startup.
param([Parameter(Mandatory)][bool]$Enabled)

$path = Join-Path $env:APPDATA 'NapPilot\settings.json'
$settings = Get-Content $path -Raw | ConvertFrom-Json
$settings.DryRun = $Enabled
$settings | ConvertTo-Json | Set-Content $path -Encoding utf8
"DryRun is now $Enabled in $path"
