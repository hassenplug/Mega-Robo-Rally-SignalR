// AIM firmware comparison tool.
//
// Connects directly to two AIM robots by IP (bypassing the game/DB layer entirely --
// this is a hardware diagnostic, not a game action), sends each the same sequence of
// ws_cmd commands, and diffs the ACK/ws_status JSON shape between the two so a firmware
// upgrade's behavior change shows up as "field added/removed/changed type" rather than
// requiring a human to eyeball two walls of JSON.
//
// Usage:
//   dotnet run --project MRR.Tools.FirmwareCompare -- [--ip1 <addr>] [--ip2 <addr>] [--move]
//
// Defaults (from install/MRRDatabase.sql RobotBases seed / install/notes.txt):
//   robot #1 (AIM-01) = 192.168.1.149
//   robot #2 (AIM-02) = 192.168.1.206   <- the one with new firmware
//
// --move additionally sends turn_for/drive_for/kick_soft, which physically move the
// robot. Off by default -- confirm both robots have clear space before passing it.

using MRR.Tools.FirmwareCompare;

string ip1 = "192.168.1.149";
string ip2 = "192.168.1.206";
bool includeMotion = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--ip1" when i + 1 < args.Length: ip1 = args[++i]; break;
        case "--ip2" when i + 1 < args.Length: ip2 = args[++i]; break;
        case "--move": includeMotion = true; break;
        case "--help":
        case "-h":
            PrintHelp();
            return;
    }
}

Console.WriteLine("AIM Firmware Comparison Tool");
Console.WriteLine($"  Robot 1: {ip1}");
Console.WriteLine($"  Robot 2: {ip2} (new firmware)");
Console.WriteLine(includeMotion
    ? "  Motion commands ENABLED -- both robots will turn and drive a short distance."
    : "  Motion commands disabled (pass --move to include turn_for/drive_for/kick_soft).");
Console.WriteLine();

var commands = TestCommands.Build(includeMotion);

var results1 = await RunSequenceAsync("Robot 1", ip1, commands);
var results2 = await RunSequenceAsync("Robot 2", ip2, commands);

PrintReport(commands, results1, results2);

static async Task<Dictionary<string, (string Ack, string Status)>> RunSequenceAsync(
    string label, string ip, List<TestCommand> commands)
{
    var results = new Dictionary<string, (string, string)>();
    Console.WriteLine($"--- Connecting to {label} at {ip} ---");

    await using var client = new AimTestClient(label, ip);
    try
    {
        await client.ConnectAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  [{label}] connection failed: {ex.Message}");
        return results;
    }

    foreach (var cmd in commands)
    {
        try
        {
            var ack = await client.SendAsync(cmd.Payload);
            if (cmd.IsMotion)
                await client.WaitUntilStoppedAsync(TimeSpan.FromSeconds(8));
            var status = await client.PollStatusAsync();

            results[cmd.Name] = (ack, status);
            Console.WriteLine($"  [{label}] {cmd.Name}: {ack}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [{label}] {cmd.Name} FAILED: {ex.Message}");
            results[cmd.Name] = ("", "");
        }

        await Task.Delay(150);
    }

    return results;
}

static void PrintReport(
    List<TestCommand> commands,
    Dictionary<string, (string Ack, string Status)> r1,
    Dictionary<string, (string Ack, string Status)> r2)
{
    Console.WriteLine();
    Console.WriteLine("================ COMPARISON ================");

    bool anyDiff = false;

    foreach (var cmd in commands)
    {
        r1.TryGetValue(cmd.Name, out var a);
        r2.TryGetValue(cmd.Name, out var b);

        Console.WriteLine();
        Console.WriteLine($"### {cmd.Name}");
        Console.WriteLine($"  robot1 ack:    {a.Ack}");
        Console.WriteLine($"  robot2 ack:    {b.Ack}");

        anyDiff |= ReportDiff("ack", JsonShapeDiff.Compare(a.Ack, b.Ack));
        anyDiff |= ReportDiff("status", JsonShapeDiff.Compare(a.Status, b.Status));
    }

    Console.WriteLine();
    Console.WriteLine(anyDiff
        ? "Structural differences found -- see '!!' lines above."
        : "No structural differences detected between the two robots' responses.");
}

static bool ReportDiff(string label, JsonShapeDiff.Result diff)
{
    if (!diff.HasDifferences) return false;

    foreach (var f in diff.OnlyInA) Console.WriteLine($"  !! {label}: only robot1 has {f.Path} = {f.Value}");
    foreach (var f in diff.OnlyInB) Console.WriteLine($"  !! {label}: only robot2 has {f.Path} = {f.Value}");
    foreach (var k in diff.TypeMismatch) Console.WriteLine($"  !! {label}: JSON type differs at {k}");

    return true;
}

static void PrintHelp()
{
    Console.WriteLine("""
        AIM Firmware Comparison Tool

        Sends the same sequence of AIM ws_cmd commands to two robots and diffs the
        shape of their ACK/ws_status JSON responses.

        Options:
          --ip1 <addr>   IP address of robot 1 (default 192.168.1.149, AIM-01)
          --ip2 <addr>   IP address of robot 2 (default 192.168.1.206, AIM-02)
          --move         Also send turn_for/drive_for/kick_soft (physically moves both
                          robots -- make sure they have clear space first)
          --help         Show this message
        """);
}
