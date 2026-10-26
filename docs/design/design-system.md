# Chango design system

Chango should feel like part of Windows 11, with one clear identity: a palette of deep blue, teal and mint as a
soft wash behind **frosted glass** panels, **Chango Teal** kept for small accents, soft rounded shapes, and friendly,
plain wording. Every colour is a token in `src/Chango.UI/Themes/Tokens.Light.xaml` and
`Tokens.Dark.xaml`; control styles are in `Styles.xaml`. Change colours there only, never in a window.

## Colour

| Token | Light | Dark | Use |
|---|---|---|---|
| `Accent` | `#0EA5A4` | `#0EA5A4` | Chango Teal: primary buttons, selected items, toggles, the highlighted wheel slice |
| `OnAccent` | `#011F1E` | `#011F1E` | Ink on teal (5.7:1) |
| `AccentText` | `#0A7C7B` | `#2DD4CF` | Teal text on the window background (links, labels, icons) |
| `AccentSubtle` | `#DFF5F4` | `#133A3A` | Teal tint, used sparingly (selected file in the tour's demo) |
| `AccentHover` / `AccentPressed` | `#14B8B6` / `#0C9493` | same | Button states |
| `Deep` / `Mint` | `#115469` / `#94EAE3` | same | The palette's other colours: the wash behind glass, the title underline |
| `Glass` / `GlassStrong` | 65 % / 90 % white | 55 % / 80 % charcoal | Frosted panels and the selected item |
| `GlassStroke` | bright top edge fading out | same, dimmer | The edge light on every glass surface |
| `Ambient` | base + washes | base + deeper washes | Window background behind glass (Settings, tour, launch card) |
| `Badge` | `#E9F0F3` | `#22303A` | Neutral round badge behind a teal setting icon |

Neutrals (surfaces, strokes, text) are cool greys; semantic colours: success green, warning amber, danger red.
Text on teal always uses `OnAccent`, never white, so it stays readable in both themes.

**Use teal sparingly.** Whole surfaces are glass or neutral; teal marks only what matters right now: the primary
button, the current item (a dot or number badge), toggles that are on, links, and the highlighted wheel slice.

## Shape

- **Fully rounded:** buttons, segmented choices, toggles, the selected Settings page, chips.
- **12 px:** cards. **14 px:** tray menu and notifications. **8 px:** menu rows.
- Icons are teal, in round, neutral 36 px badges in Settings.
- Page titles are 26 px with a short underline running from deep blue into teal.
- Glass panels: 18–20 px corners, a 1 px edge light, floating 12 px inside the window over the colour wash.

## The wheel

Slices are rounded and slightly separated, with a teal rim around the disc and a teal ring around the centre.
The centre shows the mode in teal, the option under the pointer, and a pill indicator for Convert · Tools · Actions.
A slice that opens more choices is marked with **+**. Hit-testing is by direction: anywhere in a slice's angle counts,
even past the rim, so aiming is forgiving while dragging.

## Voice

Short, warm and plain: "Drop it on the format you want", "Beauty! You made photo.jpg". Say what happens to the
user's files (a new file beside the original; nothing uploaded). Subtitle (what it is, under the name wherever there is room): **Drag-and-drop file converter and manager**.
Tagline (the catchy line): **Drag. Tap. Chango.**

## Logo and motion

The logo is a "C" of six segments radiating from its centre, each with a notch cut in it, in logo teal `#0097B2` on
a transparent background. Sources are in `assets/brand/logo`:

- `chango-mark.svg`: the mark alone, on a square canvas: the source of the app icon, tray icon, tile images and
  launch animation.
- `chango-logo-stacked.svg`: the mark above the "Chango" wordmark, on white.
- `chango-logo-horizontal.svg`: the mark with the wordmark beside it, for the website header.

`dotnet run --project tools/IconGen` regenerates `src/Chango.UI/Assets` from `chango-mark.svg`:

- `chango.ico`: 16–256 px, for the exe, taskbar, tray, window title bars and the installer.
- `chango-<size>.png` at 16, 24, 32, 48, 64, 128, 256, 512 and 1024 px. Each surface uses the smallest size that's
  still sharp at 200% scaling (the tray flyout and Settings header use 64, Store art 1024).
- `chango-intro.json`: the launch animation, built from the mark's own segments. Starting at the top right and going
  anticlockwise, each segment fades in and grows with a small overshoot, about 1 s in all.

`LottieView` plays the intro with Skottie: on the launch card (about 2 s, never at sign-in, off in Settings →
General → Startup animation), in the tour panel and on About. Reduced motion shows the finished logo instead.
