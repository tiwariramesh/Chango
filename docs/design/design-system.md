# Chango design system

Chango should feel like part of Windows 11, with one clear identity: **Chango Teal** on calm neutrals, soft rounded
shapes, and friendly, plain wording. Every colour is a token in `src/Chango.UI/Themes/Tokens.Light.xaml` and
`Tokens.Dark.xaml`; control styles are in `Styles.xaml`. Change colours there only, never in a window.

## Colour

| Token | Light | Dark | Use |
|---|---|---|---|
| `Accent` | `#0EA5A4` | `#0EA5A4` | Chango Teal: primary buttons, selected items, toggles, the highlighted wheel slice |
| `OnAccent` | `#011F1E` | `#011F1E` | Ink on teal (5.7:1) |
| `AccentText` | `#0A7C7B` | `#2DD4CF` | Teal text on the window background (links, labels, icons) |
| `AccentSubtle` | `#DFF5F4` | `#133A3A` | Teal tint: the Settings rail, icon badges, chips, the tray header |
| `AccentHover` / `AccentPressed` | `#14B8B6` / `#0C9493` | same | Button states |

Neutrals (surfaces, strokes, text) are cool greys; semantic colours: success green, warning amber, danger red.
Text on teal always uses `OnAccent`, never white, so it stays readable in both themes.

## Shape

- **Fully rounded:** buttons, segmented choices, toggles, the selected Settings page, chips.
- **12 px:** cards. **14 px:** tray menu and notifications. **8 px:** menu rows.
- Icons sit in round, teal-tinted 36 px badges in Settings.
- Page titles are 26 px with a short teal underline.

## The wheel

Slices are rounded and slightly separated, with a teal rim around the disc and a teal ring around the centre.
The centre shows the mode in teal, the option under the pointer, and a pill indicator for Convert · Tools · Actions.
A slice that opens more choices is marked with **+**. Hit-testing is by direction: anywhere in a slice's angle counts,
even past the rim, so aiming is forgiving while dragging.

## Voice

Short, warm and plain: "Drop it on the format you want", "Beauty! You made photo.jpg". Say what happens to the
user's files (a new file beside the original; nothing uploaded). Tagline: **Drag. Tap. Chango.**

## Icon

A white file card flipping over a faded one, with a curved arrow, on a teal squircle (`tools/IconGen`). At tray
sizes it simplifies to two flat cards.
