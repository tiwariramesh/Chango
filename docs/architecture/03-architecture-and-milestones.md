# OrbitDrop: Production Architecture and Milestones

| | |
|---|---|
| Status | Adopted for Milestone 1 (v0.1.0-alpha) |
| Date | 2026-10-18 |
| Basis | Initial assessment §4, feasibility report (preliminary GO), product owner's next-phase brief |

## 1. Principles carried forward

1. **Protect the proven interaction.** The drag mechanism is ported from Orbit Probe *in substance unchanged*: native `IDropTarget` + `IDropTargetHelper`, COPY-only, modifier polling only during drags, physical-pixel placement. Only the log-driven changes from feasibility §6 are applied.
2. **The UI never touches engines.** Engines never touch Win32.
3. **Everything is an operation** (convert, tool or action), described by a provider and listed by a registry. The wheel only ever shows what the registry says is valid for *all* dragged files.
4. **No fake features.** A setting, menu item or segment exists only if it works end to end.
5. **No unnecessary dependencies.** M1 ships with **zero third-party runtime packages**. WPF, WIC and Win32 are all part of Windows / .NET. Test-only packages (xUnit) don't ship.

## 2. Solution layout

```
OrbitDrop.slnx
├─ src/
│  ├─ OrbitDrop.App            WinExe: composition root, lifecycle, single instance, InteractionController
│  ├─ OrbitDrop.UI             design tokens, ThemeManager, RadialWheel, Toasts, Settings, Onboarding, About, Tray flyout
│  ├─ OrbitDrop.Windows        Win32/COM only: drag detection, drop target, tray icon, theme watcher,
│  │                           startup registration, shell integration, screen backdrop capture, DPI
│  ├─ OrbitDrop.Core           contracts & rules: FileSet, OperationDescriptor, IOperationProvider,
│  │                           OperationRegistry, SafeOutput (collision-safe atomic writes)
│  ├─ OrbitDrop.Jobs           background job queue: progress, cancellation, concurrency, outcomes
│  ├─ OrbitDrop.Conversion     engines + format conversion providers (M1: WIC image engine)
│  ├─ OrbitDrop.Tools          tool and file-action providers (M1: rotate/flip/grayscale; copy path/name, reveal)
│  └─ OrbitDrop.Infrastructure settings store (versioned JSON), file logging, app paths
├─ tests/OrbitDrop.Tests       xUnit: naming, safety, registry rules, conversions, jobs, wheel geometry, settings
├─ experiments/OrbitProbe      the feasibility probe: kept independent as a regression and diagnostics tool
├─ tools/IconGen               reproducible brand-icon generator (vector → .ico/.png)
├─ build/                      publish & installer scripts (Inno Setup)
└─ .github/workflows/          CI (build + test) and Release (tag → installer + portable)
```

### Dependency direction

```
App ──► UI ──► Windows ──► Core
 │      └──► Jobs, Infrastructure, Core
 ├────► Windows ──► Core
 ├────► Jobs ─────► Core
 ├────► Conversion ► Core
 ├────► Tools ────► Conversion, Core
 └────► Infrastructure ► Core
```

- `Core`, `Jobs` and `Infrastructure` target plain `net10.0`, so they're portable and fast to test.
- `Windows`, `UI`, `Conversion` (WIC through WPF imaging), `Tools` and `App` target `net10.0-windows10.0.19041.0`.

## 3. Runtime flow (M1)

```
Explorer drag ─► [Windows] DragSessionDetector: SysDragImage shown (WinEvent, out-of-context)
             ─► polls trigger key + drag button at 60 Hz *only while that window exists*
Trigger tap  ─► [App] InteractionController.Open ─► [UI] RadialWheel.ShowAt(cursor)  (pre-warmed window)
OLE DragEnter─► [Windows] NativeDropTarget ─► FileSet ─► [Core] registry.Query(FileSet, mode) ─► wheel segments
OLE DragOver ─► hit-test ─► highlight + DROPDESCRIPTION ("Convert to PNG") ─► DROPEFFECT_COPY
Trigger tap  ─► cycle Convert → Tools → Actions
OLE Drop     ─► [Jobs] enqueue ─► provider.ExecuteAsync ─► SafeOutput (temp → validate → atomic rename)
             ─► [UI] Toast: progress · cancel · success/partial/error · "Show in folder"
Esc / release elsewhere ─► dismiss, nothing written
```

## 4. Decisions for M1

| Topic | Decision | Reason |
|---|---|---|
| Drag-start signal | `SysDragImage` WinEvent. Optional low-level mouse hook in Advanced settings ("Extended drag detection"), off by default | Feasibility R7/R9 |
| Trigger key | Shift (default) or Ctrl, configurable. The same key cycles modes | Approved Shift-cycling. Ctrl is a real alternative with no side effects on our target |
| Session end | Drag-button release, Esc, or drop. *Not* drag-image hide | Feasibility R8 |
| Theme | WPF Fluent `ThemeMode` (System/Light/Dark, live) + OrbitDrop token dictionaries swapped live | Native Windows 11 look with Mica, no dependency. API is marked experimental (`WPF0001`); isolated in `ThemeManager` |
| Wheel material | One-time snapshot of the area behind the wheel (GDI `BitBlt` *without* `CAPTUREBLT`, so the drag image is excluded), downsampled and blurred, then tinted | The frosted look is impossible on per-pixel-alpha windows through public DWM APIs |
| Accent | Monochrome (black on light, white on dark) by default; "Use Windows accent colour" option with contrast guard | Brand is black and white (design system v1.1); a user's own accent can be dark grey (probe finding F2) |
| Processing | In-process WIC on dedicated STA threads | Built into Windows. **Process isolation (`OrbitDrop.Worker` + Job Objects) arrives in M2** together with native third-party engines |
| Tray | Own `Shell_NotifyIcon` wrapper + custom WPF flyout menu | ~150 lines, so no WinForms or third-party package, and the menu matches the design system |
| Installer | **Inno Setup 6**, per-user (`PrivilegesRequired=lowest`), no admin, Start Menu, clean uninstall | Free, standard, preinstalled on GitHub's Windows runners |
| Distribution | Self-contained single-file `OrbitDrop.exe` (win-x64) → installer `.exe` + portable `.zip` | Users don't need .NET |
| Settings | `%APPDATA%\OrbitDrop\settings.json`, `schemaVersion` + migration step, atomic write | Survives upgrades |
| Logs | `%LOCALAPPDATA%\OrbitDrop\logs\`, daily files, 7 kept. File names logged only when "Detailed logging" is on | Privacy (brief §11) |

## 5. Milestones

Each milestone ends with: tests green, light **and** dark renders reviewed, manual checklist run, and docs updated. Version numbers follow the brief's release plan.

| Milestone | Version | Scope | Exit criteria |
|---|---|---|---|
| **M1 Foundation** *(this)* | 0.1.0-alpha | Tray app, single instance, refined wheel, live themes, Settings (General · Appearance · Shortcuts · Conversions · Advanced), About, first-run guide, toasts with cancel, image convert (PNG/JPG/BMP/TIFF/GIF) + rotate/flip/grayscale, file actions, packaging pipeline, CI | Builds and tests in CI; installer installs and uninstalls cleanly; manual smoke test of drag → wheel → convert in both themes |
| **M2 Hardening** | 0.2.0 | Remaining feasibility tests (formal GO); **Fallback C** hotkey on the Explorer selection + full keyboard navigation of the wheel; `OrbitDrop.Worker` process isolation + Job Objects; Magick.NET engine (WebP, HEIC, AVIF, auto-orient); resize, compress, strip metadata; `.lnk` target resolution | Formal GO signed off; keyboard-only path demonstrated |
| **M3 Editors** | 0.3.0 | Crop editor (multi-image, aspect presets, **Apply to All**), resize/compress panels, images → PDF, collage | Crop flow matches the reference video's capability |
| **M4 Media & PDF** | 0.4.0 | FFmpeg (LGPL build) audio/video convert, extract/remove audio, trim, compress; PDF merge/split/rotate/to-image (PdfSharp, Windows.Data.Pdf) | Fixture-verified outputs |
| **M5 Stabilisation** | 0.5.0 | Documents (LibreOffice detection), archives, Explorer context menu (Fallback B), multi-monitor and mixed-DPI hardening, Windows 10 pass, accessibility audit, performance and resource budget | Brief §8 v0.5 list |
| **M6 Release** | 1.0.0 | Code signing (SignPath / Azure Trusted Signing), governance files, demo recording, licence audit, release automation polish | Brief §14 acceptance criteria |

## 6. Resource budget (targets, profiled from M2)

- Idle: 0% CPU (no timers run without a drag). Working set below ~80 MB (WPF baseline).
- Wheel open: under 16 ms after the trigger (measured median 2 ms in the probe).
- No polling, scanning or network activity while idle.
