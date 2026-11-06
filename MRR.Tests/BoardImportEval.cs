using System.Text;
using System.Text.Json;
using MRR.Config;
using MRR.Config.BoardPhotoImport;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MRR.Tests;

/// <summary>
/// Scores the board photo importer against photos of real boards whose layout is known
/// (install/Boards/photos/eval-cases.json). It runs the same call the web endpoint makes,
/// <see cref="BoardPhotoImportService.Import"/>, and compares the board it produces with the truth.
/// See BOARD_PHOTO_IMPORT_DESIGN.md, "Evaluation harness".
/// </summary>
public static class BoardImportEval
{
    public record Floor(double? Category, double? ClassNumber, double? Exact, double? Rotation,
        double? ReviewRecall = null, double? MaxFlaggedShare = null);

    public record Case(
        string Name, string Photo, int Cols, int Rows, float[][] Corners,
        string? TruthBoard, string? TruthGrid, Dictionary<string, string>? Overrides,
        bool HeldOut, Floor? Floor);

    public record Manifest(string Template, List<Case> Cases);

    public record Result(
        string Name, bool HeldOut, int Scored,
        int CategoryCorrect, int ClassNumberCorrect, int? ExactCorrect, int? BeltCells, int? BeltRotationCorrect,
        int Wrong, int WrongFlagged, int Flagged,
        Dictionary<string, (int Cells, int Correct)> ByCategory, string Grid, SortedDictionary<string, int> BeltErrors,
        List<(float Margin, bool Wrong, bool Numbered)> CellMargins)
    {
        public List<string> WrongSpeedCells { get; init; } = new();
        public List<string> WrongCells { get; init; } = new();
        public int TotalCells { get; init; }

        /// <summary>Share of the wrong cells the importer marked for review.</summary>
        public double ReviewRecall => Wrong == 0 ? 1 : (double)WrongFlagged / Wrong;

        /// <summary>Share of all cells marked for review. Recall alone is cheap to get by flagging everything.</summary>
        public double FlaggedShare => (double)Flagged / TotalCells;

        /// <summary>Share of wrong cells that would be flagged if cells with a margin below <paramref name="margin"/>, and every numbered tile, were.</summary>
        public (int Flagged, int Caught) Sweep(float margin) =>
            (CellMargins.Count(c => c.Numbered || c.Margin < margin), CellMargins.Count(c => c.Wrong && (c.Numbered || c.Margin < margin)));
        public double Category => (double)CategoryCorrect / Scored;
        public double ClassNumber => (double)ClassNumberCorrect / Scored;
        public double? Exact => ExactCorrect is null ? null : (double)ExactCorrect.Value / Scored;
        public double? Rotation => BeltRotationCorrect is null || BeltCells == 0 ? null : (double)BeltRotationCorrect.Value / BeltCells!.Value;

        public string Summary() =>
            $"{Name}{(HeldOut ? " [held out]" : "")}: category {Category:P0}, class+number {ClassNumber:P0}" +
            (Exact is null ? "" : $", exact {Exact:P0}") +
            (Rotation is null ? "" : $", belt rotation {BeltRotationCorrect}/{BeltCells} ({Rotation:P0})") +
            $"; {Scored} cells scored; review flag caught {WrongFlagged}/{Wrong} wrong cells ({Flagged} of {TotalCells} cells flagged, {FlaggedShare:P0})";
    }

    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "install", "Boards", "photos", "eval-cases.json")))
                return dir.FullName;
        throw new DirectoryNotFoundException("install/Boards/photos/eval-cases.json not found above the test output directory.");
    }

    public static string PhotosDirectory => Path.Combine(RepoRoot(), "install", "Boards", "photos");

    public static Manifest LoadManifest() =>
        JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(PhotosDirectory, "eval-cases.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    // ── Tokens ──────────────────────────────────────────────────────────────

    /// <summary>Coarse class: . blank, BN normal belt, BX express belt, G gear, H hazard, F flag, S start, P reboot.</summary>
    public static string Category(SquareType t) => (int)t switch
    {
        0 or 200 => ".", // walls are separate bars on the seams, not tiles; the importer does not match them
        >= 10 and <= 16 => "BN",
        >= 20 and <= 26 => "BX",
        31 or 32 => "G",
        40 or 41 or 44 => "H",
        100 => "F",
        110 => "S",
        120 => "P",
        _ => "?" + (int)t,
    };

    private static int Number(BoardElement square) => square.Type switch
    {
        SquareType.Flag => square.ActionList.FirstOrDefault(a => a.SquareAction == SquareAction.Flag)?.Parameter ?? 0,
        SquareType.StartSquare => square.ActionList.FirstOrDefault(a => a.SquareAction == SquareAction.PlayerStart)?.Parameter ?? 0,
        SquareType.RebootToken => square.ActionList.FirstOrDefault(a => a.SquareAction == SquareAction.Respawn)?.Parameter ?? 0,
        _ => 0,
    };

    /// <summary>Category plus number for flags, starts and reboot tokens: "F4", "S1", "BN".</summary>
    public static string ClassNumber(BoardElement? square)
    {
        if (square == null) return ".";
        var c = Category(square.Type);
        return c is "F" or "S" or "P" ? c + Number(square) : c;
    }

    private static string ExactKey(BoardElement? square) =>
        square == null ? "0" : (int)square.Type + (Number(square) > 0 ? "#" + Number(square) : "");

    private static bool IsBelt(SquareType t) => (int)t is (>= 10 and <= 16) or (>= 20 and <= 26);

    // ── Running a case ──────────────────────────────────────────────────────

    private static readonly Dictionary<string, List<RealTile>> ExtractedTiles = new();

    /// <summary>"S6" -> (110, 6), "F3" -> (100, 3), "P2" -> (120, 2), "." -> (0, 0): the cell override tokens.</summary>
    private static (int Type, int Number) ParseToken(string token) =>
        token == "." ? (0, 0) : (token[0] switch { 'F' => 100, 'S' => 110, 'P' => 120, _ => throw new FormatException(token) },
            int.Parse(token[1..]));

    /// <summary>All tiles cut from one case's photo (cached: it takes a second or two).</summary>
    private static List<RealTile> ExtractFrom(Case c, Manifest manifest)
    {
        lock (ExtractedTiles)
        {
            if (ExtractedTiles.TryGetValue(c.Name, out var cached)) return cached;
            var truth = BoardData.LoadBoardFile(Path.GetFullPath(Path.Combine(PhotosDirectory, c.TruthBoard!)))!;
            var overrides = (c.Overrides ?? []).ToDictionary(
                kv => (int.Parse(kv.Key.Split(',')[0]), int.Parse(kv.Key.Split(',')[1])), kv => ParseToken(kv.Value));
            using var photo = Image.Load<Rgb24>(Path.Combine(PhotosDirectory, c.Photo));
            var tiles = RealTiles.Extract(photo, c.Corners.Select(p => new PointF(p[0], p[1])).ToList(),
                c.Cols, c.Rows, truth, overrides, c.Name);
            return ExtractedTiles[c.Name] = tiles;
        }
    }

    /// <summary>
    /// The real tiles to score a case with: those cut from every case that has a .srx truth and is not
    /// itself held out, except this case's own photo. The two Homey photos are the same physical board,
    /// so scoring one with tiles from the other is easier than scoring a new board (compare the held-out cases).
    /// </summary>
    public static List<RealTile> RealTilesFor(Case c, Manifest manifest) =>
        RealTiles.Select(manifest.Cases.Where(o => o.TruthBoard != null && !o.HeldOut && o.Name != c.Name)
            .SelectMany(o => ExtractFrom(o, manifest)), RealTiles.DefaultPerKey);

    public static Result Run(Case c, Manifest manifest, MatchMode mode = MatchMode.Semantic, bool useRealTiles = true)
    {
        var photosDir = PhotosDirectory;
        var template = BoardData.LoadBoardFile(Path.GetFullPath(Path.Combine(photosDir, manifest.Template)))
            ?? throw new FileNotFoundException("Template board not found: " + manifest.Template);

        // The truth: one ClassNumber token per cell, plus (for a .srx) the exact square.
        BoardElementCollection? truthBoard = null;
        var truth = new string[c.Rows, c.Cols];
        if (c.TruthBoard != null)
        {
            truthBoard = BoardData.LoadBoardFile(Path.GetFullPath(Path.Combine(photosDir, c.TruthBoard)))
                ?? throw new FileNotFoundException("Truth board not found: " + c.TruthBoard);
            for (int r = 0; r < c.Rows; r++)
                for (int col = 0; col < c.Cols; col++)
                    truth[r, col] = ClassNumber(truthBoard.GetSquare(col, r));
        }
        else
        {
            var rows = File.ReadAllLines(Path.Combine(photosDir, c.TruthGrid!))
                .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
                .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();
            for (int r = 0; r < c.Rows; r++)
                for (int col = 0; col < c.Cols; col++)
                    truth[r, col] = rows[r][col];
        }
        foreach (var (key, token) in c.Overrides ?? [])
        {
            var p = key.Split(',');
            truth[int.Parse(p[1]), int.Parse(p[0])] = token;
        }

        using var photo = Image.Load<Rgb24>(Path.Combine(photosDir, c.Photo));
        var corners = c.Corners.Select(p => new PointF(p[0], p[1])).ToList();
        var service = new BoardPhotoImportService(new TileTemplateCatalog(CatalogDirectory(),
            useRealTiles ? RealTilesFor(c, manifest) : null));
        var (board, cells) = service.Import(photo, corners, c.Cols, c.Rows, mode,
            BoardPhotoImportService.DefaultThreshold, template);

        int scored = 0, catOk = 0, numOk = 0, exactOk = 0, beltCells = 0, beltRotOk = 0;
        int wrong = 0, wrongFlagged = 0, flagged = cells.Count(x => x.NeedsReview);
        bool hasExact = truthBoard != null;
        var byCat = new Dictionary<string, (int Cells, int Correct)>();
        var grid = new StringBuilder();
        var beltErrors = new SortedDictionary<string, int>();
        var beltDetail = new List<string>();
        var margins = new List<(float, bool, bool)>();
        var wrongCells = new List<string>();
        void Bump(string k) => beltErrors[k] = beltErrors.GetValueOrDefault(k) + 1;

        for (int r = 0; r < c.Rows; r++)
        {
            for (int col = 0; col < c.Cols; col++)
            {
                var got = board.GetSquare(col, r);
                string gotToken = ClassNumber(got), want = truth[r, col];
                var cell = cells.First(x => x.X == col && x.Y == r);
                if (want == "U") { grid.Append($"{gotToken,-4}~ "); continue; }

                scored++;
                string wantCat = want.TrimEnd("0123456789".ToCharArray());
                string gotCat = gotToken.TrimEnd("0123456789".ToCharArray());
                bool catRight = wantCat == gotCat, numRight = want == gotToken;
                if (catRight) catOk++;
                if (numRight) numOk++;
                byCat[wantCat] = (byCat.GetValueOrDefault(wantCat).Cells + 1, byCat.GetValueOrDefault(wantCat).Correct + (numRight ? 1 : 0));
                if (!numRight) { wrong++; if (cell.NeedsReview) wrongFlagged++; }
                if (!numRight) wrongCells.Add($"({col},{r}) got {gotToken} want {want} score {cell.Score:0.00} margin {cell.Margin:0.000}");
                margins.Add((cell.Margin, !numRight, gotToken.StartsWith('F') || gotToken.StartsWith('S') || gotToken.StartsWith('P')));

                // Exact SquareType and belt rotation need the real square, so only where the truth is a .srx.
                // A cell whose override changes its meaning (the moved start 6) has no srx square to compare.
                if (truthBoard != null)
                {
                    var truthSquare = truthBoard.GetSquare(col, r);
                    bool overridden = c.Overrides?.ContainsKey($"{col},{r}") == true;
                    if (!overridden)
                    {
                        if (ExactKey(truthSquare) == ExactKey(got)) exactOk++;
                        if (truthSquare != null && IsBelt(truthSquare.Type))
                        {
                            beltCells++;
                            if (got != null && got.Type == truthSquare.Type && got.Rotation == truthSquare.Rotation) { beltRotOk++; Bump("1 right"); }
                            else if (got == null || !IsBelt(got.Type)) Bump("5 not a belt (" + Category(got?.Type ?? SquareType.Blank) + ")");
                            else if ((int)got.Type / 10 != (int)truthSquare.Type / 10) { Bump("4 wrong speed"); beltDetail.Add($"({col},{r}) {truthSquare.Type}->{got.Type}"); }
                            else if (got.Type != truthSquare.Type && got.Rotation == truthSquare.Rotation) Bump("3 wrong subtype, rotation right");
                            else if (got.Type != truthSquare.Type) Bump("3 wrong subtype and rotation");
                            else Bump("2 right type, rotation off by " + (((int)got.Rotation - (int)truthSquare.Rotation + 4) % 4 * 90) + " deg");
                        }
                    }
                    else if (want == gotToken) exactOk++; // an overridden cell is judged on its token alone
                }
                grid.Append($"{gotToken,-4}{(numRight ? " " : "*")} ");
            }
            grid.AppendLine();
        }

        return new Result(c.Name, c.HeldOut, scored, catOk, numOk,
            hasExact ? exactOk : null, hasExact ? beltCells : null, hasExact ? beltRotOk : null,
            wrong, wrongFlagged, flagged, byCat, grid.ToString(), beltErrors, margins.Select(m => (m.Item1, m.Item2, m.Item3)).ToList()) { WrongSpeedCells = beltDetail, WrongCells = wrongCells, TotalCells = c.Cols * c.Rows };
    }

    public static string CatalogDirectory()
    {
        var dir = Path.Combine(RepoRoot(), "MRR.Config", "wwwroot", "images");
        return Directory.Exists(dir) ? dir : throw new DirectoryNotFoundException(dir);
    }

    /// <summary>Full text report for one result, with the per-cell grid ('*' wrong, '~' not scored).</summary>
    public static string Report(Result r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Summary());
        sb.AppendLine("Per class (class+number correct / cells): " +
            string.Join(", ", r.ByCategory.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value.Correct}/{k.Value.Cells}")));
        if (r.BeltErrors.Count > 0)
            sb.AppendLine("Belt cells: " + string.Join(", ", r.BeltErrors.Select(k => $"{k.Key[2..]} {k.Value}")));
        if (r.WrongSpeedCells.Count > 0) sb.AppendLine("Wrong-speed belts (col,row) truth->got: " + string.Join("  ", r.WrongSpeedCells));
        sb.AppendLine("Wrong cells: " + string.Join("; ", r.WrongCells));
        var right = r.CellMargins.Where(c => !c.Wrong).Select(c => c.Margin).OrderBy(m => m).ToList();
        if (right.Count > 0)
            sb.AppendLine($"Margin of correct cells, 10th/25th/50th percentile: {right[right.Count / 10]:0.000} / {right[right.Count / 4]:0.000} / {right[right.Count / 2]:0.000}");
        sb.AppendLine("Review flag if margin below (numbered tiles always flagged): " + string.Join("   ",
            new[] { 0f, 0.01f, 0.02f, 0.03f, 0.05f, 0.07f, 0.10f, 0.15f }.Select(m =>
            {
                var (f, c) = r.Sweep(m);
                return $"{m:0.00}: {f} flagged, {c}/{r.Wrong} wrong caught";
            })));
        sb.AppendLine("Importer's answer per cell ('*' wrong, '~' not scored):");
        sb.Append(r.Grid);
        return sb.ToString();
    }
}
