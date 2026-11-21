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
//                     every phone has a fin on both faces
//   two rails       - 1" tall strips that join the end walls (use_rails). Each sits at a fin position,
//                     the first one in from each side (between the first and second phone, and between
//                     the last two), and runs the whole length in place of that fin, so a rail adds no width.
//                     Each has rail_cuts (3) cut-outs to save material
//
// The phone size and fit (section 2 and the first two lines of section 4) are the same as in
// cad/phone_border_holder.scad, where a real phone was reported to fit well.
// Values marked ESTIMATE have NOT been measured on the real tray. Replace
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
// Same values as cad/phone_border_holder.scad, whose cradle (phone_t + 0.5 deep, phone_len + 1.0 long)
// the user reports fits a real phone well. They are still the retailer page's figures, not calipered;
// what the holder shows is that a real phone fits that cradle, not how much smaller than it the phone is.
phone_t    = 9.4;        // thickness (a camera bump in the fin zones would make the real thickness larger)
phone_len  = 148.1;      // length, bottom end to top end
phone_w    = 69.6;       // width; the phone stands on this edge, so this is its height

// --- 3. TRAY (measure with calipers) ---
rib_tip_span = 85.0;     // ESTIMATE: wall-rib tip to opposite rib tip across the tray's width.
                         // = 6 x phone_t + 28.6 mm (the user's "1 1/8 inch left after 6 phones")
floor_len    = 180.0;    // ESTIMATE: inside length of the tray at the floor
foam_t       = 0;     // MEASURED by the user: 1/2 inch foam pad under the phones

// --- 4. FIT ---
clear      = 0.5;        // Clearance per slot (total, not per side). 0.5 = the border holder's fit, which works.
                         // Below ~0.3 mm phones may bind
len_clear  = 1.0;        // Extra length between the two end walls beyond the phone's length. 1.0 = the border holder's fit
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
use_rails  = true;       // false = two loose combs (no rails; then place them by hand). Needs n_phones >= 3
rail_t     = fin_t;      // Rail thickness. A rail takes a fin's place, so it can be no thicker than a fin
rail_top   = 25.4;       // Top of the rails above the foam: 1 inch
rail_cuts  = 3;          // Cut-outs in each rail, to save material (0 = solid rail)
rail_margin = 15.0;      // Solid rail kept at each end, measured from the end wall. Must be at least fin_reach,
                         // or the fin (which is taller than the rail) would fill the first cut-out
rail_post  = 10.0;       // Solid rail left between two cut-outs
rail_bar   = 5.0;        // Solid strip left along the bottom and the top of the rail, above and below the cut-outs
rail_chamfer = 7.0;      // The top corners of each cut-out are cut at 45 degrees by this much, so the top of the
                         // cut-out is a shorter bridge when printing (the rail stands upright on the bed)
bar_drop   = foam_t;     // How far the end walls and rails reach down, past the foam, to the floor.
                         // 0 = everything stands on the foam. The rails now run between phones, over the
                         // foam, so with foam_t > 0 they would have to pass through it: then set bar_drop = 0
                         // (everything on the foam) or cut a slot in the foam for each rail

// ====================================================================
// DERIVED
// ====================================================================
eps      = 0.01;
slot_w   = phone_t + clear;
pitch    = slot_w + fin_t;
row_w    = n_phones * slot_w + (n_phones - 1) * fin_t;
len_in   = phone_len + len_clear;
side_w   = outer_fins ? fin_t : 0;          // width taken outside the row, per side (the rails are inside the row)
total_w  = row_w + 2 * side_w;
total_len = len_in + 2 * end_t;
spare_w  = rib_tip_span - 2 * side_clear - total_w;
cut_len  = rail_cuts > 0                   // length of one rail cut-out: what is left between the two end
           ? (len_in - 2 * rail_margin - (rail_cuts - 1) * rail_post) / rail_cuts   // margins, minus the posts
           : 0;
cut_h    = rail_top - 2 * rail_bar;        // height of one rail cut-out

// WIDTH: the row must fit between the rib tips. With the defaults, 6 phones leave about
// 17 mm to spare. The rails sit at fin positions, so only the outer fins take width beyond the row.
// 8 phones with phone_t = 9.4 and the border holder's clear = 0.5 do NOT fit with outer fins
// (short by 1.4 mm at fin_t = 0.8). With outer_fins = false they fit with 0.2 mm to spare, or keep the
// outer fins with clear = 0.3. 0.2 mm is inside the error of the estimated rib_tip_span - see
// section 4 of the plan.
assert(!use_rails || n_phones >= 3, "the rails need at least 3 phones (two different inner fins)");
assert(rail_t <= fin_t, "a rail takes a fin's place, so rail_t cannot exceed fin_t");
assert(rail_cuts == 0 || (rail_margin >= fin_reach && cut_len >= 10 && cut_h >= 5
                          && rail_chamfer > 0 && rail_chamfer <= cut_h && 2 * rail_chamfer < cut_len),
       str("rail cut-outs do not fit: each would be ", cut_len, " x ", cut_h,
           " mm. Needs rail_margin >= fin_reach, a length and height of at least 10 and 5 mm, ",
           "and 0 < rail_chamfer <= the cut-out's height and under half its length"));
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
    // end wall, full width including the outer fins' width
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

// One cut-out through a rail, in the rail's own frame (x = its thickness, y = along it, z = up). The
// bottom corners are square; the top corners are cut at 45 degrees
module rail_cut(y0, z0) {
    c = rail_chamfer;
    translate([-1, y0, z0])
        rotate([90, 0, 90])
            linear_extrude(rail_t + 2)
                polygon([[0, 0], [cut_len, 0], [cut_len, cut_h - c], [cut_len - c, cut_h],
                         [c, cut_h], [0, cut_h - c]]);
}

// Each rail is at the position of the first inner fin on its side (fin j = 0, and fin j = n - 2), spanning
// both end walls. It is rail_top (1") tall above the foam, with rail_cuts cut-outs between the solid ends.
module rails() {
    z0 = -bar_drop;
    h  = bar_drop + rail_top;
    for (j = [0, n_phones - 2])
        translate([(j + 1) * slot_w + j * fin_t, -end_t, z0])
            difference() {
                cube([rail_t, total_len, h]);
                // rail frame: y = 0 is the end wall's outer face, so the phones' end is at y = end_t
                for (i = [0 : 1 : rail_cuts - 1])   // explicit step: rail_cuts = 0 gives no cut-outs
                    rail_cut(end_t + rail_margin + i * (cut_len + rail_post), bar_drop + rail_bar);
            }
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
