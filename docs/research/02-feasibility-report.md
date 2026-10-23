# Flipr Feasibility Report (Step 4)

| | |
|---|---|
| Status | **PRELIMINARY GO.** The core interaction is confirmed on real hardware with physical input. The formal GO is pending the remaining tests in §5 |
| Date | 2026-10-18 |
| Machine | Windows 11 Pro build 26200, single monitor at 100% (96 DPI), .NET 10.0.13, standard (non-elevated) user |
| Evidence | Manual session 13:47–13:56 (log `probe-*.log`, 18 wheel sessions), the user's written observations, and the probe self-test (23/23) |

> **Reading guide.** "✅ confirmed" means measured in the manual session with physical input. "⏳ pending" means not yet exercised. Per the product owner's instruction, results are recorded as **preliminary positive**, not as passed acceptance criteria, until §5 is complete.

---

## 1. Headline result

**The Tangerine-style gesture works on Windows using only documented APIs.** A file dragged from Explorer or the desktop can open a radial wheel with a Shift tap. The wheel receives the **real file list before the drop**, Shift cycles modes mid-drag, and releasing on a segment runs a genuine operation. Originals stayed intact in every case.

Measured statistics (the user's summary from the probe):

```
Wheel opens ............ 18   (Shift+button but no signal: 0)
Confirmed drags ........ 18   (opens without DragEnter: 0)
Shift → wheel visible .. median 2 ms · max 19 ms (n=18)
Wheel → DragEnter ...... median 6 ms · max 80 ms (n=18)
Drops on wheel ......... 15   mode switches: 16
Operations ............. 17 ok · 2 skipped · 0 failed
Source integrity ....... 19 pass · 0 FAIL
Physical input only .... yes
```

## 2. Observations confirmed by the product owner

- Dragging files from File Explorer activates the wheel on Shift.
- The wheel appears smoothly and tracks the mouse correctly.
- Real conversions succeed.
- Multiple selected files are processed together.
- Shift during the same drag switches Convert → Tools (→ Actions).
- The interaction "feels fluid, responsive, and very close to the Tangerine experience".

## 3. Findings from the log

| # | Finding | Evidence | Consequence |
|---|---|---|---|
| R1 | **H-1 confirmed:** OLE routes Explorer drags to a wheel that appears mid-drag | 18/18 sessions logged `DragEnter`; 0 opens without one | Core mechanism validated |
| R2 | **H-2 confirmed:** the file list is available **before** the drop, cross-process | `PASS … file(s) BEFORE drop` ×18, including 3-file selections (#9, #10) | The wheel can show file-aware menus |
| R3 | Works from the **desktop** as well as Explorer windows | #16–#18 (`C:\Users\…\Desktop\…`) | — |
| R4 | **Latency is far under target:** Shift → visible median **2 ms**. The only outlier is the very first open (18.7 ms, cold window) | `WHEEL OPEN in 18.7 ms` (#1), then 1.9–2.6 ms | Production **pre-warms** the wheel at startup |
| R5 | `DragEnter` arrives a median **6 ms** after the wheel appears, with no mouse movement needed | `Wheel → DragEnter` row; `keys=0x5` shows it fires on the Shift key-state change | The empty "Move here" state is effectively invisible; the bloom hides it |
| R6 | **H-3a rejected:** `EVENT_SYSTEM_DRAGDROPSTART` is **never** raised by Explorer on this build | 0 events in the whole log | Not used in production |
| R7 | **H-3b confirmed as the primary signal:** the shell `SysDragImage` window appears on **every** Explorer/desktop drag (with or without Shift) and never on plain clicks | `image=Y` at 18/18 opens; ACTIVE/inactive pairs match drags exactly | Production arms on `SysDragImage`. No hooks needed by default |
| R8 | The shell **hides** `SysDragImage` while the cursor is over our registered target and creates a new one on `DragLeave` | `ShellDragImage → inactive` ~2–8 ms after each `WHEEL OPEN`; new hwnd on each leave | Signal "end" ≠ drag end. Production ends sessions on drag-button release / Esc, not on image hide. *(The probe's "at enter" statistic measured this wrong; fixed to "at open")* |
| R9 | **H-3c low specificity:** the mouse-hook heuristic fires on almost every click with 4–6 px of jitter, with no drag behind it | Dozens of `MouseHookHeuristic → ACTIVE` without `SysDragImage` | Kept only as an **opt-in** "extended detection" setting, for drag sources that don't use the shell drag image |
| R10 | **H-4 / H-8 strong evidence:** Explorer offered `COPY\|MOVE\|LINK`, Shift was **held** at `DragEnter` (`keys=0x5` = button + Shift, Explorer's "move" modifier), we answered COPY, and every source hashed identical | 19/19 `source intact` | Always-COPY policy carries into production. The explicit A10 test is still owed |
| R11 | Collision-safe naming and batch skip work on real drops | `b (2).jpg`, `a (3).png`, `a (rotated) (2).jpg`; `SKIP a (2).png: already PNG` | — |
| R12 | Cancel paths leave nothing behind | #6–#8 (released off-wheel or on hub): `no drop on wheel; all … still in place` | — |
| R13 | Desktop **shortcuts** arrive as the `.lnk` path, not the target | #16–#18 `Chrome Remote Desktop.lnk` | Product decision: conversions should resolve `.lnk` targets; actions act on the shortcut itself |
| R14 | The shell drag-image machinery engages over our wheel (`IsShowingLayered`, `DragWindow`, `DropDescription`, `IsShowingText` formats appear) | #4, #15, #18 format lists | Suggests drop descriptions work; **visual confirmation pending (B1)** |

## 4. Hypothesis status

| ID | Hypothesis | Status |
|---|---|---|
| H-1 | A wheel appearing mid-drag receives `DragEnter` | ✅ confirmed (18/18) |
| H-2 | File list readable before the drop | ✅ confirmed (single and multi-file, Explorer and desktop) |
| H-3 | Reliable drag-start signal | ✅ `SysDragImage` (18/18 at open) · ❌ `DRAGDROPSTART` (never fires) · ⚠️ mouse hook (noisy). False-trigger test C3 pending |
| H-4 | COPY-only answer keeps sources untouched | ✅ 19/19 hash-verified · explicit A10 pending |
| H-5 | Drag thumbnail stays visible over the wheel | ⏳ pending (B1–B3) |
| H-6 | "Convert to PNG" native drag tooltip | ⏳ visual pending (B1); data-object evidence positive (R14) |
| H-7 | Native `IDropTarget` vs WPF `AllowDrop` | ✅ native works · WPF variant untested (B4). **Decision: native**, since it's the only one supporting H-5/H-6 control |
| H-8 | Shift's move semantics don't leak | ✅ strong evidence (R10) · explicit A10 pending |

## 5. Remaining tests for the formal GO

The gesture is **not** declared production-ready until these are done. The production M1 build includes the same diagnostics, so the tests can run against the production app as well as the probe.

| Test | Gate | Why it's still needed |
|---|---|---|
| **A6** Esc while the wheel is open | G6 | The Esc path hasn't been exercised |
| **A10** Shift *held* at release | G2 | R10 is strong but indirect |
| **A11** normal drags/copies without Shift | — | Confirms Flipr is invisible otherwise (the log implies it; needs explicit confirmation) |
| **A12** non-file drag (text) + Shift | G6 | Behaviour with non-file data objects |
| **A15** 20 consecutive runs | **G3** | 15 drops so far, not a formal consecutive run |
| **B1–B4** drag thumbnail and tooltip visibility | H-5/H-6 (polish, not gating) | Decides z-order handling |
| **C2** SysDragImage-only arming, **C3** false triggers | **G5** | Confirms the chosen signal in isolation and with no false wheels |
| **D1** 150% scaling, **D2** second monitor (mixed DPI), **D3** edges | **G7** | Only 96 DPI on one monitor so far |
| **D4** dark mode | — | Visual |
| Windows 10 | — | Deferred by the product owner; required before v0.5 |

## 6. Decision

- **PRELIMINARY GO.** Start the production foundation around this interaction, as the product owner directed.
- The production code **reuses the probe's proven mechanism unchanged in substance**: native `IDropTarget` + `IDropTargetHelper`, COPY-only, 60 Hz modifier polling only during drags, physical-pixel placement.
- It changes only what the log proved necessary:
  - arm on `SysDragImage` (R7);
  - mouse hook off by default (R9);
  - end sessions on button release or Esc rather than on the image hiding (R8);
  - pre-warm the wheel (R4).
- **The formal GO** is granted when the §5 tests pass. Until then, release notes describe the interaction as "validated on Windows 11, single monitor, 100% scale".
