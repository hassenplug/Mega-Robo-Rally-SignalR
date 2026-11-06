# Photo-to-Board Import — Design

## Context

Physical board tiles are 3D-printed reproductions of the artwork already in
`MRR.Config/wwwroot/images/element{typeId}[-{parameter}]_top.png` (35 images). The GM
can lay these tiles out on a table in any board layout, then wants to photograph the
layout and have the game import it as a `BoardElementCollection` — instead of manually
placing every square one at a time in the existing board editor.

The catalog of possible tiles is closed and already known (it's the same image set the
board editor's palette uses), so this is a **template-matching** problem, not general
scene understanding: for each cell in the photographed grid, find which of the ~40
known tile images (at which of 4 rotations) it most resembles.

Decisions driving this design:
- Match against the known tile catalog (not generic visual heuristics).
- Photo is uploaded from the GM's phone through the web UI.
- Semi-automatic: the import pre-fills a board, then the GM reviews/fixes it in the
  **existing** board editor (`MRR.Config/wwwroot/board-editor.html`) before saving —
  this reuses that UI entirely rather than building new review tooling.

Everything here lives in `MRR.Config` (the authoring-side project), alongside
`BoardData.cs` and `board-editor.html`, which already own load/save of boards.

## Known risk to flag, not solve upfront

The reference images are flat-color PNG artwork; the physical tiles are 3D prints,
which may render the same design as embossed/engraved relief in a single filament
color rather than matching flat colors under arbitrary lighting. If so, raw color
comparison will match poorly and the classifier needs to lean on grayscale
structure/edges instead of color. Build the matcher swappable (color vs.
edge-based distance) and validate against one real sample photo early — don't assume
color matching works before checking.

## Design

### 1. Reference catalog (build once, cache in memory)

New `MRR.Config/BoardPhotoImport/TileTemplateCatalog.cs`:
- At startup (or first use), enumerate `wwwroot/images/element*_top.png`, parse each
  filename into `(typeId, parameter)` — same parsing `board-editor.html` already does
  via `imageSrcForType` (`board-editor.html:398`).
- Load each with `SixLabors.ImageSharp`, downscale to a small fixed size (e.g. 48×48),
  grayscale it, and pre-rotate to all 4 orientations (0/90/180/270) — no per-rotation
  image files exist; the editor achieves rotation via CSS `transform: rotate()`
  (`board-editor.html:432`, `rotationToDeg`), so do the same in code.
- Store each `(typeId, parameter, rotation)` variant's normalized pixel buffer for
  comparison.
- Pull each type's default `ActionList` (walls/flag/start/belt-direction actions) from
  the existing `BoardID = 0` template data — the same source
  `GET /api/boardeditor/types` already reads (`MRR.Config/Program.cs:29-64`, joining
  `BoardItems`/`BoardItemActions`). Reuse that query rather than duplicating it, so a
  matched tile comes with its correct actions for free, including any numbered
  variant (flag number, belt direction) already baked into which `(typeId, parameter)`
  matched.

### 2. Perspective correction (new — nothing existing does a full homography)

`GridAlignmentAgent.cs` only measures line tilt/offset for robot alignment — it does
not rectify a photo. New `MRR.Config/BoardPhotoImport/PerspectiveRectifier.cs`:
- Input: the uploaded image plus 4 corner points (pixel coords) marking the board's
  outer corners, and the board's `cols`/`rows`.
- Compute a projective (homography) transform mapping those 4 corners to a rectified
  `cols*cellPx × rows*cellPx` output canvas; for each output pixel, inverse-map to a
  source pixel and bilinear-sample (via `Image<Rgb24>` pixel access — ImageSharp has no
  built-in homography, so the 3×3 matrix solve + per-pixel sampling has to be written
  here, this is the one genuinely new piece of math in this feature).
- Slice the rectified image into a `cols × rows` grid of equal cells.

Corner points come from the GM tapping/dragging 4 markers over the photo in the
browser before upload (see UI below) — far more reliable than trying to auto-detect
board edges from a photo of many small separate tiles.

### 3. Per-cell classification

`MRR.Config/BoardPhotoImport/TileMatcher.cs`:
- For each sliced cell: downscale/grayscale the same way as the catalog, compare
  against every `(typeId, parameter, rotation)` template (normalized cross-correlation
  or SSD on the grayscale/edge buffer), take the best match and its confidence score.
- Below a confidence threshold, fall back to `Blank` (typeId 0) and mark the cell
  low-confidence so the UI can highlight it for review.

### 4. Assembly + save

`MRR.Config/BoardPhotoImport/BoardPhotoImportService.cs`:
- Build a `BoardElementCollection` (cols, rows, name) with one `BoardElement` per cell:
  `BoardCol`/`BoardRow`, matched `SquareType`, matched `Rotation`, and the `ActionList`
  copied from the catalog entry (per §1).
- Call the existing `BoardData.BoardSaveToDB(destinationID, collection)`
  (`MRR.Config/BoardData.cs:164`) — the same write path the manual editor and `.srx`
  import already use. No new persistence code needed.

### 5. New endpoint

In `MRR.Config/Program.cs`, alongside the existing `/api/boardeditor` group:
```
POST /api/boardeditor/{id}/import-photo
```
- Multipart form: image file (`IFormFile` — first file upload in the repo, so this
  introduces that pattern), `cols`, `rows`, and the 4 corner points.
- Runs steps 2–4 above, saves to board `{id}` (GM picks/creates the target board id the
  same way `.srx` import already does), returns success + per-cell confidence summary.

### 6. UI

New `MRR.Config/wwwroot/board-import.html`, linked from `board-editor.html`:
- `<input type="file" accept="image/*" capture="environment">` for the phone camera/
  photo picker.
- Canvas overlay with 4 draggable corner markers over the uploaded photo, plus
  cols/rows inputs and a target-board picker (existing or new).
- On submit, POSTs to the new endpoint; on success, redirects to
  `board-editor.html?board={id}` so the GM reviews and fixes any misreads using the
  editor's existing palette/rotation/save UI — no new review UI needed.

### 7. Project setup

Add `SixLabors.ImageSharp` (same version as `MRR/MRR.csproj:15`, `3.1.12`) to
`MRR.Config/MRR.Config.csproj` — it isn't referenced there yet.

## Verification

- Unit-test `PerspectiveRectifier` against a synthetic rectified image warped with a
  known homography, confirming round-trip pixel accuracy.
- Take one real phone photo of a small physical tile layout (e.g. 3×3 known tiles),
  run it through the pipeline, and manually confirm the resulting board in
  `board-editor.html` matches the physical layout — this is the real test of the
  color-vs-structure matching risk above, and should happen before investing further
  in matcher tuning.
- Confirm `dotnet build` succeeds for `MRR.Config` after adding ImageSharp.

## Implementation status (2026-10-26, updated 2026-11-05)

**Steps 2–7 built. Step 1 (the real-photo check) was done 2026-11-05** on two photos — see
"Real-photo check" below. It works on the printed tiles at about 70–75% of cells with the
catalog images as templates, not good enough to skip the review step; the remaining failures
are listed there.

- Code: `MRR.Config/BoardPhotoImport/` (`TileFeatures`, `TileTemplateCatalog`,
  `PerspectiveRectifier`, `GridRefiner`, `TileMatcher`, `BoardPhotoImportService`), endpoint
  `POST /api/boardeditor/{id}/import-photo` in `MRR.Config/Program.cs`, page
  `MRR.Config/wwwroot/board-import.html` (linked from the editor's top bar).
- Matching modes `Semantic` (default since 2026-11-05), `Color`, `Grayscale` and `Edges`,
  chosen per import on the page. All use normalized cross-correlation of 48×48 vectors
  (`Semantic`: color-class maps pooled to 12×12). Threshold `DefaultThreshold = 0.5` is still
  a guess and was not tuned on the real photos.
- Before matching, `BoardPhotoImportService.Import` snaps the corners onto the tile seams
  (`GridRefiner`, on by default, `refineGrid: false` to skip) and finds the black/gray cutoff for
  the photo (`TileFeatures.AdaptiveBlackMax`).
- The template supplies actions, as planned. Numbered tiles (flag, start, respawn) take
  their number from the matched image's file name. Tiles with no BoardID 0 row are imported
  with no actions and flagged for review. `element34` has no `SquareType` at all.
- Walls (`element200`) are not matched (`TileTemplateCatalog.ExcludedFiles`, 2026-11-03): the
  artwork is only a bar along one edge, about 4% of the tile, too easy to confuse with Blank.
  The GM adds walls in the editor after import. Cells are trimmed `TileFeatures.Inset` (8%) per
  side for every mode, which keeps the seams and corner-placement error out of the comparison.
  On the real boards walls are separate red bars standing on the seams, not part of any tile.
- Review hand-off: the page runs `/validate` and shows the result. "Review in editor" opens
  `board-editor.html?board={id}` with the flagged squares outlined in orange (passed in
  `sessionStorage`). A square's mark clears when it is edited.
- Corner order is TL, TR, BR, BL; the page numbers the markers 1–4. Its guide grid is
  approximate (linear, not perspective-correct).
- Upload limits: 12 MB, 40 MP, image content type, 1–40 cols/rows. The page shrinks
  photos to 2000 px before upload. There is no login on the config host, so this endpoint is
  as open as the existing PUT.
- Tests: `MRR.Tests/BoardPhotoImportTests.cs`: homography recovery, rectify round-trip,
  every catalog tile matches itself at every rotation in every mode, and a synthetic
  3×3 warped "photo" that imports correctly end to end (with `refineGrid: false`: the
  synthetic image has no seams, and the refiner moved its corners). All synthetic; the real-photo
  numbers below come from a throwaway script and are not in the test suite.
- **ImageSharp 3.1.12 has published high-severity advisories** (`dotnet list package
  --vulnerable`), and this endpoint decodes uploaded files with it. Upgrade both projects
  together once the fixed version is confirmed.

## Real-photo check (2026-11-05)

First test on real photos of the printed tiles, scored by hand-labelling the tile category
(blank, flag n, start n, belt, express belt, rotator, hazard; rotation not scored) of two
fully visible boards, 6×6 and 9×9. Cells under robots and the orange start-arrow tile (no
catalog image) are not scored. Top-1 correct, catalog images as templates, grid refined,
current code (8% edge trim, walls excluded from the catalog):

| Mode | 6×6 (35 cells) | 9×9 (74 cells) |
|---|---|---|
| Grayscale (old default) | 34% | 35% |
| Edges | 23% | 45% |
| Color | 37% | 47% |
| **Semantic** (new default) | **69%** | **74%** |

Before the edge trim and the wall exclusion were merged in, Semantic scored 77% / 78%, so the
numbers move by several points with small changes: two photos, 109 cells, one labeller.

- **Why the catalog fails raw.** The catalog art is the same designs in a different palette
  (dark-green arrows, mid-gray surface, green start ring, dark-red flag ring vs. bright
  arrows, light gray, black rings), so color/grayscale correlation fails. `Semantic` reduces
  pixels to color classes first (black, gray, red, orange, yellow, green, blue, purple). The
  black/gray cutoff is found per photo because the printed black is dark gray (V 0.25–0.5 in
  these photos); a fixed cutoff called most belts gray and scored 31%.
- **`GridRefiner`.** Snaps hand-placed corners onto the tile seams by sliding and stretching a
  regular grid over a seam-darkness profile. Both test boards were well aligned after it. The
  corners must already be within about a third of a cell. It keeps the corners if the best grid
  is not clearly better than the same grid shifted half a cell.
- **Still failing:** numbers (flag n, start n: the digit is a few pixels at 48×48, so flags match
  as the wrong number or as Blank); start gears (catalog ring is green, printed ring black);
  a hazard tile matching `element34`; anything with no catalog image (the orange start-arrow
  tile) or under a robot.
- **Large boards:** scored separately against a saved board, see the next section.
- **Next:** build templates from photos of the real tiles (one of each type), and read digits
  in a second pass at higher resolution. A separate detector for the red wall bars.

## Large-board check: board 6 "HomeyTough46" (2026-11-05)

A 12×18 board (216 cells) with full ground truth: it is saved in the DB as BoardID 6, and
exported as a file so the comparison can be rerun offline without the DB.

### Files

| File | What it is |
|---|---|
| `install/Boards/HomeyTough46.srx` | Board 6 exported from the DB (`BoardData.BoardLoadFromDB` → `XmlSerializer`, the format `.srx` import reads). Read back with `BoardData.LoadBoardFile`: all 216 cells match the DB on type, rotation and action count |
| `install/Boards/photos/HomeyTough46-1.jpg` | Photo 1, the whole board from above, no robots |
| `install/Boards/photos/HomeyTough46-2.jpg` | Photo 2, the same board, a second shot from a slightly different position |

The photos are 1500×2000 and upright. The phone originals are 3000×4000 with EXIF
orientation 6 (stored sideways). The copies here have the rotation applied and are shrunk to
2000 px, which is what `board-import.html` sends (it redraws through a canvas, which applies
EXIF, and shrinks to 2000 px). ImageSharp's `Image.Load` does **not** apply EXIF orientation,
so an original loaded straight into the importer comes out sideways.

Corners, hand-placed on the outer corners of the tile grid (inside the white border), in the
order the importer takes them (TL, TR, BR, BL), in pixels of the files above:

| Photo | TL | TR | BR | BL |
|---|---|---|---|---|
| 1 | (258, 125) | (1295, 146) | (1454, 1892) | (8, 1844) |
| 2 | (250, 78) | (1277, 110) | (1407, 1800) | (35, 1775) |

`GridRefiner` moves them to: photo 1 (264,128) (1288,149) (1442,1873) (20,1826); photo 2
(255,82) (1270,114) (1397,1789) (43,1765).

### The physical board differs from the DB in one cell

Start 6 is at (6,0) on the table (top row, right after starts 4 and 5), but at (1,16) in the
DB and so in the `.srx`. (1,16) is an empty tile in the photos. Score against the photos:
treat (6,0) as start 6 and (1,16) as blank. Otherwise the DB matches the photos cell for cell.
Walls (type 200, at (5,4) and (4,5)) are red bars on the seams of plain tiles in the photos,
so score them as Blank: the importer does not match walls.

### How it was scored

A throwaway console project referencing `MRR.Config` ran the same steps as
`BoardPhotoImportService.Import` (`GridRefiner.Refine` → `PerspectiveRectifier.Rectify` →
`TileFeatures.AdaptiveBlackMax` → `SliceCells` → `TileMatcher.Match`) in every mode, with both
the hand-placed and the refined corners, and wrote each cell's best match. A script compared
that with the ground truth. Neither the harness nor the script is in the repo.

- **Category:** blank / normal belt (10–16) / express belt (20–26) / gear / hazard (pit,
  block) / flag / start / reboot.
- **Exact:** same `SquareType`, and the same number for flags, starts and reboot tokens.
- **Belt rotation:** same type and same rotation, out of 126 belt cells.

### Results

| Corners | Mode | Photo 1 category | Photo 1 exact | Photo 2 category | Photo 2 exact |
|---|---|---|---|---|---|
| Hand-placed | Grayscale | 35% | 23% | 39% | 25% |
| Hand-placed | Edges | 50% | 32% | 55% | 38% |
| Hand-placed | Color | 43% | 30% | 48% | 34% |
| Hand-placed | **Semantic** | 73% | 59% | 74% | 60% |
| Refined | Grayscale | 57% | 52% | 51% | 44% |
| Refined | Edges | 80% | 66% | 72% | 60% |
| Refined | Color | 68% | 62% | 72% | 65% |
| Refined | **Semantic** (what the importer does) | **81%** | **75%** | **85%** | **78%** |

Belt rotation, Semantic: 46 and 47 of 126 with hand-placed corners; 80 and 84 with refined
corners.

Per category, Semantic, refined corners (photo 1 / photo 2):

| Category | Cells | Correct |
|---|---|---|
| Blank | 52 | 52 / 52 |
| Gear | 15 | 15 / 15 |
| Hazard | 8 | 8 / 8 |
| Flag (category) | 6 | 6 / 6. Number right only 1–2 of 6 |
| Normal belt | 59 | 55 / 55 |
| Express belt | 67 | 39 / 48. Most read as normal belt, the rest as Blank |
| Start | 6 | 0 / 0, all read as Blank |
| Reboot token | 3 | 0 / 0, all read as Blank |

### What it showed

- **Glare breaks the single black cutoff.** `AdaptiveBlackMax` gives one cutoff for the whole
  photo (0.52–0.53 here). Under the glare in the lower middle of the board (rows 11–13), only
  9–30% of a belt cell's pixels fall under it, against about 55% everywhere else, so those
  belts look like plain tiles with arrows on them. This is most of the express-belt misses. A
  cutoff found per region or per cell, from the brightness of the nearby tile surface, should
  fix it.
- **One homography does not fit the whole board.** With the hand-placed corners, the grid
  is on the seams at the corners but about 1/6 cell up and left of them in the middle of the
  board: lens distortion, or the board is not flat. `GridRefiner` fits the middle better but
  moves the bottom corners about 20 px (a quarter cell): the bottom row is cut (the flag 6
  tile is half out of its cell) and row 17 drops to 5 of 12. It wins on balance. Snapping
  each cell or region to its own nearby seams would fix both ends.
- **The express belt art does not match the print.** The catalog's express arrows are pale
  gray-blue, the printed ones saturated blue.
- **Start and reboot tiles do not look like the catalog.** The start tile is printed as a black
  gear ring with a green number (catalog: green ring). The reboot token is printed as a pale
  lavender outline that the classifier sees as gray (catalog: solid purple). Both always
  read as Blank.

## Status and build plan (added 2026-09-30)

**Status: design only, nothing built.** Input is confirmed as a photo of **physical tiles**
(not a flat scan), so the design above stands as written. Review notes from re-checking it
against the code:

- Line references above were updated (`imageSrcForType` :398, `rotationToDeg` :432). There are
  35 tile images, not ~40.
- **Template dependency.** §1 reads default actions from `BoardID = 0`. `MRR.Config/Program.cs`
  (comment above `POST /api/boardeditor/template/seed`) says that template can be empty on a
  fresh database. The importer must check for it and return a clear error telling the GM to
  seed it, rather than silently importing tiles with no walls/actions.
- **Board orientation.** The 4 corners must be defined in a fixed order (top-left first, then
  clockwise), and the UI must say so, or a correct photo imports rotated.
- **Wall-bearing tiles.** Check how walls are represented for each tile type (in the `ActionList`
  or in the image) before trusting the matcher with them; run the existing
  `GET /api/boardeditor/{id}/validate` on the result and show its errors.
- **Review aid.** Return a per-cell confidence list so `board-editor.html` can highlight
  low-confidence cells, not just pre-fill them.
- **Upload limits.** This is the first file upload in the repo: cap the size, accept only
  image types, and put the endpoint behind the same restriction as the other editor
  endpoints (the config host has no login of its own).

### Build order (each step has a check before the next starts)

1. **Feasibility spike (decides whether to continue).** Photograph a real 3×3 layout of known
   tiles. In a throwaway console project, crop by hand, grayscale, and match against the
   catalog. *Pass:* ≥ 8 of 9 cells correct. *Fail:* try edge-based matching; if that fails,
   stop and reconsider (e.g. a marker on each tile).
2. `TileTemplateCatalog` — loads the 35 images, makes the 4 rotations, joins `BoardID 0` actions.
   *Check:* every catalog tile matches itself at its own rotation with score ≈ perfect.
3. `PerspectiveRectifier` — homography solve and sampling. *Check:* round-trip unit test on a
   synthetic warped image.
4. `TileMatcher` + `BoardPhotoImportService` — classify, assemble, save through
   `BoardSaveToDB`. *Check:* the step-1 photo, end to end, yields the right board in the DB.
5. Endpoint `POST /api/boardeditor/{id}/import-photo` with limits and the template check.
   *Check:* bad file, huge file, missing template, and wrong corner count each return a clear 4xx.
6. `board-import.html` (camera input, 4 draggable corners, cols/rows, target board) and the
   hand-off to `board-editor.html?board={id}`. *Check:* done from a phone, over the game LAN.
7. Add `SixLabors.ImageSharp` 3.1.12 to `MRR.Config.csproj` (step 2 needs it) and confirm
   `dotnet build` is clean.

Not in scope: automatic board-edge detection, flat-image scans, and any change to the
`MRR/` game host (this is authoring-side only).
