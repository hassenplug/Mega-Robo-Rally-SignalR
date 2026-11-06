using System.Text.Json;
using MRR.Config;
using MRR.Config.BoardPhotoImport;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

// Regenerates MRR.Config/wwwroot/images/real from the eval cases that have a .srx truth and are
// not held out (the two photos of board 6). Usage:  dotnet run --project MRR.Tools.RealTiles
// Held-out cases are never used, so the eval scores on them stay honest.

var root = FindRoot();
var photos = Path.Combine(root, "install", "Boards", "photos");
var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(photos, "eval-cases.json"))).RootElement;

var all = new List<RealTile>();
foreach (var c in manifest.GetProperty("cases").EnumerateArray())
{
    bool heldOut = c.TryGetProperty("heldOut", out var h) && h.GetBoolean();
    if (heldOut || !c.TryGetProperty("truthBoard", out var truthFile)) continue;

    var name = c.GetProperty("name").GetString()!;
    var truth = BoardData.LoadBoardFile(Path.GetFullPath(Path.Combine(photos, truthFile.GetString()!)))
        ?? throw new FileNotFoundException(truthFile.GetString());
    var overrides = new Dictionary<(int, int), (int, int)>();
    if (c.TryGetProperty("overrides", out var ov))
        foreach (var o in ov.EnumerateObject())
        {
            var p = o.Name.Split(',');
            var t = o.Value.GetString()!;
            overrides[(int.Parse(p[0]), int.Parse(p[1]))] = t == "." ? (0, 0)
                : (t[0] switch { 'F' => 100, 'S' => 110, 'P' => 120, _ => throw new FormatException(t) }, int.Parse(t[1..]));
        }
    var corners = c.GetProperty("corners").EnumerateArray()
        .Select(p => new PointF(p[0].GetSingle(), p[1].GetSingle())).ToList();

    using var photo = Image.Load<Rgb24>(Path.Combine(photos, c.GetProperty("photo").GetString()!));
    var tiles = RealTiles.Extract(photo, corners, c.GetProperty("cols").GetInt32(), c.GetProperty("rows").GetInt32(),
        truth, overrides, name);
    Console.WriteLine($"{name}: {tiles.Count} cells cut");
    all.AddRange(tiles);
}

var chosen = RealTiles.Select(all, RealTiles.DefaultPerKey);
var images = Path.Combine(root, "MRR.Config", "wwwroot", "images");
RealTiles.Save(images, chosen);
Console.WriteLine($"Saved {chosen.Count} tiles ({chosen.Select(t => (t.TypeId, t.Parameter)).Distinct().Count()} types/numbers) to {Path.Combine(images, RealTiles.FolderName)}");
foreach (var g in chosen.GroupBy(t => (t.TypeId, t.Parameter)).OrderBy(g => g.Key))
    Console.WriteLine($"  type {g.Key.TypeId}{(g.Key.Parameter > 0 ? "-" + g.Key.Parameter : "")}: {g.Count()}");

static string FindRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "install", "Boards", "photos", "eval-cases.json"))) return dir.FullName;
    throw new DirectoryNotFoundException("Run from inside the repository.");
}
