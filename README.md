<p align="center"><img src="src/Chango.UI/Assets/chango-256.png" width="96" alt="Chango icon"></p>

<h1 align="center">Chango</h1>
<p align="center"><b>Drag. Tap. Chango.</b><br>Convert, edit and manage files without opening an app. For Windows 10 (19041+) and 11.</p>

Chango is a drag-and-drop file converter for Windows. Drag files, tap **Shift**, and let go on the format you want. The result appears next to the original, and the original is never changed.

Converting a file is basic computer work, so it shouldn't mean uploading it to someone else's server, waiting in a queue or paying past a limit. Chango does it on your own PC: **free forever, no uploads, no limits, no account.** Made by Ramesh Tiwari in Perth, Australia · [chango.rameshtiwari.com](https://chango.rameshtiwari.com) · ramestiwarii@gmail.com

## How it works

1. **Drag a file, then tap Shift.** Start dragging one or more files in File Explorer or on the desktop and keep holding. Tap Shift once and the wheel opens around the pointer.
2. **Let go on what you want.** The result appears **next to the original**. Let go on the centre or press **Esc** to cancel.
3. **Tap Shift again for more.** Each tap switches between **Convert**, **Tools** and **Actions**. Slices with dots (like **More** or **Compress**) open into more choices when you rest on them.

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

### Optional helpers

- **FFmpeg** unlocks the extra media formats: `winget install Gyan.FFmpeg`. Chango finds it automatically, even if you install it while Chango is running.
- **LibreOffice** is used for documents when Microsoft Office isn't installed: `winget install TheDocumentFoundation.LibreOffice`.

**Private by design:** everything runs locally. There are no uploads, no account and no telemetry. Update checks stay off until Chango has its own releases repository (`Brand.ReleasesRepo`).

## Build from source

Requirements: Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.10`). For the installer, add Inno Setup 6 (`winget install JRSoftware.InnoSetup`).

```powershell
dotnet build Chango.slnx -c Release
dotnet test tests/Chango.Tests
dotnet run --project src/Chango.App          # runs from source (lives in the notification area)
./build/package.ps1                         # → artifacts/: setup.exe, portable.zip, SHA256SUMS.txt
dotnet run --project tools/IconGen          # regenerates the icon set in assets/brand
```

Tests that need Word, Excel, PowerPoint or FFmpeg skip themselves when those aren't installed.

## Brand

| | |
|---|---|
| Name | Chango |
| Accent | Chango Teal `#0EA5A4` (fill in light and dark themes), ink on it `#011F1E` (5.7:1) |
| Teal text | `#0A7C7B` on light surfaces, `#2DD4CF` on dark |
| Icon | A file card flipping over another, on a teal tile (`tools/IconGen`, output in `assets/brand`) |
| Where it lives | Colours: `src/Chango.UI/Themes/Tokens.*.xaml` · name, release repo: `src/Chango.Infrastructure/Brand.cs` |

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
| `docs/` | Privacy policy, design system, build guide |

## License

MIT. See [LICENSE](LICENSE), [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and the full licence texts in [licenses/](licenses/).

Chango includes code from [OrbitDrop](https://github.com/i3sam/OrbitDrop), © 2026 Mohammed Essam, used under the MIT License.
