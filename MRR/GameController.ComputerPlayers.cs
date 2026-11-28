using System.Diagnostics;
using MRR.Bot;

namespace MRR.Controller
{
    /// <summary>
    /// Computer players (documents/COMPUTER_PLAYER_DESIGN.md §5.7, §5.8).
    ///
    /// On entering programming (state 4) every computer seat gets a legal Level 1 program at
    /// once, inside NextState() and before its state-4 gate runs, so the gate never waits on a
    /// computer. Then a background run improves the programs of Level 2+ computers while the
    /// humans program; Levels 2-4 stop the moment the last human is done. Expert may hold the
    /// gate for up to GraceSoftMs / GraceHardMs after that (BotTiming), and with no human
    /// programming at all (an all-computer table) the gate holds for at most SoloBudgetMs (Expert:
    /// the larger of that and its grace).
    ///
    /// Config (appsettings.json, section ComputerPlayer; all optional):
    ///   SearchEnabled (true)  -- false: every computer keeps its Level 1 program
    ///   SoloBudgetMs  (5000)  -- the search time allowed when no human is programming
    ///   GraceSoftMs   (5000)  -- Expert: may start new re-rank rounds until this long after the humans
    ///   GraceHardMs   (10000) -- Expert: stops, and the gate releases, this long after the humans
    ///   StopPollMs    (100)   -- how often the run checks whether the humans are done
    /// </summary>
    public partial class GameController
    {
        private static readonly Random s_botRandom = new();
        private readonly object _computerRunLock = new();
        private ComputerRun? _computerRun;

        private bool BotSearchEnabled => _configuration?.GetValue("ComputerPlayer:SearchEnabled", true) ?? true;
        private int BotSoloBudgetMs => _configuration?.GetValue("ComputerPlayer:SoloBudgetMs", 5000) ?? 5000;
        private int BotGraceSoftMs => _configuration?.GetValue("ComputerPlayer:GraceSoftMs", 5000) ?? 5000;
        private int BotGraceHardMs => _configuration?.GetValue("ComputerPlayer:GraceHardMs", 10000) ?? 10000;
        private int BotStopPollMs => Math.Max(20, _configuration?.GetValue("ComputerPlayer:StopPollMs", 100) ?? 100);

        /// <summary>One programming phase's search. Fields are touched by the worker, the poll and the gate.</summary>
        private sealed class ComputerRun
        {
            public readonly CancellationTokenSource Cancel = new();
            public readonly Stopwatch Clock = Stopwatch.StartNew();
            public BotTiming Timing = null!; // deadlines (MRR.Bot), on this run's Clock
            public volatile bool Finished;
            public volatile bool Holding;    // the state-4 gate is waiting for this run
            public Timer? Poll;
            public int Polling;              // guards against overlapping poll callbacks
        }

        /// <summary>
        /// Programs every computer seat that still needs it (DataService.GetComputersToProgram)
        /// with a Level 1 placeholder, then starts the search for Level 2+ seats. Called from
        /// NextState() right after SetGameState(4), from states 3 and 15. A new state-4 entry
        /// cancels any run still going.
        ///
        /// A computer player must never stall the game: any failure is logged and the robot is
        /// left as it was -- the GM can then use Sit Out for that seat.
        ///
        /// onlyRobotId: just that seat, mid-programming (the GM made it a computer, SetComputerLevel).
        /// The run already going for the other computers is left alone; this seat is searched
        /// too only if no run is going, otherwise it keeps its Level 1 program this turn.
        /// </summary>
        private void ProgramComputerPlayers(int? onlyRobotId = null)
        {
            if (onlyRobotId == null) CancelComputerRun();
            try
            {
                var seats = _dataService.GetComputersToProgram();
                if (onlyRobotId != null) seats = seats.Where(s => s.RobotID == onlyRobotId).ToList();
                if (seats.Count == 0) return;

                int phaseCount = _dataService.PhaseCount;
                var toSearch = new List<BotSeat>();
                var placeholders = new Dictionary<int, IReadOnlyList<int>>();
                foreach (var seat in seats)
                {
                    try
                    {
                        Decision decision;
                        lock (s_botRandom)
                        {
                            decision = RandomBrain.Decide(seat.Hand, phaseCount, seat.Facing,
                                facingNeeded: seat.PositionValid == 0, s_botRandom);
                        }

                        if (decision.CardTypes.Count < phaseCount)
                            Console.WriteLine($"[CPU {seat.RobotID}] hand has only {seat.Hand.Count} card(s) for {phaseCount} register(s); it cannot be Ready.");

                        bool applied = _dataService.SetRobotProgram(seat.RobotID, decision.CardTypes, decision.Facing);
                        Console.WriteLine($"[CPU {seat.RobotID}] Level {seat.Level} program " +
                            $"[{string.Join(",", decision.CardTypes)}]" +
                            (decision.Facing.HasValue ? $" facing {decision.Facing}" : "") +
                            (applied ? "" : " REFUSED"));

                        if (applied && BotProfile.ForLevel(seat.Level).Stages.Length > 0)
                        {
                            toSearch.Add(new BotSeat(seat.RobotID, seat.Level, seat.Hand, seat.DamageInDiscard,
                                DrawPool: seat.DrawPool, ChooseFacing: seat.PositionValid == 0));
                            placeholders[seat.RobotID] = decision.CardTypes;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CPU {seat.RobotID}] programming failed: {ex.Message}");
                    }
                }

                // Computer robots' lights stay off during programming (SetAllRobotLights skips
                // them on the way in); this covers the first turn, when they may still be on.
                foreach (var player in AllPlayers.Where(p => p.Computer > 0))
                {
                    _ = Task.Run(async () =>
                    {
                        try { await player.SetLightsAsync(false); }
                        catch (Exception ex) { Console.WriteLine($"[{player.ID}] SetLightsAsync error: {ex.Message}"); }
                    });
                }

                if (toSearch.Count > 0 && BotSearchEnabled && (onlyRobotId == null || !ComputerRunActive()))
                    StartComputerRun(toSearch, placeholders);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Computer players: programming failed: {ex.Message}");
            }
        }

        /// <summary>
        /// The state-4 gate's extra condition (design gap 13), checked only once every robot is
        /// Ready (BotTiming.MayLockIn). With humans at the table it is true at once -- and tells
        /// Levels 2-4 to stop -- unless an Expert is still inside its grace. With no human
        /// programming, false until every computer's time is up. Either way the poll releases the
        /// gate as soon as the search finishes. The deadlines are checked here against the run's own
        /// clock, so a dead worker cannot hold the game past them.
        /// </summary>
        private bool MayLockIn()
        {
            ComputerRun? run;
            lock (_computerRunLock) run = _computerRun;

            // The gate only asks once every robot is Ready, so with a human clock the humans are
            // done right now. Stamp it here too (BotTiming.MayLockIn does, but the check below
            // short-circuits once the search has finished), so the log line is always accurate.
            if (run != null && run.Timing.HumanClock) run.Timing.StampHumansDone();

            if (run == null || run.Finished || run.Cancel.IsCancellationRequested || run.Timing.MayLockIn())
            {
                _dataService.TitleNote = null;
                // Evidence for the timing checks in design §6.3 / §6.4: lock-in should follow
                // "humans done" at once (Levels 1-4) or within the Expert grace.
                if (run != null && !run.Cancel.IsCancellationRequested)
                    Console.WriteLine($"Computer players: lock-in allowed at {run.Clock.ElapsedMilliseconds} ms" +
                        (run.Timing.HumanClock ? $" (humans done at {run.Timing.HumansDoneAtMs} ms)" : " (no human clock)"));
                return true;
            }

            if (!run.Holding)
            {
                run.Holding = true;
                _dataService.TitleNote = "- waiting for computer players";
                Console.WriteLine("Computer players: lock-in held while the computers finish thinking.");
            }
            return false;
        }

        private bool ComputerRunActive()
        {
            lock (_computerRunLock)
                return _computerRun != null && !_computerRun.Finished && !_computerRun.Cancel.IsCancellationRequested;
        }

        /// <summary>
        /// GM screen (Shut Down view): makes a robot a computer player at a level (1-5), or hands
        /// it back to its player (0). Allowed any time a game is running. During programming a new
        /// computer is programmed at once (ProgramComputerPlayers(robotId)), so the turn doesn't
        /// wait on it; a robot handed back keeps whatever is in its registers for the player to
        /// keep or change. Returns an error message, or null.
        /// </summary>
        public string? SetComputerLevel(int robotId, int level)
        {
            if (level < 0 || level > BotProfile.MaxLevel) return $"Level must be 0-{BotProfile.MaxLevel}";
            int gameState = _dataService.GameState;
            if (gameState < 2 || gameState == 25) return "No game is running";
            if (!_dataService.SetRobotComputer(robotId, level)) return $"Robot {robotId} not found";

            _dataService.GetAllPlayers(true); // Player.Computer: lights, ScreenUI
            if (gameState == 4 && level > 0) ProgramComputerPlayers(robotId);
            UpdateGameState();
            return null;
        }

        private void CancelComputerRun()
        {
            ComputerRun? old;
            lock (_computerRunLock)
            {
                old = _computerRun;
                _computerRun = null;
            }
            if (old == null) return;
            old.Cancel.Cancel();
            old.Poll?.Dispose();
            if (old.Holding) _dataService.TitleNote = null;
        }

        private void StartComputerRun(List<BotSeat> seats, Dictionary<int, IReadOnlyList<int>> placeholders)
        {
            var run = new ComputerRun();
            run.Timing = new BotTiming(
                humanClock: _dataService.HumansStillProgramming() > 0,
                hasExpert: seats.Any(s => s.Profile.ReRank),
                BotSoloBudgetMs, BotGraceSoftMs, BotGraceHardMs,
                () => run.Clock.ElapsedMilliseconds);
            lock (_computerRunLock) _computerRun = run;

            var worker = new Thread(() => ComputerRunWorker(run, seats, placeholders))
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal, // a phone tap must not feel slower (§5.9)
                Name = "ComputerPlayers",
            };
            worker.Start();

            int pollMs = BotStopPollMs;
            run.Poll = new Timer(_ => PollComputerRun(run), null, pollMs, pollMs);
        }

        private bool ShouldStop(ComputerRun run, BotSeat seat) =>
            run.Cancel.IsCancellationRequested
            || _dataService.GameState != 4
            || run.Timing.StopFor(seat.Level);

        private void ComputerRunWorker(ComputerRun run, List<BotSeat> seats, Dictionary<int, IReadOnlyList<int>> current)
        {
            long plans = 0;
            bool complete = false;
            try
            {
                var snapshot = _dataService.BuildBotSnapshot();
                var evaluator = new ProgramEvaluator(snapshot, new FlagDistances(snapshot.Board, snapshot.TotalFlags));
                Random rng;
                lock (s_botRandom) rng = new Random(s_botRandom.Next());

                complete = BotScheduler.Run(snapshot, seats, current, evaluator, rng,
                    shouldStop: seat => ShouldStop(run, seat),
                    onImproved: (seat, program, stage) =>
                    {
                        try
                        {
                            if (run.Cancel.IsCancellationRequested) return false;
                            // The GM may have handed this robot back to its player mid-search.
                            if (!_dataService.IsComputer(seat.RobotID)) return false;
                            bool applied = _dataService.SetRobotProgram(seat.RobotID, program.Program, (int?)program.Facing);
                            Console.WriteLine($"[CPU {seat.RobotID}] {seat.Profile.Name} stage {stage} program " +
                                $"[{string.Join(",", program.Program)}] score {program.Score:0}" +
                                (program.Facing.HasValue ? $" facing {(int)program.Facing.Value}" : "") +
                                $" at {run.Clock.ElapsedMilliseconds} ms" + (applied ? "" : " REFUSED"));
                            if (applied) UpdateGameState();
                            return applied;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[CPU {seat.RobotID}] applying a program failed: {ex.Message}");
                            return false;
                        }
                    },
                    onShutDown: (seat, verdict) =>
                    {
                        try
                        {
                            if (run.Cancel.IsCancellationRequested || _dataService.GameState != 4) return false;
                            if (!_dataService.IsComputer(seat.RobotID)) return false;
                            bool applied = _dataService.SetComputerShutDown(seat.RobotID, verdict.ShutDown);
                            Console.WriteLine($"[CPU {seat.RobotID}] {(verdict.ShutDown ? "shutting down" : "not shutting down")}: " +
                                $"{verdict.Reason} at {run.Clock.ElapsedMilliseconds} ms" + (applied ? "" : " REFUSED"));
                            if (applied) UpdateGameState();
                            return applied;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[CPU {seat.RobotID}] shut-down change failed: {ex.Message}");
                            return false;
                        }
                    },
                    mayStartRound: () => run.Timing.MayStartRound);
                plans = evaluator.PlansRun;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Computer players: search failed, placeholders stand: {ex.Message}");
            }
            finally
            {
                run.Finished = true;
                Console.WriteLine($"Computer players: {(complete ? "search complete" : "search stopped")} after " +
                    $"{run.Clock.ElapsedMilliseconds} ms, {plans} plans" +
                    (run.Timing.HumanClock
                        ? (run.Timing.HumansDone ? $"; humans done at {run.Timing.HumansDoneAtMs} ms" : "; humans still programming")
                        : "; no human programming"));
            }
        }

        /// <summary>
        /// Every StopPollMs: notices the humans finishing (design §5.7 step 3) and, when the gate
        /// is holding, releases it once the search is finished or out of time.
        /// </summary>
        private void PollComputerRun(ComputerRun run)
        {
            if (Interlocked.Exchange(ref run.Polling, 1) == 1) return;
            try
            {
                if (run.Cancel.IsCancellationRequested) { run.Poll?.Dispose(); return; }

                if (_dataService.GameState != 4)
                {
                    run.Cancel.Cancel();
                    run.Poll?.Dispose();
                    if (run.Holding) _dataService.TitleNote = null;
                    return;
                }

                if (run.Timing.HumanClock && !run.Timing.HumansDone && _dataService.HumansStillProgramming() == 0)
                    run.Timing.StampHumansDone();

                if (run.Holding && (run.Finished || run.Timing.PastAllDeadlines))
                {
                    run.Holding = false;
                    _dataService.TitleNote = null;
                    Console.WriteLine($"Computer players: releasing lock-in at {run.Clock.ElapsedMilliseconds} ms.");
                    run.Poll?.Dispose();
                    AdvanceFromProgramming();
                    return;
                }

                if (run.Finished && !run.Holding) run.Poll?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Computer players: poll failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref run.Polling, 0);
            }
        }

        /// <summary>
        /// NextState() ignores a call while another is running (design gap 10), so retry briefly
        /// until the state has moved off 4. The GM's Next State button works as a fallback.
        /// </summary>
        private void AdvanceFromProgramming()
        {
            for (int attempt = 0; attempt < 20 && _dataService.GameState == 4; attempt++)
            {
                NextState();
                if (_dataService.GameState != 4) return;
                Thread.Sleep(100);
            }
        }
    }
}
