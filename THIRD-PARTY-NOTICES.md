# Third-party notices

Chango is MIT-licensed (see `LICENSE`). This file lists everything a **release build** redistributes that isn't Chango's own code. It's updated whenever a dependency is added; each addition needs a licence review first: its licence must allow free redistribution in the Microsoft Store. The full licence texts are in `licenses/`, which ships next to `Chango.exe`.

## Redistributed in release builds

| Component | Version | Licence | Why |
|---|---|---|---|
| .NET Runtime and Windows Desktop (WPF) runtime | 10.0.x | MIT, © .NET Foundation and Contributors | The self-contained build bundles the runtime so users don't need to install .NET. Source: https://github.com/dotnet/runtime, https://github.com/dotnet/wpf. Its own third-party notices: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT |
| PDFsharp | 6.2.4 | MIT, © empira Software GmbH | PDF tools: merge, split, rotate, compress. Pinned to 6.2.4, the last MIT release (6.5 and later are AGPL-3.0). Source: https://github.com/empira/PDFsharp |
| Microsoft.Extensions.Logging.Abstractions, System.Security.Cryptography.Pkcs | 8.0.x | MIT, © .NET Foundation and Contributors | Dependencies of PDFsharp |
| SkiaSharp, SkiaSharp.Skottie, SkiaSharp.NativeAssets.Win32 | 3.119.2 | MIT, © Microsoft Corporation | WebP output, reading WebP without Microsoft's WebP extension, and drawing SVGs. The native libSkiaSharp.dll contains Skia (BSD-3-Clause, © Google), libwebp (BSD-3-Clause, © Google) and the other libraries listed in https://github.com/mono/SkiaSharp/blob/main/THIRD-PARTY-NOTICES.txt. Source: https://github.com/mono/SkiaSharp |
| Svg.Skia, Svg.Model, Svg.SceneGraph, Svg.Animation, ShimSkiaSharp | 4.9.1 | MIT, © Wiesław Šoltés | Reading SVG drawings (SVG to PNG, JPG, WebP …). Source: https://github.com/wieslawsoltes/Svg.Skia |
| Svg.Custom | 4.9.1 | MS-PL (Microsoft Public License), © SVG.NET contributors | The SVG parser Svg.Skia builds on (a build of SVG.NET). Source: https://github.com/svg-net/SVG |
| ExCSS | 4.3.1 | MIT, © Tyler Brinks | CSS inside SVG files |
| HarfBuzzSharp, HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.3 | MIT, © Microsoft Corporation (HarfBuzz: MIT, © HarfBuzz contributors) | Text inside SVG files |
| OrbitDrop | 0.4.4-alpha (commit 15bcafe) | MIT, © 2026 Mohammed Essam | The application Chango is built from. Source: https://github.com/i3sam/OrbitDrop |
| PdfPig | 0.1.16 | Apache-2.0, © UglyToad (Eliot Jones) and contributors | Reading a PDF's text layer: PDF to TXT, Markdown and Excel. Source: https://github.com/UglyToad/PdfPig |
| DocumentFormat.OpenXml (Open XML SDK) | 3.5.1 | MIT, © Microsoft Corporation | Word to Markdown; writing PowerPoint and Excel files from PDFs. Source: https://github.com/dotnet/Open-XML-SDK |
| Markdig | 1.4.2 | BSD-2-Clause, © Alexandre Mutel | Markdown to HTML, DOCX, PDF and text. Source: https://github.com/xoofx/markdig |

### In ChangoCdr.exe (the built-in CorelDRAW reader)

Compiled from these unmodified upstream source releases by `build/cdr/build-cdr.ps1`, which pins each archive's SHA-256. Their licence texts ship in the `licenses` folder next to Chango.

| Component | Version | Licence | Source |
|---|---|---|---|
| libcdr | 0.1.9 | MPL 2.0, © The Document Foundation and libcdr contributors | https://dev-www.libreoffice.org/src/libcdr/libcdr-0.1.9.tar.xz |
| librevenge | 0.0.6 | MPL 2.0 (dual-licensed MPL 2.0 / LGPL 2.1+; used under the MPL 2.0), © librevenge contributors | https://dev-www.libreoffice.org/src/librevenge-0.0.6.tar.bz2 |
| Little CMS (lcms2) | 2.16 | MIT, © Marti Maria Saguer | https://github.com/mm2/Little-CMS/releases/tag/lcms2.16 |
| zlib | 1.3.1 | zlib licence, © Jean-loup Gailly and Mark Adler | https://github.com/madler/zlib/releases/tag/v1.3.1 |
| Boost (header-only parts) | 1.86.0 | Boost Software License 1.0 | https://archives.boost.io/release/1.86.0/source/boost_1_86_0.tar.bz2 |

The MPL 2.0 covered files are used as published; their source code is available at the links above. The reader's own code is `src/Chango.CdrReader` in this repository (MIT). Text-encoding support comes from the copy of ICU built into Windows (not redistributed).

## Used from Windows, not redistributed

- **Windows Imaging Component (WIC):** image decoding and encoding.
- **Windows Media Foundation:** video and audio transcoding (H.264/AAC, WMV/WMA, MP3, WAV). Codecs come from Windows; HEVC, VP9 and similar depend on the Microsoft Store video extensions the user has installed.
- **Segoe UI Variable, Segoe Fluent Icons and Segoe MDL2 Assets fonts:** referenced by name and installed with Windows. Chango doesn't ship these fonts.
- **OLE drag-and-drop, Shell and DWM APIs:** documented Windows APIs.
- **tar.exe (bsdtar / libarchive, BSD-2-Clause):** ships with Windows 10 1803 and later. Used to extract 7Z, RAR, TAR.XZ, TAR.BZ2, CAB and ISO, and to make 7Z and TAR.GZ archives.

## Used when the user has installed them, not redistributed

- **FFmpeg** (LGPL/GPL, depending on the build the user installs): MKV, WebM, MOV, AVI, OGG, Opus and AIFF, run as a separate program. Chango doesn't bundle or link it.
- **Microsoft Office** (Word, Excel, PowerPoint) or **LibreOffice** (MPL 2.0): document layout and conversion, driven through COM or the command line.

## Development only (not shipped)

| Component | Licence |
|---|---|
| xUnit, xunit.runner.visualstudio | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |
| Inno Setup (installer compiler) | Inno Setup License (modified BSD-style) |

## Trademarks

CorelDRAW is a trademark of Corel Corporation (Alludo). Microsoft Word, Excel, PowerPoint and Windows are trademarks of Microsoft. Adobe, Illustrator and Photoshop are trademarks of Adobe. Chango is an independent project: it isn't affiliated with, sponsored or endorsed by any of them, and it mentions their file formats only to say which files it can open.

## Brand assets

The Chango name and logo (`assets/brand/logo`, and the icons and launch animation `tools/IconGen` makes from it) are reserved by Ramesh Tiwari and aren't covered by the MIT licence: see [assets/brand/README.md](assets/brand/README.md). No other product's assets are used.
