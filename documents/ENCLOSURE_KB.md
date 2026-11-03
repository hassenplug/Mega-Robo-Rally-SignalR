# Angled Enclosure — Knowledge Base

**Status (2026-11-03):** OpenSCAD model written, **never rendered, sliced, or printed** — OpenSCAD is not
installed on the dev machine, so the geometry below was worked out by hand and is unchecked. Treat the
"Observations" section as things to verify on first render.

## Purpose
A printable desk stand holding the Hosyond 5" DSI touch screen tilted for viewing, with the Ubiquiti UniFi
Travel Router (UTR) in a pocket on the ground behind it. Tracks the open task in
[install/todo.md](../install/todo.md) ("Design a printable case for the Pi, its screen, and the router").
Hardware background: [PROJECT_STATUS.md](../PROJECT_STATUS.md) §1.9, [install/NETWORK_SETUP.md](../install/NETWORK_SETUP.md).

## Files
| File | Notes |
|---|---|
| [cad/Pi_Router_enclosure.scad](../cad/Pi_Router_enclosure.scad) | The model (renamed 2026-11-03 from `angled_enclosure_with_unifi_router_dock.scad`). Edit this one |
| `cad/angled_enclosure_with_unifi_router_dock.scad`, `cad/g_angled_enclosure_with_unifi_router_dock.scad` | **Stale.** Both still on disk and tracked in git, holding the *original* single-assembly version (mockups, `RENDER_MODE`, 20° tilt). Safe to delete once confirmed |
| `cad/pi-case.scad` | Former empty placeholder named in todo.md; no longer exists. todo.md now points at the file above |

## Layout (revised 2026-11-03)
World frame: Z up, viewer at −Y. The case's bottom-back corner is the origin. No mockups are drawn.
The case and the router pocket lean back at the **same** `tilt_angle`, parallel, `pocket_gap` apart.

```
side view (viewer on the left)

              /  /
             /  /      case (blue) and pocket (black) are parallel
            /  /
           /--/        arms (white): pocket top -> case back
          /  /
         /  /
        /  /
   ____/__/_____       legs (gray): on the ground, case bottom -> pocket bottom
    case  pocket
```

| Module | Colour | What it is |
|---|---|---|
| `case()` | blue | Thin shell, `case_depth` = 12.7 mm (½"). Bezel opening, 3×2 side vent holes, cable hole. Open back. Rotated by `tilt_angle` about its bottom edge |
| `legs()` | gray | Two ground-level bars from the case's bottom edge to the pocket's bottom end. *(Colour not specified by the user; gray chosen)* |
| `arms()` | white | Two struts from near the top of the pocket's front wall to the back of the case, roughly perpendicular to both. These are what used to be called "legs" |
| `router_pocket()` | black | Sleeve parallel to the case, bottom edge on the ground, `leg_len` behind it. Solid wall faces the case; the back lip has a window showing the router's face. **Top end open**: the router slides in from the top and rests on the bottom end wall |

Colours show in OpenSCAD preview (F5) only. To print, export each part separately (comment out the
others at the bottom); the parts overlap where they join.

## Parameters (all mm)
| Group | Name | Value | Meaning |
|---|---|---|---|
| Display | `pcb_w × pcb_h × pcb_t` | 121.10 × 77.90 × 1.60 | Screen PCB |
| | `glass_w × glass_h × glass_t` | 110.00 × 66.80 × 1.80 | Glass (declared, unused) |
| | `active_w × active_h` | 108.00 × 64.80 | Visible area (bezel opening is +1.0 each way) |
| Router | `router_w × router_h × router_t` | 97.00 × 66.00 × 13.50 | UTR nominal 95.95 × 65 × 12.5 plus ~1 mm tolerance |
| Case | `tilt_angle` | 60° | Angle of the **screen from horizontal** (90 = upright). The original 20° value was the wrong convention for a desk stand |
| | `wall_t` | 2.80 | Wall thickness |
| | `case_depth` | 12.70 | Case thickness (½") |
| | `case_w / case_h` | pcb + 2·wall | Derived |
| Stand | `pocket_gap` | 10 | Clear space between the case back and the pocket's front face |
| | `leg_len` | derived (≈33.6 at 60°) | Ground distance, case bottom edge to pocket bottom edge = (pocket_h + pocket_gap) / sin(tilt) |
| | `bar_t` | 5 | Square section of legs and arms |
| | `pocket_w / _l / _h` | derived | 102.6 × 68.8 × 19.1 (width × along the screen × thickness) |

## Not recorded / still unknown
The screen's real outline, its mounting-hole positions and thread (they are on the **back** of the screen,
not at the corners — the old corner bosses were removed), the screen's own leg angle, and the UTR's measured
size. The Pi's location is no longer designed (see below).

## Observations to check on first render (from reading the code, not from a render)
- **No room for the Pi.** At ½" the case holds the screen only (~9.9 mm of cavity behind a 2.8 mm front wall).
  A Pi 4 behind a DSI screen needs more. It must go elsewhere or the case needs to be deeper in places —
  decide where it lives.
- **"½" deep at the base"** was read as the whole case being ½" thick. If a wedge that is ½" at the bottom and
  deeper toward the top was meant, `case()` needs changing.
- **Nothing secures the screen.** No bosses, and the back is open, so the screen can fall out the back until
  mounting-hole positions are known.
- **Stability.** The screen leans over the front legs and the pocket leans behind; check the whole thing
  doesn't tip, and consider a larger `pocket_gap`. Positions of the legs/arms change with `tilt_angle`.
- **Router orientation.** The pocket is parallel to the screen (as todo.md asked). The window is in the lip
  facing *away* from the screen, so the router's face shows from behind; the router slides in from the top
  and sits on the bottom end wall. Confirm the window/ports line up with the real UTR.
- **Arm placement.** Arms are struts perpendicular to the case, attached 8 mm below the pocket's top end;
  their position along the screen is clamped to stay inside the case height.
- The cable hole in the case back is moot while the back is open.

## Change log
- 2026-11-03 — Split into three coloured parts (case / legs / router pocket); removed mockups and `RENDER_MODE`.
- 2026-11-03 — Removed the corner screw bosses and `corner_*` parameters (mounting holes are on the screen's back).
- 2026-11-03 — Restructured: case thinned to ½"; router pocket moved to the ground; new gray `legs()`
  (ground bars case→pocket); old white legs became `arms()` (pocket top→case back). `tilt_angle` is now the
  screen angle from horizontal; the whole-assembly tilt was dropped.
- 2026-11-03 — Model file renamed to `cad/Pi_Router_enclosure.scad`.
- 2026-11-03 — Router pocket re-tilted to match the screen (parallel to the case, behind it, bottom edge on the
  ground) instead of lying flat. `leg_len` is now derived; `arm_attach` removed.
