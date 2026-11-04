// ====================================================================
// Parametric Enclosure for Hosyond 5" Display + Pi + UniFi Travel Router
// Features angled desktop feet (~20 deg view) and rear router pocket
// Dimensions in millimeters (mm)
// ====================================================================

$fn = 32; // Cylinder resolution

// --- TOGGLES FOR PREVIEW / PRINTING ---
RENDER_MODE = "CASE_ONLY"; // Options: "ALL", "CASE_ONLY", "DISPLAY_MOCKUP", "ROUTER_MOCKUP"

// --- 1. DISPLAY MODULE DIMENSIONS ---
pcb_w         = 121.10;
pcb_h         = 77.90;
pcb_t         = 1.60;
glass_w       = 110.00;
glass_h       = 66.80;
glass_t       = 1.80;
active_w      = 108.00;
active_h      = 64.80;

corner_dist_x = 113.10;
corner_dist_y = 69.90;
corner_hole_d = 2.75;

// --- 2. UNIFI TRAVEL ROUTER DIMENSIONS (UTR) ---
// Nominal: 95.95 x 65 x 12.5 mm -> Added +1.0mm print tolerance
router_w      = 97.00; 
router_h      = 66.00; 
router_t      = 13.50; 

// --- 3. ENCLOSURE PARAMETERS ---
tilt_angle    = 20;    // Viewing tilt angle in degrees
wall_t        = 2.80;  // Shell wall thickness
case_depth    = 38.00; // Overall internal depth (Pi + standoff space)

case_w        = pcb_w + (wall_t * 2);
case_h        = pcb_h + (wall_t * 2);

// --- MAIN ASSEMBLY ---
module main_assembly() {
    if (RENDER_MODE == "ALL" || RENDER_MODE == "CASE_ONLY") {
        angled_enclosure();
    }
    
    if (RENDER_MODE == "ALL" || RENDER_MODE == "DISPLAY_MOCKUP") {
        // Place display mock inside case frame
        translate([0, 0, case_depth - pcb_t/2])
            display_mockup();
    }
    
    if (RENDER_MODE == "ALL" || RENDER_MODE == "ROUTER_MOCKUP") {
        // Place router mockup in back pocket
        translate([0, -case_h/2 + router_h/2 + wall_t, -router_t/2 - wall_t])
            router_mockup();
    }
}

// --- ANGLED DESK ENCLOSURE WITH ROUTER POCKET ---
module angled_enclosure() {
    rotate([tilt_angle, 0, 0]) {
        union() {
            // Main Display Shell
            difference() {
                // Outer Box
                cube([case_w, case_h, case_depth], center = true);

                // Inner Cavity
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

            // Internal Screws Bosses / Standoffs for Screen
            for (x = [-corner_dist_x/2, corner_dist_x/2]) {
                for (y = [-corner_dist_y/2, corner_dist_y/2]) {
                    translate([x, y, case_depth/2 - 6]) {
                        difference() {
                            cylinder(h = 6, d = 6, center = false);
                            cylinder(h = 8, d = 2.2, center = false); // Pilot hole for self-tapping M2.5
                        }
                    }
                }
            }

            // --- REAR UNIFI TRAVEL ROUTER SLEEVE / POCKET ---
            translate([0, -case_h/2 + router_h/2 + wall_t, -case_depth/2 - router_t/2]) {
                difference() {
                    // Outer Pocket Block
                    cube([router_w + (wall_t * 2), router_h + wall_t, router_t + wall_t], center = true);
                    
                    // Pocket Inner Cavity (Top open for insertion)
                    translate([0, wall_t/2, wall_t/2])
                        cube([router_w, router_h + 1.0, router_t], center = true);

                    // Top Insertion Slot Cutout
                    translate([0, router_h/2, wall_t/2])
                        cube([router_w, wall_t * 3, router_t + 1.0], center = true);

                    // Cutout for UniFi Screen / Ports visibility
                    translate([0, -router_h/4, -router_t/2])
                        cube([router_w - 20, router_h/2, wall_t * 3], center = true);
                }
            }

            // --- ANGLED BASE LEGS / WEDGE FEET ---
            rotate([-tilt_angle, 0, 0]) {
                translate([0, 0, -case_depth/2 - 5]) {
                    // Right Leg Wedge
                    translate([case_w/2 - 8, 0, 0])
                        angleg_leg_foot();
                    // Left Leg Wedge
                    translate([-case_w/2 + 8, 0, 0])
                        angleg_leg_foot();
                }
            }
        }
    }
}

// Helper Foot Wedge Module
module angleg_leg_foot() {
    rotate([0, 90, 0])
        cylinder(h = 12, d = 16, center = true);
}

// --- DISPLAY MOCKUP ---
module display_mockup() {
    color([0.2, 0.5, 0.2, 0.8])
        cube([pcb_w, pcb_h, pcb_t], center = true);
    translate([0, 0, (pcb_t + glass_t)/2])
        color([0.1, 0.1, 0.1, 0.9])
            cube([glass_w, glass_h, glass_t], center = true);
}

// --- UNIFI TRAVEL ROUTER MOCKUP ---
module router_mockup() {
    color([0.9, 0.9, 0.95, 0.9])
        cube([router_w - 1, router_h - 1, router_t - 1], center = true);
}

// Render Model
main_assembly();