# Chango for macOS: plan

Chango for macOS is a **native Swift app** (SwiftUI and AppKit) with the same gesture as on Windows: drag files, tap **Shift**, and let go on the wheel. It lives in `macos/` in this repository and shares the brand, the design system and the website, but none of the Windows code.

## Ground rules: Windows stays untouched

- All Mac code goes in **`macos/`**. Don't change `src/`, `tests/`, `build/`, `Chango.slnx` or the Windows workflows for the Mac app.
- The Windows CI (`.github/workflows/ci.yml`) builds `Chango.slnx` only, so a Mac project in `macos/` doesn't affect it. Give the Mac app its **own** workflow on a macOS runner, triggered only by changes under `macos/`.
- Shared material is read, not moved: `assets/brand/` (logo), `docs/design/design-system.md` (colours, shapes, wording) and the conversion list in `README.md`.
- The Windows app in `src/` is the **specification**: the same wheel layouts, slice names, notification wording and behaviour (new file beside the original, original never changed, undo for a few seconds).

## Set up the Mac

1. Install **Xcode** from the Mac App Store, open it once and accept the licence, then run `xcode-select --install`.
2. Install Claude Code and the GitHub CLI, then sign in with `gh auth login`.
3. `git clone https://github.com/tiwariramesh/Chango`
4. Enrol in the **Apple Developer Program** (US$99 a year) for signing, notarization and the Mac App Store.

Target: **Apple silicon, macOS 14 (Sonoma) or later**.

## How Windows pieces map to the Mac

| Windows Chango | Chango for macOS |
|---|---|
| Low-level mouse hook + Shift detection while dragging | `NSEvent` global monitors for mouse drags and modifier flags; read the dragged files from the drag pasteboard (`NSPasteboard(name: .drag)`) |
| Top-most wheel window at the pointer | Borderless, non-activating `NSPanel` at the pointer, registered as a drop target; slices hit-tested by angle (as in `RadialWheel.cs`) |
| Tray icon and menu | Menu-bar item (`MenuBarExtra` / `NSStatusItem`) |
| Start with Windows | Login item (`SMAppService.mainApp`) |
| File Explorer right-click (`ChangoShell.dll`) | Finder **Quick Action** (and later a Finder Sync extension) |
| Ctrl + Alt + Space, Explorer selection | Global hotkey (e.g. Control + Option + Space); Finder selection via AppleScript / Scripting Bridge |
| Toast notifications with Show / Undo | `UserNotifications`, plus an in-app floating card for Undo |
| Undo to the Recycle Bin | `FileManager.trashItem` |
| Images (WIC, codecs) | ImageIO / Core Image (HEIC, AVIF, RAW are native) |
| PDF tools, PDF to images | PDFKit |
| Video and audio | AVFoundation (`AVAssetExportSession`); optional FFmpeg for the extra formats, detected like on Windows |
| Word, RTF, ODT, HTML, TXT | Built-in `textutil` (no Office needed) |
| Spreadsheets and slides | LibreOffice when installed (`soffice --headless --convert-to`) |
| PDF to Word, PowerPoint, Excel | Port the logic later; PDFKit for text and layout |
| OCR (copy text) | Vision (`VNRecognizeTextRequest`) |
| Archives | libarchive (ZIP, 7Z, TAR, RAR extract) |
| Subtitles (SRT, VTT, transcript) | Port the parser from `src/Chango.Conversion` |
| CorelDRAW (libcdr) | Build libcdr for arm64 (later) |
| Settings, tour, design tokens | SwiftUI views using the same colours and wording from `docs/design/design-system.md` |

## Permissions the tour must ask for

- **Accessibility**: so Chango can notice the Shift tap while a drag is in progress. Explain why, link to System Settings, and work (with the hotkey and Quick Action) until it's granted.
- **Automation (Finder)**: only for the hotkey's "selected files". Asked the first time it's used.
- Notifications: optional.

## Distribution

1. **Version 1:** direct download, signed with a Developer ID and **notarized**, as a `.dmg`, from GitHub Releases and the website. Updates with Sparkle (or a GitHub release check, as on Windows).
2. **Later:** the Mac App Store. The sandbox allows saving a file beside the original with the same name and a different extension (the "related items" rule, `NSFilePresenter.primaryPresentedItemURL`), so conversions can work there; tools that write other names may need the user to grant the folder.

The privacy promise stays the same: everything runs on the Mac, nothing is uploaded, no telemetry. Update `docs/PRIVACY.md` and `website/privacy.html` to cover the Mac app when it ships.

## Version 1 scope

- [ ] Menu-bar app with Settings (theme, wheel size, start at login) and the first-run tour
- [ ] Gesture: drag + tap Shift opens the wheel; Shift again cycles Convert, Tools, Actions; letting go in the centre or Esc cancels
- [ ] Wheel drawn like Windows: rounded slices, teal rim, centre showing mode, option and a mode indicator; **+** slices open more
- [ ] Convert: images (PNG, JPG, WebP, HEIC in, TIFF, GIF, BMP, PDF) and PDF to PNG/JPG
- [ ] Tools: compress, resize, rotate for images; merge, split, compress for PDFs
- [ ] Actions: copy path, show in Finder, copy/move to, archive as ZIP
- [ ] Result notification with Show and Undo
- [ ] Signed, notarized `.dmg`

## Later

- [ ] Video and audio (AVFoundation, FFmpeg for More)
- [ ] Documents via `textutil` and LibreOffice
- [ ] OCR, metadata removal, subtitles, archives beyond ZIP
- [ ] Global hotkey and Finder Quick Action
- [ ] PDF to Word, PowerPoint and Excel
- [ ] CorelDRAW
- [ ] Mac App Store listing

## When it ships

- Website: switch the macOS mentions from "in development" to a download, in `website/index.html` (FAQ) and `README.md` (Install table, badge).
- Add the Mac app to `docs/PRIVACY.md` and `website/privacy.html`.
