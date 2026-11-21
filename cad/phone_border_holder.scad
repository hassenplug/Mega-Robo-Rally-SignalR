// ====================================================================
// Player phone holder that plugs into the slot in the TOP of the playing-field border
// One phone, landscape, tilted back so a seated player can read and touch it.
// Dimensions in millimeters (mm). OpenSCAD 2021.01.
// Related: cad/phone_tray_insert.scad (the storage tray insert) and
//          documents/PHONE_TRAY_INSERT_PLAN.md (same phone, same printer).
//
// REVISED 2026-11-17 (2nd): slot is in the TOP of the border (tab points down); nothing may go past
// the back (field-side) face of the tab; the holder stands on the table; prints on its back.
//
// World frame: x runs along the border; y runs across it, y = 0 at the slot's centre, the field
// on the -y side and the player on the +y side; z is up, z = 0 is the border's top face and
// z = -border_h is the table.
//
// One printable part:
//   tab      - a thin blade that drops down into the slot (2" long x 1/4" thick x 3/4" deep)
//   base     - a block that rests on the border's top face between the tab and the outer edge
//   skirt    - a thin plate down the border's OUTER face, standing on the table
//   link     - a strip along the table that joins the skirt's bottom to the cradle's lowest edge
//   cradle   - back plate leaning toward the field, a shelf the phone's long edge stands on, a small
//              front lip and an end post at each end of the phone
//   fill     - a solid block, as wide as the tab, filling the space above the tab (above the border's top
//              face) between the back plane and the cradle's back plate
//   ribs     - one small triangular rib on each side, standing against the outer edge of the back window
//              (at the cradle's ends if there are no windows), above the border's top face only
//              (there are no ribs below the top of the wall)
//
// The plate leans from the skirt: the steeper the tilt, the closer the phone sits to the wall. The
// plate's back face stays outside the border's outer face (checked by an assert), so the highest
// tilt that works is about 76 degrees. The file is set to 50 (the user's choice).
//
// RULE: no part of the holder is at y < -tab_t/2, the tab's back face. The cradle is pushed OUT
// until its leaning back plate meets that plane at its top, so nothing hangs over the field.
// That plane is also the print bed: the part prints lying on its back with the tab flat on the bed.
//
// PRINTING (part = "print"): the back plane is on the bed. The tab lies flat; the back plate is a
// shallow ramp rising from the bed (90 - tilt degrees from vertical). Everything below the border top (the skirt,
// and the plate and shelf where they hang down toward the table) floats up to about 20 mm above the
// bed: turn on slicer supports, "build plate only", for the plate's back face and the skirt's inner face.
// Cutting the support interface off those two faces is the only cleanup.
//
// MEASURED by the user: border 1 3/8" tall (first 1 7/16", corrected 2026-11-20); wall around the slot about 1/16"
// each side; slot 2" x 1/4" x 3/4".
// ASSUMED: the outer face is flat and vertical right down to the table; the slot is centred (the wall
// is 1/16" on both sides); the table under the holder is level with the table under the border.
// ESTIMATE: the phone's size (retailer spec page).
//
// part = "assembly"  holder + ghost phone + stand-ins for the border and the table (default)
//        "print"     holder only, on its back, as it prints
//        "check"     holder intersected with the phone, the border, the table and the area behind the
//                    tab's back plane; must come out EMPTY
// ====================================================================

part = "print";  // "assembly", "print", "check"

// --- 1. SLOT IN THE TOP OF THE BORDER ---
slot_w     = 38.1;       // 2"   : along the border
slot_t     = 6.35;       // 1/4" : across the border
slot_depth = 19.05;      // 3/4" : down into the border

// --- 2. BORDER (measured) ---
slot_to_outer = 1.5875;  // 1/16": wall between the slot and the border's outer face (same on the field side)
border_h      = 34.925;  // 1 3/8": border height = how far down the table is (was 1 7/16", 36.5125, until the
                         // user found the wall about 1/16" shorter on 2026-11-20)

// --- 3. PHONE (TCL A502DL, same estimates as phone_tray_insert.scad) ---
phone_len  = 148.1;      // ESTIMATE: length (lies along the border, landscape)
phone_w    = 69.6;       // ESTIMATE: width (up the cradle's back plate)
phone_t    = 9.4;        // ESTIMATE: thickness

// --- 4. FIT ---
tab_clear_w = 0.5;       // Tab is narrower than the slot by this (total)
tab_clear_t = 0.4;       // Tab is thinner than the slot by this (total)
tab_inset   = 1.0;       // Tab stops this far short of the slot's bottom
phone_clear = 0.5;       // Gap around the phone: thickness, and 2x this in length
face_gap    = 0.6;       // Gap between the skirt and the border's outer face
top_gap     = 0.5;       // Gap between the base and the border's top face (the holder rests on the table)

// --- 5. POSE ---
tilt        = 60;        // Screen angle from horizontal (90 = upright). Higher = the phone sits closer to the
                         // wall. About 76 is the most that keeps the plate outside the border (assert)
cradle_z    = -border_h; // Height of the cradle's lowest point (the shelf's back-bottom edge) above the
                         // border's top face. -border_h = standing on the table, the lowest it can go.
                         // 0 = level with the border's top. Moving it only moves the phone straight up or
                         // down; it does not change how far out the cradle sits

// --- 6. HOOK (base + skirt) ---
base_h      = 8.0;       // Height of the base block above the border's top face
skirt_t     = 3.0;       // Skirt thickness
hook_w      = slot_w - 0.5;  // Width (along the border) of the base and skirt
link_t      = skirt_t;   // Thickness of the strip along the table that joins the skirt's bottom to the cradle's bottom
link_w      = hook_w;    // Its width (along the border)

// --- 7. CRADLE ---
plate_t     = 3.0;       // Back plate thickness
shelf_t     = 3.0;       // Shelf thickness (under the phone's long edge)
front_lip_t = 2.5;       // Front lip thickness
front_lip_h = 7.0;       // Front lip height above the shelf
end_t       = 3.0;       // End post thickness
end_h       = 30.0;      // End post height above the shelf
back_window = true;      // Cut two windows in the back plate (saves plastic, frees the camera)
window_margin = 14.0;    // Solid plate left around the windows
                         // A solid rib, as wide as the tab, is left between the two windows (see mid_rib_w)
lip_notch_w = 125.0;      // Finger notch cut in the middle of the front lip (0 = none)

// --- 8. RIBS ---
gusset_t    = 2.4;       // Thickness of each side rib. It stands just outside the window (touching its outer edge,
                         // not over it), so it does not cover the window

// ====================================================================
// DERIVED
// ====================================================================
eps    = 0.01;
tab_w  = slot_w - tab_clear_w;
mid_rib_w = tab_w;                          // width of the solid rib between the two back windows
tab_t  = slot_t - tab_clear_t;
tab_len = slot_depth - tab_inset;

yo     = slot_t / 2 + slot_to_outer;        // border's outer face (and, mirrored, its field-side face)
sk_y0  = yo + face_gap;                     // skirt's inner face
sk_y1  = sk_y0 + skirt_t;                   // skirt's outer face
back_y = -tab_t / 2;                        // THE back plane: nothing goes below this y; it is the print bed

len_in = phone_len + 2 * phone_clear;       // inside length between the end posts
cav_t  = phone_t + phone_clear;             // inside depth of the cradle, plate to front lip
L      = len_in + 2 * end_t;                // overall length of the cradle
plate_h = phone_w;                          // back plate height (the phone's width)
win_out = len_in / 2 - window_margin;       // x of each back window's outer edge (the windows run from mid_rib_w/2 to this)
rib_x   = back_window ? win_out + gusset_t / 2   // side rib centre: against the window's outer edge
                      : L / 2 - gusset_t / 2;    // no windows: at the cradle's end

phi = 90 - tilt;                            // rotation of the cradle about X
sp  = sin(phi);
cp  = cos(phi);

// Cradle frame: x along the border; y = depth (plate back at y = -plate_t, plate front at 0);
// z = up the plate. The shelf's top face is at z = 0. Placed by rotate([phi,0,0]) + translate.
// The cradle is pushed out until the plate's top back corner is exactly on the back plane.
yc = back_y + plate_t * cp + plate_h * sp;
zc = plate_t * sp + shelf_t * cp + cradle_z;
function gy(yl, zl) = yl * cp - zl * sp + yc;           // local -> global y
function gz(yl, zl) = yl * sp + zl * cp + zc;           // local -> global z

front_end = cav_t + front_lip_t;            // local y of the shelf's front edge
yB = gy(-plate_t, -shelf_t);                // back-bottom corner of the shelf
zB = gz(-plate_t, -shelf_t);
yF = gy(front_end, -shelf_t);               // front-bottom corner of the shelf
zF = gz(front_end, -shelf_t);
yT = gy(-plate_t, plate_h);                 // top back corner of the plate (= back_y by construction)
zT = gz(-plate_t, plate_h);

assert(tilt > 30 && tilt < 85, "tilt should be between 30 and 85 degrees");
assert(tab_t > 1 && tab_w > 10, "tab is too small: check the slot figures");
y_plate_at_top = yT + zT * tan(phi);        // y of the plate's back face where it crosses the border's top face
assert(y_plate_at_top >= yo + 0.5, str("tilt is too steep: the plate's back face would be inside the border (y = ",
       y_plate_at_top, ", border face at ", yo, "). Lower tilt, or raise cradle_z"));
assert(yB > sk_y1 + 1, "tilt is too steep: the cradle would run into the skirt. Lower tilt");
assert(zB >= -border_h - 0.001, "cradle_z is below the table");
assert(front_lip_t < phone_t, "front lip should be thinner than the phone");
assert(L <= 256 && zT + border_h <= 256 && yF - back_y <= 256, "does not fit the P2S bed (256 x 256 mm)");

echo(str("cradle length=", L, "  shelf front is ", yF - yo, " mm out from the border's outer face",
         "  phone top is ", zT + border_h, " mm above the table",
         "  holder is ", yF - back_y, " mm front to back (the print height)"));

// ====================================================================
// PARTS
// ====================================================================

module tab() {
    translate([-tab_w / 2, back_y, -tab_len])
        cube([tab_w, tab_t, tab_len + top_gap + eps]);
}

// Block on the border's top face from the tab to the skirt, and the skirt down to the table
module hook() {
    translate([-hook_w / 2, back_y, top_gap])
        cube([hook_w, sk_y1 - back_y, base_h - top_gap]);
    translate([-hook_w / 2, sk_y0, -border_h])
        cube([hook_w, skirt_t, border_h + base_h]);
}

// The cradle, in its own frame (before rotating)
module cradle_local() {
    // back plate
    difference() {
        translate([-L / 2, -plate_t, -shelf_t])
            cube([L, plate_t, plate_h + shelf_t]);
        if (back_window)
            // two windows, one each side of the middle rib
            for (s = [-1, 1])
                translate([s > 0 ? mid_rib_w / 2 : -win_out, -plate_t - 1, window_margin])
                    cube([win_out - mid_rib_w / 2, plate_t + 2, plate_h - 2 * window_margin]);
    }
    // shelf, from the plate's back to the front lip
    translate([-L / 2, -plate_t, -shelf_t])
        cube([L, plate_t + front_end, shelf_t]);
    // front lip, with a finger notch
    difference() {
        translate([-L / 2, cav_t, -shelf_t])
            cube([L, front_lip_t, shelf_t + front_lip_h]);
        if (lip_notch_w > 0)
            translate([-lip_notch_w / 2, cav_t - 1, 0])
                cube([lip_notch_w, front_lip_t + 2, front_lip_h + 1]);
    }
    // end posts
    for (s = [-1, 1])
        translate([s > 0 ? len_in / 2 : -L / 2, -plate_t, -shelf_t])
            cube([end_t, plate_t + front_end, shelf_t + end_h]);
}

// Strip from the skirt's bottom along the table to the cradle's lowest edge (flat when the cradle stands on
// the table; sloped up to it if cradle_z is raised). It ends exactly at that edge (y = yB), inside the
// cradle's back plate, so it never sticks out below the shelf's underside.
module link() {
    hull() {
        translate([-link_w / 2, sk_y0, -border_h])
            cube([link_w, skirt_t, link_t]);
        translate([-link_w / 2, yB - 3, zB])
            cube([link_w, 3, link_t]);
    }
}

module cradle() {
    translate([0, yc, zc])
        rotate([phi, 0, 0])
            cradle_local();
}

// Side view (y, z) of a rib: a triangle above the border's top face, bounded by the back plane (left),
// the plate's back face (right) and the level of the border's top face (bottom). Nothing below the top.
function plate_y_at(z) = yT + (zT - z) * (yB - yT) / (zT - zB);   // plate's back face, at height z
function rib_pts() = [
    [back_y, top_gap],
    [back_y, zT],
    [plate_y_at(top_gap), top_gap]
];

// One rib, or the fill, is this triangle extruded along x
module rib_at(x, w) {
    translate([x - w / 2, 0, 0])
        rotate([90, 0, 90])
            linear_extrude(w)
                polygon(rib_pts());
}

// One rib on each side, against the outer edge of the back window, plus the fill over the tab
module gussets() {
    for (s = [-1, 1])
        rib_at(s * rib_x, gusset_t);
    rib_at(0, hook_w);
}

// Space the phone occupies (with clearance), used to trim the holder
module phone_space() {
    translate([0, yc, zc])
        rotate([phi, 0, 0])
            translate([-len_in / 2, 0, 0])
                cube([len_in, cav_t, plate_h + 20]);
}

module holder() {
    difference() {
        union() {
            tab();
            hook();
            link();
            cradle();
            gussets();
        }
        phone_space();
    }
}

// The real phone, no clearance, leaning on the plate: for the collision check
module phone_actual() {
    translate([0, yc, zc])
        rotate([phi, 0, 0])
            translate([-phone_len / 2, 0.01, 0.01])
                cube([phone_len, phone_t, phone_w]);
}

// The border: wall yo on both sides of the slot, border_h tall, with the slot cut in
module border_solid() {
    difference() {
        translate([-100, -yo, -border_h]) cube([200, 2 * yo, border_h]);
        translate([-slot_w / 2, -slot_t / 2, -slot_depth]) cube([slot_w, slot_t, slot_depth + 1]);
    }
}

module table_slab()   { translate([-200, -200, -border_h - 50]) cube([400, 400, 50 - 0.001]); }
module behind_tab()   { translate([-200, -400, -200]) cube([400, 400 + back_y - 0.001, 400]); }

// ====================================================================
// OUTPUT
// ====================================================================
if (part == "print") {
    // lying on its back: the back plane (y = back_y) becomes the bed (z = 0), up (z) becomes -y
    translate([0, 0, -back_y]) rotate([90, 0, 0]) holder();
} else if (part == "check") {
    union() {
        intersection() { holder(); phone_actual(); }
        intersection() { holder(); border_solid(); }
        intersection() { holder(); table_slab(); }
        intersection() { holder(); behind_tab(); }
    }
} else {
    color("orange") holder();
    color("dimgray", 0.5) phone_actual();
    color("saddlebrown", 0.35) border_solid();
    color("silver", 0.2) translate([-100, -60, -border_h - 2]) cube([200, 140, 2]);   // the table
}
