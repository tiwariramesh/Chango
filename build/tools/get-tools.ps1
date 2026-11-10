<#
.SYNOPSIS
  Fetches the command-line tools Chango ships next to Chango.exe: FFmpeg and ExifTool, x64.
.DESCRIPTION
  Downloads the exact releases below, each checked against its SHA-256 (the publisher's own checksum), so every
  build ships the same, unmodified programs. Nothing is installed or run. Archives are cached in artifacts\tools-src.

  Output (artifacts\tools):
    ffmpeg\ffmpeg.exe, ffmpeg\LICENSE.txt          FFmpeg 9.0.2 "essentials" (Gyan Doshi's build), GPL-3.0
    exiftool\exiftool.exe, exiftool\exiftool_files  ExifTool 13.61 (Phil Harvey), Perl's Artistic licence or GPL

  Both are separate programs that Chango starts with arguments (never linked into it). FFmpeg's source:
  https://github.com/FFmpeg/FFmpeg (the commit is in the build's README), build scripts: https://www.gyan.dev/ffmpeg/builds/.
  ExifTool's source is the Perl code inside exiftool_files.
#>
param([string]$OutDir)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts\tools' }
$cache = Join-Path $root 'artifacts\tools-src'
New-Item -ItemType Directory -Force $OutDir, $cache | Out-Null

$tools = @(
    @{
        Name = 'ffmpeg'; File = 'ffmpeg-9.0.2-essentials_build.zip'
        Urls = @('https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip',
                 'https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip')
        Sha = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
    },
    @{
        Name = 'exiftool'; File = 'exiftool-13.61_64.zip'
        # exiftool.org keeps only the newest release; SourceForge keeps every one.
        Urls = @('https://sourceforge.net/projects/exiftool/files/exiftool-13.61_64.zip/download',
                 'https://exiftool.org/exiftool-13.61_64.zip')
        Sha = 'ab75b37515385a753aa60a58f68ffffe1ff81e43b319df9540978fb19e0580e5'
    }
)

foreach ($t in $tools) {
    $archive = Join-Path $cache $t.File
    if (Test-Path $archive) {
        if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $t.Sha) { Remove-Item $archive }
    }
    foreach ($url in $t.Urls) {
        if (Test-Path $archive) { break }
        Write-Host "  downloading $url"
        try { Invoke-WebRequest $url -OutFile $archive -UseBasicParsing -UserAgent 'Chango-build' }
        catch { Write-Host "    failed: $($_.Exception.Message)"; continue }
        $sha = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($sha -ne $t.Sha) { Remove-Item $archive; Write-Host "    checksum mismatch: got $sha" }
    }
    if (-not (Test-Path $archive)) { throw "Couldn't download $($t.File) with the expected checksum." }

    $unpacked = Join-Path $cache $t.Name
    if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
    Expand-Archive $archive -DestinationPath $unpacked
    $dest = Join-Path $OutDir $t.Name
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Force $dest | Out-Null

    switch ($t.Name) {
        'ffmpeg' {
            $dir = Get-ChildItem $unpacked -Directory | Select-Object -First 1
            Copy-Item (Join-Path $dir.FullName 'bin\ffmpeg.exe') $dest       # ffprobe and ffplay aren't used
            Copy-Item (Join-Path $dir.FullName 'LICENSE') (Join-Path $dest 'LICENSE.txt')
            Copy-Item (Join-Path $dir.FullName 'README.txt') (Join-Path $dest 'README.txt') # build configuration and source commit
        }
        'exiftool' {
            $dir = Get-ChildItem $unpacked -Directory | Select-Object -First 1
            # The "(-k)" in the name only makes it pause for a key when double-clicked; Chango runs it with arguments.
            Copy-Item (Join-Path $dir.FullName 'exiftool(-k).exe') (Join-Path $dest 'exiftool.exe')
            Copy-Item (Join-Path $dir.FullName 'exiftool_files') (Join-Path $dest 'exiftool_files') -Recurse
            Copy-Item (Join-Path $dir.FullName 'README.txt') (Join-Path $dest 'README.txt')
        }
    }
    Remove-Item $unpacked -Recurse -Force
}
Write-Host "Tools ready: $OutDir"
