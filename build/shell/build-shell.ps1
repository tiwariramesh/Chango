<#
.SYNOPSIS
  Builds FliprShell.dll, Flipr's File Explorer right-click entry (src/Flipr.ShellMenu), x64.
.DESCRIPTION
  Finds the Visual C++ build tools with vswhere and compiles with the static C runtime, so the DLL needs nothing
  installed beyond Windows. Output: artifacts\shell\FliprShell.dll
#>
param([string]$OutDir)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts\shell' }
New-Item -ItemType Directory -Force $OutDir | Out-Null

$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
$vswhere = Join-Path $installer 'vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found: install Visual Studio (or Build Tools) with C++.' }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'No Visual C++ x64 build tools found.' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$env:PATH = "$installer;$env:PATH" # vcvars64 looks for vswhere on PATH

$src = Join-Path $root 'src\Flipr.ShellMenu'
$obj = Join-Path $OutDir 'obj'
New-Item -ItemType Directory -Force $obj | Out-Null
$cpp = Join-Path $src 'FliprShell.cpp'
$def = Join-Path $src 'FliprShell.def'
$o = Join-Path $obj 'FliprShell.obj'
$dll = Join-Path $OutDir 'FliprShell.dll'

$compile = "cl /nologo /c /O2 /MT /EHsc /std:c++17 /W4 /WX /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /Fo""$o"" ""$cpp"""
$link = "link /nologo /DLL /DEF:""$def"" /OUT:""$dll"" ""$o"" ole32.lib shell32.lib shlwapi.lib user32.lib advapi32.lib kernel32.lib"
cmd /c """$vcvars"" >nul && $compile && $link"
if ($LASTEXITCODE -ne 0) { throw 'Building FliprShell.dll failed.' }
Remove-Item (Join-Path $OutDir 'FliprShell.exp'), (Join-Path $OutDir 'FliprShell.lib') -ErrorAction SilentlyContinue
Write-Host "Built $dll"
