# Orbit Probe: Manual Test Sheet

| | |
|---|---|
| Purpose | Decide whether OrbitDrop's signature interaction (drag in Explorer → tap Shift → radial wheel → release) works reliably on Windows, as Tangerine's does on macOS |
| Who runs it | You, with a **physical mouse and keyboard**. The probe flags any synthetic input in its statistics ("Physical input only"). |
| Time needed | ~45 minutes for Blocks A–D, plus a short screen recording |
| Build | `experiments/OrbitProbe` (Release) |

---

## 1. What you'll deliver back (and what I'll deliver after)

| # | You send me | How |
|---|---|---|
| 1 | The **filled-in results table** (section 6) | Edit this file or paste it in chat |
| 2 | The **probe log file** | Path shown at the top of the probe's log pane (`%LOCALAPPDATA%\OrbitProbe\probe-*.log`). Paste the path; I'll read it |
| 3 | The **statistics summary** after Block A (A15) | Click **Copy summary** and paste into chat |
| 4 | A **screen recording** of A1, A3, A4 and A6 | **Win + Shift + R** (Snipping Tool video) or **Win + Alt + R** (Game Bar). Keep it local and tell me the path |
| 5 | Anything that *felt* wrong: lag, flicker, the wheel in the wrong place, the drag thumbnail vanishing | Free-text notes column |

**After your run, I deliver** `docs/research/02-feasibility-report.md`. It records which drag-start signal won, which drop-target variant to use, measured latencies, the pass/fail table and the GO / CONDITIONAL GO / NO-GO decision (criteria in section 5).

---

## 2. Setup (once)

1. Open a terminal in the repo root and run:
   ```
   dotnet build experiments/OrbitProbe -c Release
   experiments\OrbitProbe\bin\Release\net10.0-windows10.0.19041.0\OrbitProbe.exe
   ```
   Start it **normally (not "Run as administrator")**. The first log lines must show `elevated=False`, `WinEvent hooks: dragdrop=ok, show/hide=ok` and `RegisterDragDrop(native) hr=0x00000000`.
2. Click **Create test files**, then **Open C:\OrbitTest**. The folder contains `a.jpg`, `a.png` (collision bait: must never be overwritten), `b.png` (has a transparent hole), `d.bmp`, `c.pdf`, `notes.txt`, `ü nicode 名前.jpg` and a deep folder `LLL…\MMM…\long path image.jpg`.
3. Arrange the screen so the probe window and the Explorer window are side by side.
4. Copy (Ctrl+C / Ctrl+V) `b.png` onto your **desktop** for test A2.
5. Leave all probe options at their defaults: **variant B**, helper ON, descriptions ON, all three signals ON, diagnostic OFF.

> **The gesture:** press and hold the left button on a file, start dragging, keep holding, **tap Shift once**, move onto a segment, release.
> Tap Shift again (still holding the mouse) to cycle **Convert → Tools → Actions**. **Esc** cancels.

---

## 3. Test procedure

### Block A: core interaction (variant B, default settings)

Click **Reset statistics** before starting Block A.

| ID | Steps | Expected result (pass condition) |
|---|---|---|
| **A1** | In Explorer, drag `a.jpg` a little, tap Shift, move onto **PNG**, release | Wheel blooms **centred on the cursor** almost instantly. Hub shows the file size; segments read PNG / BMP / TIFF / GIF. Hovered segment fills with your accent colour and the hub shows its name. Release → wheel vanishes and **`a (2).png`** appears. `a.png` is unchanged. Log: green `PASS … DragEnter … BEFORE drop`, `PASS a.jpg → a (2).png`, `PASS source intact` |
| **A2** | On the **desktop**, drag `b.png`, tap Shift, release on **JPG** | `b.jpg` appears on the desktop. The transparent hole is **white**, not black |
| **A3** | Select `a.jpg` + `b.png` + `d.bmp` (Ctrl+click), drag, tap Shift, release on **JPG** | Hub reads **"3 files"**. `b (2).jpg` (or `b.jpg`) and `d.jpg` are created. `a.jpg` is logged as `SKIP … already JPG`, not FAIL |
| **A4** | Drag `a.jpg`, tap Shift (Convert), **tap Shift again** (Tools), release on **Rotate ↻** | Hub title changes CONVERT → TOOLS and the segments change in place. `a (rotated).jpg` is created and is portrait |
| **A5** | Drag `notes.txt`, tap Shift **three times** (→ Actions), release on **Copy path** | Paste in Notepad → the quoted full path of `notes.txt` |
| **A6** | Drag `a.jpg`, tap Shift, then press **Esc** while still holding the mouse | Wheel disappears immediately and the drag is cancelled. **No new file**. Log: `closed (Escape pressed)` |
| **A7** | Drag `a.jpg`, tap Shift, release over the **centre hub** | Nothing created. The drag tooltip near the cursor says *"Release to cancel"*, or no tooltip at all |
| **A8** | Drag `a.jpg`, tap Shift, move **off** the wheel, then **back on**, release on **BMP** | Wheel **stays where it opened** while you're off it. `a.bmp` is created |
| **A9** | Drag `a.jpg`, tap Shift, move off the wheel, release on **empty space in the same Explorer folder** | Wheel dismisses. Nothing created, nothing moved. Log: `all 1 source(s) still in place` |
| **A10** | Drag `a.jpg`, tap Shift, then **press and hold Shift** while releasing on any segment | The operation runs, and **`a.jpg` is still there and unchanged** (`PASS source intact`). Shift normally means "move" in Explorer; this proves OrbitDrop overrides that to copy |
| **A11** | **Without** Shift, Ctrl-drag `notes.txt` into the `LLL…` subfolder | Normal Explorer copy. **Wheel never appears** |
| **A12** | Select some text in Notepad or a browser, drag it, tap Shift | Either no wheel, or a wheel showing **"No files"**. Nothing created, no crash |
| **A13** | Drag `c.pdf`, tap Shift, look, then tap Shift twice more (→ Actions), release on **Copy name** | Convert and Tools show an **empty ring** with "Not supported". Actions works: clipboard = `c.pdf` |
| **A14** | Convert `ü nicode 名前.jpg` → PNG. Then open `LLL…\MMM…` and convert `long path image.jpg` → PNG | Both outputs are created with correct names |
| **A15** | **Reliability run:** do the A1 gesture **20 times in a row** on `a.jpg` (any target; vary them). Then click **Copy summary** and paste it to me | **20/20** conversions and **20 integrity passes**. Explorer never freezes |

### Block B: drop-target variants and visuals (H-5, H-6, H-7)

Use `d.bmp` → PNG for each row. Watch the **drag thumbnail** (the semi-transparent file icon that follows the cursor) and the **tooltip** under it.

| ID | Probe settings | What to observe and record |
|---|---|---|
| **B1** | Variant B, helper ON, descriptions ON (default) | Is the drag thumbnail visible **on top of** the wheel? Does the tooltip read **"Convert to PNG"** with a copy (+) icon? |
| **B2** | Variant B, helper **OFF** | Thumbnail visible? Tooltip? |
| **B3** | Variant B, helper OFF, **"Place wheel below the drag image" ON** | Thumbnail visible? |
| **B4** | **Variant A** (WPF AllowDrop) | Does the conversion still work? Thumbnail? Tooltip? |
| **B5** | Back to defaults, **Bloom animation** ON vs OFF | Does the bloom feel smooth? Any flicker or a black square flashing? |

### Block C: drag-start detection (H-3) and false triggers

| ID | Steps | What to record |
|---|---|---|
| **C1** | Read the **"Signal active at enter"** row of the statistics after A15 | e.g. `OLE event 0/20 · SysDragImage 20/20 · mouse hook 20/20` |
| **C2** | Untick signals so **only (a)** is ticked. Try A1 three times. Then **only (b)**, three times. Then **only (c)**, three times | For each: did the wheel open? (Log shows `WHEEL OPEN`, or `WARN Shift tapped … no enabled drag signal`) |
| **C3** | All signals ON. Do each of these **with Shift tapped mid-gesture**: (1) rubber-band select on empty Explorer space, (2) drag a window by its title bar, (3) drag-select text in Notepad | Did a wheel appear? These are **false triggers**. Log shows `closed … WITHOUT DragEnter` |
| **C4** | Only if C2 shows the wheel never opens: tick **Diagnostic: open on Shift whenever button is held** and repeat A1 | Does the drop work? This separates "can't detect the drag" from "can't receive the drop" |

### Block D: displays and edges

| ID | Steps | Expected |
|---|---|---|
| **D1** | Settings → Display → **Scale 150%** (keep the probe running), click **Preview wheel**, then repeat A1 | The wheel is centred on the cursor, crisp (not blurry) and proportionally larger. **The highlighted segment is always the one under the pointer** |
| **D2** | Second monitor: drag `a.jpg` from Explorer **onto the second monitor**, tap Shift there, convert. If the monitors use different scales, also test both directions | Same as D1, on the second monitor |
| **D3** | Open the wheel with the cursor near a **screen corner** and near the **taskbar** | Wheel is pushed fully on-screen (not centred on the cursor there, by design) and still works |
| **D4** | Switch Windows to **dark mode**, restart the probe, click **Preview wheel** | Dark wheel with readable labels |

Restore your display scale afterwards.

---

## 4. If something goes wrong

- **The wheel never appears:** check that the log has `elevated=False`. Try C2/C4 and note the result.
- **The wheel appears but nothing happens on release:** look for `closed … WITHOUT DragEnter` in the log. That means the OLE drag never reached the wheel, which is the key thing I need to know.
- **Anything in red (FAIL):** copy those lines into the notes column.
- **Explorer froze or crashed:** note the test ID and time; the log timestamps will line up.
- Delete `C:\OrbitTest` whenever you like; **Create test files** recreates it.

---

## 5. Acceptance criteria for the feasibility gate

These come from section 7 of the initial assessment, made measurable.

| # | Criterion | Measured by | Threshold |
|---|---|---|---|
| G1 | The wheel receives the **real file list before the drop** (H-1, H-2) | A1, A2, A3: `PASS … DragEnter … BEFORE drop` | Works from Explorer **and** the desktop |
| G2 | **Originals are never moved, modified or deleted** (H-4) | `Source integrity` row; A9, A10 | **0 FAIL** across all tests |
| G3 | Reliability | A15 | **20/20** operations; no Explorer hang |
| G4 | Responsiveness | `Shift → wheel visible` median | **≤ 150 ms** after the Shift tap (poll granularity ≤ 15 ms) |
| G5 | At least one drag-start signal is reliable with no meaningful false triggers (H-3) | C1, C2, C3 | One signal at **100%** of confirmed drags; false triggers **0** for the chosen signal |
| G6 | Cancel paths are clean | A6, A7, A9 | No files created, wheel dismissed |
| G7 | DPI and multi-monitor | D1, D2 | Correct size and position, accurate hit-testing |
| G8 | Standard user | Setup step 1 | `elevated=False` throughout |

**Decision rule** (unchanged from the initial assessment):
- **GO** (Tangerine-faithful primary gesture): G1–G8 all pass.
- **CONDITIONAL GO**: G1, G2 and G6 pass, but G4 or G5 is flaky. The primary gesture stays and gets hardened in v0.1; the **auto-orb (Fallback A)** is prototyped as a complement, not a replacement.
- **NO-GO on drag**: G1 or G2 fails. I stop and report back before anything else is built.

Visual polish (B1–B5, D4) does **not** gate feasibility. It decides implementation choices.

---

## 6. Results table (fill in)

Tester: ______ · Date: ______ · Windows build: 26200 · Monitors / scale: ______

| ID | Pass / Fail | Notes |
|---|---|---|
| A1 | | |
| A2 | | |
| A3 | | |
| A4 | | |
| A5 | | |
| A6 | | |
| A7 | | |
| A8 | | |
| A9 | | |
| A10 | | |
| A11 | | |
| A12 | | |
| A13 | | |
| A14 | | |
| A15 | | n = __ / 20 |
| B1 | — | thumbnail on top? Y/N · tooltip? Y/N |
| B2 | — | thumbnail? Y/N · tooltip? Y/N |
| B3 | — | thumbnail? Y/N |
| B4 | | works? Y/N · thumbnail? Y/N |
| B5 | — | smooth? flicker? |
| C1 | — | OLE __/__ · image __/__ · hook __/__ |
| C2 | — | (a) only: __/3 · (b) only: __/3 · (c) only: __/3 |
| C3 | — | false wheels: rubber-band __ · title bar __ · text __ |
| C4 | — | (only if needed) |
| D1 | | |
| D2 | | |
| D3 | | |
| D4 | | |
| Recording | — | path: |
