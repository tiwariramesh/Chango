<#
.SYNOPSIS
  Regenerates website/assets from the app itself: the guide's demo scenes as looping MP4s (light and dark), the
  wheels, Settings and a notification as WebP, the social card, and the icons.
.DESCRIPTION
  Everything is rendered by Chango's own offscreen renderers (--render-guide-frames, --render-film, --render-store),
  so the site always shows the real UI. Needs FFmpeg (winget install Gyan.FFmpeg).
  The demo videos are flattened onto the page colour (--bg in website/styles.css); change both together.
.EXAMPLE
  ./build/website/make-assets.ps1
  ./build/website/make-assets.ps1 -Exe artifacts\store\layout\Chango.exe
#>
param([string]$Exe)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$site = Join-Path $root 'website'
$assets = Join-Path $site 'assets'
$demo = Join-Path $assets 'demo'
$work = Join-Path $root 'artifacts\website'

$ff = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $ff) { $ff = (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Gyan.FFmpeg*\*\bin\ffmpeg.exe" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName }
if (-not $ff) { throw 'FFmpeg not found: winget install Gyan.FFmpeg' }

if (-not $Exe) {
  Write-Host '> Build'
  dotnet build (Join-Path $root 'src\Chango.App\Chango.App.csproj') -c Release --nologo -v q | Out-Null
  if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }
  $Exe = (Get-ChildItem (Join-Path $root 'src\Chango.App\bin\Release') -Recurse -Filter Chango.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}
$Exe = (Resolve-Path $Exe).Path

function Render([string]$flag, [string]$dir) {
  if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
  New-Item -ItemType Directory -Force $dir | Out-Null
  $p = Start-Process $Exe -ArgumentList $flag, "`"$dir`"" -PassThru -Wait
  if ($p.ExitCode -ne 0) { throw "Chango $flag failed ($($p.ExitCode))" }
}
Write-Host '> Render'
Render '--render-guide-frames' (Join-Path $work 'frames')
Render '--render-film' (Join-Path $work 'film')
Render '--render-store' (Join-Path $work 'store')

function Run([string[]]$a) { & $ff -hide_banner -loglevel error -y @a; if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed: $a" } }
New-Item -ItemType Directory -Force $assets, $demo | Out-Null
$bg = @{ '' = '0xFAFBFC'; '-dark' = '0x0E1214' }

Write-Host '> Demo videos'
# Frames are 1200x640 with transparent rounded corners: flatten them on the page colour.
foreach ($scene in 'drag','release','modes','groups','keyboard','customize') {
  foreach ($t in '', '-dark') {
    $frames = Join-Path $work "frames\$scene$t"
    Run @('-framerate','30','-i',(Join-Path $frames '%04d.png'),'-filter_complex',"color=c=$($bg[$t]):s=1200x640:r=30[b];[b][0:v]overlay=shortest=1,format=yuv420p",
          '-c:v','libx264','-preset','slow','-crf','27','-tune','animation','-movflags','+faststart','-an',(Join-Path $demo "$scene$t.mp4"))
    # Poster: a representative frame, shown until the video plays (and instead of it with reduced motion)
    $n = (Get-ChildItem $frames).Count
    $frame = Join-Path $frames ('{0:D4}.png' -f [int]($n * 0.55))
    Run @('-i',$frame,'-filter_complex',"color=c=$($bg[$t]):s=1200x640[b];[b][0:v]overlay",'-frames:v','1','-c:v','libwebp','-quality','80',(Join-Path $demo "$scene$t.webp"))
  }
}

Write-Host '> Screenshots'
function Img([string]$name, [int]$width, [string]$out) {
  foreach ($t in '', '-dark') {
    Run @('-i',(Join-Path $work "film\$name$t.png"),'-vf',"scale=${width}:-1:flags=lanczos",'-c:v','libwebp','-quality','88','-pix_fmt','yuva420p',(Join-Path $assets "$out$t.webp"))
  }
}
Img 'wheel-convert-idle' 720 'wheel-idle'
Img 'wheel-convert-png' 720 'wheel-convert'
Img 'wheel-tools-rotate' 720 'wheel-tools'
Img 'wheel-actions-reveal' 720 'wheel-actions'
Img 'toast-photo-saved' 740 'toast'
Img 'settings-appearance' 1400 'settings'

Write-Host '> Social card and icons'
Run @('-i',(Join-Path $work 'store\hero-16x9-3840x2160.png'),'-vf','scale=1200:-1:flags=lanczos,crop=1200:630','-q:v','3',(Join-Path $assets 'social.jpg'))
Copy-Item (Join-Path $root 'assets\brand\logo\chango-mark.svg') (Join-Path $site 'favicon.svg')
Copy-Item (Join-Path $root 'src\Chango.UI\Assets\chango.ico') (Join-Path $site 'favicon.ico')
Run @('-i',(Join-Path $root 'src\Chango.UI\Assets\chango-256.png'),'-vf','scale=180:180:flags=lanczos,format=rgba','-frames:v','1',(Join-Path $site 'apple-touch-icon.png'))

$sum = (Get-ChildItem $assets -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Done: website/assets is {0:N1} MB" -f ($sum / 1MB))
