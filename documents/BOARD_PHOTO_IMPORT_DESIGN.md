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
