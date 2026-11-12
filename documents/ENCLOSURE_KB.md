# Angled Enclosure — Knowledge Base

**Status (2026-11-12):** OpenSCAD model revised and, for the first time, **rendered** (OpenSCAD 2021.01 is now
installed on the dev machine at `C:\Program Files\OpenSCAD`). It builds as one connected solid and passes the
interference checks listed under "Verification". **Never sliced or printed**, and several sizes are still
estimates, not measurements — see "Estimates and unknowns".

## Purpose
A printable desk stand holding the Hosyond 5" DSI touch screen tilted for viewing, with the Ubiquiti UniFi
Travel Router (UTR) in a pocket behind it. Tracks the open task in
[install/todo.md](../install/todo.md) ("Design a printable case for the Pi, its screen, and the router").
Hardware background: [PROJECT_STATUS.md](../PROJECT_STATUS.md) §1.9, [install/NETWORK_SETUP.md](../install/NETWORK_SETUP.md).

## Files
| File | Notes |
|---|---|
| [cad/Pi_Router_enclosure.scad](../cad/Pi_Router_enclosure.scad) | The model (renamed 2026-11-03 from `angled_enclosure_with_unifi_router_dock.scad`). Edit this one |
| `cad/angled_enclosure_with_unifi_router_dock.scad`, `cad/g_angled_enclosure_with_unifi_router_dock.scad` | **Stale.** Both still on disk and tracked in git, holding the *original* single-assembly version (mockups, `RENDER_MODE`, 20° tilt). Safe to delete once confirmed |
| `cad/pi-case.scad` | Former empty placeholder named in todo.md; no longer exists. todo.md now points at the file above |

## Layout (revised 2026-11-12)
World frame: Z up, viewer at −Y. The case's bottom-back corner is the origin. No mockups are drawn.
The case and the router pocket lean back at the **same** `tilt_angle`, parallel, `pocket_gap` apart.

```
side view (viewer on the left)

        screen + Pi in      router in
              |                 |
              v     __arm__     v
             /  /--'       /  /
            /  /          /  /      case (blue) and pocket (yellow) are parallel;
           /  / [Pi's    /  /       the gap between them is the Pi's room
          /  /   room]  /  /
         /  /          /  /
   _____/__/__________/__/____      legs (gray): ground frame, case base -> pocket base
     case              pocket
```

**How the parts go in.** The screen, with the Pi already mounted on its back, slides down into the case from
the open top. It rests on the bottom wall and leans back on three rails (left, right, bottom) that overlap the
back of its PCB by `rail_w`; the Pi comes down through the opening between the rails. The router drops into its
pocket from the top, display toward the window, and sits between two end ledges; its plugs go in through the
pocket's open ends.

| Module | Colour | What it is |
|---|---|---|
| `case()` | blue | Shell `case_depth` = 12.7 mm (½") thick: front wall with the bezel opening, bottom wall, two side walls, **no top wall**. Behind the screen slot are the back rails; between them the back is open, up to the top. 3×2 side vent holes. On `ribbon_side` the side wall is carried back `pi_rear` (23 mm) behind the case to shield the screen-to-Pi ribbon, with its lower rear corner cut off along the ground |
| `legs()` | gray | Ground-level frame: two bars in line with the case's side walls, from the case's bottom edge back to a cross bar along the pocket's bottom edge. *(Colour not specified by the user; gray chosen)* |
| `arms()` | white | One per side, three bars each: beside the pocket's open end at its top corner (back face to front face), a short step outward, then a strut in line with the case's side wall to the top of the case back (all drawn in the case frame, so the ends are square to the case) |
| `router_pocket()` | yellow | Sleeve parallel to the case, bottom edge on the ground. Solid wall faces the case; the back lip has a window showing the router's face. Top end open (router goes in there). Both side ends open above a `ledge_h` ledge. On `eth_end` the back lip is cut away for `eth_cover_len` so the router's Ethernet cover (its "door") can lift; a `door_lip_h` strip of the lip is left below and above that opening to keep the router in place |

Helper modules: `case_frame()` / `pocket_frame()` place geometry in each part's tilted frame; `screen_slot()`,
`pi_room()` and `router_cavity()` are the spaces the three devices take. `keep_clear()` is the union of those
spaces, and `legs()` / `arms()` subtract it — so however the parameters change, a support cannot end up inside
the screen's slot, the Pi's room (including its path in from the top) or the router's cavity.

Colours show in OpenSCAD preview (F5) only. The four parts overlap where they join; a full render (F6) and
export gives one solid.

## What the layout assumes
- **The Pi is mounted on the back of the screen**, on the screen's own standoffs, components facing away from
  the screen. Everything behind the back opening, to `pi_rear` deep, is reserved for it.
- **The ribbon is on one side of the Pi and the Pi's USB/Ethernet jacks on the other** (true of a Pi 4: the
  DISPLAY connector is at the end opposite the USB/Ethernet jacks). `ribbon_side` says which; its default (−1,
  left seen from the front) is a guess, based on the Pi's power socket having to point up, not down into the
  table. Check it against the real assembly and flip the sign if needed.
- **The router's sockets are on its two short ends** — from review photos of the UTR
  ([Dong Knows](https://dongknows.com/ubiquiti-utr-unifi-travel-router-review/)), not from the unit itself:
  one end has two USB-C sockets (power near the middle, tethering beside it); the other has two Ethernet
  jacks under a single flip-up cover (door) that is part of the display face and lifts when a plug goes in.
  Measured off enlarged crops of two photos (so ±2 mm at best): the cover is about 44 mm of the 65 mm width,
  centred, which puts it about 10 mm in from each long edge, and about 22–24 mm long from the end; the jacks
  sit roughly 15–27 mm and 36–50 mm across the end. The router's corners are rounded.
- **The router lies on a long edge with its display toward the pocket's window**, Ethernet end toward
  `eth_end` (default: the side the Pi's own Ethernet jack is on, i.e. opposite `ribbon_side`).

## Parameters (all mm)
| Group | Name | Value | Meaning |
|---|---|---|---|
| Display | `pcb_w × pcb_h × pcb_t` | 121.10 × 77.90 × 1.60 | Screen PCB |
| | `glass_w × glass_h × glass_t` | 110.00 × 66.80 × 1.80 | Glass (declared, unused) |
| | `active_w × active_h` | 108.00 × 64.80 | Visible area (bezel opening is +1.0 each way) |
| | `screen_t` | 7.50 **estimate** | Glass face to the back of the PCB, at the PCB's edges. Sets the slot depth |
| Pi | `pi_back` | 25 **estimate** | Back of the screen's PCB to the rear-most point of the Pi (standoffs + board + jacks, or the ribbon's loop) |
| | `ribbon_side` | −1 **guess** | Side the ribbon is on, seen from the front: −1 left, 1 right |
| Router | `router_w × router_h × router_t` | 97.00 × 66.00 × 13.50 | UTR nominal 95.95 × 65 × 12.5 plus ~1 mm tolerance |
| | `eth_end` | `-ribbon_side` | End of the pocket the router's Ethernet jacks face: −1 left, 1 right |
| | `eth_cover_len` | 36 **estimate** | Length of the flip-up Ethernet cover, from the router's Ethernet end. Probably generous: the closer photo measurement above suggests 22–24 |
| | `door_lip_h` | 6 | Strip of the back lip left below and above the cover's opening. Must stay under the cover's distance from the router's long edges (about 10 from photos) |
| Case | `tilt_angle` | 60° | Angle of the **screen from horizontal** (90 = upright). The original 20° value was the wrong convention for a desk stand |
| | `wall_t` | 2.80 | Wall thickness |
| | `case_depth` | 12.70 | Case thickness (½") |
| | `case_w / case_h` | pcb + 2·wall | Derived: 126.7 × 83.5 |
| | `slot_clr` | 0.40 | Front-to-back clearance for the screen |
| | `rail_w` | 3.50 | How far the back rails reach in over the back of the PCB |
| | `rail_t` | derived (2.0) | Rail thickness = `case_depth − wall_t − screen_t − slot_clr`. An `assert` stops the build below 1.2 |
| | `pi_rear` | derived (23) | How far the Pi reaches behind the case's back plane = `pi_back − rail_t` |
| Stand | `pocket_gap` | derived (26) | Clear space between the case back and the pocket's front face = `pi_rear + 3` |
| | `leg_len` | derived (≈52.1 at 60°) | Ground distance, case bottom edge to pocket bottom edge = (pocket_h + pocket_gap) / sin(tilt) |
| | `bar_t` | 5 | Square section of legs and arms |
| | `ledge_h` | 6 | Height of the ledge across the bottom of each open end of the pocket |
| | `pocket_w / _l / _h` | derived | 102.6 × 68.8 × 19.1 (width × along the screen × thickness) |
| | `side_x` / `arm_x` | derived | Centre lines of the bars: in line with the case's side walls (60.85) / just outside the pocket (52.8) |

Overall size with these values: 126.7 wide × 97.5 deep × 78.7 tall, about 63 cm³ of plastic.

## Estimates and unknowns
Measure these on the real parts before printing; each is a parameter at the top of the model.
- `screen_t` — the screen module's thickness. Too small and the screen will not slide in.
- `pi_back` — how far the Pi sticks out behind the screen. Too small and the Pi hits the router pocket.
- `ribbon_side` — which side the ribbon is on.
- `eth_cover_len`, how far the cover is from the router's long edges (`door_lip_h` has to stay under it),
  and whether the cover really is on the display face — taken from photos.
- The UTR's measured size, and where exactly its sockets are across each end.
- The screen's real outline, its mounting-hole positions and thread (on the **back** of the screen, not at the
  corners), and the screen's own leg angle (`tilt_angle` is meant to match it; 60° is a placeholder).
- What is on the back of the screen's PCB within `rail_w` of its left, right and bottom edges — the rails sit
  there.

## Open points
- **The Pi may touch the table.** At 60°, anything that reaches the full 23 mm behind the case has to be at
  least 13.3 mm up the case from its bottom edge to clear the ground — about 10.7 mm above the bottom edge of
  the screen's PCB. A Pi 4 centred on that PCB has its lower edge 10.95 mm up, so the lower rear corner of its
  USB/Ethernet block comes out roughly level with the table. If the real numbers confirm this, the fix is a
  steeper `tilt_angle` or raising the case; not done here.
- **Nothing secures the screen** beyond gravity and the rails: no bosses, no screws (mounting-hole positions
  unknown). It lifts straight out of the top.
- **The screen's top edge is exposed** — the price of the open top. The front wall and side walls stand
  2.8 mm proud of it.
- **Stability.** A firm push on the screen may tip the stand backward around the pocket's base (a hand
  estimate, not tested); check on the real print, and consider longer legs.
- **Thin joints.** Each arm meets the pocket through a 1 mm overlap with the 2.8 mm ends of the pocket's
  front wall and back lip.
- **The vent holes** now open into the screen's slot at the edge of the PCB, so they vent little.
- **Print orientation and supports** have not been considered.
- A wider `bar_t` lowers the arms across the pocket's ends (they scale from the top corner); at `bar_t = 8`
  they come down to about where the upper Ethernet jack is, and the plug checks fail.

## Verification (2026-11-12, OpenSCAD 2021.01 command line)
Method: intersect the parts with a test volume, render, and export — an empty result means clear. Three
control cases that must *not* be empty were run alongside and came out non-empty.

| Check | Result |
|---|---|
| Screen slot, extended upward as the screen's path in — vs. all four parts | clear |
| Pi room (opening between the rails × 23 mm deep), extended upward as its path in — vs. all four parts | clear |
| Router cavity, extended upward as its path in — vs. all four parts | clear |
| Plugs leaving both ends of the router (40 mm outward, from the ledge top to 56 mm up the end) | clear |
| Ethernet cover lifting 10 mm, and the Ethernet plugs beyond that end | clear — with the cover taken as wide as 6.5–58.5 mm across the router, i.e. wider than the photos suggest |
| Door lips: the pocket before vs. after adding them | nothing removed; two strips added (38.8 × 6 × 2.8 mm each) |
| Nothing below the ground plane | clear |
| Arms overlap the case and the pocket; legs overlap the case and the pocket | yes, on both sides |
| Whole model | one connected, simple (manifold) solid |

Repeated with `ribbon_side = 1`, with `eth_end` on the ribbon side, at 50° and 75° tilt, and with
`pi_back = 32, screen_t = 6`.

The same checks on the **previous** version (2026-11-03) found what the review was asked to look for: the arms
ended inside the open cavity where the screen sits and touched the case only by a sliver (under 0.1 mm thick,
0.75 mm³ in total); the legs poked
into the cavity's bottom corner; and the pocket, 10 mm behind the case, sat where a Pi on the screen's back
would be, with the arms and legs crossing that space too.

Not verified: anything that depends on the estimates above, fit of the real parts, and printability.

## Change log
- 2026-11-03 — Split into three coloured parts (case / legs / router pocket); removed mockups and `RENDER_MODE`.
- 2026-11-03 — Removed the corner screw bosses and `corner_*` parameters (mounting holes are on the screen's back).
- 2026-11-03 — Restructured: case thinned to ½"; router pocket moved to the ground; new gray `legs()`
  (ground bars case→pocket); old white legs became `arms()` (pocket top→case back). `tilt_angle` is now the
  screen angle from horizontal; the whole-assembly tilt was dropped.
- 2026-11-03 — Model file renamed to `cad/Pi_Router_enclosure.scad`.
- 2026-11-03 — Router pocket's two side (left/right) ends opened, so the router can slide in or out
  sideways as well as from the top; the white arms moved from the pocket's outer edge to just outside it
  (`arm_x`, 1 mm into the pocket's side face). Legs unchanged. Not rendered.
- 2026-11-03 — White arms now start at the pocket's back face (flush with the back lip) instead of its
  front wall, so they run the full thickness of the pocket beside it on the way to the case.
- 2026-11-03 — Router pocket re-tilted to match the screen (parallel to the case, behind it, bottom edge on the
  ground) instead of lying flat. `leg_len` is now derived; `arm_attach` removed.
- 2026-11-12 — **Screen slides in from the top.** Case rewritten in a corner-origin frame: top wall removed,
  back rails added (`rail_w`, `rail_t`), back open between the rails for the Pi. New `screen_t`, `slot_clr`.
  The cable pass-through hole was dropped (it had no wall left to cut).
- 2026-11-12 — **Ribbon shield:** the side wall on `ribbon_side` is carried back `pi_rear` behind the case.
- 2026-11-12 — **Supports reviewed and moved clear of the screen and Pi.** `pocket_gap` is now derived from
  the new `pi_back` (10 → 26). Legs became a ground frame in line with the case's side walls (`side_x`) with a
  cross bar at the pocket; arms keep their run beside the pocket, then step out to `side_x` before going to
  the case. Both subtract `keep_clear()`. `bar_x` removed.
- 2026-11-12 — **Router pocket ends:** a `ledge_h` ledge across the bottom of each open end; back lip cut away
  on `eth_end` for the router's flip-up Ethernet cover (`eth_cover_len`).
- 2026-11-12 — First render and interference checks (see "Verification").
- 2026-11-12 — **Door lips:** the opening in the back lip for the Ethernet cover no longer runs from the
  pocket's floor to its top; a `door_lip_h` (6 mm) strip of lip is left below and above it to keep the router
  in place. Material added only. The top strip also ties the lip's top band to the pocket's end, so it is no
  longer a cantilever.
- 2026-11-12 — **Square-ended arms:** `arms()` is now drawn inside `case_frame()`, so every bar's end faces are
  parallel to the case's back and the pocket's faces instead of being cut at a slant by world-axis cubes.
  Same three bars, same end points; the sloping bar now ends flat on the case back. All checks pass on six
  variants.
