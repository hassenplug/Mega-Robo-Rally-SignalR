using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MRR.Config.BoardPhotoImport
{
    /// <summary>
    /// What the importer decided for one cell. <see cref="GuessTypeId"/>/<see cref="GuessParameter"/>
    /// are always the best match, even when the cell was set to Blank for scoring too low,
    /// so the GM can see what it nearly was.
    /// </summary>
    public record ImportedCell(
        int X, int Y, int SquareType, int Rotation, float Score, bool NeedsReview,
        int GuessTypeId, int GuessParameter, string? Reason);

    /// <summary>
    /// Photo → BoardElementCollection: rectify, slice, match each cell, then give each cell the
    /// actions of the same tile type on the BoardID 0 template, so a matched tile comes with
    /// its walls, belt moves, flag number and so on.
    /// </summary>
    public class BoardPhotoImportService
    {
        /// <summary>Pixels per cell in the rectified image; larger than TileFeatures.Size so resizing only shrinks.</summary>
        public const int CellPx = 64;

        /// <summary>
        /// Cells whose best match scores below this become Blank and are marked for review.
        /// Not tuned: it needs the real-photo check in BOARD_PHOTO_IMPORT_DESIGN.md's build
        /// order (step 1), which has not been done.
        /// </summary>
        public const float DefaultThreshold = 0.5f;

        private readonly TileMatcher _matcher;

        public BoardPhotoImportService(TileTemplateCatalog catalog)
        {
            _matcher = new TileMatcher(catalog);
        }

        /// <summary>Corners are top-left, top-right, bottom-right, bottom-left (see PerspectiveRectifier.Rectify).</summary>
        public (BoardElementCollection Board, List<ImportedCell> Cells) Import(
            Image<Rgb24> photo, IReadOnlyList<PointF> corners, int cols, int rows,
            MatchMode mode, float threshold, BoardElementCollection template)
        {
            using var rectified = PerspectiveRectifier.Rectify(photo, corners, cols, rows, CellPx);
            var cells = PerspectiveRectifier.SliceCells(rectified, cols, rows);
            var matches = new TileMatch[rows, cols];
            try
            {
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                        matches[r, c] = _matcher.Match(cells[r, c], mode);
            }
            finally
            {
                foreach (var cell in cells) cell.Dispose();
            }
            return BuildBoard(matches, threshold, template);
        }

        /// <summary>
        /// Assembles the board from per-cell matches. Separate from <see cref="Import"/> so it
        /// can be tested without a photo.
        /// </summary>
        public static (BoardElementCollection Board, List<ImportedCell> Cells) BuildBoard(
            TileMatch[,] matches, float threshold, BoardElementCollection template)
        {
            int rows = matches.GetLength(0), cols = matches.GetLength(1);
            var board = new BoardElementCollection(cols, rows);
            var report = new List<ImportedCell>(rows * cols);

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var match = matches[r, c];
                    var guess = match.Template;
                    string? reason = null;
                    int typeId = guess.TypeId;
                    int parameter = guess.Parameter;

                    if (match.Score < threshold)
                    {
                        reason = $"Best match {guess.FileName} scored {match.Score:0.00}, below {threshold:0.00}; set to Blank.";
                        typeId = (int)SquareType.Blank;
                        parameter = 0;
                    }

                    var templateSquare = template.GetSquare((SquareType)typeId);
                    BoardActionsCollection actions;
                    Direction rotation;
                    if (templateSquare == null)
                    {
                        // A tile image with no template row (element34 has no SquareType at
                        // all). Importing it with no actions would make a belt that doesn't
                        // move, so say so rather than letting it look right.
                        reason ??= $"Matched {guess.FileName}, but BoardID 0 has no type {typeId} square, so it has no actions.";
                        actions = new BoardActionsCollection();
                        rotation = typeId == (int)SquareType.Blank ? Direction.None : guess.Rotation;
                    }
                    else
                    {
                        actions = new BoardActionsCollection(templateSquare.ActionList);
                        // Rotation None on the template means the game ignores which way the
                        // tile points (gears, pits, flags), however it was laid down.
                        rotation = templateSquare.Rotation == Direction.None ? Direction.None : guess.Rotation;
                        if (parameter > 0 && !SetNumber(actions, typeId, parameter))
                            reason ??= $"Matched {guess.FileName}, but the template's type {typeId} square has no action to carry number {parameter}.";
                    }

                    // SetSquare sets Rotation and then ActionList, which re-points belt moves
                    // and walls (ActionIncludesRotation) to the new rotation.
                    board.SetSquare(c, r, (SquareType)typeId, rotation, actions);
                    report.Add(new ImportedCell(c, r, typeId, (int)rotation, match.Score, reason != null,
                        guess.TypeId, guess.Parameter, reason));
                }

            board.TotalFlags = board.CalcTotalFlags();
            return (board, report);
        }

        /// <summary>
        /// Writes the number in the image file name (flag 3, start 5, respawn B) onto the action
        /// that carries it. The template has one square per type, numbered 1.
        /// </summary>
        private static bool SetNumber(BoardActionsCollection actions, int typeId, int parameter)
        {
            SquareAction? numbered = (SquareType)typeId switch
            {
                SquareType.Flag => SquareAction.Flag,
                SquareType.StartSquare => SquareAction.PlayerStart,
                SquareType.RebootToken => SquareAction.Respawn,
                _ => null,
            };
            var action = numbered == null ? null : actions.FirstOrDefault(a => a.SquareAction == numbered);
            if (action == null) return false;
            action.Parameter = parameter;
            return true;
        }
    }
}
