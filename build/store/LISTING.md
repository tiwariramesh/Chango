# Microsoft Store submission: OrbitDrop - Drag & Convert

Everything to paste into Partner Center, section by section. Make the package with `./build/store/package-store.ps1` and the artwork with `OrbitDrop.exe --render-store <folder>`.

Partner Center: <https://partner.microsoft.com/dashboard/apps-and-games/overview> → **OrbitDrop - Drag & Convert** → **Start your submission**.

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
| Privacy policy URL | `https://github.com/i3sam/OrbitDrop/blob/main/docs/PRIVACY.md` |
| Website | `https://github.com/i3sam/OrbitDrop` |
| Support contact info | `https://github.com/i3sam/OrbitDrop/issues` |
| Does this product access, collect, or transmit personal information? | **No** |
| Product declarations | Leave all unticked, **except** "This app has been tested to meet accessibility guidelines" (leave unticked too, unless you've tested it). |
| System requirements | Minimum: **Keyboard**, **Mouse**. Memory 4 GB. Architecture **x64**. |

## 3. Age ratings

Choose **"Utility, productivity, communication or other"** as the app type, then answer:

| Question | Answer |
|---|---|
| Violence, fear, sexuality, language, drugs, gambling | No to all |
| Can users interact or exchange content with other users? | **No** (Share hands the file to Windows' own Share window; OrbitDrop has no messaging or accounts) |
| Does the app share the user's current physical location with other users? | **No** |
| Does the app allow purchases of digital goods? | **No** |
| Does the app contain unrestricted internet access (a browser)? | **No** |

Expected result: **3+ / Everyone**.

## 4. Packages

Upload `artifacts\store\OrbitDrop-<version>-Store.msix`. Partner Center checks it. The identity must match exactly (already set in `build/store/AppxManifest.xml`):

- Name: `MohammedEssam.OrbitDrop-DragConvert`
- Publisher: `CN=FD78A452-347F-4CF8-BE72-9A808019B3D2`
- Publisher display name: `Mohammed Essam`

Device families: tick **Windows 10/11 Desktop** only.

## 5. Store listing (English, United States)

### Description

```
Convert, edit and manage files without opening an app.

Drag any file in File Explorer, on the desktop or in VS Code, tap Shift, and a wheel of choices opens right under your pointer. Let go on what you want: the result appears next to the original. Your original is never changed, and nothing is ever uploaded. Everything happens on your PC.

CONVERT
• Images to JPG, PNG, WebP, JFIF, GIF, BMP, TIFF or PDF, including SVG drawings. Also reads HEIC, AVIF, JPEG XL and camera RAW when Windows has the codec.
• Several images into one PDF, or PDF pages into PNG or JPG.
• Video to MP4 or WMV; audio to MP3, M4A, WAV, WMA or FLAC; the soundtrack out of a video.
• Word, Excel and PowerPoint files to PDF, when Microsoft Office or LibreOffice is installed.
• CorelDRAW (.cdr, .cmx) to PDF, SVG, PNG, JPG, WebP, TIFF or BMP. Built in: nothing else to install.

TOOLS
• A built-in photo editor: crop, adjust brightness, contrast, saturation and warmth, mark up with pen, arrows, boxes and text, and redact.
• Compress photos to half the size or under 1 MB, 500 KB or 200 KB. Compress videos to 1080p, 720p or 480p.
• PDF tools: merge several PDFs into one, split into pages, compress, rotate pages.
• Resize, rotate, flip and grayscale.
• Copy the text out of an image (offline text recognition).
• Trim video and audio, join clips, make a GIF from a video, take a snapshot.
• Normalize quiet recordings, switch audio to mono or stereo.
• See and edit file details, or remove all metadata or just the GPS location from photos.

ACTIONS
• Copy to or move to a folder, share, zip or extract, open a terminal here, copy the path or name.
• Float: park files on a small always-on-top shelf and drag them out later.
• Set a photo as your wallpaper.

MADE TO FIT YOUR FLOW
• Tap Shift again to switch between Convert, Tools and Actions.
• Options with dots open into more choices when you rest on them.
• No dragging needed: select files and press Ctrl + Alt + Space.
• Make the wheel yours: hide what you don't use and drag slices into your own order.
• A guided tour with animations and a hands-on practice shows you everything in a minute.
• Light and dark themes that follow Windows.

PRIVATE BY DESIGN
No account, no ads, no tracking, no telemetry. OrbitDrop works offline and only touches the files you choose.

Free and open source (MIT): github.com/i3sam/OrbitDrop
```

### What's new in this version

```
OrbitDrop 0.4.2: CorelDRAW files convert with nothing else installed (to PDF, SVG, PNG, JPG, WebP, TIFF, BMP). OrbitDrop 0.4: right-click files and choose OrbitDrop; Undo after every conversion (to the Recycle Bin); copy a screenshot or image and press Ctrl + Alt + Space to save or convert it; optional size estimates on the wheel and "last choice first". The wheel itself works exactly as before. Also new recently: SVG, PDF to Word, documents to PDF, WebP and JFIF.
```

### Product features (one per box, up to 20)

```
Drag a file, tap Shift, let go on what you want
Results appear next to the original; the original is never changed
Works offline; nothing is uploaded
Convert images to JPG, PNG, WebP, JFIF, GIF, BMP, TIFF or PDF
Merge, split and compress PDFs
Built-in photo editor: crop, adjust, mark up, redact
Compress photos to a target size like 1 MB or 200 KB
Compress, trim and join videos; make GIFs
Convert audio to MP3, M4A, WAV or FLAC; normalize volume
Copy text out of images
Remove location or all metadata from photos
Copy, move, share, zip and extract in one gesture
Float files on an always-on-top shelf
Ctrl + Alt + Space: use it without dragging
Customize the wheel: hide options and reorder them
Light and dark themes
```

### Screenshots (desktop; first one is shown first)

Upload in this order, from the art folder. Use the light set; the dark set can replace it if you prefer:

1. `screenshot-01-convert.png`: Convert files without opening an app.
2. `screenshot-02-result.png`: The result lands next to the original.
3. `screenshot-03-tools.png`: Compress, resize, edit and more.
4. `screenshot-07-editor.png`: A photo editor, built in.
5. `screenshot-04-modes.png`: Convert. Tools. Actions.
6. `screenshot-05-keyboard.png`: No dragging needed.
7. `screenshot-06-customize.png`: Make the wheel yours.

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
image converter
compress images
photo editor
drag and drop
radial menu
remove metadata
```

### Other fields

| Field | Value |
|---|---|
| Short title | OrbitDrop |
| Short description | Convert, edit and manage files without opening an app. Drag, tap Shift, let go. |
| Copyright and trademark info | © 2026 Mohammed Essam |
| Additional license terms | MIT License: https://github.com/i3sam/OrbitDrop/blob/main/LICENSE |
| Developed by | Mohammed Essam |

## 6. Submission options

### Restricted capabilities: runFullTrust

Partner Center asks why the app uses **runFullTrust**. Paste:

```
OrbitDrop is a classic Win32 desktop app (WPF, .NET) packaged as MSIX. It needs full trust to:
1. Notice the gesture system-wide: while the user drags files in File Explorer or on the desktop, it watches the mouse button (a low-level mouse hook) and the Shift key, then shows its wheel as a top-most window at the pointer, over other apps.
2. Register a global keyboard shortcut (Ctrl + Alt + Space) and read the current File Explorer selection.
3. Read the files the user chooses and write the results next to them, in any folder (the original is never changed).
No input is recorded, stored or sent anywhere; the app makes no network requests in its Store version.
```

### Notes for certification

```
How to test: after installing, OrbitDrop opens its guide and sits in the notification area (system tray).
1. In File Explorer, start dragging any image (keep holding the mouse button), tap Shift once, move onto "JPG" and let go. A JPG copy appears next to the original.
2. Or select a file and press Ctrl + Alt + Space, then click an option.
3. Settings: click the OrbitDrop icon in the notification area → Settings.
No account or sign-in is needed.
```

Then **Submit to the Store**. Certification usually takes 1 to 3 business days.
