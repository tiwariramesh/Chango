# OrbitDrop — Initial Assessment (Steps 1–2 of the Master Brief)

| | |
|---|---|
| Status | Research complete; awaiting go-ahead for the Step 3 proof of concept |
| Date | 2026-10-18 |
| Dev machine | Windows 11 Pro, build 26200 (single machine; no Win10 VM yet) |
| Toolchain found | .NET SDK 8.0.411 only (**.NET 10 SDK not installed**), Git, winget. FFmpeg 9.0.2 (Gyan full build) installed for frame analysis |

This document is the "first required agent response" from section 15 of the brief. It covers the seven required items in order. **No production code has been written.** Every Windows behavior below that has not yet been tested on this machine is marked as a **hypothesis (H-n)**. The proof of concept exists to confirm or reject those hypotheses.

---

## 1. Understanding of the product

OrbitDrop is a **tray-resident Windows utility whose whole product is one gesture**:

> Start dragging files in Explorer or on the desktop → press a key → a radial menu blooms under the cursor → release over an option → the result appears next to the original.

Things that follow from that:

- **The interaction is the product.** A converter with 300 formats behind a normal window is the existing [File Converter](https://github.com/Tichau/FileConverter), not OrbitDrop. Format count is secondary.
- **The UI is out of the way by default.** There's no main window. The surfaces are the wheel, a progress pill or toast, small parameter editors (crop, compress), settings, and the tray menu.
- **Trust is a feature.** Originals are never touched. Nothing overwrites anything. Nothing leaves the machine. Failures are reported per file.
- **Extensibility is structural.** Adding a converter or tool means adding a provider. It never means editing the wheel or the core.

## 2. Analysis of the Tangerine reference interaction

I extracted the 19-second demo at 2 fps and 10 fps and analysed it frame by frame. All times are from the video.

### 2.1 Timeline

| t (s) | What happens | Design takeaway |
|---|---|---|
| 0.0–0.4 | Mouse down on `Page 001.pdf`. Drag begins at ~0.4 and the drag image leaves the icon. | The wheel is never shown before a real drag exists. |
| 0.5 | The wheel starts as a small disc (~30% size) centred on the cursor. | It blooms *from the cursor*. |
| 0.6 | Wheel at full size. The bloom takes **~150–200 ms** (scale + opacity, slight overshoot). | Fast and springy, never sluggish. |
| 0.6 | 4 segments: DOCX, JPG, PNG, TXT. The hub pill shows the file size (`1.1 MB`). | The hub shows context when nothing is hovered. |
| 1.0 | The cursor moves onto TXT. The segment fills with the solid accent colour, the hub pill now reads `TXT`, and the drag badge shows a green **"+"**. | Hover = solid fill and the hub echoes the label. The "+" means the wheel accepts the drop as **copy**. |
| 1.5–2.3 | Hovers JPG, then releases. | The wheel stays **anchored where it opened**; the drag image moves across it. |
| 2.4 | The wheel is gone within ≤100 ms of release (effectively no exit animation). | Dismissal is instant. |
| 2.5–2.7 | A small toast near the cursor: "✕ Converting to JPG · 1 of 1 saved" with a thin progress bar. It auto-dismisses. | Progress is transient and cancellable (✕). |
| 2.8 | `Page 001.jpg` appears beside `Page 001.pdf`. | Same base name, new extension. No dialogs. |
| 4.0–5.0 | Marquee-selects 2 images and drags them. The drag image is stacked with a red **"2"** count badge. | Multi-file drag is first-class. |
| 5.3–5.5 | The wheel blooms with **7** segments: WEBP, HEIC, TIFF, AVIF, BMP, PDF, DOCX. | The segment count adapts to what's valid for *all* selected files. |
| 5.8 | Mode switch **during the same drag**: COMPRESS, CROP, CREATE PDF, COLLAGE, each with an icon. | Modes switch without ending the drag. Tools get icon + label; formats get text only. |
| 7.7 | Release over CROP. The wheel vanishes and the crop editor **zooms in** (blurred → sharp over ~400 ms). | Tools that need parameters open a compact floating panel. |
| 8.5–15 | Crop editor: large preview with corner/edge handles, a thumbnail strip (one per file, with a "modified" badge), aspect presets *Free / 1:1 / 16:9 / 9:16 / 4:3 / 3:4*, W/H px fields, *Apply to All*, *Reset*, *Apply*. | Per-file crop state inside one batch editor. |
| 15.1–16 | Apply → toast "Cropping image · 2 of 2 saved" → `tangerine tree Cropped.png`, `rhys-kentish-…Cropped.jpg`. | Tools keep the source format and add a suffix. |

### 2.2 Visual anatomy of the wheel

```
            ╭──────────── outer translucent rim ────────────╮
          ╱    ╭─────────╮                                     ╲
         │    │  DOCX   │   ← rounded "petal" segments          │
         │ ╭──╯         ╰──╮   with ~6 px gaps                  │
         │ │ TXT ╭───────╮ JPG │                                │
         │ │     │ 1.1MB │     │ ← hub pill: context / hovered  │
         │ ╰──╮  ╰───────╯  ╭──╯    label                       │
         │    │    PNG      │                                   │
          ╲   ╰─────────────╯                                  ╱
            ╰───────────────────────────────────────────────╯
```

- The material is frosted and translucent. It tints from the background.
- Hover state = solid accent fill with white label. Every other segment stays frosted.
- The diameter is about 2.5× a desktop icon (≈ 260 logical px on the Retina capture).

### 2.3 What OrbitDrop should do differently (originality, not copying)

- **Accent:** default to the **Windows accent colour** (user-configurable), not Tangerine's orange.
- **Icons, illustrations, motion curves, logo and toasts** are designed fresh. Only the *interaction pattern* is shared.
- **Naming:** conversions keep the base name (`Page 001.jpg`). Tools add a suffix: `photo (cropped).png`, following the brief's `(converted)` style rather than Tangerine's ` Cropped`. Collisions append ` (2)`, ` (3)` and so on.

---

## 3. Windows Shell drag-and-drop feasibility strategy

### 3.1 The key insight

The brief is right that a background process **cannot read another process's in-flight drag payload**. It doesn't need to.

During `DoDragDrop`, OLE hit-tests the window under the cursor on every mouse move or key-state change. If that window is registered with `RegisterDragDrop`, OLE marshals Explorer's `IDataObject` to it **across processes** and calls `DragEnter` / `DragOver` / `Drop`.

So OrbitDrop doesn't *intercept* the drag. It **becomes the drop target** by putting a registered, topmost window under the cursor at the right moment. This is the documented, supported mechanism. It needs no injection, no hooks into Explorer and no admin rights.

That reframes the risk into four narrower questions, which the PoC answers:

| # | Question | Hypothesis |
|---|---|---|
| **H-1** | Can a topmost window that appears *mid-drag* under the cursor receive `DragEnter` with Explorer's data object? | Yes. OLE re-evaluates the target on the next mouse move or key-state change. |
| **H-2** | Can we read the file list in `DragEnter`, **before** the drop, so the wheel can show valid formats? | Yes. `CF_HDROP` / `Shell IDList Array` are readable in `DragEnter`. |
| **H-3** | Can we reliably detect *that a shell drag has started* without polling or injection? | Probably, through one of: (a) `SetWinEventHook(EVENT_SYSTEM_DRAGDROPSTART/END)`, (b) `EVENT_OBJECT_SHOW` of the shell's `SysDragImage` window, (c) a low-level mouse hook heuristic (button down + moved > `SM_CXDRAG`). The PoC logs all three and we pick the most reliable. |
| **H-4** | Does returning `DROPEFFECT_COPY` guarantee Explorer never deletes or moves the source? | Yes, because `MOVE` is the only effect that makes the source delete. We will **never** return `DROPEFFECT_MOVE`, and the PoC verifies with SHA-256 before and after. |
| H-5 | Does the drag image stay *above* our topmost wheel? | Uncertain. Fixes: call `IDropTargetHelper` (the documented way to keep the shell drag image correct over a target), or insert our window below `SysDragImage` in the z-order. |
| H-6 | Can we show Windows-native drop text ("Convert to PNG") on the drag image? | Yes, via `DROPDESCRIPTION` (Vista+) set on the data object from `DragOver`. |
| H-7 | Does WPF's built-in `AllowDrop` suffice, or do we need a raw COM `IDropTarget`? | A raw `IDropTarget` is likely needed for H-5 and H-6 control. The PoC tests both. |
| H-8 | Does tapping **Shift** mid-drag cause side effects in Explorer? | Shift = "force move" *while over Explorer's own target*. It is harmless unless the user also releases over Explorer. Once the cursor is on our wheel, *we* choose the effect, so we always choose copy. |

### 3.2 Proposed detection pipeline (lowest-footprint design)

```
idle ──(WinEvent: drag start, out-of-context, zero polling)──▶ drag-armed
drag-armed: poll GetAsyncKeyState(Shift, LButton) at 60 Hz *only while a drag exists*
     │  Shift tapped
     ▼
show wheel (topmost, WS_EX_NOACTIVATE, PMv2 DPI-aware, registered IDropTarget)
     │  OLE: DragEnter(IDataObject) → read CF_HDROP → build menu from capability registry
     │  OLE: DragOver(pt, keys) → hit-test segment → highlight + DROPDESCRIPTION → DROPEFFECT_COPY
     │  Shift tapped again → cycle mode (Convert → Tools → Actions)
     ▼
Drop(pt) → copy paths out → return COPY → hide wheel → enqueue job → progress pill
DragLeave → keep the wheel anchored until the drag ends (user may come back)
WinEvent: drag end / LButton up / Escape → dismiss
```

**Why no global keyboard hook:** a `WH_KEYBOARD_LL` hook is what keyloggers use. AV and anti-cheat software flag it, and Windows silently removes it if the UI thread stalls. Polling two key states **only during an active drag** costs nothing, and it gives us a strong privacy statement: *OrbitDrop only looks at modifier keys while you're dragging a file.* If H-3(a/b) fail, the fallback is a `WH_MOUSE_LL` hook, which is still no keyboard hook.

**Must-not-run-elevated:** UIPI blocks drag-and-drop from a medium-integrity Explorer into a high-integrity process. OrbitDrop must run as the normal user. This aligns with the brief's least-privilege rule.

### 3.3 Known Windows constraints (documented up front)

1. **Keyboard navigation during a drag is limited.** Explorer owns focus and is inside its modal drag loop, so arrow keys and Enter go to Explorer. We can poll arrows to *move the highlight*, but the user still confirms by releasing the mouse. A fully keyboard-driven flow is provided by Fallback C (hotkey on the selection). This is the main accessibility path.
2. **Alt as a modifier is risky.** Tapping Alt alone can put the focused app into menu mode after the drop. The brief's *Ctrl+Alt* for Actions should become **Shift-tap to cycle modes**, plus an optional hub "dwell to switch". Final choice comes after PoC testing.
3. **Escape cancels the OLE drag itself** (Explorer handles it). We see `DragLeave` and the drag ending, and we dismiss. This matches the brief.
4. **Frosted glass** isn't available for arbitrary-shaped WPF layered windows through public APIs. Plan: when the wheel opens, take a one-time snapshot of the area behind it with `BitBlt` *without* `CAPTUREBLT` (this excludes layered windows such as the drag image), blur it and use it as the wheel's backdrop. The wheel is anchored, so a static backdrop looks correct. Visual polish, not a feasibility risk.
5. **Virtual files** (Outlook attachments, zip contents, phones) arrive as `FileGroupDescriptor` rather than `CF_HDROP`. They are out of scope for v0.1. Later we can materialise them to temp.

### 3.4 Fallback hierarchy (only if H-1…H-4 fail)

| Fallback | Mechanism | Fidelity |
|---|---|---|
| **A — Auto orb** | On drag start, a small orb appears *near* the cursor without a key press. Dragging onto it springs it open into the full wheel. | ~90%: one extra flick of the wrist |
| **B — Context menu** | Classic `HKCU\…\shell` verb (no admin) → wheel opens at the cursor in click mode. The Windows 11 *top-level* menu needs `IExplorerCommand` + package identity (MSIX/sparse package + signing). Deferred. | Different gesture |
| **C — Hotkey on selection** | A global hotkey (`RegisterHotKey`, not a hook) reads the selected items of the foreground Explorer window via the documented `Shell.Application` / `IShellWindows` automation, then opens the wheel in click and keyboard mode. | Different gesture, but fully keyboard-accessible, so **shipped regardless** as the accessibility path |

All fallbacks are labelled as such in settings and docs.

### 3.5 Explicitly ruled out

DLL injection into Explorer, hooking `DoDragDrop`, `SetWindowsHookEx` with in-process hooks, shell-extension hacks to read drags, undocumented `SetWindowCompositionAttribute` as a hard dependency, and synthetic input injection (`SendInput`) to complete or cancel drags.

---

## 4. Proposed system architecture

### 4.1 Projects and dependency direction

```
                    ┌──────────────────┐
                    │  OrbitDrop.App   │  composition root, tray, lifecycle, DI, single-instance
                    └──┬──┬──┬──┬──┬───┘
        ┌──────────────┘  │  │  │  └──────────────┐
        ▼                 ▼  │  ▼                 ▼
 OrbitDrop.UI   OrbitDrop.Windows  OrbitDrop.Jobs  OrbitDrop.Infrastructure
 (wheel, editors, (drag detection,  (queue, progress, (settings, logging,
  toasts, settings) IDropTarget,     cancellation,     safe file output,
        │          DPI, hotkey,      worker processes)  localization)
        │          Explorer selection)   │                 │
        └────────────┬───────────┴───────┴─────────────────┘
                     ▼
              OrbitDrop.Core     ← no dependencies: FileSet, FileKind, OperationDescriptor,
                     ▲              IOperationProvider, ICapabilityRegistry, OutputPlan, Result types
        ┌────────────┴────────────┐
 OrbitDrop.Conversion      OrbitDrop.Tools      ← providers (plugins); reference Core only
 (engines: Magick, FFmpeg,  (crop, compress, resize,
  PdfSharp, WIC, LibreOffice…) merge, split, actions)
                     │
              OrbitDrop.Worker   ← separate process that actually runs engines (crash isolation)
              OrbitDrop.Tests
```

Rules:
- **Core** has zero dependencies.
- **Windows** is the only project that touches Win32 or COM.
- **UI** never calls engines.
- **Providers** never reference UI or Windows.
- **App** wires it all together.

### 4.2 The plugin model: everything is an *Operation*

```csharp
// Sketch of the contract, not final code
public sealed record OperationDescriptor(
    string Id,                     // "convert.image.png", "tool.image.crop", "action.copy-path"
    OperationMode Mode,            // Convert | Tool | Action
    string Label, string IconKey,
    BatchSemantics Batch,          // PerFile | Aggregate (merge→1 output) | Ordered (PDF merge order)
    string? EditorId,              // null = runs immediately; "crop" = opens crop panel first
    IReadOnlyList<string> RequiredEngines);

public interface IOperationProvider
{
    IEnumerable<OperationDescriptor> Describe();
    bool CanApply(OperationDescriptor op, FileSet files);   // cheap, extension/signature based
    Task<OperationResult> ExecuteAsync(OperationRequest req, IProgress<JobProgress> p, CancellationToken ct);
}
```

- **Capability matrix:** the registry is the single source of truth. A build step exports it to `docs/capability-matrix.{json,md}` (source, target, engine, presets, batch, limitations, licence, test status). A unit test fails if any advertised conversion has no passing fixture test, so **we can't advertise an untested conversion**.
- **Engine availability** is probed at startup and lazily (for example, is LibreOffice installed?). Operations whose engine is missing are hidden or shown as "install to enable", never offered and then failed.

### 4.3 Event flow (convert case)

```
Explorer DoDragDrop ─▶ [Windows] DragSession(start) ─▶ [App] arm
Shift tap ─▶ [Windows] WheelRequested(pt, monitor) ─▶ [UI] show wheel
OLE DragEnter(dataObj) ─▶ [Windows] FileSet ─▶ [Core] registry.Query(FileSet, Mode) ─▶ [UI] segments
OLE DragOver(pt) ─▶ [UI] hit-test ─▶ highlight + [Windows] DROPDESCRIPTION
OLE Drop ─▶ [App] OperationRequest ─▶ (EditorId? [UI] editor ─▶ params) ─▶ [Jobs] enqueue
[Jobs] ─▶ [Worker process] engine ─▶ temp file ─▶ validate ─▶ atomic rename ─▶ [UI] progress/toast
```

### 4.4 Reliability and safety design

- **Output pipeline:**
  1. Write `.orbitdrop-<guid>.tmp` (hidden) **in the destination folder** (same volume).
  2. Validate the output: non-empty, decodes or probes correctly, and page or frame count is plausible.
  3. `MoveFileEx` **without** `REPLACE_EXISTING` to the collision-safe name. On a race, retry with the next ` (n)` suffix.

  A failure at any step deletes the temp file, so no partial outputs are ever visible.
- **Crash isolation:** native codecs parsing untrusted files run in `OrbitDrop.Worker.exe`, not in the tray process. A malformed HEIC can't take down OrbitDrop.
- **No orphaned processes:** every worker and ffmpeg child is placed in a **Win32 Job Object** with `KILL_ON_JOB_CLOSE`. Cancel kills the whole tree, and so does a crash of OrbitDrop.
- **Concurrency:** a bounded channel with a default of `min(cores/2, 4)`. Video jobs are serialised by default.
- **Input validation:** resolve and canonicalise every path, reject device paths and ADS (`:`), and pass arguments with `ProcessStartInfo.ArgumentList` (never string concatenation). Archive extraction guards against zip-slip and symlinks.
- **UI thread** only renders. The wheel never waits on I/O. `DragEnter` classifies by extension and does cheap header sniffing only if needed.

### 4.5 Technology decisions (proposed)

| Concern | Choice | Note |
|---|---|---|
| Runtime | **.NET 10 (LTS)**, `net10.0-windows10.0.19041` | Needs the SDK installed (see "What I need from you") |
| UI | WPF, Per-Monitor-V2 DPI manifest, MVVM (CommunityToolkit.Mvvm, MIT) | Wheel drawn with WPF geometry, no bitmaps, so it's crisp at every scale |
| Interop | `CsWin32` source generator (MIT) for Win32 / COM signatures | Typed, no hand-written P/Invoke drift |
| Tray | Small `Shell_NotifyIcon` wrapper (or H.NotifyIcon, MIT) | — |
| DI / logging | Microsoft.Extensions.Hosting + Serilog to a local rolling file | Logs never contain file *contents*; paths are redacted by default |

---

## 5. First proof-of-concept milestone: **"Orbit Probe"** (v0.0, throwaway)

### 5.1 Scope (deliberately tiny, about 600–900 lines)

A single WPF exe, `experiments/OrbitProbe/`, **not** part of the product solution:

1. **Signal logger:** subscribes to `EVENT_SYSTEM_DRAGDROPSTART/END`, `EVENT_OBJECT_SHOW/HIDE` (filtered to class `SysDragImage`) and `EVENT_SYSTEM_CAPTURESTART/END`. It writes timestamped events to an on-screen log and `probe.log`, which answers H-3.
2. **Wheel stub:** a plain 4-segment ring (no polish), topmost, no-activate, PMv2. It appears centred on the cursor when Shift is tapped during a detected drag and is clamped to the monitor work area.
3. **Drop target, two variants** (switchable): (A) WPF `AllowDrop`, (B) raw COM `IDropTarget` + `IDropTargetHelper` + `DROPDESCRIPTION`. Answers H-1, H-5, H-6 and H-7.
4. **Real data:** logs every path from `CF_HDROP` at `DragEnter` (H-2). Segments are filled from the extension: for an image, PNG / JPG / BMP / TIFF.
5. **One genuine operation:** image format conversion through **WIC** (built into Windows, so no third-party dependency). Output goes beside the original with collision-safe naming via temp file + atomic rename.
6. **Safety proof:** SHA-256 of every source before the drop and after the job (H-4). Returned effect is always `DROPEFFECT_COPY`.

### 5.2 Exact test procedure (performed by you, physically. No simulated input)

Setup: `C:\OrbitTest\` containing `a.jpg`, `b.png`, `c.pdf`, `ü nicode 名前.jpg` and an existing `a.png` (collision test). Start OrbitProbe.

| # | Steps | Pass condition |
|---|---|---|
| T1 | Drag `a.jpg` in an Explorer window, tap Shift, move onto PNG, release | Wheel appears ≤150 ms after the tap, PNG highlights, `a (2).png` created and valid, `a.jpg` hash unchanged, original `a.png` untouched |
| T2 | Same as T1 but from the **desktop** | Same |
| T3 | Select `a.jpg` + `b.png` and drag. Tap Shift | Log shows 2 paths at `DragEnter`, before any drop |
| T4 | Open the wheel, then press **Esc** | Drag cancelled, wheel gone, no files created, sources unchanged |
| T5 | Open the wheel, move off it, release over empty space in the **same** folder *(corrected: releasing on the desktop from another folder is a normal Explorer move; see feasibility report F4)* | Nothing created, wheel dismissed, file *not* moved |
| T6 | Open the wheel, move off it and back, release on JPG | Conversion succeeds (wheel stayed anchored) |
| T7 | Tap Shift while over another Explorer folder, open the wheel, release on the wheel | Source **not moved** despite Shift = "move" semantics |
| T8 | Drag *without* tapping Shift into another folder | Normal Explorer behaviour, OrbitDrop invisible |
| T9 | Drag text from a browser or VS Code and tap Shift | Wheel either doesn't appear or shows "no file operations" and dismisses safely |
| T10 | Drag image visibility over the wheel, for variants A and B | Record which variant keeps the image above the wheel |
| T11 | Repeat T1 at **150%** display scaling (and on a 2nd monitor if available) | Wheel centred on the cursor and correctly sized, hit-testing accurate |
| T12 | Run T1 **20 times** in a row | 20/20 success. Explorer never hangs; log shows no missed `DragEnter` |

You record a short screen capture of T1, T3 and T4. That's the brief's "real screen recording" evidence for this gate.

### 5.3 Deliverable after the PoC

`docs/research/02-feasibility-report.md`: which detection signal won, which drop variant, measured latencies, a pass/fail table and the chosen fallback (if any). This is the **decision gate**.

---

## 6. Major technical and licensing risks

### 6.1 Technical

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| No reliable drag-start signal (H-3) | Medium | High | Three candidate signals tested; mouse-hook heuristic as backup; Fallback A |
| Drag image hidden behind the wheel (H-5) | Medium | Medium (looks wrong) | `IDropTargetHelper` and z-order insertion |
| Shift/Ctrl semantics confuse users | Medium | Medium | Always `COPY` on our target; configurable trigger; Shift-tap cycles modes |
| OrbitDrop launched elevated → drops blocked by UIPI | Low | High | `asInvoker` manifest; detect and warn |
| AV flags hooks or behaviour | Low–Med | Medium | No keyboard hook; signed builds; transparent README |
| Windows 11 updates change Explorer drag behaviour | Low | High | Only documented OLE contracts used; probe kept as a regression tool |
| Native codec crashes on malicious files | Medium | Medium | Out-of-process worker, Job Objects, timeouts |
| Windows 10 untested (only Win11 here) | Certain until a VM exists | Medium | Hyper-V / Windows 10 VM before v0.5 |

### 6.2 Licensing (dependency candidates, preliminary; full audit before adoption)

| Candidate | Licence | Verdict for an MIT app |
|---|---|---|
| **WIC** (Windows Imaging Component) | OS component | ✅ Zero-dependency baseline for JPG / PNG / BMP / GIF / TIFF. HEIC, WebP and AVIF depend on Store codec extensions (HEVC is paid). Not reliable alone |
| **Magick.NET / ImageMagick** | Apache-2.0 / ImageMagick (permissive) | ✅ Preferred image engine. Its bundled **libheif / libde265 are LGPL**: OK as dynamically linked, notices required. HEVC **patent** exposure to note |
| **FFmpeg** | LGPL-2.1, *or* GPL if built with x264/x265 | ⚠️ Ship an **LGPL shared build** only, or download on demand. Don't redistribute GPL builds such as the one installed here for dev. H.264/HEVC/AAC patent caveats in NOTICE |
| **PdfSharp** (6.x) | MIT | ✅ Merge, split, images→PDF |
| **PDFium** (via a MIT/BSD wrapper) or **Windows.Data.Pdf** | BSD-3 / OS | ✅ PDF→image rendering. Windows.Data.Pdf is built in: try it first |
| **qpdf** | Apache-2.0 | ✅ Optional (linearize, repair) |
| **LibreOffice** (headless) | MPL-2.0 | ⚠️ ~350 MB, so **don't bundle**: detect an existing install and offer "install to enable" |
| **Pandoc** | **GPL-2.0+** | ⚠️ Invoke as a separate user-installed exe only. Never bundle in the MIT distribution |
| **SharpCompress** | MIT | ✅ Archives (7-Zip is LGPL with an unRAR restriction: avoid bundling) |
| **Svg.Skia** | MIT | ✅ SVG rasterising |
| **Ghostscript, iText** | **AGPL** | ❌ Excluded |

Strategy: **the core distribution contains only MIT, Apache, BSD and LGPL (dynamic) components.** GPL or heavy tools are optional *engines* the user installs, detected at runtime. That keeps the MIT promise clean.

### 6.3 Other

- **Name / trademark:** "OrbitDrop" still needs a USPTO/EUIPO search plus GitHub, domain and winget-ID checks. The video file `m2-res_1080p.mp4` is Tangerine's material: **it must never be committed** (excluded in `.gitignore`).
- **Code signing:** unsigned builds trigger SmartScreen. Options: **SignPath Foundation** (free for OSS) or **Azure Trusted Signing** (low monthly cost). The decision is needed before v1.0, and also before the Windows 11 top-level context menu (Fallback B) is possible.

---

## 7. Criteria for proceeding to development

**GO for v0.1 (full radial architecture)** if every one of these holds:

1. H-1 and H-2 are confirmed: the wheel receives the real file list from Explorer **and** the desktop at `DragEnter`.
2. H-4 is confirmed: **0** source files moved, modified or deleted across all trials (hash-verified).
3. T12 passes 20/20, and the wheel appears ≤150 ms after the trigger (logged).
4. At least one drag-start signal (H-3) fires in 100% of T1–T12 trials with no false wheel pop-ups during T8.
5. Escape and drag-off (T4–T6) leave no artifacts.
6. It works at 100% and 150% scaling.
7. It runs entirely as a standard user.

**CONDITIONAL GO**: if 1, 2 and 5 hold but 3 or 4 is flaky, build v0.1 on **Fallback A** (auto orb) and keep the Shift-tap trigger as experimental.

**NO-GO on drag**: if H-1 or H-2 fail, pivot the primary gesture to Fallback C + B, report back before writing anything else, and document the deviation transparently (acceptance criterion 20).

---

## What I need from you

1. **Install the .NET 10 SDK.** Only 8.0.411 is installed. Run `winget install Microsoft.DotNet.SDK.10` (it may prompt for admin), or let me run it. Alternatively I can build the throwaway probe on .NET 8 now and retarget later.
2. **Approve the PoC (Step 3).** About 600–900 lines in `experiments/OrbitProbe/`, isolated from the future product.
3. **Hardware:** is a second monitor available for T11? Is a Windows 10 machine or VM available later?
4. **Accept two proposed deviations from the brief:**
   (a) Actions mode via **Shift-tap cycling** instead of Ctrl+Alt (Alt-tap side effects);
   (b) Fallback C (hotkey on selection) shipped *always*, as the keyboard-accessibility path.
