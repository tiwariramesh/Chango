# Chango Design System ("Orbit")

| | |
|---|---|
| Version | 1.1 (monochrome rebrand) |
| Source of truth | `src/Chango.UI/Themes/Tokens.Light.xaml`, `Tokens.Dark.xaml`. This document describes them, and the code must match |
| Review tool | `Chango.exe --render-ui <folder>` renders every surface in light and dark at 100% and 150% |

## 1. Personality

**Minimal · precise · fast · elegant · friendly · professional.** Chango should feel like part of Windows 11, with one memorable signature: the orbit.

- **Native first.** Windows 11 typography, corner radii, Mica on windows, Fluent controls.
- **One accent, used sparingly.** Colour means "this is what will happen". Everything else stays neutral.
- **Motion with purpose.** Things bloom from where your attention already is (the cursor) and get out of the way instantly.
- **Original identity.** Black and white, no brand hue, no Tangerine shapes or assets. The Chango motif is a droplet in orbit.

## 2. Colour tokens

Every UI colour comes from a token. Tokens exist once per theme and are swapped live when the theme changes.

### 2.1 Brand (monochrome)

Chango is black and white. Emphasis comes from contrast and weight, never from a brand hue. (Changed in v1.1: the original violet accent was retired.)

| Token | Light | Dark | Use |
|---|---|---|---|
| `Accent` | `#111114` | `#F4F4F6` | Hovered wheel segment, primary buttons, toggles |
| `AccentHover` | `#2B2B31` | `#FFFFFF` | Hover on accent buttons |
| `AccentPressed` | `#000000` | `#D9D9DE` | Pressed |
| `AccentText` | `#111114` | `#FFFFFF` | Links, emphasised text |
| `AccentSubtle` | `#ECECEF` | `#2E2E34` | Selected navigation item |
| `OnAccent` | `#FFFFFF` | `#111114` | Text and icons on `Accent` (≥ 15:1 in both themes) |

**Windows accent option.** When "Use Windows accent colour" is on, `Accent` comes from the system, with a 3:1 contrast guard against the wheel surface; below that, the monochrome accent is kept.

### 2.2 Neutrals

| Token | Light | Dark | Use |
|---|---|---|---|
| `SurfaceBase` | `#F3F3F6` | `#1C1C21` | Window background fallback when Mica is unavailable |
| `Surface` | `#FFFFFF` | `#26262D` | Cards, toast body |
| `SurfaceAlt` | `#F7F7FA` | `#2D2D35` | Inputs, inner panels |
| `Stroke` | `#E4E4EB` | `#3A3A45` | Card borders, dividers |
| `StrokeStrong` | `#CFCFDA` | `#4C4C58` | Focus-adjacent borders |
| `TextPrimary` | `#17171C` | `#F3F3F7` | Body, titles |
| `TextSecondary` | `#5D5D6B` | `#B7B7C3` | Descriptions |
| `TextTertiary` | `#8A8A97` | `#868694` | Hints, disabled |

### 2.3 Semantic

| Token | Light | Dark |
|---|---|---|
| `Success` | `#1E8E5A` | `#4CC38A` |
| `Warning` | `#B26B00` | `#F5B04C` |
| `Danger` | `#C4314B` | `#FF7A8A` |

### 2.4 Wheel material

| Token | Light | Dark |
|---|---|---|
| `WheelTint` (over the blurred backdrop) | `#FFFFFF` @ 58% | `#1D1D23` @ 62% |
| `WheelSegment` | `#FFFFFF` @ 84% | `#2F2F38` @ 86% |
| `WheelSegmentText` | `#17171C` | `#F3F3F7` |
| `WheelHub` | `#FFFFFF` @ 94% | `#2A2A32` @ 96% |
| `WheelRim` | `#FFFFFF` @ 80% → 20% (top → bottom) | `#FFFFFF` @ 22% → 6% |
| `WheelShadow` | `#000000` @ 20%, blur 28, y 8 | `#000000` @ 50%, blur 28, y 8 |

The backdrop blur keeps the wheel legible on any wallpaper, bright or dark: text sits on `WheelSegment`, never directly on the backdrop.

## 3. Typography

Typeface: **Segoe UI Variable** (Windows 11), falling back to Segoe UI. Icons: **Segoe Fluent Icons**, falling back to Segoe MDL2 Assets (system fonts, not redistributed).

| Style | Size / line | Weight | Use |
|---|---|---|---|
| `Caption` | 12 / 16 | Regular | Hints, metadata |
| `Body` | 14 / 20 | Regular | Default |
| `BodyStrong` | 14 / 20 | Semibold | Setting titles |
| `Subtitle` | 20 / 28 | Semibold | Page titles |
| `Title` | 28 / 36 | Semibold | Onboarding |
| `WheelLabel` | 13 / 16 | Semibold, uppercase for formats | Segment labels |
| `WheelHubTitle` | 10 / 12 | Semibold, letter-spaced +8% | Mode name |
| `WheelHubValue` | 16 / 20 | Semibold | Hovered target / file summary |

## 4. Spacing, size and shape

- **Grid:** 4 px. Spacing scale: 4 · 8 · 12 · 16 · 20 · 24 · 32 · 40 · 48.
- **Radii:** controls 4 · cards and inputs 6 · windows, flyouts and toasts 8 · pills 999.
- **Hit targets:** at least 32 × 32 px for mouse, 40 × 40 for primary actions.
- **Settings window:** 860 × 600 default. Left navigation 220 px. Content max width 640.

### 4.1 Wheel geometry (Standard size; Compact × 0.86, Large × 1.16)

| Property | Value |
|---|---|
| Disc diameter | 300 |
| Shadow margin around the disc | 28 |
| Ring outer / inner radius | 140 / 66 |
| Hub radius | 56 |
| Segment gap | 6 |
| Segment corner radius | 10 |
| Max segments per ring | 8 (more → "More…" second ring, M2+) |
| Hover lift | Segment moves 3 px outward |

## 5. Elevation

| Level | Light | Dark | Use |
|---|---|---|---|
| `E1` | 0 1 3 @ 6% | 0 1 3 @ 30% | Cards |
| `E2` | 0 8 24 @ 14% | 0 8 24 @ 40% | Toasts, tray flyout |
| `E3` | 0 8 28 @ 20% | 0 8 28 @ 50% | Wheel |

Windows use the system DWM shadow and Mica; custom shadows only on per-pixel-transparent surfaces.

## 6. Motion

| Token | Duration | Curve | Use |
|---|---|---|---|
| `Instant` | 0 | — | Reduced motion, dismissals |
| `Fast` | 90 ms | cubic out | Hover fill, hub label, segment lift |
| `Bloom` | 170 ms | back out (amplitude 0.3) | Wheel opening (scale 0.4 → 1, opacity 0 → 1 over 110 ms) |
| `ModeSwap` | 140 ms | cubic out | Segments rotate 14° → 0 and fade in on mode change |
| `Exit` | 90 ms | cubic in | Wheel close after a drop (scale 1 → 1.04, fade out) |
| `Toast` | 180 ms in / 140 ms out | cubic out / in | Slide 8 px + fade |

**Reduced motion** (Settings → Appearance, or Windows "Animation effects" off): scale and rotation are removed, opacity transitions are capped at 80 ms, and the wheel appears at full size.

## 7. Iconography

- UI glyphs from Segoe Fluent Icons, at 16 px in lists and 20 px on wheel tool segments.
- Format segments are **text-only** (PNG, JPG…); tool and action segments are **icon + label**.
- **App icon:** a glossy near-black squircle tile with a shaded white droplet orbited by a ring that passes behind and in front of the drop, plus a small moon. Generated from vectors by `tools/IconGen` at 16–1024 px; at 16–24 px the drop is flat and the ring thicker so it stays crisp.

## 8. Surfaces

| Surface | Spec |
|---|---|
| **Radial wheel** | Frosted disc (backdrop snapshot, blur, `WheelTint`), `WheelRim` gradient stroke, `E3` shadow. Segments `WheelSegment`; hovered segment `Accent` with `OnAccent` text. Hub shows the mode (top), the hovered target or file summary (centre) and a three-dot mode indicator (bottom) |
| **Toast** | 340 px wide, radius 8, `Surface` + 1 px `Stroke`, `E2`. Leading status icon (accent spinner ring / success / warning / danger), title (`BodyStrong`), detail (`Caption`), 3 px progress line, trailing ✕ (cancel or close) and an optional "Show" link. Appears near where the files were dropped, bottom-right fallback |
| **Tray flyout** | 260 px, radius 8, `Surface`, `E2`. Header with icon, name and status line, then menu rows of 36 px with glyph + label |
| **Settings** | Mica window, left navigation (icon + label), setting cards (radius 6, `Surface` @ 70% on Mica, 1 px `Stroke`), title + description + control on the right |
| **Onboarding** | 3 steps, illustration + title + one sentence, step dots, "Next" / "Get started" |

## 9. Accessibility

- Text contrast is at least 4.5:1 (body) and 3:1 (large or semibold ≥ 14 px). All token pairs above meet this.
- High-contrast mode: when `SystemParameters.HighContrast` is on, tokens map to system colours and the wheel backdrop is replaced by a solid `Window` colour.
- Every interactive element is reachable by keyboard and has a visible focus ring (2 px `Accent`). Fallback C (M2) provides a fully keyboard-driven wheel.
- Respect "Animation effects" and the in-app reduced-motion setting.
