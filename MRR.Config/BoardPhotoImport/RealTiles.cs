using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MRR.Config.BoardPhotoImport
{
    /// <summary>
    /// One photographed tile of a known type, turned to the catalog's "Up" and darkened so that
    /// its black print sits under <see cref="TileFeatures.DefaultBlackMax"/> (so the same color-class
    /// rules apply to it as to the catalog art). Several of the same type are kept: they differ in
    /// glare, shadow and print variation.
    /// </summary>
    public record RealTile(int TypeId, int Parameter, Image<Rgb24> Image, string Source);

    /// <summary>
    /// Templates cut from photos of the printed tiles. The catalog art is the same designs in a
    /// different palette (and, for flags, turned), which is why start, reboot and express-belt
    /// tiles never matched (BOARD_PHOTO_IMPORT_DESIGN.md). Where real tiles exist for a type and
    /// number they replace the catalog art for it; the rest of the catalog is still used.
    /// </summary>
    public static class RealTiles
    {
        public const string FolderName = "real";

        /// <summary>Exemplars kept per type and number.</summary>
        public const int DefaultPerKey = 8;

        private static readonly Regex FileNamePattern =
            new(@"^real(?<type>\d+)(-(?<param>\d+))?_(?<k>\d+)\.png$", RegexOptions.IgnoreCase);

        /// <summary>Loads the real tiles saved under <c>{imageDirectory}/real</c>; empty if there are none.</summary>
        public static List<RealTile> Load(string imageDirectory)
        {
            var tiles = new List<RealTile>();
            var dir = Path.Combine(imageDirectory, FolderName);
            if (!Directory.Exists(dir)) return tiles;
            foreach (var path in Directory.EnumerateFiles(dir, "real*.png").Order())
            {
                var m = FileNamePattern.Match(Path.GetFileName(path));
                if (!m.Success) continue;
                int type = int.Parse(m.Groups["type"].Value);
                int param = m.Groups["param"].Success ? int.Parse(m.Groups["param"].Value) : 0;
                tiles.Add(new RealTile(type, param, Image.Load<Rgb24>(path), Path.GetFileName(path)));
            }
            return tiles;
        }

        /// <summary>Replaces the contents of <c>{imageDirectory}/real</c> with these tiles.</summary>
        public static void Save(string imageDirectory, IEnumerable<RealTile> tiles)
        {
            var dir = Path.Combine(imageDirectory, FolderName);
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.EnumerateFiles(dir, "real*.png")) File.Delete(old);
            foreach (var group in tiles.GroupBy(t => (t.TypeId, t.Parameter)))
            {
                int k = 1;
                foreach (var t in group)
                    t.Image.SaveAsPng(Path.Combine(dir,
                        $"real{t.TypeId}{(t.Parameter > 0 ? "-" + t.Parameter : "")}_{k++}.png"));
            }
        }

        /// <summary>
        /// Cuts the cells out of a photo of a board whose layout is known and labels each with
        /// what is really there. <paramref name="overrides"/> maps (col, row) to (type, number)
        /// for cells where the physical board differs from <paramref name="truth"/>. Walls are
        /// not tiles (they are bars on the seams) and count as Blank.
        /// </summary>
        public static List<RealTile> Extract(
            Image<Rgb24> photo, IReadOnlyList<PointF> corners, int cols, int rows,
            BoardElementCollection truth, IReadOnlyDictionary<(int Col, int Row), (int Type, int Number)>? overrides,
            string source)
        {
            var refined = GridRefiner.Refine(photo, corners, cols, rows);
            using var rectified = PerspectiveRectifier.Rectify(photo, refined, cols, rows, BoardPhotoImportService.CellPx);
            var cutoffs = TileFeatures.LocalBlackMax(rectified, cols, rows);
            var cells = PerspectiveRectifier.SliceCells(rectified, cols, rows);
            var tiles = new List<RealTile>();
            try
            {
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        int type, number = 0;
                        Direction rotation = Direction.None;
                        if (overrides != null && overrides.TryGetValue((c, r), out var o)) { type = o.Type; number = o.Number; }
                        else
                        {
                            var square = truth.GetSquare(c, r);
                            type = square == null || square.Type == SquareType.Walls ? 0 : (int)square.Type;
                            if (square != null && type != 0) { number = NumberOf(square); rotation = square.Rotation; }
                        }

                        var image = cells[r, c].Clone(ctx =>
                        {
                            // Turn it to the catalog's Up so the catalog's four rotations apply.
                            if (rotation is >= Direction.Up and <= Direction.Left && rotation != Direction.Up)
                                ctx.Rotate((RotateMode)((360 - ((int)rotation - 1) * 90) % 360));
                        });
                        Darken(image, TileFeatures.DefaultBlackMax / Math.Max(cutoffs[r, c], 0.05f));
                        tiles.Add(new RealTile(type, number, image, $"{source} ({c},{r})"));
                    }
            }
            finally
            {
                foreach (var cell in cells) cell.Dispose();
            }
            return tiles;
        }

        /// <summary>
        /// At most <paramref name="perKey"/> tiles of each type and number, chosen to differ from
        /// each other as much as possible (farthest-point sampling on a small color thumbnail),
        /// so a handful of exemplars still cover glare and print variation.
        /// </summary>
        public static List<RealTile> Select(IEnumerable<RealTile> tiles, int perKey)
        {
            var chosen = new List<RealTile>();
            foreach (var group in tiles.GroupBy(t => (t.TypeId, t.Parameter)))
            {
                var items = group.ToList();
                if (items.Count <= perKey) { chosen.AddRange(items); continue; }
                var thumbs = items.Select(t => Thumbnail(t.Image)).ToList();
                var picked = new List<int> { 0 };
                var minDist = thumbs.Select(t => Distance(t, thumbs[0])).ToArray();
                while (picked.Count < perKey)
                {
                    int next = Array.IndexOf(minDist, minDist.Max());
                    picked.Add(next);
                    for (int i = 0; i < items.Count; i++) minDist[i] = Math.Min(minDist[i], Distance(thumbs[i], thumbs[next]));
                }
                chosen.AddRange(picked.Select(i => items[i]));
            }
            return chosen;
        }

        private static int NumberOf(BoardElement square) => square.Type switch
        {
            SquareType.Flag => square.ActionList.FirstOrDefault(a => a.SquareAction == SquareAction.Flag)?.Parameter ?? 0,
            SquareType.StartSquare => square.ActionList.FirstOrDefault(a => a.SquareAction == SquareAction.PlayerStart)?.Parameter ?? 0,
            SquareType.RebootToken => square.ActionList.FirstOrDefault(a => a.SquareAction == SquareAction.Respawn)?.Parameter ?? 0,
            _ => 0,
        };

        /// <summary>Scales brightness by a constant. Hue and saturation do not change, so only the black cutoff moves.</summary>
        private static void Darken(Image<Rgb24> image, float factor)
        {
            factor = Math.Clamp(factor, 0.2f, 1.5f);
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    var p = image[x, y];
                    image[x, y] = new Rgb24(
                        (byte)Math.Min(255, p.R * factor), (byte)Math.Min(255, p.G * factor), (byte)Math.Min(255, p.B * factor));
                }
        }

        private static float[] Thumbnail(Image<Rgb24> image)
        {
            using var small = image.Clone(ctx => ctx.Resize(12, 12));
            var v = new float[12 * 12 * 3];
            for (int y = 0; y < 12; y++)
                for (int x = 0; x < 12; x++)
                {
                    var p = small[x, y];
                    int i = (y * 12 + x) * 3;
                    v[i] = p.R; v[i + 1] = p.G; v[i + 2] = p.B;
                }
            return v;
        }

        private static float Distance(float[] a, float[] b)
        {
            float d = 0;
            for (int i = 0; i < a.Length; i++) { float e = a[i] - b[i]; d += e * e; }
            return d;
        }
    }
}
