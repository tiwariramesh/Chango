<p align="center">
  <img src="assets/brand/logo/chango-mark.svg" width="112" height="112" alt="Chango logo">
</p>

<h1 align="center">Chango</h1>

<p align="center">
  <b>Drag-and-drop file converter and manager</b><br>
  Drag. Tap. Chango. Convert, edit and manage files without opening an app.
</p>

<p align="center">
  <a href="https://github.com/tiwariramesh/Chango/actions/workflows/ci.yml"><img src="https://github.com/tiwariramesh/Chango/actions/workflows/ci.yml/badge.svg" alt="CI status"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0097B2" alt="Windows 10 and 11">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/tiwariramesh/Chango?color=0097B2" alt="MIT licence"></a>
</p>

<p align="center">
  <!-- When chango.rameshtiwari.com is live, point this button at its download page. -->
  <a href="https://github.com/tiwariramesh/Chango/releases/latest"><img src="https://img.shields.io/github/v/release/tiwariramesh/Chango?style=for-the-badge&label=Download%20for%20Windows&color=0097B2" alt="Download the latest Chango for Windows" height="34"></a>
  <!-- Store listing pending: when Partner Center gives the product ID, swap this for Microsoft's official badge:
       <a href="https://apps.microsoft.com/detail/<STORE_ID>"><img src="https://get.microsoft.com/images/en-us%20dark.svg" alt="Get it from Microsoft" height="34"></a> -->
  <img src="https://img.shields.io/badge/Microsoft%20Store-coming%20soon-3A3F47?style=for-the-badge" alt="Microsoft Store: coming soon" height="34">
  <img src="https://img.shields.io/badge/macOS-coming%20soon-3A3F47?style=for-the-badge" alt="macOS: coming soon" height="34">
</p>

<p align="center">
  <a href="https://github.com/tiwariramesh/Chango/releases/latest">Latest release</a> ·
  <a href="#install">Install</a> ·
  <a href="#what-it-converts">What it converts</a> ·
  <a href="#build-from-source">Build from source</a> ·
  <a href="docs/PRIVACY.md">Privacy</a> ·
  <a href="#credits">Credits</a>
</p>

---

Chango is a drag-and-drop file converter for your desktop. Drag files, tap **Shift**, and let go on the format you want. The result appears next to the original, and the original is never changed.

Converting a file is basic computer work, so it shouldn't mean uploading it to someone else's server, waiting in a queue or paying past a limit. Chango does it on your own computer: **free forever, no uploads, no limits, no account.**

**Chango is available for Windows 10 (19041+) and 11. A native macOS version is in development.**

## How it works

1. **Drag a file, then tap Shift.** Start dragging one or more files in File Explorer or on the desktop and keep holding. Tap Shift once and the wheel opens around the pointer.
2. **Let go on what you want.** The result appears **next to the original**. Let go on the centre or press **Esc** to cancel.
3. **Tap Shift again for more.** Each tap switches between **Convert**, **Tools** and **Actions**. Slices marked **+** (like **More** or **Compress**) open into more choices when you rest on them.

**No dragging?** Select files, press **Ctrl + Alt + Space** and click an option. **Or right-click** files or folders in File Explorer and choose **Chango**. Every result can be **undone** for a few seconds (it goes to the Recycle Bin).

**Make it yours.** Settings → **Wheel** shows every wheel: click a slice to hide it, drag a slice to move it, then **Save**.

## What it converts

| From | To | Needs |
|---|---|---|
| **Images** (PNG, JPG, WebP, JFIF, BMP, TIFF, GIF, SVG; HEIC, AVIF, JPEG XL, RAW with Windows codecs) | PNG, JPG, WebP, JFIF, BMP, TIFF, GIF, PDF | — |
| **PDF** | Word (DOCX), PowerPoint (PPTX, a slide per page), Excel (XLSX, tables rebuilt from the layout), TXT, Markdown, PNG, JPG | Word only for DOCX |
| **Word** (DOC, DOCX, RTF, ODT) | PDF, DOCX, TXT, Markdown, HTML, RTF, ODT | Microsoft Office or LibreOffice |
| **Markdown / TXT** | PDF, DOCX, HTML (and Markdown → TXT) | Office or LibreOffice for PDF and DOCX |
| **Spreadsheets** (XLS, XLSX, ODS, CSV, TSV) | PDF, XLSX, CSV, TSV, ODS | Office or LibreOffice |
| **Slides** (PPT, PPTX, ODP) | PDF, PPTX, PNG, JPG (an image per slide) | Office or LibreOffice |
| **Subtitles** (SRT, VTT) | SRT, VTT, TXT transcript | — |
| **Video** (MP4, MOV, MKV, WebM, AVI, WMV …) | MP4, WMV, GIF; extract MP3, M4A, WAV, WMA, FLAC · **More:** MKV, WebM, MOV, AVI, OGG, Opus, AIFF | FFmpeg for **More** |
| **Audio** (MP3, M4A, WAV, WMA, FLAC; OGG, Opus, AIFF …) | MP3, M4A, WAV, WMA, FLAC, OGG, Opus, AIFF | FFmpeg for OGG, Opus, AIFF and their inputs |
| **CorelDRAW** (CDR, CMX) | PDF, SVG, PNG, JPG, WebP, TIFF, BMP | — |

**Tools:** image edit (crop, adjust, markup, redact), compress, resize, rotate, grayscale, OCR · PDF merge, split, compress, rotate · video trim, compress, remove audio, snapshot, join · audio trim, compress, normalize, mono/stereo · extract ZIP, 7Z, RAR, TAR, TAR.GZ, TAR.XZ, TAR.BZ2, GZ, CAB, ISO · file info, edit details, remove metadata.

**Actions:** copy path or name, show in folder, copy/move to, float shelf, share, **archive as ZIP, 7Z or TAR.GZ**, terminal, set as wallpaper. These work on folders too.

## Install

| Platform | How to get it | Status |
|---|---|---|
| **Windows 10 (19041+) and 11**, x64 | [`Chango-Setup.exe`](https://github.com/tiwariramesh/Chango/releases/latest/download/Chango-Setup.exe) (installer) or [`Chango-Portable.zip`](https://github.com/tiwariramesh/Chango/releases/latest/download/Chango-Portable.zip) (no install), from the [latest release](https://github.com/tiwariramesh/Chango/releases/latest) | Available |
| **Windows**, Microsoft Store | Automatic updates and a one-click install | Coming soon |
| **Website** | [chango.rameshtiwari.com](https://chango.rameshtiwari.com), with downloads and a guide | Coming soon |
| **macOS** | A native Mac app with the same drag, tap and let-go wheel | In development |

- **Installer:** run `Chango-Setup.exe`. It installs for your account only, so it doesn't need administrator rights, and adds Chango to the Start menu. It keeps itself up to date (see [Privacy](#privacy)). Uninstall it from Settings → Apps.
- **Portable:** unzip `Chango-Portable.zip` anywhere and run `Chango.exe`. It tells you when a new version is out.

The first time Chango starts, a short tour shows you the gesture and lets you try it on a sample file. After that Chango waits quietly in the notification area; the tour is in its menu whenever you want it again.

Each release includes `SHA256SUMS.txt`, so you can check the download is intact (`Get-FileHash Chango-Setup.exe` in PowerShell). Releases aren't code-signed yet, so Windows SmartScreen may warn the first time you run Chango. Choose **More info → Run anyway**.

### Optional helpers (Windows)

- **FFmpeg** unlocks the extra media formats: `winget install Gyan.FFmpeg`. Chango finds it automatically, even if you install it while Chango is running.
- **LibreOffice** is used for documents when Microsoft Office isn't installed: `winget install TheDocumentFoundation.LibreOffice`.

## Privacy

Everything runs locally. There are no uploads, no account and no telemetry. The one network request is the update check: every few hours Chango asks GitHub whether a newer version exists, sending nothing about you or your files. Turn it off in Settings → About. See the full [privacy policy](docs/PRIVACY.md).

## Roadmap

- **Microsoft Store** listing, with automatic updates.
- **Code-signed** Windows releases, so SmartScreen stops warning.
- **Chango for macOS**: the same wheel, converters and tools, built for the Mac.

Ideas and requests are welcome: [open an issue](https://github.com/tiwariramesh/Chango/issues) or email ramestiwarii@gmail.com.

## Build from source

Requirements: Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.10`). For the installer, add Inno Setup 6 (`winget install JRSoftware.InnoSetup`). More detail is in [docs/development/building.md](docs/development/building.md).

```powershell
dotnet build Chango.slnx -c Release
dotnet test tests/Chango.Tests
dotnet run --project src/Chango.App          # runs from source (lives in the notification area)
./build/package.ps1                         # → artifacts/: setup.exe, portable.zip, SHA256SUMS.txt
dotnet run --project tools/IconGen          # regenerates the icons and launch animation from the logo
```

Tests that need Word, Excel, PowerPoint or FFmpeg skip themselves when those aren't installed.

## Brand

| | |
|---|---|
| Name | Chango, the drag-and-drop file converter and manager. Tagline: **Drag. Tap. Chango.** |
| Logo | A "C" of six notched segments radiating from its centre, in logo teal `#0097B2`. The source is [`assets/brand/logo/chango-mark.svg`](assets/brand/logo/chango-mark.svg), with wordmark versions beside it |
| Icon sizes | `tools/IconGen` builds `chango.ico` and `chango-<size>.png` (16 to 1024 px) in `src/Chango.UI/Assets`, plus the launch animation, so each surface loads the smallest sharp image |
| Palette | UI accent Chango Teal `#0EA5A4` with ink `#011F1E` (5.7:1); deep blue `#115469`, teal `#20B4A6` and mint `#94EAE3` for the wash behind glass |
| Look | Frosted glass panels over a soft wash of the palette; teal only for small accents |
| Teal text | `#0A7C7B` on light surfaces, `#2DD4CF` on dark |
| Where it lives | Colours: `src/Chango.UI/Themes/Tokens.*.xaml` · name, release repo: `src/Chango.Infrastructure/Brand.cs` · details: [docs/design/design-system.md](docs/design/design-system.md) |

## Repository map

| Path | What |
|---|---|
| `src/Chango.App` | Composition root, tray lifecycle, interaction controller |
| `src/Chango.UI` | Design system, radial wheel, toasts, Settings, onboarding |
| `src/Chango.Windows` | Win32/COM: drag detection, OLE drop target, tray, theme, shell |
| `src/Chango.Core` | Operation contracts, registry, collision-safe atomic output |
| `src/Chango.Conversion` | Converters: images, PDF (`Pdf/`), documents (`Documents/`), media (`Media/`, FFmpeg included), subtitles |
| `src/Chango.Tools` | Tools and actions: image tools, archives, metadata, file actions |
| `src/Chango.Jobs` / `Infrastructure` | Background jobs, settings, brand, local logs |
| `assets/brand` | Logo sources (not MIT: see [assets/brand/README.md](assets/brand/README.md)) |
| `docs/` | Privacy policy, design system, build guide |

## Credits

Chango is built on **[OrbitDrop](https://github.com/i3sam/OrbitDrop)** by Mohammed Essam. The radial wheel that opens while you drag a file, and the code behind it, is based on Orbit's work, shared under the MIT License. Chango adds its own converters, tools, design and tour on top.

Chango also stands on open-source libraries including PDFsharp, PdfPig, the Open XML SDK, SkiaSharp, Svg.Skia and Markdig. Every one is listed, with its licence, in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## License

The code is MIT, © 2026 Ramesh Tiwari and © 2026 Mohammed Essam (OrbitDrop). See [LICENSE](LICENSE), [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and the full licence texts in [licenses/](licenses/).

The Chango name and logo aren't covered by the MIT licence: a fork needs its own. See [assets/brand/README.md](assets/brand/README.md).

Made by Ramesh Tiwari · [chango.rameshtiwari.com](https://chango.rameshtiwari.com) · ramestiwarii@gmail.com
