# Phone Tray Insert — Plan

**Status (2026-11-16, revised after the user's answers): plan only. Phone model, printer and layout are now
known; the tray and the phone are still not caliper-measured, and no CAD is written.**
A 3D-printed insert for the orange tool-box tray that holds the player phones upright, side by side,
separated by thin flanges so they cannot rub. First version holds **6** phones; a later version may hold **8**.

Follows the same workflow as [ENCLOSURE_KB.md](ENCLOSURE_KB.md): parametric OpenSCAD in `cad/`, all
dimensions as named variables, render + interference check before printing.

**CAD: [cad/phone_tray_insert.scad](../cad/phone_tray_insert.scad)** (written 2026-11-16, renders in OpenSCAD
2021.01 as one solid). It runs on the **estimates** in this document. Values marked ESTIMATE in the file must
be replaced with caliper readings before anything is printed. The **phone size and fit** are now the same as in the
border holder (`phone_clear` 0.5 there = `clear` 0.5 / `len_clear` 1.0 here), which the user reports fits a real
phone well (2026-11-20). Defaults: 6 phones fit with 17.2 mm to spare across and 24.9 mm along. The file's asserts refuse to render a design that does not fit the tray, the 0.5" fin limit,
or the P2S bed.

## 1. Requirements (from the user)

| # | Requirement |
|---|---|
| R1 | Insert drops into the orange tray pictured. |
| R2 | Holds 6 phones now; design must be extendable to 8. |
| R3 | A thin flange separates each phone from the next (no phone-to-phone contact). |
| R4 | Flanges grip only the ends of the phone: each reaches **less than ½" in from the top end and from the bottom end** of the phone. The middle of the phone is left free. |
| R5 | Fact: with the phones touching the tray's supports (the ribs on the walls), **1⅛" (28.6 mm) is left over after 6 phones**. |

## 2. What the photos show (read by eye — not measurements)

- Tray interior at the rim is roughly **4½" wide × 7–7½" long** (tape laid across the top, photo taken at an
  angle, so ±¼"). Walls slope inward toward the floor.
- Each long wall has **two angled ribs** (gussets) near the middle of its length, plus smaller ones in the
  corners. These are the "supports". Rib tip to rib tip across the width looks like **about 3–3½"** at floor
  level. The phone in photos 2–3 stands on its long edge and leans on the ribs of the right-hand wall.
- A black foam pad covers part of the floor (roughly 3½" × 5").
- A multi-connector USB charging cable lies beside the tray. **The phones do not charge in the tray** — the
  tray is lifted out to charge them (Q3), so the insert needs no cable access.
- **Phone:** the About screen reads **model A502DL**, Android 8.1.0, Tracfone. One retailer spec page lists the
  A502DL (sold as the Alcatel TCL LX, 2018) at **148.1 × 69.6 × 9.4 mm**
  ([itechguides](https://www.itechguides.com/?p=164659)). That is a single source and has not been calipered.
  The phones in the photos have no case. **Update 2026-11-20:** the user reports that a real phone fits well in the
  border holder's cradle, which is cut for exactly these figures plus 0.5 mm (9.9 mm deep, 149.1 mm long). That
  shows a real phone fits a 9.9 × 149.1 mm space; it does not show how much smaller than that it is.

## 3. Questions and answers

| # | Question | Answer (2026-11-16) |
|---|---|---|
| Q1 | Layout of the flanges | **Confirmed:** one comb at each end of the phones, top and bottom, each reaching <½" along the phone. |
| Q2 | Phone model | **TCL A502DL** (boot screen and About screen in the photos). Size from a retailer page, not yet measured: 148.1 × 69.6 × 9.4 mm; a real phone fits the border holder's cradle cut for those figures + 0.5 mm (2026-11-20). Still need: camera-bump position and height, and the real thickness. |
| Q3 | Charging in the tray | **No.** The tray is removed to charge the phones, so the insert has no cable, port or plug constraints. The 8-phone tray is a separate print and will not charge in place either. |
| Q4 | Foam pad | **Stays; ½" (12.7 mm) thick.** It is on the floor to cushion the phones, so the phones rest on the foam. Still need its footprint, and whether it is firm enough that the comb bars can sit on it or must sit on the tray floor beside it. |
| Q5 | Printer | **Bambu Lab P2S**, build volume 256 × 256 × 256 mm ([3DJake](https://www.3djake.com/bambu-lab/p2s), [B&H](https://www.bhphotovideo.com/c/product/1946344-REG/bambu_lab_pf004_u_us_p2s_3_d_printer.html)). The tray is ~7½" (190 mm) long, so a one-piece ladder frame fits the bed. Material not chosen: PETG suggested. |

## 3a. Measurements to take (calipers, not tape)

1. **Phone** (as used, case on): thickness at the *top-end* and *bottom-end* zones separately (camera bump!),
   length, width. Weigh one.
2. **Tray floor** (inside, at the floor): length × width. **Tray depth.** Corner radius.
3. **Ribs:** rib-tip to rib-tip across the width; distance of each rib from the short walls; **height of each
   rib's lower edge above the floor** (this decides whether phones are stopped by the ribs at all);
   protrusion from the wall.
4. **Foam:** length × width, and position (thickness is known: ½" = 12.7 mm). Also measure how far it
   compresses under a phone, and the tray depth, so the top of the standing phones (foam 12.7 + phone
   width 69.6 mm ≈ 82 mm above the floor) can be compared with the rim and the ribs.
5. Re-check the "1⅛" figure: measure rib-tip to rib-tip, then 6 phones side by side, in mm.

## 4. Width budget (the part that can sink the 8-phone version)

Let *W* = rib-tip to rib-tip width. R5 says **W = 6t + 28.6 mm**, where *t* = phone thickness.
Per phone *c* = clearance (0.4 mm suggested), per flange *f* = fin thickness. With the outer ends held by the
ribs/walls there are N slots and N−1 fins:

> N phones fit when **N·t + (N−1)·f + N·c ≤ W**

- **6 phones:** overhead = 5f + 6c = 8.4 mm at f = 1.2, c = 0.4 → **20 mm spare**. Comfortable for any *t*.
- **8 phones:** need **2t + 7f + 8c ≤ 28.6 mm**. Largest phone thickness that still fits:

| Fin *f* | Max *t* for 8 phones (c = 0.4) |
|---|---|
| 0.8 mm | 9.9 mm (0.39") |
| 1.2 mm | 8.4 mm (0.33") |
| 1.6 mm | 7.1 mm (0.28") |

**With the A502DL at t = 9.4 mm (unconfirmed):** W = 6 × 9.4 + 28.6 = 85.0 mm (3.35"), which agrees with my
by-eye 3–3½" rib span. 8 phones need 7f + 8c ≤ 28.6 − 2 × 9.4 = **9.8 mm**:

| Fin *f* | Clearance *c* | 7f + 8c | Margin vs 9.8 mm |
|---|---|---|---|
| 1.2 mm | 0.4 mm | 11.6 | **−1.8 mm (does not fit)** |
| 1.0 mm | 0.4 mm | 10.2 | −0.4 mm (does not fit) |
| 1.0 mm | 0.3 mm | 9.4 | +0.4 mm (barely) |
| 0.8 mm | 0.5 mm | 9.6 | +0.2 mm (the border holder's fit) |
| 0.8 mm | 0.4 mm | 8.8 | +1.0 mm |
| 0.8 mm | 0.3 mm | 8.0 | +1.8 mm |

(This table counts only the fins between phones. The CAD also adds an outer fin and a rail on each side, which
take 2 × 0.8 mm more; see the 8-phone check in section 5.)

So **8 phones is plausible but tight**: it needs ~0.8 mm fins (two perimeters at a 0.4 mm nozzle, so thin and
flexible; the P2S can also print them, but test the strength) and phones that really are ≤ 9.4 mm. Each
0.1 mm of extra phone thickness costs 0.2 mm of margin; a camera bump in a fin zone, or a case, ends it.
The 6-phone design is safe either way. Decide the 8-phone version only after the caliper check and a
test print. If it does not fit, options: bigger tray, second tray, or two stacked tiers. Do not shave
clearance below ~0.3 mm to force it; phones that bind are worse than no insert.

## 5. Design concept

Phones stand on their long edge, faces parallel to the long walls, stacked across the tray's short axis like
files in a rack.

```
         top view (not to scale)                 end view of one comb
   short wall                                   
  ┌─────────────────────────────────┐           fin   fin   fin       ← fins ≤ ½" along the phone
  │ ▒▒comb▒▒   (phone ends)  ▒▒comb▒▒│            │     │     │          (they stand ~¾" tall, tips chamfered)
  │ ▒ ┃┃┃┃ ▒  ░░░░░░░░░░░░░░  ▒ ┃┃┃┃ ▒│         ┌─┴─────┴─────┴─┐
  │ ▒ ┃┃┃┃ ▒  ░░phones░░░░░░  ▒ ┃┃┃┃ ▒│         └───────────────┘   ← base bar, sits on floor/foam
  └─────────────────────────────────┘           (one slot per phone, pitch = t + f + c)
        ┃ = fins, one gap per phone
```

**Part A — comb:** a short base bar (≈ ½" along the tray length, as wide as the slot row) with N−1 upright
fins. One comb at each end of the phones. Each fin is ≤ ½" long along the phone, about ¾" tall (to be
confirmed by Q2/§3a), top edge chamfered so a phone lowers in easily.

**Layout choice (decide in phase 1):**

| Option | Description | For | Against |
|---|---|---|---|
| **1 — ladder (recommended first try)** | Both combs joined by two thin side rails into one frame. | One print, one piece to lift out, combs can't drift apart. | Rails must pass the ribs (notches or sit below them); needs bed ≥ frame length. |
| 2 — two loose combs | Two separate bars. | Easy to print, fits any bed, trivial to change N. | Combs can shift; needs a locating feature on the floor/ribs. |

Either way the combs should locate against the tray's short walls and the foam edge so they cannot walk.

**Making 6 → 8 cheap:** keep `N` (number of phones) as one variable, with all positions derived from the
pitch `p = t + f + c`. The 8-phone version is then a parameter change plus the fit check in §4, not a new
design.

**As built in the CAD (ladder option, `use_rails = true`):** each comb is a full-width **end wall** just beyond the
phone's end, with the fins reaching inward from it. The end walls drop `bar_drop` (= foam thickness 12.7 mm)
to the tray floor, because the phone (148 mm) is longer than the foam appears to be (roughly 5"), so the walls
stand on the floor past the foam's ends. Two **rails** (as thick as a fin, 1.2 mm, and **1 inch tall** above the foam
since 2026-11-20) join the walls. Each rail sits at a fin position, the first one in from each side: between the first
and second phone, and between the last two, running the whole length in place of that fin. (Until 2026-11-20 the
rails were 4 mm tall and outside the row, at the outer fins; they took 2 × `rail_t` out of the width budget.)
The rails now run between phones, over the foam, so if the foam comes back (`foam_t` > 0) they would have to pass
through it: set `bar_drop = 0` (everything on the foam) or cut a slot for each rail. The rails clear the wall ribs,
which only reach the rib tips at the edges of the row, so the rail height no longer has to stay below the ribs'
lower edges. Each rail has **three cut-outs** (`rail_cuts`) to save material: 15 mm of solid rail at each end
(`rail_margin`, at least `fin_reach`), 10 mm posts between them (`rail_post`), and 5 mm strips along the top and
bottom (`rail_bar`); each is about 33 × 15.4 mm, with the top corners cut at 45° by 7 mm (`rail_chamfer`) so the top
of a cut-out is a 19 mm bridge instead of 33 mm.

**8-phone check with the CAD** (`-D n_phones=8 -D fin_t=0.8 -D clear=0.3 -D rail_t=0.8`): renders, with
**0.2 mm to spare** across the estimated 85.0 mm rib span. That is within the error of every number it rests
on, so treat it as "not ruled out", not "fits".

**8 phones at the border holder's fit (`clear` 0.5), checked 2026-11-20:** with the outer fins
(`-D n_phones=8 -D fin_t=0.8`) the width assert fails, **short by 1.4 mm**. With `outer_fins=false` it renders,
rails included, with **0.2 mm to spare**, again inside the error of the estimated rib span. So at that fit the
8-phone tray gives up the outer fins, or goes back to `clear` 0.3. (The rails no longer cost width, so they stay.)

## 6. Parameters (OpenSCAD, all in mm)

*(Implemented in [cad/phone_tray_insert.scad](../cad/phone_tray_insert.scad); the list below is the original
plan and the file's names differ slightly: `phone_t`, `phone_len`, `phone_w`, `rib_tip_span`, `floor_len`,
`foam_t`, `clear`, `len_clear`, `side_clear`, `fin_t`, `fin_reach`, `fin_h`, `lead_h`, `end_t`, `end_h`,
`use_rails`, `rail_t` (= `fin_t`), `rail_top` (25.4, i.e. 1"), `rail_cuts` (3), `rail_margin`, `rail_post`,
`rail_bar`, `rail_chamfer`, `bar_drop`.)*

`n_phones`, `phone_t`, `phone_len`, `phone_w`, `fin_t` (1.2), `clear` (0.5 since 2026-11-20, was 0.4; `len_clear` 1.0, was 0.6), `fin_reach` (≤ 12.7, i.e. <½"),
`fin_h`, `bar_len`, `bar_t`, `rib_tip_span` (W), `floor_len`, `floor_w`, `foam_t`, `bump_extra` (camera bump,
if it sits in a fin zone).

Derived: `pitch = phone_t + fin_t + clear`, `row_width = n·phone_t + (n−1)·fin_t + n·clear`, with an
assertion `row_width <= rib_tip_span` so the file refuses to render a design that does not fit.

## 7. Risks and how to catch them

| Risk | Mitigation |
|---|---|
| Camera bump sits inside a fin zone, so the real thickness there is *t + bump* and phones jam | Measure thickness at the fin zones (§3a-1). Put the bump end's fins on the bump side, or notch the fins. |
| Ribs collide with the frame or the phones | Model the ribs as keep-out volumes in the OpenSCAD file and run the same intersection-is-empty check as in ENCLOSURE_KB.md. |
| Tray walls slope, so width at the floor ≠ width at the rim | Measure at the height where the fins actually are, not at the rim. |
| Fins too thin and snap | ≥ 1.2 mm (3 perimeters at 0.4 mm nozzle), PETG preferred; tie fins to the base bar with a fillet. |
| 8 phones do not fit | §4 gives the limit; decide before printing the 8-phone version. |
| Phones slide out when the box is carried | Not covered by R1–R5. If the box travels, add a lid pad or tall end fins — ask. |

## 8. Phases

1. **Take the caliper measurements** (§3a; Q1–Q5 are answered). Update the parameter list with real numbers.
2. **Choose layout** (ladder vs loose combs) and sketch the 6-phone width budget with real numbers.
3. **Write `cad/phone_tray_insert.scad`** with the parameters and the fit assertion; render; interference
   check against the rib keep-outs.
4. **Test print:** one comb section with 3 slots. Drop a real phone in and out. Adjust `clear` and
   `fin_t`.
5. **Full 6-phone print** and fit in the tray with all six phones.
6. **8-phone insert (separate print):** change `n_phones` to 8; if the assertion passes, print it as its own
   part; if not, take the §4 options.

## 9. Success criteria

- All 6 phones drop in and lift out by hand with no force, no phone touching another phone.
- Fins reach <½" from each phone end (verified by caliper on the printed part).
- The insert sits flat in the tray and does not move when the tray is tilted.
- 8-phone version: assertion passes in the CAD **and** a real fit test passes, or the doc records that it
  does not fit and why.

## 9a. Related: one-phone holder for the field border

A separate part, not the tray insert: [cad/phone_border_holder.scad](../cad/phone_border_holder.scad) (2026-11-17).
One TCL phone, landscape. **Latest (2026-11-17, 4th revision): tilt 75° (was 60°) and no ribs below the top of
the wall.** With the plate pinned at the tab's back plane, the only way to bring the phone closer to the wall is
a steeper tilt; 76° is the most the plate can take before its back face would be inside the border (assert).
At 75° the shelf front is 26 mm out from the border's face (was 42 mm), the phone top is 70 mm above the table,
the part prints about 155 × 71 × 34 mm and uses ~46 cm³. The plate now leans straight from the skirt down to the
table; only small triangular ribs remain, above the wall. Slicer supports are needed under the plate's back face.
**Then (2026-11-18):** the user set `tilt = 50` in the file (shelf front 51 mm out from the border's face, phone top
56 mm above the table); I filled the space above the tab up to the cradle with a solid block as wide as the tab and
cut the ribs to one at each end of the cradle (the ribs between the middle and the ends were removed). Prints
about 155 × 58 × 58 mm, ~48 cm³. Collision checks still empty; never printed.
**Then:** the skirt (the "outside tab") was joined to the cradle's lowest edge by a 3 mm strip along the table,
tab-width wide, closing the skirt / table / back-plate triangle (~53 cm³ total). The strip was then trimmed to end
exactly at the cradle's lowest edge (it had stuck out 2 mm below the shelf). The thin top of the triangular opening
was filled solid down to where the opening is 6 mm wide (`gap_min_w`), leaving a trapezoid; the user said that
was not what they wanted, and it was removed again. Instead the back plate's one large window became **two
windows with a solid rib between them, as wide as the tab** (`mid_rib_w = tab_w`, ~57 cm³ total). The user had
set `tilt = 70` in the file by then.
**Then (2026-11-18, 5th revision) — reverted at the user's request:** the link strip had been raised to the
height of the tab's bottom, the cradle's lowest edge raised with it, and the skirt shortened to the tab's depth,
so the holder hung from the border instead of standing on the table. Back to the previous state: skirt, link and
cradle on the table (`cradle_z = -border_h`), phone top 68 mm above the table, about 155 × 39 × 69 mm printed,
~57 cm³. Collision checks empty; never printed.
**Then (2026-11-20) — side ribs moved in:** the two triangular ribs (`gusset_t` 2.4 mm), which stood at the ends of the
cradle, now stand against the outer edge of each back window (`rib_x = win_out + gusset_t/2`, so x = 60.55–62.95 mm
from the centre at the current settings), touching the window but not over it. The windows' outer edge is now a named
value, `win_out`. Checked by sampling the exported STL at one height above the border: no material at x = 19.5–60.4
(the window is clear), the rib and plate at x = 60.7–62.8, plate only beyond 63.2. Volume unchanged at 51.0 cm³;
one solid; the collision check is empty. With `back_window = false` the ribs stay at the cradle's ends.
**Border height corrected (2026-11-20):** the user found the wall about 1/16" shorter than first measured, so
`border_h` is now 1 3/8" (34.925 mm), was 1 7/16" (36.5125 mm). The cradle still stands on the table, so it sits
1.6 mm higher relative to the border's top face; its distance out from the border (41.9 mm to the shelf front), the
phone's height above the table (62.9 mm) and the print height (49.6 mm) are unchanged. The skirt and link are 1.6 mm
shorter and the ribs and fill block 1.6 mm taller, so the part is 51.9 cm³ (was 51.0). Still one solid; the collision
check is empty. Earlier figures in this section use 1 7/16" and are left as written.
(The text below describes the earlier revisions, with its numbers at 60°.) Revised the same day, several times. The user's photos showed the slot is in
the **top** of the border (not the side wall): a tab drops down into the slot (2" × ¼" × ¾", the user's figures),
a base rests on the top face, and a skirt runs down the border's outer face to the table. Then the user's
measurements: border 1 7/16" tall, wall about 1/16" each side of the slot, nothing may reach past the tab's
back (field-side) face, the holder stands on the table, and it prints on its back with the tab flat on the bed.
The cradle is therefore pushed out until its leaning back plate meets the tab's back plane at the top (shelf
front 42 mm out from the border's face), carried to the table by rib frames. The cradle was then lowered
straight down until the shelf's back edge stands on the table (`cradle_z = -border_h`): phone top 63 mm above
the table, 26 mm above the border's top. Prints lying on its back (about 155 × 64 × 50 mm, ~59 cm³ of plastic);
needs slicer supports (build plate only) under the skirt and lower ribs. Rendered as one solid; the collision checks (phone, border, table,
behind the tab) are all empty; never printed. Assumptions to confirm are listed at the top of the file.

## 10. Changelog

- 2026-11-16 — Plan written from three photos and the user's note (1⅛" spare with 6 phones). No CAD yet.
- 2026-11-16 — User answered Q1–Q5: combs at both ends, phone is the A502DL, phones may charge in the tray,
  foam stays under the phones, printer is a Bambu P2S. Width budget redone with t = 9.4 mm (8 phones tight,
  needs ~0.8 mm fins); added a plug-room risk.
- 2026-11-16 — Confirmed: phone is a TCL; foam is ½" (12.7 mm) thick.
- 2026-11-16 — First CAD: `cad/phone_tray_insert.scad`. Rendered 6-phone and 8-phone variants (both one
  solid, bounding box 67 × 155 × 32 mm for 6 phones); the 8-phone variant with default fins correctly fails the
  width assert. Not printed; ESTIMATE values not yet replaced with measurements. Ghost-phone preview
  not viewed on screen (only rendered to STL).
- 2026-11-16 — Added `outer_fins` (default on): a fin on the outside of each outermost phone, so every phone
  has a fin on both faces (N + 1 fins per comb). Outer fins share the rails' x position, so the width budget
  is unchanged (`side_w = max(fin_t, rail_t)`). Rendered one solid for 6 phones and for the tight 8-phone set;
  previewed the assembly image. The user had edited the file to `foam_t = 0` and `fin_h = end_h = 49`; kept as
  given (the comments there still say 1/2" foam and 3/4" fins, and the text above still quotes 12.7 mm / 19 mm).
- 2026-11-16 — User corrected Q3: phones do **not** charge in the tray (it is lifted out to charge), and
  the 8-phone tray is a separate print. Removed the charging/plug risks and success criterion.
- 2026-11-20 — User reported the border holder fits a real phone well and asked for the tray insert to fit the
  same phone. The phone size was already identical in both files (148.1 × 69.6 × 9.4 mm), so only the fit changed:
  `clear` 0.4 → 0.5 and `len_clear` 0.6 → 1.0, the border holder's clearances. 6 phones render as one solid with
  17.2 mm spare across (was 17.8) and 24.9 mm along; the ESTIMATE tags on the phone values were removed (the
  figures are still the retailer's, not calipered). 8 phones at that fit fail the width assert by 1.4 mm with outer
  fins and rails, and have 0.2 mm spare as two loose combs without outer fins.
- 2026-11-20 — **Rails moved in and made 1" tall.** The two rails that join the end walls now sit at the first
  inner fin position on each side (between phones 1–2 and between the last two) and are `rail_top` = 25.4 mm
  tall (was 4 mm, outside the row). `rail_t` now defaults to `fin_t`; `side_w` is just the outer fins' width, so the
  rails cost no width (6 phones still 17.2 mm spare). New asserts: the rails need `n_phones` ≥ 3, and `rail_t` ≤
  `fin_t`. Rendered one solid; rail tops measured at x = 9.9–11.1 and 54.3–55.5 mm for 6 phones. The 8-phone
  set with `outer_fins=false` now keeps its rails (0.2 mm spare). Not printed.
- 2026-11-20 — **Three cut-outs in each rail** (`rail_cuts`, `rail_margin`, `rail_post`, `rail_bar`,
  `rail_chamfer`): about 33 × 15.4 mm each, top corners cut at 45°. The 6-phone frame goes from 35.9 to 32.6 cm³ (saves
  3.3 cm³, 9%). Still one solid with the same bounding box. New assert checks that the cut-outs fit and start beyond the
  fins' reach; `rail_cuts = 0` gives solid rails. Not printed, not sliced.
