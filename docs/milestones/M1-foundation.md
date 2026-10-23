# Milestone 1 (Foundation): summary

| | |
|---|---|
| Version | 0.1.0-alpha |
| Date | 2026-10-18 |
| Status | **Implemented and verified by automation; awaiting the product owner's manual smoke test (§4)** |
| Gate | The interaction is *not yet production-ready*: formal GO pending ([feasibility §5](../research/02-feasibility-report.md)) |

## 1. Implemented

| Area | What works | How it was verified |
|---|---|---|
| **Radial interaction** | Drag files → tap Shift (or Ctrl) → wheel at the pointer → real file list before the drop → hover → release runs the operation. Shift cycles Convert → Tools → Actions. Esc / centre / release elsewhere cancels. COPY-only answers to OLE | Ported unchanged in substance from the probe (18/18 manual sessions). Startup log confirms the drop target registers. **Manual run on the production build pending (§4)** |
| **Drag detection** | `SysDragImage` WinEvent latched until button release (R7/R8); optional mouse-hook "extended detection" (R9); 60 Hz polling only during drags | Idle CPU measured at **0 ms over 5 s** |
| **Wheel design** | Frosted backdrop snapshot, rim highlight, E3 shadow, rounded segments with hover lift, icons for tools and actions, hub with mode dots, bloom / mode-swap / exit motion, reduced-motion paths, high-contrast palette, Compact / Standard / Large, pre-warmed | 100 offscreen renders reviewed (light/dark × bright/dark wallpaper × 100/150%) |
| **Themes** | System / Light / Dark, applied **live**; follows Windows changes; optional Windows accent with a contrast guard | Renders in both themes; theme switch exercised in the review harness |
| **Tray** | Custom icon, flyout (status, Pause/Resume, Settings, How to use, About, Quit), re-adds itself after an Explorer restart | Startup log `Tray icon added`; flyout render |
| **Settings** | General, Appearance, Shortcuts, Conversions, Advanced, About. Every control is wired: sign-in launch (HKCU Run), completion notifications, reveal in Explorer, theme, accent, wheel size, reduce motion, trigger key, output location (beside the original / chosen folder), JPG quality, extended detection, detailed logging, open data folders, reset | View-model wiring and settings round-trip/migration tests; renders |
| **Progress and results** | One toast per job: progress ring and bar, cancel, success / partial / error / canceled / nothing-to-do summaries, "Show" reveals outputs, hover pauses auto-dismiss | `Summarize` unit tests; renders |
| **First run** | Three-step guide with original illustrations; shown once, reopenable from tray and Settings | Renders |
| **Operations** | Convert PNG/JPG/BMP/TIFF/GIF; rotate right/left, flip, grayscale (keeps transparency); copy path/name, show in folder. **Video/audio via Windows Media Foundation** (added after the first review): MP4/WMV, extract audio as MP3/M4A/WAV/WMA, audio conversions, compress to 720p, remove audio, with per-file progress | 61 tests (real WIC and Media Foundation transcodes, outputs decoded or signature-checked) |
| **Integrity** | Hidden temp → validate → atomic no-replace rename; collision numbering; nothing partial on failure or cancel; originals hash-identical | `SafeOutputTests`, `ImageOperationTests` |
| **Lifecycle** | Single instance (a second launch opens Settings), `--background`, crash-safe handlers, local daily logs (7 kept, file names only in detailed mode) | Smoke test: second instance exited 0 |
| **Packaging** | Self-contained single-file exe, per-user Inno installer, portable zip, SHA256SUMS, CI + draft-release workflows | Built locally; silent install and uninstall verified clean |

## 2. Known limitations (honest list)

1. **Manual Explorer testing of the production build hasn't been done yet**, and multi-monitor, mixed-DPI and Windows 10 remain unverified (feasibility §5).
2. **Image engine is WIC only:** no WebP/HEIC/AVIF, no EXIF auto-rotation, first frame only for animated GIF and multi-page TIFF. Magick.NET arrives in M2.
   **Video uses Windows Media Foundation:** no WebM/MKV *output*, no video→GIF, and input codecs depend on what Windows has installed. FFmpeg (LGPL) is planned for M4.
3. **Processing is in-process** (WIC is a Windows component). The isolated worker process and Job Objects arrive in M2 with third-party engines.
4. **No keyboard-only path yet.** Fallback C (hotkey on the Explorer selection) is M2 and will be in every build from then on, as agreed.
5. **The frosted backdrop is a snapshot**, so video playing behind the wheel won't update in it. If Windows includes the drag image in the snapshot on some configurations, a faint blurred ghost may appear (to check during the manual test).
6. **Toasts stack at the bottom-right of the drop monitor**, not beside the cursor as in the reference video. That's a deliberate choice so they don't cover your work; open to feedback.
7. **Memory:** about 100 MB working set at idle (WPF baseline); target 80 MB, profiling in M2.
8. **Unsigned builds:** SmartScreen warns until signing (M6).
9. **Desktop `.lnk` shortcuts** are treated as the shortcut file (target resolution in M2, feasibility R13).
10. WPF Fluent `ThemeMode` is an experimental .NET API (`WPF0001`), isolated in `ThemeManager` so it can be replaced.

## 3. Next priorities (M2, in order)

1. Run the remaining feasibility tests on the production build → **formal GO**.
2. Fallback C hotkey + full keyboard navigation of the wheel (accessibility).
3. `OrbitDrop.Worker` process isolation + Job Objects.
4. Magick.NET engine (licence review first): WebP, HEIC, AVIF, auto-orient; resize, compress and strip-metadata tools.
5. `.lnk` target resolution; memory profiling.
6. Then M3: the **crop editor** with multi-image and Apply to All, as in the reference video.

## 4. Manual smoke test for the product owner (~10 minutes)

Install `artifacts/OrbitDrop-0.1.0-alpha-win-x64-setup.exe` (or run the portable exe). Keep `C:\OrbitTest` from the probe run.

| # | Do | Expect |
|---|---|---|
| S1 | Install, leave "Start OrbitDrop now" ticked | The welcome guide opens; the OrbitDrop icon is in the notification area (it may be in the overflow ^) |
| S2 | Finish the guide. Drag `a.jpg`, tap Shift, release on **PNG** | The frosted wheel blooms at the pointer; the drag tooltip says "Convert to PNG"; a toast "Saved a (N).png"; **Show** opens Explorer on it |
| S3 | Drag 3 images, tap Shift ×2 (Tools), release on **Rotate right** | Hub says "3 files"; toast shows progress, then "3 files saved" |
| S4 | Drag `c.pdf`, tap Shift, then tap twice more, release on **Copy path** | Convert shows "No conversions yet · Tap Shift for more"; Actions works; toast "Copy path" |
| S5 | Esc during a drag with the wheel open | Wheel vanishes, nothing created |
| S6 | Tray icon → Settings → Appearance: switch Light / Dark / System | Settings, tray menu and the next wheel follow instantly |
| S7 | Settings → Appearance → **Preview the wheel**; switch Compact / Large | Wheel previews at each size |
| S8 | Settings → Shortcuts → **Ctrl**, then repeat S2 with Ctrl | Works with Ctrl; Shift no longer opens it |
| S9 | Tray → Pause, try S2; tray → Resume | No wheel while paused |
| S10 | Note anything that looks off: a faint ghost in the frosted backdrop, toast placement, motion, sizes | Free text |

Then uninstall from Settings → Apps. Everything should be removed; the uninstaller asks whether to keep your settings.
