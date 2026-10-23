# Building, testing and packaging Flipr

Everything is reproducible on a clean Windows 10/11 machine with the tools below. No admin rights are needed.

## 1. Prerequisites

| Tool | Version | Install |
|---|---|---|
| .NET SDK | 10.0.x | `winget install Microsoft.DotNet.SDK.10` |
| Inno Setup | 6.x (installer only) | `winget install JRSoftware.InnoSetup --scope user` |
| Git | any | `winget install Git.Git` |

## 2. Everyday commands

```powershell
dotnet build Flipr.slnx -c Debug          # whole solution, including the probe and IconGen
dotnet test  tests/Flipr.Tests            # 48 unit + integration tests (real WIC conversions)
dotnet run   --project src/Flipr.App      # run from source; look for the icon in the notification area
```

Command-line switches for `Flipr.exe`:

| Switch | Effect |
|---|---|
| `--background` | Start silently (used by "launch at sign-in"): no welcome toast or guide |
| `--render-ui <folder>` | Render every surface (wheel, toasts, Settings pages, guide, tray menu) in light and dark at 100% and 150% to PNG. No screen capture; settings untouched. Can run alongside a live instance |

The probe remains available for regression and diagnostics:
```powershell
experiments\OrbitProbe\bin\Debug\net10.0-windows10.0.19041.0\OrbitProbe.exe --selftest <folder>
```

## 3. Release packaging

```powershell
./build/package.ps1                         # version from build/Common.props
./build/package.ps1 -Version 0.1.1-alpha    # override
./build/package.ps1 -SkipTests -SkipInstaller
```

Outputs in `artifacts/`:

| File | What |
|---|---|
| `Flipr-<ver>-win-x64-setup.exe` | Per-user installer (no admin): Start Menu shortcut, optional "start at sign-in", optional desktop icon, clean uninstall |
| `Flipr-<ver>-win-x64-portable.zip` | Self-contained single `Flipr.exe` + licence files. Unzip and run; no installation |
| `SHA256SUMS.txt` | Checksums for both |

The exe is **self-contained** (the .NET runtime is bundled), so users never install .NET. WPF's native libraries self-extract to `%TEMP%\.net` on first launch.

### What the installer does and undoes

| Install | Uninstall |
|---|---|
| Copies files to `%LOCALAPPDATA%\Programs\Flipr` | Removes them |
| Start Menu shortcut (+ desktop shortcut if chosen) | Removed |
| `HKCU\…\Run\Flipr` if "start at sign-in" is chosen | Always removed (also if the app set it) |
| — | Deletes logs (`%LOCALAPPDATA%\Flipr`); asks before deleting settings (`%APPDATA%\Flipr`) |

Verified locally on 2026-10-18: silent install → files, shortcut and uninstall entry present; silent uninstall → nothing left behind.

## 4. Continuous integration

- `.github/workflows/ci.yml` runs on push and PR: build, tests, the probe self-test, and the UI renders (uploaded as the `ui-review` artifact so reviewers see light **and** dark for every change).
- `.github/workflows/release.yml` runs on a `v*` tag: `package.ps1`, then a **draft** GitHub Release (pre-release when the tag contains `-`). A maintainer reviews and publishes it.

## 5. Brand assets

`tools/IconGen` regenerates the icon set from vectors:
```powershell
dotnet run --project tools/IconGen -- src/Flipr.UI/Assets
```

## 6. Signing (planned for 1.0)

Builds are currently unsigned, so Windows SmartScreen will warn on first run. Code signing (SignPath Foundation or Azure Trusted Signing) is scheduled for milestone M6.
