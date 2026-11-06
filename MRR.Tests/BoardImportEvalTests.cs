using Xunit.Abstractions;

namespace MRR.Tests;

/// <summary>
/// Regression scores for the board photo importer on real photos (see BoardImportEval). Each
/// case fails if a score drops below its "floor" in install/Boards/photos/eval-cases.json.
/// To see the numbers and the per-cell grid:
///   dotnet test MRR.Tests --filter BoardImportEvalTests --logger "console;verbosity=detailed"
/// The grids are also written to eval-out/ beside the test binaries.
/// </summary>
public class BoardImportEvalTests
{
    private readonly ITestOutputHelper _output;

    public BoardImportEvalTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> CaseNames() =>
        BoardImportEval.LoadManifest().Cases.Select(c => new object[] { c.Name });

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ScoresAtOrAboveFloor(string caseName)
    {
        var manifest = BoardImportEval.LoadManifest();
        var c = manifest.Cases.Single(x => x.Name == caseName);
        var result = BoardImportEval.Run(c, manifest);

        var report = BoardImportEval.Report(result);
        _output.WriteLine(report);
        var outDir = Path.Combine(AppContext.BaseDirectory, "eval-out");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, caseName + ".txt"), report);

        var floor = c.Floor;
        if (floor == null) return;
        if (floor.Category is { } f1) Assert.True(result.Category >= f1, $"category {result.Category:P1} < floor {f1:P1}");
        if (floor.ClassNumber is { } f2) Assert.True(result.ClassNumber >= f2, $"class+number {result.ClassNumber:P1} < floor {f2:P1}");
        if (floor.Exact is { } f3) Assert.True(result.Exact >= f3, $"exact {result.Exact:P1} < floor {f3:P1}");
        if (floor.Rotation is { } f4) Assert.True(result.Rotation >= f4, $"belt rotation {result.Rotation:P1} < floor {f4:P1}");
        if (floor.ReviewRecall is { } f5) Assert.True(result.ReviewRecall >= f5, $"review recall {result.ReviewRecall:P1} < floor {f5:P1}");
        if (floor.MaxFlaggedShare is { } f6) Assert.True(result.FlaggedShare <= f6, $"flagged share {result.FlaggedShare:P1} > ceiling {f6:P1}");
    }

    /// <summary>
    /// Report only (no assertions; needs MRR_EVAL_MODES=1): every match mode, with and without real
    /// tiles, so a change of default mode can be judged. Shows "class+number" (and exact for the Homey board).
    /// </summary>
    [Fact]
    public void ModesCompared()
    {
        // Slow (8 full runs of every case), so only when asked for: set MRR_EVAL_MODES=1.
        if (Environment.GetEnvironmentVariable("MRR_EVAL_MODES") != "1")
        {
            _output.WriteLine("Skipped. Set MRR_EVAL_MODES=1 to compare every match mode.");
            return;
        }
        var manifest = BoardImportEval.LoadManifest();
        foreach (var useReal in new[] { false, true })
            foreach (var mode in Enum.GetValues<MRR.Config.BoardPhotoImport.MatchMode>())
            {
                var line = string.Join("   ", manifest.Cases.Select(c =>
                {
                    var r = BoardImportEval.Run(c, manifest, mode, useReal);
                    return $"{c.Name} {r.ClassNumber:P0}" + (r.Exact is { } e ? $" (exact {e:P0})" : "");
                }));
                _output.WriteLine($"{(useReal ? "real tiles" : "catalog art")} / {mode,-9}: {line}");
            }
    }
}
