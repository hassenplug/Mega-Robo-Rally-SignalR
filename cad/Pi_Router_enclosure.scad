// ====================================================================
// Parametric Enclosure for Hosyond 5" Display + UniFi Travel Router
// Screen case and router pocket, both leaning back at the same angle.
// Dimensions in millimeters (mm)
//
// World frame: Z is up, the viewer is at -Y looking toward +Y.
// The case's bottom-back corner is the origin; the router pocket is parallel
// to the case, just behind it, with its bottom edge on the ground.
//
// Four parts, each one solid item:
//   case          (blue)  - thin screen shell, bezel opening, vents
//   legs          (gray)  - two bars on the ground: case base -> router pocket base
//   arms          (white) - two struts: near the top of the router pocket -> back of case
//   router_pocket (black) - sleeve that holds the UniFi Travel Router, same tilt as the case
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

// --- 2. UNIFI TRAVEL ROUTER DIMENSIONS (UTR) ---
// Nominal: 95.95 x 65 x 12.5 mm -> Added +1.0mm print tolerance
router_w      = 97.00;
router_h      = 66.00;
router_t      = 13.50;

// --- 3. ENCLOSURE PARAMETERS ---
tilt_angle    = 60;      // Angle of the screen from horizontal (90 = upright)
wall_t        = 2.80;    // Shell wall thickness
case_depth    = 12.70;   // Case thickness, 1/2 inch

case_w        = pcb_w + (wall_t * 2);
case_h        = pcb_h + (wall_t * 2);

// --- 4. ROUTER POCKET, LEGS AND ARMS ---
bar_t         = 5;       // Square section of the legs and arms
pocket_gap    = 10;      // Clear space between the case back and the pocket's front face

// Router pocket, in its own tilted frame: x = width, y = along the screen (up),
// z = thickness (toward the screen). Same tilt as the case.
pocket_w      = router_w + 2 * wall_t;
pocket_l      = router_h + wall_t;          // bottom end wall + router; top end is open for insertion
pocket_h      = router_t + 2 * wall_t;      // front wall + router + back lip

// Ground distance from the case's bottom edge to the pocket's bottom-back edge,
// chosen so the two parallel parts are pocket_gap apart.
leg_len       = (pocket_h + pocket_gap) / sin(tilt_angle);
bar_x         = pocket_w/2 - bar_t/2;       // legs and arms sit at the pocket's outer sides

// World (Y, Z) of a point given in the pocket's tilted frame
function pocket_pt(y, z) = [leg_len + y * cos(tilt_angle) - z * sin(tilt_angle),
                            y * sin(tilt_angle) + z * cos(tilt_angle)];

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

// --- CASE: shell, tilted back, bottom-back corner at the origin ---
module case() {
    translate([0,
               (case_h/2) * cos(tilt_angle) - (case_depth/2) * sin(tilt_angle),
               (case_h/2) * sin(tilt_angle) + (case_depth/2) * cos(tilt_angle)])
        rotate([tilt_angle, 0, 0])
            difference() {
                // Outer Box
                cube([case_w, case_h, case_depth], center = true);

                // Inner Cavity (open at the back)
                translate([0, 0, -wall_t])
                    cube([pcb_w + 0.5, pcb_h + 0.5, case_depth], center = true);

                // Front Screen Cutout (Bezel)
                translate([0, 0, case_depth/2 - wall_t/2])
                    cube([active_w + 1.0, active_h + 1.0, wall_t * 2], center = true);

                // Side Ventilation Slots
                for (y = [-15, 0, 15]) {
                    translate([case_w/2 - wall_t, y, 0])
                        rotate([0, 90, 0])
                            cylinder(h = wall_t * 3, d = 4, center = true);
                    translate([-case_w/2 + wall_t, y, 0])
                        rotate([0, 90, 0])
                            cylinder(h = wall_t * 3, d = 4, center = true);
                }

                // Cable Pass-through Hole to Router
                translate([0, -case_h/4, -case_depth/2])
                    cube([25, 12, wall_t * 4], center = true);
            }
    // No screw bosses: the screen's mounting holes are on its back side.
    // Add bosses here once their positions are known.
}

// --- ROUTER POCKET: parallel to the case, behind it, bottom edge on the ground ---
// Solid wall faces the case; the lip with the window faces away. The router
// slides in from the top end and rests on the bottom end wall.
module router_pocket() {
    translate([0, leg_len, 0])
        rotate([tilt_angle, 0, 0])
            translate([-pocket_w/2, 0, 0])
                difference() {
                    cube([pocket_w, pocket_l, pocket_h]);

                    // Router cavity, open at the top end
                    translate([(pocket_w - router_w)/2, wall_t, wall_t])
                        cube([router_w, router_h + 1.0, router_t]);

                    // Window in the back lip so the router's screen/ports show
                    translate([(pocket_w - (router_w - 20))/2, wall_t + router_h/4, -1])
                        cube([router_w - 20, router_h/2, wall_t + 2]);
                }
}

// --- LEGS: ground-level bars from the case base to the router pocket base ---
module legs() {
    for (x = [-bar_x, bar_x])
        bar([x, -bar_t/2, bar_t/2],
            [x, leg_len - 1, bar_t/2]);      // ends 1 mm inside the pocket's bottom end wall
}

// --- ARMS: struts from near the top of the router pocket to the back of the case ---
module arms() {
    // Position along the screen (u, measured up the case back from its bottom edge),
    // kept inside the case's height
    yt = min(pocket_l - 8, case_h - bar_t - leg_len * cos(tilt_angle));
    u  = leg_len * cos(tilt_angle) + yt;
    p  = pocket_pt(yt, pocket_h - wall_t + bar_t/2);   // inside the pocket's front wall
    for (x = [-bar_x, bar_x])
        bar([x, p[0], p[1]],
            [x,
             u * cos(tilt_angle) - 2 * sin(tilt_angle),   // 2 mm into the case wall
             u * sin(tilt_angle) + 2 * cos(tilt_angle)]);
}
