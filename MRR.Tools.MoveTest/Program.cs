// Robot movement Phase 0 experiments (documents/ROBOT_MOVEMENT_HANDOFF.md, "Next steps" 1).
//
// Talks to one AIM robot directly over ws_cmd + ws_status, outside the game. Run it with the
// game host STOPPED: the server must not be holding the robot's sockets, and whether an AIM
// accepts two controllers at once has not been tested.
//
// Every command is logged as one JSONL line (pose before/after, ack, elapsed ms, and any
// hand measurement typed in) to <log-dir>/<robot>-<experiment>-<time>.jsonl.
//
// Moves and turns are sent exactly as the game sends them (RobotConnection.MoveAndWaitAsync /
// TurnAsync): drive_for distance = squares x 75 mm, drive_speed 200, final_heading 0,
// turn_for angle = n x 90 at turn_rate 200, unless an experiment varies one of those.

using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using MRR.Tools.MoveTest;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

const int MmPerSquare = 75; // what the game drives per square (76 - 1, RobotConnection.MoveAndWaitAsync)

// The AIM hostnames from the RobotBases seed in install/MRRDatabase.sql.
var aimHosts = new Dictionary<string, string>
{
    ["1"] = "AIM-328D8418", ["2"] = "AIM-426E8118", ["3"] = "AIM-1AA57518", ["4"] = "AIM-3A8A8B18",
    ["5"] = "AIM-12598D18", ["6"] = "AIM-427D7018", ["7"] = "AIM-1A5E7918",
};

string? robotArg = null, experiment = null;
string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "images", "movetest");
int speed = 200, turnRate = 200, turnSpeed = 0, reps = 0, squares = 8;
bool calibrate = true, measure = false, yes = false, ignoreHost = false;

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
    switch (args[i])
    {
        case "--robot": robotArg = Next(); break;
        case "--log-dir": logDir = Next(); break;
        case "--speed": speed = int.Parse(Next()); break;
        case "--turn-rate": turnRate = int.Parse(Next()); break;
        case "--turn-speed": turnSpeed = int.Parse(Next()); break;
        case "--reps": reps = int.Parse(Next()); break;
        case "--squares": squares = int.Parse(Next()); break;
        case "--no-calibrate": calibrate = false; break;
        case "--measure": measure = true; break;
        case "--yes": yes = true; break;
        case "--ignore-game-host": ignoreHost = true; break;
        case "-h": case "--help": PrintHelp(); return 0;
        default:
            if (args[i].StartsWith('-')) { Console.Error.WriteLine($"Unknown option {args[i]}"); return 2; }
            experiment = args[i];
            break;
    }
}

string[] experiments = ["status", "heading-hold", "odom-frame", "pose-heading", "odometry", "hand-rotate", "turn-rate", "turn-to", "abs-turn", "speed", "moves", "loop"];
if (robotArg == null || experiment == null || !experiments.Contains(experiment))
{
    PrintHelp();
    return 2;
}

if (!ignoreHost && GameHostListening())
{
    Console.Error.WriteLine("The game host is listening on port 5000. Stop it first (it may hold this robot's sockets),");
    Console.Error.WriteLine("or pass --ignore-game-host if you are sure it is not connected to this robot.");
    return 3;
}

string host = aimHosts.GetValueOrDefault(robotArg, robotArg);
bool moves = experiment is not ("status" or "pose-heading" or "hand-rotate");

Console.WriteLine($"Robot {host}, experiment {experiment}");
if (moves && !yes)
{
    Console.WriteLine("This experiment MOVES the robot. Table clear, one robot, hand near it? Ctrl+C stops it.");
    if (!string.Equals(Prompt("Type y to continue"), "y", StringComparison.OrdinalIgnoreCase)) return 1;
}

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    if (cancel.IsCancellationRequested) return; // second Ctrl+C: let the process die
    e.Cancel = true;
    cancel.Cancel();
    Console.WriteLine("\nStopping...");
};

Directory.CreateDirectory(logDir);
string logPath = Path.Combine(logDir, $"{host}-{experiment}-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
await using var log = new StreamWriter(logPath, append: false) { AutoFlush = true };

await using var robot = new AimMoveClient(host);
try
{
    await robot.ConnectAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not connect to {host}: {ex.Message}");
    return 4;
}

int step = 0;
try
{
    var start = await robot.GetPoseAsync();
    Console.WriteLine($"Connected. Battery {start.Battery}%. {start}");
    Log("connect", null, null, null, start, 0);

    if (calibrate && experiment != "status")
    {
        await Calibrate();
    }

    switch (experiment)
    {
        case "status": await StatusExp(); break;
        case "heading-hold": await HeadingHoldExp(); break;
        case "odom-frame": await OdomFrameExp(); break;
        case "pose-heading": await PoseHeadingExp(); break;
        case "odometry": await OdometryExp(); break;
        case "hand-rotate": await HandRotateExp(); break;
        case "turn-rate": await TurnRateExp(); break;
        case "turn-to": await TurnToExp(); break;
        case "abs-turn": await AbsTurnExp(); break;
        case "speed": await SpeedExp(); break;
        case "moves": await MovesExp(); break;
        case "loop": await LoopExp(); break;
    }
}
catch (OperationCanceledException ex)
{
    Console.WriteLine(ex.Message);
    Log("aborted", null, null, null, null, 0, ex.Message);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed: {ex.Message}");
    Log("error", null, null, null, null, 0, ex.Message);
    try { await robot.StopAsync(); } catch { /* connection may be gone */ }
}

Console.WriteLine($"Log: {logPath}");
return 0;

// ── Experiments ──────────────────────────────────────────────────────────────

// Prints the pose every 0.5 s until Enter or Ctrl+C. Sends nothing that moves the robot.
async Task StatusExp()
{
    // With no keyboard (input redirected), take 5 readings and stop.
    bool keyboard = !Console.IsInputRedirected;
    Console.WriteLine(keyboard ? "Printing status every 0.5 s. Press Enter to stop." : "Printing 5 readings.");
    for (int n = 0; !cancel.IsCancellationRequested && (keyboard || n < 5); n++)
    {
        if (keyboard && Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Enter) break;
        Console.WriteLine(await robot.GetPoseAsync());
        await Task.Delay(500);
    }
}

// #1: does final_heading hold a heading, and relative to what?
async Task HeadingHoldExp()
{
    int first = -1;
    foreach (var target in new int?[] { 0, 90, null })
    {
        Pause("Put the robot on its start mark and turn it by hand about 20 degrees off. Enter when ready");
        var pre = await robot.GetPoseAsync();
        int fh = target ?? (int)Math.Round(pre.Heading) % 360;
        if (target == null && fh == 0) fh = 1; // 0 means "don't correct"
        if (first < 0) first = fh;
        Console.WriteLine($"Heading now {pre.Heading:F1}. drive_for 150 mm with final_heading = {fh}");
        await Drive(150, 0, speed, fh, $"final_heading={fh}" + (target == null ? " (current heading)" : ""));
    }
}

// #2: which way do robot_x / robot_y point after a turn?
async Task OdomFrameExp()
{
    Pause("Put the robot on its start mark, facing forward. Enter when ready");
    foreach (int quarter in new[] { 0, 1, 2, 3 })
    {
        if (quarter > 0) await Turn(quarter * 90, turnRate, $"turn to {quarter * 90}");
        await SetPose(0, 0);
        var post = await Drive(MmPerSquare, 0, speed, 0, $"forward 75 after turning {quarter * 90}");
        Console.WriteLine($"  after {quarter * 90,3} deg: moved x={post.X:F1} y={post.Y:F1}  (heading {post.Heading:F1})");
        await Drive(MmPerSquare, 180, speed, 0, "back to start");
        if (quarter > 0) await Turn(-quarter * 90, turnRate, "turn back");
    }
}

// #3: does set_pose accept a heading? Field names are guesses; the ack and status are logged.
async Task PoseHeadingExp()
{
    foreach (var cmd in new object[]
             {
                 new { cmd_id = "set_pose", x = 0, y = 0, heading = 45 },
                 new { cmd_id = "set_pose", x = 0, y = 0, rotation = 45 },
                 new { cmd_id = "set_pose", x = 0, y = 0, theta = 45 },
             })
    {
        var pre = await robot.GetPoseAsync();
        string ack = await robot.SendAsync(cmd);
        await Task.Delay(200);
        var post = await robot.GetPoseAsync();
        Console.WriteLine($"{JsonSerializer.Serialize(cmd)}\n  ack {ack}\n  before {pre}\n  after  {post}");
        Log("pose-heading", cmd, ack, pre, post, 0);
    }
    await SetPose(0, 0);
}

// #4: odometry vs tape over `squares` drives out and the same number back.
async Task OdometryExp()
{
    Pause($"Mark the start. The robot drives {squares} x 75 mm forward and back. Enter when ready");
    await SetPose(0, 0);
    for (int i = 0; i < squares * 2; i++)
    {
        bool outbound = i < squares;
        var post = await Drive(MmPerSquare, outbound ? 0 : 180, speed, 0, outbound ? $"out {i + 1}" : $"back {i + 1 - squares}");
        Console.WriteLine($"  drive {i + 1,2}: odometry x={post.X:F1} y={post.Y:F1}");
    }
    MeasureFinal("Distance from the start mark");
}

// #5: does the heading zero survive moving the robot by hand?
async Task HandRotateExp()
{
    var pre = await robot.GetPoseAsync();
    Console.WriteLine($"After calibrate: {pre}");
    Pause("Rotate the robot by hand 90 degrees clockwise, without lifting it. Enter when done");
    var turned = await robot.GetPoseAsync();
    Console.WriteLine($"After hand turn: {turned}");
    Log("hand-turn-90", null, null, pre, turned, 0);
    Pause("Lift the robot, turn it back to where it started and set it down. Enter when done");
    var lifted = await robot.GetPoseAsync();
    Console.WriteLine($"After lift and replace: {lifted}");
    Log("lift-replace", null, null, turned, lifted, 0);
}

// Phase 1 candidate: turn_for +/-90, then turn_to the exact target heading. Does turn_to fix a
// sub-degree error, which way does it go across 0/360, and how long does it take?
async Task TurnToExp()
{
    Pause("Put the robot on its start mark. Enter when ready");
    int n = reps > 0 ? reps : 2;
    double target = 0; // calibrated heading at the start
    for (int r = 0; r < n; r++)
        foreach (int dir in new[] { 1, 1, 1, 1, -1, -1, -1, -1 })
        {
            target = ((target + dir * 90) % 360 + 360) % 360;
            var after = await Turn(dir * 90, turnRate, $"turn_for {dir * 90:+0;-0}");
            Console.WriteLine($"  error after turn_for {Wrap(after.Heading - target),5:F1}");
            var fixedPose = await Motion($"turn_to {target:F0}",
                new { cmd_id = "turn_to", heading = target, turn_rate = 45, stacking_type = 0 });
            Console.WriteLine($"  error after turn_to  {Wrap(fixedPose.Heading - target),5:F1}");
        }
}

// Phase 1 candidate: each turn_for angle = target heading - current heading (fractional),
// so every turn absorbs the previous error with no extra command.
async Task AbsTurnExp()
{
    Pause("Put the robot on its start mark. Enter when ready");
    int n = reps > 0 ? reps : 2;
    double target = 0;
    for (int r = 0; r < n; r++)
        foreach (int dir in new[] { 1, 1, 1, 1, -1, -1, -1, -1 })
        {
            target = ((target + dir * 90) % 360 + 360) % 360;
            var now = await robot.GetPoseAsync();
            double needed = Math.Round(Wrap(target - now.Heading), 1);
            if (Math.Abs(needed) < 45) needed += dir * 360; // not expected; keeps the turn direction if it happens
            var after = await Motion($"turn_for {needed:F1} (to {target:F0})",
                new { cmd_id = "turn_for", angle = needed, turn_rate = turnRate, stacking_type = 0 });
            Console.WriteLine($"  error {Wrap(after.Heading - target),5:F1}");
        }
}

// #6: is turn_rate clamped above 180? Duration and heading error per rate.
async Task TurnRateExp()
{
    Pause("Put the robot on its start mark. Enter when ready");
    int n = reps > 0 ? reps : 1;
    foreach (int rate in new[] { 100, 180, 200 })
        for (int r = 0; r < n; r++)
        {
            await Turn(90, rate, $"turn_rate {rate}");
            await Turn(-90, rate, $"turn_rate {rate} back");
        }
}

// #7: speed vs accuracy. Forward and back so the robot stays near its mark.
async Task SpeedExp()
{
    Pause("Put the robot on its start mark. Enter when ready");
    int n = reps > 0 ? reps : 10;
    foreach (int s in new[] { 200, 120, 80 })
        for (int r = 0; r < n; r++)
        {
            await SetPose(0, 0);
            await Drive(MmPerSquare, r % 2 == 0 ? 0 : 180, s, 0, $"speed {s}");
        }
}

// Phase 0 step 2: each game move type and its reverse, `reps` times.
async Task MovesExp()
{
    Pause("Put the robot on its start mark, with room for 3 squares ahead and 1 to the right. Enter when ready");
    int n = reps > 0 ? reps : 5;
    var pairs = new (string Name, Func<Task> Go, Func<Task> Back)[]
    {
        ("forward 1", () => Square(1, 0), () => Square(1, 180)),
        ("forward 3", () => Square(3, 0), () => Square(3, 180)),
        ("strafe right 1", () => Square(1, 90), () => Square(1, 270)),
        ("turn +90", () => Turn(90, turnRate, "turn +90"), () => Turn(-90, turnRate, "turn -90")),
        ("u-turn", () => Turn(180, turnRate, "u-turn"), () => Turn(180, turnRate, "u-turn")),
    };
    foreach (var p in pairs)
    {
        Console.WriteLine($"-- {p.Name} x {n}");
        for (int r = 0; r < n; r++)
        {
            await SetPose(0, 0);
            await p.Go();
            await p.Back();
        }
    }
}

// 16 game moves that should end on the start square, facing the start direction:
// 4 x (forward 2, turn right, then forward 1 + back 1 or strafe right 1 + strafe left 1).
async Task LoopExp()
{
    Pause("Mark the start square and facing. Needs a 3 x 3 square area ahead and to the right. Enter when ready");
    await SetPose(0, 0);
    for (int side = 0; side < 4; side++)
    {
        await Square(2, 0);
        await Turn(90, turnRate, "turn right");
        if (side % 2 == 0) { await Square(1, 0); await Square(1, 180); }
        else { await Square(1, 90); await Square(1, 270); }
    }
    MeasureFinal("Error from the start square");
}

// ── Helpers ──────────────────────────────────────────────────────────────────

async Task Calibrate()
{
    // The game sends imu_calibrate at connect and carries on at once. How long the IMU needs
    // to be still is not documented, so wait 2 s here (an assumption) before reading.
    Console.WriteLine("imu_calibrate (keep the robot still)...");
    string ack = await robot.SendAsync(new { cmd_id = "imu_calibrate" });
    await Task.Delay(2000);
    await SetPose(0, 0);
    var post = await robot.GetPoseAsync();
    Console.WriteLine($"Calibrated. {post}");
    Log("imu_calibrate", new { cmd_id = "imu_calibrate" }, ack, null, post, 0);
}

async Task SetPose(int x, int y)
{
    var cmd = new { cmd_id = "set_pose", x, y };
    string ack = await robot.SendAsync(cmd);
    Log("set_pose", cmd, ack, null, null, 0);
}

Task<Pose> Square(int count, int angle) =>
    Drive(count * MmPerSquare, angle, speed, 0, $"{count} square(s) at {angle}");

Task<Pose> Drive(int mm, int angle, int driveSpeed, int finalHeading, string label) =>
    Motion(label, new
    {
        cmd_id = "drive_for",
        distance = mm,
        angle,
        drive_speed = driveSpeed,
        turn_speed = turnSpeed,
        final_heading = finalHeading,
        stacking_type = 0,
    });

Task<Pose> Turn(int degrees, int rate, string label) =>
    Motion(label, new { cmd_id = "turn_for", angle = degrees, turn_rate = rate, stacking_type = 0 });

async Task<Pose> Motion(string label, object cmd)
{
    cancel.Token.ThrowIfCancellationRequested();
    var pre = await robot.GetPoseAsync();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    string ack = await robot.SendAsync(cmd);
    var post = await robot.WaitForStopAsync(TimeSpan.FromSeconds(10), cancel.Token);
    long ms = sw.ElapsedMilliseconds;
    double dHeading = Wrap(post.Heading - pre.Heading);
    Console.WriteLine($"{label,-32} {ms,5} ms  dx={post.X - pre.X,7:F1} dy={post.Y - pre.Y,7:F1} dHeading={dHeading,6:F1}");
    string? measured = measure ? Prompt("  measured error 'along,sideways,degrees' in mm/deg (blank to skip)") : null;
    Log(label, cmd, ack, pre, post, ms, measured);
    return post;
}

void MeasureFinal(string what)
{
    string m = Prompt($"{what}: 'forward,right,degrees' (mm, mm, deg clockwise; blank to skip)");
    Log("final-measurement", null, null, null, null, 0, m);
}

void Log(string label, object? cmd, string? ack, Pose? pre, Pose? post, long ms, string? note = null)
{
    var line = new
    {
        time = DateTime.Now.ToString("O"),
        robot = host,
        experiment,
        step = step++,
        label,
        cmd,
        ack,
        pre,
        post,
        elapsed_ms = ms,
        note = string.IsNullOrWhiteSpace(note) ? null : note,
    };
    log.WriteLine(JsonSerializer.Serialize(line));
}

static double Wrap(double deg) => ((deg % 360) + 540) % 360 - 180;

static string Prompt(string text)
{
    Console.Write($"{text}: ");
    return Console.ReadLine()?.Trim() ?? "";
}

void Pause(string text)
{
    cancel.Token.ThrowIfCancellationRequested();
    Prompt(text);
    cancel.Token.ThrowIfCancellationRequested();
}

static bool GameHostListening()
{
    try
    {
        using var tcp = new TcpClient();
        return tcp.ConnectAsync("127.0.0.1", 5000).Wait(500) && tcp.Connected;
    }
    catch
    {
        return false;
    }
}

static void PrintHelp()
{
    Console.WriteLine("""
        Robot movement Phase 0 experiments (documents/ROBOT_MOVEMENT_HANDOFF.md).
        Run with the game host stopped. Ctrl+C sends a stop.

        Usage:
          dotnet run --project MRR.Tools.MoveTest -- --robot <1-7|hostname|ip> <experiment> [options]

        Experiments (handoff table number in brackets):
          status        print the pose every 0.5 s; no movement
          heading-hold  [1] drive_for 150 mm with final_heading 0, 90, then the current heading
          odom-frame    [2] forward 75 mm after turning 0/90/180/270; which odometry axis moves
          pose-heading  [3] set_pose with heading/rotation/theta fields; no movement
          odometry      [4] --squares (8) x 75 mm out and back; type the tape reading at the end
          hand-rotate   [5] calibrate, turn by hand 90, lift and replace; no motor movement
          turn-rate     [6] turn_for +/-90 at turn_rate 100, 180, 200
          abs-turn      turn_for (target - current heading), fractional, 4 right + 4 left x --reps (2)
          turn-to       turn_for +/-90 (4 right, 4 left) x --reps (2), each followed by turn_to the exact
                        target heading at rate 45; in place, no hand steps
          speed         [7] --reps (10) x 75 mm at 200, 120, 80
          moves         Phase 0 step 2: forward 1, forward 3, strafe right 1, turn +90, u-turn,
                        each with its reverse, --reps (5) times
          loop          16 game moves that should end on the start square

        Options:
          --speed N           drive_speed for game-style moves (default 200, as the game)
          --turn-rate N       turn_rate for turns (default 200, as the game was before 2026-11-28)
          --turn-speed N      drive_for turn_speed (default 0, as the game sends)
          --reps N            repetitions (default per experiment)
          --squares N         odometry experiment length (default 8)
          --measure           after every move, ask for the hand-measured error
          --no-calibrate      skip imu_calibrate at the start
          --log-dir DIR       default ~/images/movetest
          --yes               skip the "table clear?" confirmation
          --ignore-game-host  run even though something listens on port 5000
        """);
}
