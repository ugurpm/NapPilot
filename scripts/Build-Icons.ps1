# Renders src/PowerPlan.App/Assets/nappilot.svg into the app icons: nappilot.png (tray/window), nappilot.ico (Windows exe
# and installer), nappilot.icns (macOS bundle) and the README logo. Uses headless Edge to draw the SVG, so nothing to install.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src\PowerPlan.App\Assets'
$work = Join-Path ([IO.Path]::GetTempPath()) "nappilot-icons-$PID"
New-Item -ItemType Directory -Force $work | Out-Null

$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found.' }

# An <img> pinned to the top-left: headless Edge centres a bare SVG in a viewport wider than the window.
Copy-Item (Join-Path $assets 'nappilot.svg') $work
Set-Content (Join-Path $work 'render.html') '<html><body style="margin:0"><img src="nappilot.svg" style="display:block;width:1024px;height:1024px"></body></html>'
$master = Join-Path $work 'master.png'
# Edge reports progress on stderr, which Windows PowerShell would turn into a terminating error.
$ErrorActionPreference = 'Continue'
& $edge --headless=new --disable-gpu --hide-scrollbars --default-background-color=00000000 "--user-data-dir=$work\profile" `
    --window-size=1024,1024 "--screenshot=$master" ([Uri](Join-Path $work 'render.html')).AbsoluteUri 2>&1 | Out-Null
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $master)) { throw 'Edge did not write the screenshot.' }

function Get-Png([int]$size) {
    $source = [Drawing.Image]::FromFile($master)
    try {
        $bitmap = New-Object Drawing.Bitmap $size, $size
        $g = [Drawing.Graphics]::FromImage($bitmap)
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.PixelOffsetMode = 'HighQuality'
        $g.DrawImage($source, 0, 0, $size, $size)
        $g.Dispose()
        $stream = New-Object IO.MemoryStream
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
        return , $stream.ToArray()
    } finally { $source.Dispose() }
}

[IO.File]::WriteAllBytes((Join-Path $assets 'nappilot.png'), (Get-Png 256))

# ICO with PNG-compressed entries (supported since Windows Vista).
$icoSizes = 16, 24, 32, 48, 64, 128, 256
$images = $icoSizes | ForEach-Object { , (Get-Png $_) }
$ico = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$icoSizes.Count)
$offset = 6 + 16 * $icoSizes.Count
for ($i = 0; $i -lt $icoSizes.Count; $i++) {
    $s = $icoSizes[$i] % 256   # 0 means 256
    $w.Write([byte]$s); $w.Write([byte]$s); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
[IO.File]::WriteAllBytes((Join-Path $assets 'nappilot.ico'), $ico.ToArray())

# ICNS with PNG entries; lengths are big-endian.
function Write-BigEndian([IO.BinaryWriter]$writer, [int]$value) {
    $bytes = [BitConverter]::GetBytes($value); [Array]::Reverse($bytes); $writer.Write($bytes)
}
$icnsTypes = [ordered]@{ icp4 = 16; icp5 = 32; ic11 = 32; ic12 = 64; ic07 = 128; ic08 = 256; ic13 = 256; ic09 = 512; ic14 = 512; ic10 = 1024 }
$body = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter $body
foreach ($type in $icnsTypes.Keys) {
    $png = Get-Png $icnsTypes[$type]
    $bw.Write([Text.Encoding]::ASCII.GetBytes($type)); Write-BigEndian $bw (8 + $png.Length); $bw.Write($png)
}
$icns = New-Object IO.MemoryStream
$iw = New-Object IO.BinaryWriter $icns
$iw.Write([Text.Encoding]::ASCII.GetBytes('icns')); Write-BigEndian $iw (8 + $body.Length); $iw.Write($body.ToArray())
[IO.File]::WriteAllBytes((Join-Path $assets 'nappilot.icns'), $icns.ToArray())

[IO.File]::WriteAllBytes((Join-Path $root 'docs\images\nappilot-512.png'), (Get-Png 512))

Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
"Icons written to $assets"
