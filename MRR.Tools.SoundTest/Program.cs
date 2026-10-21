// Manual hardware check: play every built-in AIM sound, one at a time. Connects directly to one
// robot over its WebSocket API (no game, no database -- same spirit as MRR.Tools.FontTest) and
// sends play_sound for each name in turn, so you can hear what each one is and note which suit
// which game event (see documents/ROBOT_EFFECTS_DESIGN.md).
//
// Usage:
//   dotnet run --project MRR.Tools.SoundTest -- <robot-ip> [options] [sound ...]
//
//   sound ...     sounds to play (default: all of them), e.g. tada fail
//   --volume N    0-100 (default 60)
//   --auto N      advance every N seconds instead of waiting for Enter (default: wait for Enter)
//   --notes       after the sounds, also play a C-to-B scale in octave 6 with play_note
//
// The game server must not be connected to the same robot while this runs (disconnect it from
// the GM screen, or `mrrctl stop game`).

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

// SoundType built-in names, from .claude/agents/aim-robot-api.md section 5.
string[] allSounds =
{
    "doorbell", "tada", "fail", "sparkle", "flourish",
    "forward", "reverse", "right", "left", "blinker",
    "crash", "brakes", "huah", "pickup", "cheer",
    "sensing", "detected", "obstacle", "looping", "complete",
    "pause", "resume", "send", "receive",
    "act_happy", "act_sad", "act_excited", "act_angry", "act_silly",
};
string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

// ── Arguments ────────────────────────────────────────────────────────────────
if (args.Length == 0 || args[0].StartsWith("-"))
{
    Console.WriteLine("Usage: dotnet run --project MRR.Tools.SoundTest -- <robot-ip> [--volume N] [--auto N] [--notes] [sound ...]");
    return 2;
}

string robotIp = args[0];
int volume = 60;
int autoSeconds = 0;
bool playNotes = false;
var sounds = new List<string>();
for (int i = 1; i < args.Length; i++)
{
    if (args[i] == "--volume" && i + 1 < args.Length) volume = int.Parse(args[++i]);
    else if (args[i] == "--auto" && i + 1 < args.Length) autoSeconds = int.Parse(args[++i]);
    else if (args[i] == "--notes") playNotes = true;
    else sounds.Add(args[i].ToLowerInvariant());
}
if (sounds.Count == 0) sounds.AddRange(allSounds);

// ── Connect ──────────────────────────────────────────────────────────────────
using var socket = new ClientWebSocket();
Console.WriteLine($"Connecting to ws://{robotIp}/ws_cmd ...");
await socket.ConnectAsync(new Uri($"ws://{robotIp}/ws_cmd"), CancellationToken.None);
await Send(new { cmd_id = "program_init" });
Console.WriteLine($"Connected. Volume {volume}.");

// ── Play ─────────────────────────────────────────────────────────────────────
for (int i = 0; i < sounds.Count; i++)
{
    await Send(new { cmd_id = "stop_sound" });
    Console.WriteLine($"[{i + 1}/{sounds.Count}] {sounds[i]}");
    await Send(new { cmd_id = "play_sound", name = sounds[i], volume });
    if (!await Advance()) return 0;
}

if (playNotes)
{
    Console.WriteLine("Scale: C to B, octave 6");
    foreach (string note in noteNames)
    {
        Console.WriteLine($"  {note}6");
        await Send(new { cmd_id = "play_note", note = Array.IndexOf(noteNames, note), octave = 6, duration = 400, volume });
        await Task.Delay(500);
    }
}

Console.WriteLine("Done.");
return 0;

// Waits for Enter (false = the user typed q) or for --auto seconds.
async Task<bool> Advance()
{
    if (autoSeconds > 0) { await Task.Delay(autoSeconds * 1000); return true; }
    Console.Write("  Enter for the next sound (q + Enter to quit): ");
    return (Console.ReadLine() ?? "").Trim().ToLowerInvariant() != "q";
}

// Sends one command and waits for the robot's reply, as RobotConnection.SendCommandAsync does.
async Task Send(object command)
{
    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(command);
    await socket.SendAsync(bytes, WebSocketMessageType.Binary, true, CancellationToken.None);

    var buffer = new byte[4096];
    using var reply = new MemoryStream();
    WebSocketReceiveResult result;
    do
    {
        result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close)
            throw new WebSocketException("The robot closed the connection.");
        reply.Write(buffer, 0, result.Count);
    } while (!result.EndOfMessage);

    string text = Encoding.UTF8.GetString(reply.ToArray());
    if (text.Contains("cmd_unknown") || text.Contains("\"error\""))
        Console.WriteLine($"  robot rejected {Encoding.UTF8.GetString(bytes)} -> {text}");
}
