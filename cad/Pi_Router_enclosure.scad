// ====================================================================
// Parametric Enclosure for Hosyond 5" Display + UniFi Travel Router
// Screen case and router pocket, both leaning back at the same angle.
// Dimensions in millimeters (mm)
//
// World frame: Z is up, the viewer is at -Y looking toward +Y.
// The case's bottom-back corner is the origin; the router pocket is parallel
// to the case, behind it, with its bottom edge on the ground.
//
// The screen slides into the case from the top, with the Pi mounted on its back.
// Values marked ESTIMATE have not been measured on the real parts.
//
// Four parts, each one solid item:
//   case          (blue)  - screen shell: bezel opening, vents, open top, back rails that
//                           hold the screen in, one side carried back over the Pi's ribbon
//   legs          (gray)  - ground frame: two bars from the case base to a cross bar at the
//                           router pocket's base
//   arms          (white) - two struts: top corners of the router pocket -> back of case, square-ended
//   router_pocket (yellow) - sleeve that holds the UniFi Travel Router, same tilt as the case;
//                            open at the top and, above a ledge, at both side ends
// ====================================================================

$fn = 32; // Cylinder resolution

// --- 1. DISPLAY MODULE DIMENSIONS ---
pcb_w         = 121.10;
pcb_h         = 77.90;
pcb_t         = 1.60;
glass_w       = 110.00;
glass_h       = 66.80;
glass_t       = 1.80;
active_w      = 108.00;
active_h      = 64.80;
screen_t      = 7.50;    // ESTIMATE: glass face to the back of the PCB, at the PCB's edges

// Pi mounted on the back of the screen
pi_back       = 25;      // ESTIMATE: back of the screen's PCB to the rear-most point of the Pi
                         // (standoffs + board + USB/Ethernet jacks, or the ribbon's loop)
ribbon_side   = -1;      // Side the screen-to-Pi ribbon is on, seen from the front: -1 left, 1 right

// --- 2. UNIFI TRAVEL ROUTER DIMENSIONS (UTR) ---
// Nominal: 95.95 x 65 x 12.5 mm -> Added +1.0mm print tolerance
router_w      = 97.00;
router_h      = 66.00;
router_t      = 13.50;
// The router lies on a long edge with its display facing the pocket's window. Its sockets are
// on its two short ends: USB-C power on one, two Ethernet jacks under a flip-up cover on the other.
eth_end       = -ribbon_side;  // End of the pocket the Ethernet jacks face, seen from the front:
                               // -1 left, 1 right (default: the side the Pi's own Ethernet jack is on)
eth_cover_len = 36;      // ESTIMATE: length of the flip-up cover, measured from the router's Ethernet end
door_lip_h    = 6;       // Strip of the pocket's back lip left below and above the opening for that
                         // cover. Keep it under the cover's distance from the router's long edges
                         // (about 10 mm, read off photos)

// --- 3. ENCLOSURE PARAMETERS ---
tilt_angle    = 60;      // Angle of the screen from horizontal (90 = upright)
wall_t        = 2.80;    // Shell wall thickness
case_depth    = 12.70;   // Case thickness, 1/2 inch

case_w        = pcb_w + (wall_t * 2);
case_h        = pcb_h + (wall_t * 2);

slot_clr      = 0.40;    // Front-to-back clearance for the screen in its slot
rail_w        = 3.50;    // How far the back rails reach in over the back of the screen's PCB
rail_t        = case_depth - wall_t - screen_t - slot_clr;   // Back rail thickness: what case_depth leaves
pi_rear       = pi_back - rail_t;                            // How far the Pi reaches behind the case back
assert(rail_t >= 1.2, "screen_t leaves less than 1.2 mm for the back rails; raise case_depth");

// --- 4. ROUTER POCKET, LEGS AND ARMS ---
bar_t         = 5;       // Square section of the legs and arms
pocket_gap    = pi_rear + 3;   // Clear space between the case back and the pocket's front face: the Pi plus 3 mm
ledge_h       = 6;       // Ledge across the bottom of each open end of the pocket; keeps the router in

// Router pocket, in its own tilted frame: x = width, y = along the screen (up),
// z = thickness (toward the screen). Same tilt as the case.
pocket_w      = router_w + 2 * wall_t;
pocket_l      = router_h + wall_t;          // bottom end wall + router; top end is open for insertion
pocket_h      = router_t + 2 * wall_t;      // front wall + router + back lip

// Ground distance from the case's bottom edge to the pocket's bottom-back edge,
// chosen so the two parallel parts are pocket_gap apart.
leg_len       = (pocket_h + pocket_gap) / sin(tilt_angle);
side_x        = case_w/2 - bar_t/2;         // legs and arm struts run in line with the case's side walls,
                                            // outside the back opening that the Pi uses
arm_x         = pocket_w/2 + bar_t/2 - 1;   // arms sit outside the pocket, 1 mm into its side face

// --- ASSEMBLY ---
color("blue")  case();
color("gray")  legs();
color("white") arms();
color("yellow") router_pocket();

// Straight square bar between two points (hull of two cubes)
module bar(p1, p2, t = bar_t) {
    hull() {
        translate(p1) cube(t, center = true);
        translate(p2) cube(t, center = true);
    }
}

// Case's tilted frame: x = width (centred), y = up the screen from the case's bottom edge,
// z = depth, from the case's back plane (0) to its front face (case_depth)
module case_frame() {
    rotate([tilt_angle, 0, 0])
        children();
}

// Router pocket's tilted frame: x = width (0 to pocket_w), y = along the screen (up),
// z = thickness (toward the screen)
module pocket_frame() {
    translate([0, leg_len, 0])
        rotate([tilt_angle, 0, 0])
            translate([-pocket_w/2, 0, 0])
                children();
}

// Space the screen takes, in the case's frame: it slides in from the top and sits between
// the front wall and the back rails
module screen_slot() {
    translate([-(pcb_w + 0.5)/2, wall_t - 0.25, rail_t])
        cube([pcb_w + 0.5, case_h, case_depth - wall_t - rail_t]);
}

// Room for the Pi on the screen's back, in the case's frame: the opening between the back
// rails and the space behind it, tall enough to cover the Pi's path in from the top
module pi_room() {
    translate([-pcb_w/2 + rail_w, wall_t + rail_w, -pi_rear])
        cube([pcb_w - 2 * rail_w, 3 * case_h, pi_rear + rail_t + 1]);
}

// Space the router takes, in the pocket's frame
module router_cavity() {
    translate([wall_t, wall_t, wall_t])
        cube([router_w, router_h + 1.0, router_t]);
}

// What the legs and arms are cut back from, so they cannot block the screen, the Pi or the router
module keep_clear() {
    case_frame() {
        screen_slot();
        pi_room();
    }
    pocket_frame()
        router_cavity();
}

// --- CASE: shell, tilted back, bottom-back corner at the origin ---
// The screen slides in from the top. It rests on the bottom wall and leans on the back rails;
// the Pi on its back comes down through the opening between the rails.
module case() {
    case_frame() {
        difference() {
            // Outer Box
            translate([-case_w/2, 0, 0])
                cube([case_w, case_h, case_depth]);

            // Screen slot, open at the top
            screen_slot();

            // Back opening between the rails, open at the top
            pi_room();

            // Front Screen Cutout (Bezel)
            translate([-(active_w + 1.0)/2, (case_h - active_h - 1.0)/2, case_depth - wall_t * 1.5])
                cube([active_w + 1.0, active_h + 1.0, wall_t * 2]);

            // Side Ventilation Slots
            for (y = [-15, 0, 15]) {
                translate([case_w/2 - wall_t, case_h/2 + y, case_depth/2])
                    rotate([0, 90, 0])
                        cylinder(h = wall_t * 3, d = 4, center = true);
                translate([-case_w/2 + wall_t, case_h/2 + y, case_depth/2])
                    rotate([0, 90, 0])
                        cylinder(h = wall_t * 3, d = 4, center = true);
            }
        }

        // Side wall carried back as far as the Pi reaches, to shield the screen-to-Pi ribbon.
        // Its lower rear corner is cut off along the ground. The other side stays open for
        // the Pi's USB and Ethernet plugs.
        translate([ribbon_side > 0 ? (pcb_w + 0.5)/2 : -case_w/2, 0, 0])
            rotate([90, 0, 90])
                linear_extrude((case_w - pcb_w - 0.5)/2)
                    polygon([[0, 0], [0, 1], [case_h, 1], [case_h, -pi_rear],
                             [pi_rear / tan(tilt_angle), -pi_rear]]);
    }
    // No screw bosses: the screen's mounting holes are on its back side.
    // Add bosses here once their positions are known.
}

// --- ROUTER POCKET: parallel to the case, behind it, bottom edge on the ground ---
// Solid wall faces the case; the lip with the window faces away. The router drops in from
// the top end, display toward the window, and rests on the bottom end wall between the two
// end ledges. Its plugs go in through the open ends.
module router_pocket() {
    pocket_frame()
        difference() {
            cube([pocket_w, pocket_l, pocket_h]);

            // Router cavity, open at the top end
            router_cavity();

            // Both side ends open above the ledge
            translate([-1, wall_t + ledge_h, wall_t])
                cube([pocket_w + 2, router_h + 1.0, router_t]);

            // Window in the back lip so the router's screen/ports show
            translate([(pocket_w - (router_w - 20))/2, wall_t + router_h/4, -1])
                cube([router_w - 20, router_h/2, wall_t + 2]);

            // Back lip cut away over the flip-up cover of the Ethernet jacks, so it can lift.
            // A strip of the lip stays below and above the opening to keep the router in place.
            translate([eth_end > 0 ? pocket_w - wall_t - eth_cover_len : -1, wall_t + door_lip_h, -1])
                cube([eth_cover_len + wall_t + 1, router_h - 2 * door_lip_h, wall_t + 2]);
        }
}

// --- LEGS: ground-level frame from the case base to the router pocket base ---
// Two side bars in line with the case's side walls, joined by a cross bar along the
// pocket's bottom edge (the pocket is narrower than the case).
module legs() {
    yc = leg_len - 1;                        // 1 mm inside the pocket's bottom end wall
    difference() {
        union() {
            for (x = [-side_x, side_x])
                bar([x, -bar_t/2, bar_t/2],
                    [x, yc, bar_t/2]);
            bar([-side_x, yc, bar_t/2],
                [side_x, yc, bar_t/2]);
        }
        keep_clear();
    }
}

// --- ARMS: struts from the top corners of the router pocket to the back of the case ---
// Drawn in the case's tilted frame, so every bar's end faces are square to the case and the
// pocket (parallel to the case's back face, not cut at a slant). Each arm runs beside the
// pocket's open end from its back face to its front face, steps out to the line of the case's
// side wall, then slopes up the screen to the top of the case's back. The sloping bar crosses
// the pocket's end at the top corner, above the router's sockets.
module arms() {
    gap = pocket_gap;
    ya  = leg_len * cos(tilt_angle) + pocket_l - bar_t;   // along the screen, near the pocket's top end
    zb  = -(pocket_h + gap) + bar_t/2;                   // flush with the pocket's back face
    zf  = -gap - bar_t/2;                                // flush with the pocket's front face
    u   = case_h - bar_t;                                // up the case back from its bottom edge
    difference() {
        case_frame()
            for (s = [-1, 1]) {
                bar([s * arm_x,  ya, zb], [s * arm_x,  ya, zf]);
                bar([s * arm_x,  ya, zf], [s * side_x, ya, zf]);
                bar([s * side_x, ya, zf], [s * side_x, u,  2]);   // 2 mm into the case wall
            }
        keep_clear();
    }
}
