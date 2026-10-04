using System.Collections.Concurrent;

namespace MRR.Devices
{
    /// <summary>
    /// LED + speaker effects for events a player can't see on the board (laser fired, damage,
    /// flag touched, energy gained, ...). Decoration only: never delays a turn, never throws
    /// into the command pipeline. See documents/ROBOT_EFFECTS_DESIGN.md.
    ///
    /// Entry point is <see cref="Play"/>, called from Player.SendRobotCommandAsync for the
    /// robot-bound commands that have no move/turn/colour/flash meaning. Effects are plain
    /// data (a table of <see cref="Step"/> lists) so a colour or sound is a one-line edit.
    /// </summary>
    public static class RobotEffects
    {
        /// <summary>Master switch (no GM toggle yet -- see design doc section 4).</summary>
        public static bool Enabled { get; set; } = true;
        public static int Volume { get; set; } = 80;

        // At most this many effects waiting per robot; extras are dropped so a burst of events
        // can't leave a robot playing last phase's effects for minutes.
        private const int MaxQueued = 3;

        // ── Steps ───────────────────────────────────────────────────────────────
        private abstract record Step;
        private sealed record Led(string Name, int R, int G, int B) : Step;
        private sealed record Snd(string Name) : Step;
        private sealed record Wait(int Ms) : Step;

        private static readonly string[] Ring = { "light1", "light2", "light3", "light4", "light5", "light6" };

        private static Step All(int r, int g, int b) => new Led("all", r, g, b);
        private static Step Off() => new Led("all", 0, 0, 0);

        // ── Effects ─────────────────────────────────────────────────────────────
        // Returns null for commands with no effect.
        private static List<Step>? Build(CommandItem cmd)
        {
            switch (cmd.CommandType)
            {
                case SquareAction.FireCannon: // shooter only; the victim's effect comes from Damage
                {
                    var s = new List<Step> { new Snd("send") }; //
                    foreach (var led in Ring)
                    {
                        s.Add(new Led(led, 255, 255, 255));
                        s.Add(new Wait(30));
                    }
                    s.Add(All(255, 0, 0));
                    s.Add(new Wait(120));
                    s.Add(Off());
                    return s;
                }

                case SquareAction.Damage:
                    if (cmd.Value <= 0) return null; // negative = repair
                    return new List<Step>
                    {
                        new Snd("crash"),
                        All(255, 0, 0), new Wait(120), Off(), new Wait(100),
                        All(255, 0, 0), new Wait(120), Off(),
                    };

                case SquareAction.Flag:
                case SquareAction.TouchFlag:
                case SquareAction.TouchKotHFlag:
                case SquareAction.TouchLastManFlag:
                {
                    // Value = flag number reached (0 = flag cleared for another robot: no effect).
                    if (cmd.Value <= 0) return null;
                    var s = new List<Step> { new Snd("tada"), Off() };
                    for (int i = 0; i < Math.Min(cmd.Value, Ring.Length); i++)
                    {
                        s.Add(new Led(Ring[i], 255, 180, 0));
                        s.Add(new Wait(80));
                    }
                    s.Add(new Wait(800));
                    return s;
                }

                case SquareAction.SetEnergy: // only ever emitted when energy is gained
                    return new List<Step> { new Snd("pickup"), All(0, 255, 0), new Wait(300) };

                case SquareAction.PlayOptionCard:
                    return new List<Step>
                    {
                        new Snd("flourish"),
                        All(160, 0, 255), new Wait(250), Off(), new Wait(100), All(160, 0, 255), new Wait(250),
                    };

                case SquareAction.Dead:
                {
                    var s = new List<Step> { new Snd("act_sad") };
                    for (int level = 255; level > 0; level -= 51)
                    {
                        s.Add(All(level, 0, 0));
                        s.Add(new Wait(150));
                    }
                    s.Add(Off());
                    return s;
                }

                case SquareAction.SetShutDownMode:
                    return new List<Step> { new Snd("pause"), All(40, 40, 40), new Wait(400) };

                case SquareAction.GameWinner:
                {
                    var s = new List<Step> { new Snd("cheer") };
                    (int r, int g, int b)[] colors = { (255, 0, 0), (255, 160, 0), (255, 255, 0), (0, 255, 0), (0, 128, 255), (160, 0, 255) };
                    for (int lap = 0; lap < 3; lap++)
                        for (int i = 0; i < Ring.Length; i++)
                        {
                            s.Add(new Led(Ring[i], colors[i].r, colors[i].g, colors[i].b));
                            s.Add(new Wait(60));
                        }
                    return s;
                }

                default:
                    return null;
            }
        }

        // ── Runner ──────────────────────────────────────────────────────────────
        private sealed class Gate
        {
            public readonly SemaphoreSlim Lock = new(1, 1);
            public int Pending;
        }

        private static readonly ConcurrentDictionary<int, Gate> Gates = new();

        /// <summary>
        /// Starts the effect for <paramref name="cmd"/> on <paramref name="player"/>'s robot, if
        /// it has one. Returns immediately; the effect runs on its own task and one robot only
        /// ever plays one effect at a time.
        /// </summary>
        public static void Play(Player player, CommandItem cmd)
        {
            if (!Enabled) return;
            var connection = player.Connection;
            if (connection == null || !connection.IsConnected) return;

            var steps = Build(cmd);
            if (steps == null) return;

            _ = RunAsync(connection, steps).ContinueWith(
                t => Console.WriteLine($"[{connection.RobotID}] effect failed: {t.Exception?.GetBaseException().Message}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        private static async Task RunAsync(RobotConnection connection, List<Step> steps)
        {
            var gate = Gates.GetOrAdd(connection.RobotID, _ => new Gate());
            if (Interlocked.Increment(ref gate.Pending) > MaxQueued)
            {
                Interlocked.Decrement(ref gate.Pending);
                return;
            }

            await gate.Lock.WaitAsync();
            try
            {
                bool touchedLeds = false;
                foreach (var step in steps)
                {
                    if (!connection.IsConnected) return;
                    switch (step)
                    {
                        case Snd s:
                            await connection.PlaySoundAsync(s.Name, Volume);
                            break;
                        case Wait w:
                            await Task.Delay(w.Ms);
                            break;
                        case Led l:
                            // The "waiting for you" ring spin owns the LEDs while it runs.
                            if (connection.Flash) break;
                            await connection.SetLedAsync(l.Name, l.R, l.G, l.B);
                            touchedLeds = true;
                            break;
                    }
                }

                if (touchedLeds && connection.IsConnected && !connection.Flash)
                    await connection.RestoreLightsAsync();
            }
            finally
            {
                gate.Lock.Release();
                Interlocked.Decrement(ref gate.Pending);
            }
        }
    }
}
