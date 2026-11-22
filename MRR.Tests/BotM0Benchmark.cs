using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Xunit.Abstractions;

namespace MRR.Tests;

/// <summary>
/// Computer-player M0 timings (documents/COMPUTER_PLAYER_DESIGN.md §6). These answer the question
/// the plan cannot answer from reading code: how long does one turn plan take, and therefore how
/// wide a search can each level afford?
///
/// It is NOT a pass/fail test and does nothing unless MRR_BENCH=1 is set, so it never slows a
/// normal test run. Run it by hand, in Release, ideally on the Raspberry Pi as well as the
/// development machine:
///
///   MRR_BENCH=1 dotnet test MRR.Tests -c Release --filter "Category=Benchmark" --logger "console;verbosity=detailed"
///   (PowerShell:  $env:MRR_BENCH = "1"; dotnet test MRR.Tests -c Release --filter "Category=Benchmark" --logger "console;verbosity=detailed")
///
/// The report is printed, and also written to bot-m0-results.txt beside the test assembly.
/// The board is synthetic (see BotFixtures), so treat numbers as an order of magnitude.
/// </summary>
[Trait("Category", "Benchmark")]
public class BotM0Benchmark
{
    private readonly ITestOutputHelper _output;
    private readonly StringBuilder _report = new();

    public BotM0Benchmark(ITestOutputHelper output) => _output = output;

    private void Line(string text = "")
    {
        _output.WriteLine(text);
        _report.AppendLine(text);
    }

    // ---- measurement helpers -----------------------------------------------------------------

    private record Stats(double Mean, double Median, double P95, double Min, double Max)
    {
        public static Stats Of(double[] ms)
        {
            var s = ms.OrderBy(x => x).ToArray();
            return new Stats(s.Average(), s[s.Length / 2], s[(int)Math.Min(s.Length - 1, Math.Floor(s.Length * 0.95))], s[0], s[^1]);
        }

        public override string ToString() => $"mean {Mean,7:F3}  median {Median,7:F3}  p95 {P95,7:F3}  min {Min,7:F3}  max {Max,7:F3} ms";
    }

    /// <summary>Times <paramref name="act"/> after a warm-up; also reports bytes allocated per call.</summary>
    private static (Stats stats, long bytesPerCall) Time(int warmup, int runs, Action act)
    {
        for (int i = 0; i < warmup; i++) act();
        var ms = new double[runs];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < runs; i++)
        {
            long t = Stopwatch.GetTimestamp();
            act();
            ms[i] = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        }
        return (Stats.Of(ms), (GC.GetAllocatedBytesForCurrentThread() - before) / runs);
    }

    private static double Median(IEnumerable<double> values)
    {
        var s = values.OrderBy(x => x).ToArray();
        return s[s.Length / 2];
    }

    // ---- the search shape the plan proposes (stage = one beam search of a given width) -------

    private record BeamEntry(List<MoveCard.tCardType> Prefix, List<MoveCard.tCardType> Remaining, int Score);

    /// <summary>
    /// One stage of the plan's iterative-widening search (§5.2), with a stand-in score. Returns
    /// how many plans it ran and how long that took. Every plan is a prefix plan: PhaseCount = k.
    /// </summary>
    private static (int plans, double ms, int best) BeamStage(BoardElementCollection board, PlayerStates players, int me, int width)
    {
        var start = players.First(p => p.ID == me);
        var entries = new List<BeamEntry> { new([], BotFixtures.Hand.ToList(), 0) };
        int plans = 0;
        long t0 = Stopwatch.GetTimestamp();

        for (int k = 1; k <= 5; k++)
        {
            var children = new List<BeamEntry>();
            foreach (var entry in entries)
            {
                foreach (var type in entry.Remaining.Distinct())
                {
                    var prefix = new List<MoveCard.tCardType>(entry.Prefix) { type };
                    var remaining = new List<MoveCard.tCardType>(entry.Remaining);
                    remaining.Remove(type);

                    var plan = BotFixtures.PlanSolo(board, players, me, prefix, k);
                    plans++;
                    children.Add(new BeamEntry(prefix, remaining, BotFixtures.TrivialScore(plan, start)));
                }
            }
            entries = children.Select((c, i) => (c, i))
                .OrderByDescending(x => x.c.Score).ThenBy(x => x.i)
                .Take(width).Select(x => x.c).ToList();
        }

        return (plans, Stopwatch.GetElapsedTime(t0).TotalMilliseconds, entries[0].Score);
    }

    // ---- the measurement ---------------------------------------------------------------------

    [Fact]
    public void Measure()
    {
        if (Environment.GetEnvironmentVariable("MRR_BENCH") != "1") return; // opt-in only

        // The planner prints "Planned N commands" on every call (design doc gap 9). Console output
        // takes a global lock and talks to the test host, so left on it would dominate (and
        // serialize) the timings below. It is silenced for the measurements and measured on its
        // own in S1c, which is the evidence for deleting that line.
        var originalOut = Console.Out;
        Console.SetOut(TextWriter.Null);
        try { RunMeasurements(originalOut); }
        finally { Console.SetOut(originalOut); }
    }

    private void RunMeasurements(TextWriter originalOut)
    {
        var board = BotFixtures.Board();
        var players = BotFixtures.Players();

        // Warm up the JIT properly (tiered compilation needs a while), so the first scenario is
        // not unfairly slower than the last. Same mix of work as the measurements below.
        var warm = Stopwatch.StartNew();
        while (warm.Elapsed < TimeSpan.FromSeconds(3))
        {
            for (int k = 1; k <= 5; k++)
                BotFixtures.PlanSolo(board, players, 1, BotFixtures.RandomProgram(k).Take(k).ToArray());
            var cards = new CardList();
            for (int r = 1; r <= 6; r++) BotFixtures.AddProgram(cards, r, BotFixtures.RandomProgram(100 + r));
            new CreateCommands(BotFixtures.Request(board, players, cards, 5)).CreateTurn();
            BeamStage(board, players, 1, 3);
        }

        Line("Computer player M0: planner timings");
        Line($"  runtime : {RuntimeInformation.FrameworkDescription}  {RuntimeInformation.ProcessArchitecture}");
        Line($"  os      : {RuntimeInformation.OSDescription}");
        Line($"  cores   : {Environment.ProcessorCount} logical");
#if DEBUG
        Line("  build   : DEBUG  -- NOT a valid benchmark; rerun with -c Release");
#else
        Line("  build   : Release");
#endif
        Line($"  board   : synthetic {BotFixtures.Size}x{BotFixtures.Size}, belts/gears/pits/walls/lasers, {BotFixtures.TotalFlags} flags; 6 robots");
        Line($"  hand    : {string.Join(", ", BotFixtures.Hand)}");
        Line();

        // S0: how expensive is looking up a square? (GetSquare scans the whole list.)
        const int lookups = 200_000;
        long t = Stopwatch.GetTimestamp();
        int hits = 0;
        for (int i = 0; i < lookups; i++)
            if (board.GetSquare(i % BotFixtures.Size, (i / BotFixtures.Size) % BotFixtures.Size) != null) hits++;
        double perLookupUs = Stopwatch.GetElapsedTime(t).TotalMilliseconds * 1000.0 / lookups;
        Line($"S0  board.GetSquare(x, y): {perLookupUs:F2} us per call ({hits} hits; linear scan of {board.BoardElements.Count} squares)");
        Line();

        // S1: one computer, everyone else cardless -- the Level 2-4 model.
        Line("S1  one plan, only robot 1 holds cards (Levels 2-4 model), by number of phases planned");
        for (int k = 1; k <= 5; k++)
        {
            var program = BotFixtures.RandomProgram(7).Take(k).ToArray();
            int commands = BotFixtures.PlanSolo(board, players, 1, program).Commands.Count;
            var (stats, bytes) = Time(60, 250, () => BotFixtures.PlanSolo(board, players, 1, program));
            Line($"  {k} phase(s): {stats}   {bytes / 1024.0,7:F1} KB/plan   {commands} commands");
        }
        Line();

        // S1c: the cost of the planner's own "Planned N commands" line (design doc gap 9).
        {
            var program = BotFixtures.RandomProgram(7);
            var (quiet, _) = Time(60, 400, () => BotFixtures.PlanSolo(board, players, 1, program));
            Console.SetOut(originalOut);
            var (noisy, _) = Time(60, 400, () => BotFixtures.PlanSolo(board, players, 1, program));
            Console.SetOut(TextWriter.Null);
            Line($"S1c the planner's console line, 5-phase solo plan: silenced {quiet.Mean:F3} ms  vs  printed to the test host {noisy.Mean:F3} ms  (x{noisy.Mean / quiet.Mean:F1})");
            Line("    (a console under systemd/journald is a different sink, so this is an indication only)");
        }
        Line();

        // S1b: how much of that is setup the bot pays for itself (card list, request) vs the planner?
        var (setup, setupBytes) = Time(500, 5000, () =>
        {
            var cards = new CardList();
            BotFixtures.AddProgram(cards, 1, BotFixtures.RandomProgram(7));
            _ = BotFixtures.Request(board, players, cards, 5);
        });
        Line($"S1b setup only (new CardList + 5 cards + request, no planning): {setup}   {setupBytes} B");
        Line();

        // S2: every robot programmed -- what one Expert opponent-world costs.
        Line("S2  one 5-phase plan, all six robots hold cards (an Expert opponent world)");
        double allSixMean;
        {
            var programs = Enumerable.Range(1, 6).Select(r => BotFixtures.RandomProgram(100 + r)).ToArray();
            void PlanAllSix()
            {
                var cards = new CardList();
                for (int r = 1; r <= 6; r++) BotFixtures.AddProgram(cards, r, programs[r - 1]);
                new CreateCommands(BotFixtures.Request(board, players, cards, 5)).CreateTurn();
            }
            var (stats, bytes) = Time(40, 150, PlanAllSix);
            allSixMean = stats.Mean;
            Line($"  {stats}   {bytes / 1024.0,7:F1} KB/plan");
            Line($"  => one Expert re-rank of 12 candidates x 8 worlds = 96 plans ~ {96 * allSixMean / 1000.0:F2} s on this machine");
        }
        Line();

        // S3: the search stages (§5.2). Width 1 is greedy; each width is one complete stage.
        Line("S3  search stages for ONE computer (iterative widening; median of 3 runs)");
        Line("    width   plans   total ms   ms/plan");
        foreach (int width in new[] { 1, 3, 10, 30, 60 })
        {
            BeamStage(board, players, 1, width); // warm-up
            var runs = Enumerable.Range(0, 3).Select(_ => BeamStage(board, players, 1, width)).ToArray();
            double ms = Median(runs.Select(r => r.ms));
            int plans = runs[0].plans;
            Line($"    {width,5}   {plans,5}   {ms,8:F1}   {ms / plans,7:F3}");
        }
        Line();

        // S4: the number the plan's M0 acceptance depends on.
        Line("S4  time for stage 1 (width 1) for FIVE computers, one after another (median of 5 runs)");
        double Stage1FiveComputers()
        {
            double total = 0;
            for (int robot = 1; robot <= 5; robot++) total += BeamStage(board, players, robot, 1).ms;
            return total;
        }
        Stage1FiveComputers(); // warm-up
        double fiveMs = Median(Enumerable.Range(0, 5).Select(_ => Stage1FiveComputers()));
        Line($"  {fiveMs:F1} ms   (target <= 1000 ms on the Raspberry Pi)");
        Line();

        // S5: what would exhaustive search cost for this hand?
        int distinct = BotFixtures.DistinctSequences(BotFixtures.Hand, 5);
        var (fullPlan, _) = Time(60, 200, () => BotFixtures.PlanSolo(board, players, 1, BotFixtures.RandomProgram(11)));
        Line($"S5  exhaustive search: {distinct} distinct 5-card type sequences x {fullPlan.Mean:F3} ms/plan ~ {distinct * fullPlan.Mean / 1000.0:F1} s for one computer");
        Line();

        // S6: does running several planners at once pay?
        Line("S6  throughput of 5-phase solo plans across threads (a shared board and players)");
        const int jobs = 480;
        double baseline = 0;
        foreach (int threads in new[] { 1, 2, 4, 8 }.Where(n => n <= Math.Max(1, Environment.ProcessorCount)))
        {
            Parallel.For(0, 40, new ParallelOptions { MaxDegreeOfParallelism = threads },
                i => BotFixtures.PlanSolo(board, players, 1 + i % 6, BotFixtures.RandomProgram(i))); // warm-up
            long t1 = Stopwatch.GetTimestamp();
            Parallel.For(0, jobs, new ParallelOptions { MaxDegreeOfParallelism = threads },
                i => BotFixtures.PlanSolo(board, players, 1 + i % 6, BotFixtures.RandomProgram(i)));
            double seconds = Stopwatch.GetElapsedTime(t1).TotalSeconds;
            double perSecond = jobs / seconds;
            if (threads == 1) baseline = perSecond;
            Line($"  {threads} thread(s): {perSecond,8:F0} plans/s   x{perSecond / baseline:F2} vs 1 thread");
        }
        Line();

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "bot-m0-results.txt"), _report.ToString());
        Line($"(also written to {Path.Combine(AppContext.BaseDirectory, "bot-m0-results.txt")})");
    }
}
