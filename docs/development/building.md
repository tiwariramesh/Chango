# Building, testing and packaging Chango

Everything is reproducible on a clean Windows 10/11 machine with the tools below. No admin rights are needed.

## 1. Prerequisites

| Tool | Version | Install |
|---|---|---|
| .NET SDK | 10.0.x | `winget install Microsoft.DotNet.SDK.10` |
| Inno Setup | 6.x (installer only) | `winget install JRSoftware.InnoSetup --scope user` |
| Git | any | `winget install Git.Git` |

## 2. Everyday commands

```powershell
dotnet build Chango.slnx -c Debug          # whole solution, including the probe and IconGen
dotnet test  tests/Chango.Tests            # 48 unit + integration tests (real WIC conversions)
dotnet run   --project src/Chango.App      # run from source; look for the icon in the notification area
```

Command-line switches for `Chango.exe`:

| Switch | Effect |
|---|---|
| `--background` | Start silently (used by "launch at sign-in"): no welcome toast or guide |
| `--render-ui <folder>` | Render every surface (wheel, toasts, Settings pages, guide, tray menu) in light and dark at 100% and 150% to PNG. No screen capture; settings untouched. Can run alongside a live instance. CI runs it for design review |
| `--render-store <folder>` | The Microsoft Store artwork: screenshots, box art, poster, hero and Store logo (see `build/store/LISTING.md`) |
| `--render-guide <folder>` | The tour's demonstrations as looping GIFs, light and dark, for the README and docs |
| `--render-guide-frames <folder>` | The same demonstrations as PNG frames (2×, 30 fps), to encode into looping web videos for the website |
| `--render-film <folder>` | The real UI states the launch film is made from, at 3× on transparent backgrounds |

The `--render-*` switches run offscreen alongside a live Chango and never touch settings or the screen.

## 3. Release packaging

```powershell
./build/package.ps1                         # version from build/Common.props
./build/package.ps1 -Version 1.0.1          # override
./build/package.ps1 -SkipTests -SkipInstaller
```

Outputs in `artifacts/`:

| File | What |
|---|---|
| `Chango-<ver>-win-x64-setup.exe` | Per-user installer (no admin): Start Menu shortcut, optional "start at sign-in", optional desktop icon, clean uninstall |
| `Chango-<ver>-win-x64-portable.zip` | Self-contained single `Chango.exe` + licence files. Unzip and run; no installation |
| `SHA256SUMS.txt` | Checksums for both |

The exe is **self-contained** (the .NET runtime is bundled), so users never install .NET. WPF's native libraries self-extract to `%TEMP%\.net` on first launch.

### What the installer does and undoes

| Install | Uninstall |
|---|---|
| Copies files to `%LOCALAPPDATA%\Programs\Chango` | Removes them |
| Start Menu shortcut (+ desktop shortcut if chosen) | Removed |
| `HKCU\…\Run\Chango` if "start at sign-in" is chosen | Always removed (also if the app set it) |
| — | Deletes logs (`%LOCALAPPDATA%\Chango`); asks before deleting settings (`%APPDATA%\Chango`) |

Verified locally on 2026-10-18: silent install → files, shortcut and uninstall entry present; silent uninstall → nothing left behind.

## 4. Continuous integration

- `.github/workflows/ci.yml` runs on push and PR: build, tests and the UI renders (uploaded as the `ui-review` artifact so reviewers see light **and** dark for every change).
- `.github/workflows/release.yml` runs on a `v*` tag: `package.ps1`, then publishes the GitHub Release with `build/release-notes.md` as its text (a pre-release when the tag contains `-`, such as `v1.1.0-beta`).

## 5. Brand assets

`tools/IconGen` regenerates the icons and launch animation in `src/Chango.UI/Assets` from `assets/brand/logo/chango-mark.svg`:
```powershell
dotnet run --project tools/IconGen
```
The name and logo aren't MIT-licensed: see `assets/brand/README.md`.

## 6. Signing

Releases are unsigned for now, so Windows SmartScreen warns the first time someone runs the installer. The options, cheapest first:

| Option | Cost | Notes |
|---|---|---|
| **Microsoft Store (MSIX)** | Free | Microsoft signs the package; Store installs show no SmartScreen warning. Individual developer accounts are free. `build/store/package-store.ps1` and `LISTING.md` are ready. |
| **SignPath Foundation** | Free for open source | Signs GitHub release builds from CI. Needs an OSI licence, a public repo, an automated build, and a code-signing policy page; the certificate is in SignPath Foundation's name. |
| **Azure Artifact Signing** | About US$10 a month | Individuals in the USA and Canada only (organisations also in the EU and UK). |
| **OV certificate** (Certum, Sectigo …) | About US$50–300 a year | Worldwide; the key lives on a hardware token or cloud HSM. |

No certificate removes the warning straight away any more (EV lost that in 2024): SmartScreen trusts a publisher once its signed downloads build up reputation.
