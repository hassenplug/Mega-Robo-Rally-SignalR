using MRR.Config.BoardPhotoImport;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MRR.Tests;

/// <summary>
/// Board photo import (BOARD_PHOTO_IMPORT_DESIGN.md). Everything here is synthetic: photos are
/// made by warping the tile artwork itself. That proves the geometry and the plumbing, not that
/// a real phone photo of 3D-printed tiles matches -- that needs the design's step-1 check.
/// </summary>
public class BoardPhotoImportTests
{
    private static readonly string ImageDirectory = FindImageDirectory();
    private static readonly TileTemplateCatalog Catalog = new(ImageDirectory);

    private static string FindImageDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "MRR.Config", "wwwroot", "images");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("MRR.Config/wwwroot/images not found above the test output directory.");
    }

    // A fixed perspective transform, flat board -> photo, written out by hand so the test does
    // not depend on PerspectiveRectifier.SolveHomography to make its input.
    private static readonly double[] FlatToPhoto = [2.1, 0.25, 90, -0.12, 1.9, 70, 0.0004, 0.0003, 1];

    private static PointF Map(double[] m, double x, double y)
    {
        double w = m[6] * x + m[7] * y + m[8];
        return new PointF((float)((m[0] * x + m[1] * y + m[2]) / w), (float)((m[3] * x + m[4] * y + m[5]) / w));
    }

    private static double[] Invert(double[] m)
    {
        double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
        double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        return
        [
            (e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det,
            (f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det,
            (d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det,
        ];
    }

    /// <summary>Warps a flat image into a "photo" and returns the photo plus where its corners landed (TL, TR, BR, BL).</summary>
    private static (Image<Rgb24> Photo, PointF[] Corners) MakePhoto(Image<Rgb24> flat)
    {
        var inverse = Invert(FlatToPhoto);
        var photo = new Image<Rgb24>(1000, 900, new Rgb24(40, 40, 40));
        for (int y = 0; y < photo.Height; y++)
            for (int x = 0; x < photo.Width; x++)
            {
                var src = Map(inverse, x + 0.5, y + 0.5);
                int sx = (int)Math.Floor(src.X), sy = (int)Math.Floor(src.Y);
                if (sx >= 0 && sy >= 0 && sx < flat.Width && sy < flat.Height) photo[x, y] = flat[sx, sy];
            }
        PointF[] corners =
        [
            Map(FlatToPhoto, 0, 0), Map(FlatToPhoto, flat.Width, 0),
            Map(FlatToPhoto, flat.Width, flat.Height), Map(FlatToPhoto, 0, flat.Height),
        ];
        return (photo, corners);
    }

    [Fact]
    public void SolveHomography_RecoversAKnownTransform()
    {
        PointF[] from = [new(0, 0), new(256, 0), new(256, 192), new(0, 192)];
        var to = from.Select(p => Map(FlatToPhoto, p.X, p.Y)).ToArray();

        var h = PerspectiveRectifier.SolveHomography(from, to);

        for (int k = 0; k < 9; k++)
            Assert.Equal(FlatToPhoto[k], h[k], Math.Abs(FlatToPhoto[k]) * 1e-3 + 1e-6);
    }

    [Fact]
    public void Rectify_UndoesAPerspectiveWarp()
    {
        const int cols = 4, rows = 3, cell = 64;
        // Smooth pattern, so bilinear sampling error stays small and any misplacement shows.
        using var flat = new Image<Rgb24>(cols * cell, rows * cell);
        for (int y = 0; y < flat.Height; y++)
            for (int x = 0; x < flat.Width; x++)
                flat[x, y] = new Rgb24(
                    (byte)(128 + 100 * Math.Sin(x / 9.0)),
                    (byte)(128 + 100 * Math.Cos(y / 7.0)),
                    (byte)(128 + 100 * Math.Sin((x + y) / 13.0)));

        var (photo, corners) = MakePhoto(flat);
        using (photo)
        using (var rectified = PerspectiveRectifier.Rectify(photo, corners, cols, rows, cell))
        {
            Assert.Equal(flat.Width, rectified.Width);
            Assert.Equal(flat.Height, rectified.Height);

            double totalError = 0;
            int count = 0;
            for (int y = 2; y < flat.Height - 2; y++)
                for (int x = 2; x < flat.Width - 2; x++)
                {
                    Rgb24 a = flat[x, y], b = rectified[x, y];
                    totalError += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                    count += 3;
                }
            Assert.True(totalError / count < 3, $"Mean channel error {totalError / count:0.00} is too high.");
        }
    }

    [Theory]
    [InlineData(MatchMode.Color)]
    [InlineData(MatchMode.Grayscale)]
    [InlineData(MatchMode.Edges)]
    public void EveryCatalogTile_MatchesItselfAtEveryRotation(MatchMode mode)
    {
        var matcher = new TileMatcher(Catalog);
        var failures = new List<string>();
        foreach (var path in Directory.EnumerateFiles(ImageDirectory, "element*_top.png"))
        {
            Assert.True(TileTemplateCatalog.TryParseFileName(Path.GetFileName(path), out int typeId, out int parameter));
            using var image = Image.Load<Rgb24>(path);
            foreach (var rotation in new[] { Direction.Up, Direction.Right, Direction.Down, Direction.Left })
            {
                using var rotated = image.Clone(ctx => ctx.Rotate((RotateMode)(((int)rotation - 1) * 90)));
                var match = matcher.Match(rotated, mode);
                if (match.Template.TypeId != typeId || match.Template.Parameter != parameter || match.Score < 0.999f)
                    failures.Add($"{Path.GetFileName(path)} {rotation}: got {match.Template.FileName} {match.Template.Rotation} ({match.Score:0.000})");
            }
        }
        Assert.Empty(failures);
    }

    private static BoardElementCollection Template()
    {
        var template = new BoardElementCollection(8, 1);
        template.SetSquare(0, 0, SquareType.Blank, Direction.None, []);
        template.SetSquare(1, 0, SquareType.NormalBelt, Direction.Up, [new BoardAction(SquareAction.Move, 1, 3, 31)]);
        template.SetSquare(2, 0, SquareType.Flag, Direction.None, [new BoardAction(SquareAction.Flag, 1, 10, 31)]);
        template.SetSquare(3, 0, SquareType.StartSquare, Direction.Up, [new BoardAction(SquareAction.PlayerStart, 1, 1, 31)]);
        template.SetSquare(4, 0, SquareType.GearCW, Direction.None, [new BoardAction(SquareAction.Rotate, 1, 4, 31)]);
        template.SetSquare(5, 0, SquareType.Walls, Direction.Up, [new BoardAction(SquareAction.BlockDirection, 1, 0, 31)]);
        template.SetSquare(6, 0, SquareType.Pit, Direction.None, [new BoardAction(SquareAction.Damage, 100, 0, 0)]);
        template.SetSquare(7, 0, SquareType.FastTurnCW, Direction.Up,
            [new BoardAction(SquareAction.Move, 1, 1, 31), new BoardAction(SquareAction.Rotate, 1, 2, 31)]);
        // No Water square: the import must flag the water tile rather than give it no actions silently.
        return template;
    }

    [Fact]
    public void Import_ReadsASyntheticPhotoOfKnownTiles()
    {
        // (file, rotation) laid out row by row, 3x3.
        (string File, Direction Rotation)[] layout =
        [
            ("element0_top.png", Direction.Up),  ("element10_top.png", Direction.Right), ("element100-3_top.png", Direction.Up),
            ("element110-2_top.png", Direction.Down), ("element31_top.png", Direction.Up), ("element200_top.png", Direction.Left),
            ("element21_top.png", Direction.Up), ("element40_top.png", Direction.Up), ("element55_top.png", Direction.Up),
        ];
        const int cell = 100;
        using var flat = new Image<Rgb24>(3 * cell, 3 * cell);
        for (int i = 0; i < layout.Length; i++)
        {
            using var tile = Image.Load<Rgb24>(Path.Combine(ImageDirectory, layout[i].File));
            tile.Mutate(ctx => ctx.Resize(cell, cell).Rotate((RotateMode)(((int)layout[i].Rotation - 1) * 90)));
            int col = i % 3, row = i / 3;
            flat.Mutate(ctx => ctx.DrawImage(tile, new Point(col * cell, row * cell), 1f));
        }

        var (photo, corners) = MakePhoto(flat);
        using (photo)
        {
            var (board, cells) = new BoardPhotoImportService(Catalog).Import(
                photo, corners, 3, 3, MatchMode.Grayscale, BoardPhotoImportService.DefaultThreshold, Template());

            SquareType TypeAt(int c, int r) => board.GetSquare(c, r)!.Type;
            BoardAction ActionAt(int c, int r, SquareAction sa) => board.GetSquare(c, r)!.ActionList.Single(a => a.SquareAction == sa);

            Assert.Equal(SquareType.Blank, TypeAt(0, 0));
            Assert.Equal(SquareType.NormalBelt, TypeAt(1, 0));
            Assert.Equal((int)Direction.Right, ActionAt(1, 0, SquareAction.Move).Parameter);
            Assert.Equal(3, ActionAt(2, 0, SquareAction.Flag).Parameter);
            Assert.Equal(Direction.None, board.GetSquare(2, 0)!.Rotation);
            Assert.Equal(Direction.Down, board.GetSquare(0, 1)!.Rotation);
            Assert.Equal(2, ActionAt(0, 1, SquareAction.PlayerStart).Parameter);
            Assert.Equal(SquareType.GearCW, TypeAt(1, 1));
            Assert.Equal((int)Direction.Left, ActionAt(2, 1, SquareAction.BlockDirection).Parameter);
            Assert.Equal(SquareType.FastTurnCW, TypeAt(0, 2));
            Assert.Equal(SquareType.Pit, TypeAt(1, 2));
            Assert.Equal(3, board.CalcTotalFlags());

            var water = cells.Single(c => c.X == 2 && c.Y == 2);
            Assert.Equal((int)SquareType.Water, water.SquareType);
            Assert.True(water.NeedsReview);
            Assert.Empty(board.GetSquare(2, 2)!.ActionList);
            Assert.Equal(1, cells.Count(c => c.NeedsReview));
        }
    }

    [Fact]
    public void BuildBoard_LowScoreBecomesBlankAndIsFlagged()
    {
        var belt = Catalog.GetTemplates(MatchMode.Grayscale).First(t => t.TypeId == 10 && t.Rotation == Direction.Up);
        var matches = new TileMatch[1, 2] { { new TileMatch(belt, 0.9f), new TileMatch(belt, 0.2f) } };

        var (board, cells) = BoardPhotoImportService.BuildBoard(matches, 0.5f, Template());

        Assert.Equal(SquareType.NormalBelt, board.GetSquare(0, 0)!.Type);
        Assert.False(cells[0].NeedsReview);
        Assert.Equal(SquareType.Blank, board.GetSquare(1, 0)!.Type);
        Assert.True(cells[1].NeedsReview);
        Assert.Equal(10, cells[1].GuessTypeId);
    }
}
