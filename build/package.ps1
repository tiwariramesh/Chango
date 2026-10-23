<#
.SYNOPSIS
  Builds an Flipr release: tests, self-contained single-file exe, portable zip, installer and checksums.

.EXAMPLE
  ./build/package.ps1                      # version from build/Common.props
  ./build/package.ps1 -Version 0.1.0-alpha -SkipTests
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $Runtime = "win-x64",
    [switch] $SkipTests,
    [switch] $SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$publish = Join-Path $artifacts "publish\$Runtime"

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root "build\Common.props"))).Project.PropertyGroup.Version | Select-Object -First 1
}
$numeric = ($Version -split '-')[0] + ".0"
Write-Host "Flipr $Version ($Runtime)" -ForegroundColor Cyan

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
New-Item -ItemType Directory -Force $artifacts | Out-Null

# Native parts first: the tests exercise them too.
Write-Host "> Right-click menu and CorelDRAW reader (C++)"
& (Join-Path $root "build\shell\build-shell.ps1") | Out-Null
& (Join-Path $root "build\cdr\build-cdr.ps1") | Out-Null

if (-not $SkipTests) {
    Write-Host "> Tests"
    dotnet test (Join-Path $root "tests\Flipr.Tests") -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
}

Write-Host "> Publish (self-contained, single file)"
dotnet publish (Join-Path $root "src\Flipr.App\Flipr.App.csproj") -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:Version=$Version -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

Copy-Item (Join-Path $root "LICENSE") (Join-Path $publish "LICENSE.txt")
Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") (Join-Path $publish "THIRD-PARTY-NOTICES.md")

# A new name per version: File Explorer keeps the loaded DLL open, and updates must never need to replace it.
Copy-Item (Join-Path $root "artifacts\shell\FliprShell.dll") (Join-Path $publish "FliprShell-$(($Version -split '-')[0]).dll")
Copy-Item (Join-Path $root "artifacts\cdr\FliprCdr.exe") $publish
Copy-Item (Join-Path $root "artifacts\cdr\licenses") (Join-Path $publish "licenses") -Recurse

Write-Host "> Portable zip"
$zip = Join-Path $artifacts "Flipr-$Version-$Runtime-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip

$outputs = @($zip)
if (-not $SkipInstaller) {
    Write-Host "> Installer"
    $iscc = @($env:ISCC, "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
        Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 not found. Install it (winget install JRSoftware.InnoSetup) or set `$env:ISCC." }
    & $iscc /Q "/DAppVersion=$Version" "/DNumericVersion=$numeric" "/DSourceDir=$publish" "/DOutputDir=$artifacts" `
        (Join-Path $root "build\installer\Flipr.iss")
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
    $outputs += Join-Path $artifacts "Flipr-$Version-$Runtime-setup.exe"
}

# Stable names for one-click download links (README -> releases/download/<tag>/Flipr-Setup.exe)
Copy-Item $zip (Join-Path $artifacts "Flipr-Portable.zip") -Force
$outputs += Join-Path $artifacts "Flipr-Portable.zip"
if (-not $SkipInstaller) {
    Copy-Item (Join-Path $artifacts "Flipr-$Version-$Runtime-setup.exe") (Join-Path $artifacts "Flipr-Setup.exe") -Force
    $outputs += Join-Path $artifacts "Flipr-Setup.exe"
}

Write-Host "> Checksums"
$sums = $outputs | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $_ -Leaf) }
# UTF-8 without BOM so `sha256sum -c SHA256SUMS.txt` works.
[IO.File]::WriteAllLines((Join-Path $artifacts "SHA256SUMS.txt"), [string[]]$sums, (New-Object Text.UTF8Encoding $false))

Write-Host ""
$outputs | ForEach-Object { "{0,-48} {1,8:N1} MB" -f (Split-Path $_ -Leaf), ((Get-Item $_).Length / 1MB) } | Write-Host
Write-Host "Done: $artifacts" -ForegroundColor Green
