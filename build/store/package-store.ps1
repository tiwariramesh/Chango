<#
.SYNOPSIS
  Builds the Microsoft Store package: artifacts\store\Flipr-<version>-Store.msix
.DESCRIPTION
  1. Publishes Flipr (self-contained, single file, win-x64), the same build as the installer.
  2. Generates the Store icons (tiles, taskbar, Store logo) from the master icon.
  3. Writes the manifest (identity from Partner Center; version from build/Common.props) and indexes the icons
     with makepri, then packs with makeappx (Windows SDK).
  The package is unsigned: Partner Center signs Store submissions.
.EXAMPLE
  ./build/store/package-store.ps1
#>
param([string]$Version)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $Version) { $Version = ([xml](Get-Content (Join-Path $root 'build\Common.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
# Store versions are four numbers ending in .0: "0.2.7-alpha" -> "0.2.7.0".
$numeric = (($Version -split '-')[0] -split '\.' | Select-Object -First 3) -join '.'
$storeVersion = "$numeric.0"
Write-Host "Flipr Store package $storeVersion (from $Version)"

$sdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\makeappx.exe' | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $sdk) { throw 'makeappx.exe not found: install the Windows 10/11 SDK.' }
$makeappx = $sdk.FullName
$makepri = Join-Path $sdk.DirectoryName 'makepri.exe'

$out = Join-Path $root 'artifacts\store'
$layout = Join-Path $out 'layout'
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force $layout, (Join-Path $layout 'Assets') | Out-Null

Write-Host '> Publish'
dotnet publish (Join-Path $root 'src\Flipr.App\Flipr.App.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -o $layout --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Get-ChildItem $layout -Filter *.pdb | Remove-Item
Write-Host '> Right-click menu and CorelDRAW reader'
& (Join-Path $root 'build\shell\build-shell.ps1') | Out-Null
& (Join-Path $root 'build\cdr\build-cdr.ps1') | Out-Null
Copy-Item (Join-Path $root 'artifacts\shell\FliprShell.dll') $layout
Copy-Item (Join-Path $root 'artifacts\cdr\FliprCdr.exe') $layout
Copy-Item (Join-Path $root 'artifacts\cdr\licenses') (Join-Path $layout 'licenses') -Recurse
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') (Join-Path $layout 'THIRD-PARTY-NOTICES.md')
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $layout 'LICENSE.txt')

Write-Host '> Icons'
Add-Type -AssemblyName System.Drawing
$master = [System.Drawing.Image]::FromFile((Join-Path $root 'src\Flipr.UI\Assets\flipr-1024.png'))
function Save-Icon([string]$name, [int]$w, [int]$h, [double]$fill) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $side = [Math]::Round([Math]::Min($w, $h) * $fill)
    $g.DrawImage($master, [int](($w - $side) / 2), [int](($h - $side) / 2), [int]$side, [int]$side)
    $bmp.Save((Join-Path $layout "Assets\$name"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}
foreach ($s in @(@(100, 1), @(125, 1.25), @(150, 1.5), @(200, 2), @(400, 4))) {
    $k = $s[1]
    Save-Icon "Square44x44Logo.scale-$($s[0]).png" ([int](44 * $k)) ([int](44 * $k)) 1.0
    Save-Icon "Square150x150Logo.scale-$($s[0]).png" ([int](150 * $k)) ([int](150 * $k)) 0.66
    Save-Icon "Wide310x150Logo.scale-$($s[0]).png" ([int](310 * $k)) ([int](150 * $k)) 0.66
    Save-Icon "StoreLogo.scale-$($s[0]).png" ([int](50 * $k)) ([int](50 * $k)) 1.0
}
# Taskbar, Start list and Explorer sizes, without the tile plate.
foreach ($t in 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256) {
    Save-Icon "Square44x44Logo.targetsize-$t.png" $t $t 1.0
    Save-Icon "Square44x44Logo.targetsize-$($t)_altform-unplated.png" $t $t 1.0
    Save-Icon "Square44x44Logo.targetsize-$($t)_altform-lightunplated.png" $t $t 1.0
}
$master.Dispose()

Write-Host '> Manifest and resources'
(Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw).Replace('{VERSION}', $storeVersion) |
    Set-Content (Join-Path $layout 'AppxManifest.xml') -Encoding utf8
$priconfig = Join-Path $out 'priconfig.xml'
& $makepri createconfig /cf $priconfig /dq en-US /o | Out-Null
# One index with every icon size: the default config splits scales into resource packs, which only bundles use.
[xml]$cfg = Get-Content $priconfig
foreach ($p in @($cfg.SelectNodes('//packaging'))) { [void]$p.ParentNode.RemoveChild($p) }
$cfg.Save($priconfig)
& $makepri new /pr $layout /cf $priconfig /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makepri failed' }

Write-Host '> Pack'
$msix = Join-Path $out "Flipr-$Version-Store.msix"
& $makeappx pack /d $layout /p $msix /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makeappx failed' }
Write-Host ("`n{0}  {1:N1} MB" -f $msix, ((Get-Item $msix).Length / 1MB))
