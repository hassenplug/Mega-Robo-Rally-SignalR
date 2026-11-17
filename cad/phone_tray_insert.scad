// ====================================================================
// Phone tray insert - holds N phones on edge, separated by thin fins
// Plan and reasoning: documents/PHONE_TRAY_INSERT_PLAN.md
// Dimensions in millimeters (mm). OpenSCAD 2021.01.
//
// Frame (x across the tray's width = the direction the phones stack,
//        y along the tray's length = the phone's length, z up):
//   x = 0 .. row_w     the row of slots; slot i holds phone i
//   y = 0 .. len_in    the phones' length (bottom end at y = 0)
//   z = 0              top of the foam pad, i.e. the face the phones stand on
//
// One printable part, a "ladder":
//   two end walls   - one beyond each phone end, full width. They stand on the tray
//                     floor (bar_drop deep, past the foam), stop the phones sliding
//                     along the tray and carry the fins
//   fins            - thin plates inside each end wall, one between each pair of
//                     phones, each reaching fin_reach (< 1/2") along the phone from its end.
//                     With outer_fins, one more on the outside of each outermost phone, so
//                     every phone has a fin on both faces. An outer fin and a rail share the
//                     same x position, so the outer fins cost no width beyond the thicker of the two
//   two side rails  - low strips outside the row that join the end walls (use_rails)
//
// Values marked ESTIMATE have NOT been measured on the real tray or phone. Replace
// them with caliper readings; the asserts below refuse to render a design that does
// not fit.
//
// part = "assembly"  frame + ghost phones, for looking at   (default)
//        "print"     frame only, flat on the bed, as it prints
//        "phones"    ghost phones only
// For an 8-phone insert: set n_phones = 8 and read the notes under "WIDTH" below.
// ====================================================================

part = "print";

// --- 1. WHAT TO HOLD ---
n_phones   = 6;          // 6 now; 8 for the separate 8-phone insert

// --- 2. PHONE (TCL A502DL) ---
phone_t    = 9.4;        // ESTIMATE: thickness (retailer spec page; caliper it, incl. any camera bump
                         // in the fin zones - the bump makes the real thickness there larger)
phone_len  = 148.1;      // ESTIMATE: length, bottom end to top end
phone_w    = 69.6;       // ESTIMATE: width; the phone stands on this edge, so this is its height

// --- 3. TRAY (measure with calipers) ---
rib_tip_span = 85.0;     // ESTIMATE: wall-rib tip to opposite rib tip across the tray's width.
                         // = 6 x phone_t + 28.6 mm (the user's "1 1/8 inch left after 6 phones")
floor_len    = 180.0;    // ESTIMATE: inside length of the tray at the floor
foam_t       = 0;     // MEASURED by the user: 1/2 inch foam pad under the phones

// --- 4. FIT ---
clear      = 0.4;        // Clearance per slot (total, not per side). Below ~0.3 mm phones may bind
len_clear  = 0.6;        // Extra length between the two end walls beyond the phone's length
side_clear = 0.0;        // Extra gap kept between the insert and each rib tip

// --- 5. FINS ---
fin_t      = 1.2;        // Fin thickness. 1.2 = 3 perimeters at a 0.4 mm nozzle. For 8 phones try 0.8
fin_reach  = 10.0;       // How far a fin reaches along the phone from its end. Must be < 12.7 (1/2")
fin_h      = 49.0;       // Fin height above the foam (about 3/4")
lead_h     = 3.0;        // Height of the chamfer on the fin top that guides a phone in
outer_fins = true;       // Also put a fin outside each outermost phone (N + 1 fins per comb)

// --- 6. FRAME ---
end_t      = 3.0;        // Thickness of each end wall
end_h      = 49.0;       // Height of the end walls above the foam
use_rails  = true;       // false = two loose combs (no rails; then place them by hand)
rail_t     = 1.2;        // Rail thickness. Rails take width from the row, so for 8 phones use 0.8
rail_top   = 4.0;        // Top of the rails above the foam (keep below the ribs' lower edges)
bar_drop   = foam_t;     // How far the end walls and rails reach down, past the foam, to the floor.
                         // 0 = everything stands on the foam. Rails at full depth need the foam to
                         // be narrower than the row plus rails; if it is not, set bar_drop = 0

// ====================================================================
// DERIVED
// ====================================================================
eps      = 0.01;
slot_w   = phone_t + clear;
pitch    = slot_w + fin_t;
row_w    = n_phones * slot_w + (n_phones - 1) * fin_t;
len_in   = phone_len + len_clear;
side_w   = max(outer_fins ? fin_t : 0, use_rails ? rail_t : 0);   // width taken outside the row, per side
total_w  = row_w + 2 * side_w;
total_len = len_in + 2 * end_t;
spare_w  = rib_tip_span - 2 * side_clear - total_w;

// WIDTH: the row must fit between the rib tips. With the defaults, 6 phones leave about
// 17 mm to spare. 8 phones with phone_t = 9.4 need roughly fin_t = 0.8, clear = 0.3,
// rail_t = 0.8 (or use_rails = false) - see section 4 of the plan.
assert(spare_w >= 0,
       str("Does not fit across the tray: need ", total_w, " mm, have ",
           rib_tip_span - 2 * side_clear, " mm (short by ", -spare_w, " mm)"));
assert(total_len <= floor_len,
       str("Does not fit along the tray: need ", total_len, " mm, floor is ", floor_len, " mm"));
assert(fin_reach < 12.7, "fin_reach must be under 1/2 inch (12.7 mm)");
assert(fin_reach * 2 < len_in, "fins from both ends would overlap");
assert(fin_t >= 0.8, "fin_t under 0.8 mm is too thin to print reliably");
assert(clear >= 0.3, "clear under 0.3 mm: phones may bind");
assert(total_w <= 256 && total_len <= 256, "does not fit the P2S bed (256 x 256 mm)");

echo(str("n_phones=", n_phones, "  row_w=", row_w, "  total_w=", total_w,
         "  spare across=", spare_w, " mm  total_len=", total_len,
         "  spare along=", floor_len - total_len, " mm"));

// ====================================================================
// PARTS
// ====================================================================

// One fin: x = 0..fin_t, y = 0..fin_reach, z = 0..fin_h, chamfered top.
module fin() {
    lx = fin_t * 0.25;
    translate([0, fin_reach, 0])
        rotate([90, 0, 0])
            linear_extrude(fin_reach)
                polygon([[0, 0], [fin_t, 0], [fin_t, fin_h - lead_h],
                         [fin_t - lx, fin_h], [lx, fin_h], [0, fin_h - lead_h]]);
}

// The comb at the phones' bottom end (y = 0): end wall at y < 0, fins at y > 0.
module comb_bottom() {
    // end wall, full width including the rails' width
    translate([-side_w, -end_t, -bar_drop])
        cube([row_w + 2 * side_w, end_t, bar_drop + end_h]);
    // fins, sunk eps into the wall so the union is one solid
    // fin j sits after slot j: j = -1 is the outer fin before slot 0, j = n-1 the one after the last slot
    for (j = [(outer_fins ? -1 : 0) : (outer_fins ? n_phones - 1 : n_phones - 2)])
        translate([(j + 1) * slot_w + j * fin_t, -eps, 0])
            fin();
}

module comb_top() {
    translate([0, len_in, 0])
        mirror([0, 1, 0])
            comb_bottom();
}

module rails() {
    z0 = -bar_drop;
    h  = bar_drop + rail_top;
    for (x = [-rail_t, row_w])
        translate([x, -end_t, z0])
            cube([rail_t, total_len, h]);
}

module frame() {
    comb_bottom();
    comb_top();
    if (use_rails) rails();
}

module ghost_phones() {
    color("dimgray", 0.5)
        for (i = [0 : n_phones - 1])
            translate([i * pitch + clear / 2, 0, 0])
                cube([phone_t, phone_len, phone_w]);
}

// ====================================================================
// OUTPUT
// ====================================================================
if (part == "print") {
    // flat on the bed: lowest face (the end walls' and rails' floor face) at z = 0
    translate([0, 0, bar_drop]) frame();
} else if (part == "phones") {
    ghost_phones();
} else {
    color("orange") frame();
    ghost_phones();
}
