# Microsoft Store submission: Chango

Everything to paste into Partner Center, section by section. Make the package with `./build/store/package-store.ps1` and the artwork with `Chango.exe --render-store <folder>`.

Partner Center: <https://partner.microsoft.com/dashboard/apps-and-games/overview> → **Chango** → **Start your submission**.

---

## 1. Pricing and availability

| Field | Value |
|---|---|
| Markets | All markets (default) |
| Visibility | Public audience |
| Schedule | As soon as possible |
| Pricing | **Free** |
| Free trial | No free trial |

## 2. Properties

| Field | Value |
|---|---|
| Category | **Productivity** |
| Subcategory | (none) |
| Privacy policy URL | `https://github.com/tiwariramesh/Chango/blob/main/docs/PRIVACY.md` (docs/PRIVACY.md in the public repository) |
| Website | `https://chango.rameshtiwari.com` |
| Support contact info | `ramestiwarii@gmail.com` |
| Does this product access, collect, or transmit personal information? | **No** |
| Product declarations | Leave all unticked, **except** "This app has been tested to meet accessibility guidelines" (leave unticked too, unless you've tested it). |
| System requirements | Minimum: **Keyboard**, **Mouse**. Memory 4 GB. Architecture **x64**. |

## 3. Age ratings

Choose **"Utility, productivity, communication or other"** as the app type, then answer:

| Question | Answer |
|---|---|
| Violence, fear, sexuality, language, drugs, gambling | No to all |
| Can users interact or exchange content with other users? | **No** (Share hands the file to Windows' own Share window; Chango has no messaging or accounts) |
| Does the app share the user's current physical location with other users? | **No** |
| Does the app allow purchases of digital goods? | **No** |
| Does the app contain unrestricted internet access (a browser)? | **No** |

Expected result: **3+ / Everyone**.

## 4. Packages

Upload `artifacts\store\Chango-<version>-Store.msix`. Partner Center checks it. The identity must match exactly (already set in `build/store/AppxManifest.xml`):

- Name: `{IDENTITY_NAME}` (Partner Center → Product identity → Package/Identity/Name)
- Publisher: `{PUBLISHER_ID}` (Package/Identity/Publisher, starts with `CN=`)
- Publisher display name: `Ramesh Tiwari`

Device families: tick **Windows 10/11 Desktop** only.

## 5. Store listing (English, United States)

### Description

```
Change any file into the format you need, without opening an app.

Drag a file in File Explorer, on the desktop or in VS Code, tap Shift, and the Chango wheel opens under your pointer. Let go on a format and the new file lands right beside the original. Your original is never changed and nothing is uploaded: everything happens on your PC.

DOCUMENTS AND PDF
• PDF to Word, PowerPoint (a slide per page), Excel (tables rebuilt as real cells), text or Markdown.
• Word to PDF, text, Markdown, HTML, RTF or ODT. Markdown and text to PDF, Word or HTML.
• Spreadsheets to PDF, XLSX, CSV, TSV or ODS. Slides to PDF, or to an image per slide.
• Merge, split, compress and rotate PDFs. Combine images into one PDF.
(Word, Excel and PowerPoint conversions use Microsoft Office or LibreOffice on your PC. PDF to PowerPoint, Excel, text and Markdown are built in.)

IMAGES, VIDEO AND AUDIO
• Images to JPG, PNG, WebP, JFIF, GIF, BMP, TIFF or PDF, including SVG; reads HEIC, AVIF, JPEG XL and RAW with Windows' codecs.
• Video to MP4, WMV or GIF; audio to MP3, M4A, WAV, WMA or FLAC.
• With the free FFmpeg installed: MKV, WebM, MOV, AVI, OGG, Opus and AIFF too.
• Subtitles: SRT and VTT both ways, or a plain-text transcript.

TOOLS AND ACTIONS
• Edit photos (crop, adjust, mark up, redact), compress to a target size, resize, rotate, copy text out of images.
• Trim and join clips, normalize audio, remove metadata or GPS location.
• Archive as ZIP, 7Z or TAR.GZ; extract ZIP, 7Z, RAR, TAR and more.
• Copy or move to a folder, share, float files on a shelf, set a wallpaper.

YOUR WAY
• Tap Shift again to switch between the Convert, Tools and Actions wheels.
• Rather click? Select files and press Ctrl + Alt + Space.
• Hide the slices you never use and put favourites first.
• A short tour with a hands-on practice gets you going in a minute.

PRIVATE BY DESIGN
No account, no ads, no tracking, no telemetry. Chango works offline and only touches the files you choose.

Free and open source (MIT): https://github.com/tiwariramesh/Chango
```

### What's new in this version

```
Chango 1.0: PDF to Word, PowerPoint, Excel, text and Markdown; Word, Markdown, spreadsheet and slide conversions; subtitles; MKV, WebM, MOV, AVI, OGG, Opus and AIFF with FFmpeg; 7Z and RAR archives; and a brand-new look.
```

### Product features (one per box, up to 20)

```
Drag a file, tap Shift, let go on a format
The new file lands beside the original; the original is never changed
Works offline; nothing is uploaded
PDF to Word, PowerPoint, Excel, text or Markdown
Word to PDF, text, Markdown, HTML, RTF or ODT
Spreadsheets to PDF, XLSX, CSV, TSV or ODS
Images to JPG, PNG, WebP, GIF, TIFF or PDF
Video and audio to MP4, MP3, M4A, WAV, FLAC and more
Subtitles: SRT and VTT both ways
Merge, split and compress PDFs
Built-in photo editor: crop, adjust, mark up, redact
Archive as ZIP, 7Z or TAR.GZ; extract RAR and 7Z
Ctrl + Alt + Space: use it without dragging
Customize the wheel: hide options and reorder them
Light and dark themes
```

### Screenshots (desktop; first one is shown first)

Upload in this order, from the art folder. Use the light set; the dark set can replace it if you prefer:

1. `screenshot-01-convert.png`: Change files into any format.
2. `screenshot-02-result.png`: The new file lands right beside the old one.
3. `screenshot-03-tools.png`: More choices, one + away.
4. `screenshot-07-editor.png`: Quick edits, built in.
5. `screenshot-04-modes.png`: Three wheels: Convert, Tools, Actions.
6. `screenshot-05-keyboard.png`: Rather click? Press Ctrl + Alt + Space.
7. `screenshot-06-customize.png`: Your wheel, your way.

Optional caption for each: the text after the colon.

### Store logos

| Slot | File |
|---|---|
| 1:1 Box art (2160 × 2160) | `boxart-1x1-2160.png` |
| 2:3 Poster art (1440 × 2160) | `poster-2x3-1440x2160.png` |
| 16:9 Super hero art (3840 × 2160) | `hero-16x9-3840x2160.png` |
| Store logo (300 × 300) | `storelogo-300.png` |

### Search terms (up to 7)

```
file converter
pdf to word
word to pdf
image converter
pdf to excel
drag and drop
compress images
```

### Other fields

| Field | Value |
|---|---|
| Short title | Chango |
| Short description | Drag-and-drop file converter and manager. Change files into PDF, Word, Excel, JPG, MP3 and more. Drag, tap Shift, let go. |
| Copyright and trademark info | © 2026 Ramesh Tiwari |
| Additional license terms | MIT License: https://github.com/tiwariramesh/Chango/blob/main/LICENSE |
| Developed by | Ramesh Tiwari |

## 6. Submission options

### Restricted capabilities: runFullTrust

Partner Center asks why the app uses **runFullTrust**. Paste:

```
Chango is a classic Win32 desktop app (WPF, .NET) packaged as MSIX. It needs full trust to:
1. Notice the gesture system-wide: while the user drags files in File Explorer or on the desktop, it watches the mouse button (a low-level mouse hook) and the Shift key, then shows its wheel as a top-most window at the pointer, over other apps.
2. Register a global keyboard shortcut (Ctrl + Alt + Space) and read the current File Explorer selection.
3. Read the files the user chooses and write the results next to them, in any folder (the original is never changed).
No input is recorded, stored or sent anywhere; the app makes no network requests in its Store version.
```

### Notes for certification

```
How to test: after installing, Chango opens its guide and sits in the notification area (system tray).
1. In File Explorer, start dragging any image (keep holding the mouse button), tap Shift once, move onto "JPG" and let go. A JPG copy appears next to the original.
2. Or select a file and press Ctrl + Alt + Space, then click an option.
3. Settings: click the Chango icon in the notification area → Settings.
No account or sign-in is needed.
```

Then **Submit to the Store**. Certification usually takes 1 to 3 business days.
