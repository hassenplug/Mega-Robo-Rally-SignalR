// Manual hardware check: which characters each AIM screen font can draw. Connects directly to
// one robot over its WebSocket API (no game, no database -- same spirit as
// MRR.Tools.RobotSpriteDraw) and prints every printable ASCII character (32-126) plus a few
// extras the game would like to use (arrows etc.) in each font, one screen at a time.
// Photograph each screen; a missing or wrong glyph means the font doesn't have it.
//
// Usage:
//   dotnet run --project MRR.Tools.FontTest -- <robot-ip> [options] [font ...]
//
//   font ...      fonts to test (default: all of them), e.g. mono20 mono40
//   --raw-utf8    send non-ASCII characters as raw UTF-8 instead of \uXXXX escapes (the game
//                 sends escapes -- compare the two if the extras don't show)
//   --auto N      advance every N seconds instead of waiting for Enter
//
// The game server must not be connected to the same robot while this runs (disconnect it from
// the GM screen, or `mrrctl stop game`).

using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

string[] allFonts =
{
    "mono12", "mono15", "mono20", "mono24", "mono30", "mono36", "mono40", "mono60",
    "prop20", "prop24", "prop30", "prop36", "prop40", "prop60",
};

// Non-ASCII characters to try: the seat arrows the game prints (↑ → ↓ ←), then a few others
// that would be handy if they exist.
const string Extras = "↑→↓←°±×÷éñü£€©■●▲►▼◄";

// ── Arguments ────────────────────────────────────────────────────────────────
if (args.Length == 0 || args[0].StartsWith("-"))
{
    Console.WriteLine("Usage: dotnet run --project MRR.Tools.FontTest -- <robot-ip> [--raw-utf8] [--auto N] [font ...]");
    return 2;
}

string robotIp = args[0];
bool rawUtf8 = false;
int autoSeconds = 0;
var fonts = new List<string>();
for (int i = 1; i < args.Length; i++)
{
    if (args[i] == "--raw-utf8") rawUtf8 = true;
    else if (args[i] == "--auto" && i + 1 < args.Length) autoSeconds = int.Parse(args[++i]);
    else fonts.Add(args[i].ToLowerInvariant());
}
if (fonts.Count == 0) fonts.AddRange(allFonts);

var jsonOptions = new JsonSerializerOptions
{
    Encoder = rawUtf8 ? JavaScriptEncoder.UnsafeRelaxedJsonEscaping : JavaScriptEncoder.Default,
};

// Every printable ASCII character, then the extras, as one list of strings (an extra may be
// more than one UTF-16 unit).
var characters = new List<string>();
for (int c = 32; c <= 126; c++) characters.Add(((char)c).ToString());
var extras = System.Globalization.StringInfo.GetTextElementEnumerator(Extras);
while (extras.MoveNext()) characters.Add((string)extras.Current);

// ── Connect ──────────────────────────────────────────────────────────────────
using var socket = new ClientWebSocket();
Console.WriteLine($"Connecting to ws://{robotIp}/ws_cmd ...");
await socket.ConnectAsync(new Uri($"ws://{robotIp}/ws_cmd"), CancellationToken.None);
await Send(new { cmd_id = "program_init" });
Console.WriteLine($"Connected. Sending non-ASCII as {(rawUtf8 ? "raw UTF-8" : "\\uXXXX escapes (same as the game)")}.");

// ── Draw ─────────────────────────────────────────────────────────────────────
// The screen is a 240x240 circle; keep the grid inside the square that fits in it.
const int Left = 40, Right = 200, Top = 52, Bottom = 196;

foreach (string font in fonts)
{
    int size = int.Parse(new string(font.Where(char.IsDigit).ToArray()));
    int cellWidth = Math.Max(1, (int)Math.Ceiling(size * (font.StartsWith("prop") ? 0.7 : 0.5)));
    int lineHeight = size + 2;
    int perLine = Math.Max(1, (Right - Left) / cellWidth);
    int linesPerScreen = Math.Max(1, (Bottom - Top) / lineHeight);
    int perScreen = perLine * linesPerScreen;
    int screens = (characters.Count + perScreen - 1) / perScreen;

    for (int screen = 0; screen < screens; screen++)
    {
        int first = screen * perScreen;
        var chunk = characters.Skip(first).Take(perScreen).ToList();

        await Send(new { cmd_id = "lcd_clear_screen", r = 0, g = 0, b = 0 });

        // Header in a small font: which font, which page.
        await Send(new { cmd_id = "lcd_set_pen_color", r = 255, g = 255, b = 0 });
        await Send(new { cmd_id = "lcd_set_fill_color", r = 0, g = 0, b = 0, transparent = false });
        await Send(new { cmd_id = "lcd_set_font", fontname = "mono15" });
        await Send(new { cmd_id = "lcd_print_at", @string = $"{font} {screen + 1}/{screens}", x = 62, y = 40, b_opaque = true });

        // One print per character at a fixed grid position, so a missing glyph can't shift the
        // ones after it. print_at's y is the text baseline.
        await Send(new { cmd_id = "lcd_set_pen_color", r = 255, g = 255, b = 255 });
        await Send(new { cmd_id = "lcd_set_font", fontname = font });
        var expected = new StringBuilder();
        for (int i = 0; i < chunk.Count; i++)
        {
            int col = i % perLine, row = i / perLine;
            if (col == 0 && i > 0) expected.AppendLine();
            expected.Append(chunk[i]);
            if (chunk[i] == " ") continue;
            await Send(new
            {
                cmd_id = "lcd_print_at",
                @string = chunk[i],
                x = Left + col * cellWidth,
                y = Top + (row + 1) * lineHeight - lineHeight / 5,
                b_opaque = true,
            });
        }

        Console.WriteLine();
        Console.WriteLine($"=== {font}  screen {screen + 1}/{screens}  (characters {first + 1}-{first + chunk.Count} of {characters.Count}) ===");
        Console.WriteLine("Should show (a blank first cell is the space character):");
        Console.WriteLine(expected.ToString());

        if (autoSeconds > 0) await Task.Delay(autoSeconds * 1000);
        else
        {
            Console.Write("Photograph it, then press Enter for the next screen (q + Enter to quit): ");
            if ((Console.ReadLine() ?? "").Trim().ToLowerInvariant() == "q") return 0;
        }
    }
}

await Send(new { cmd_id = "lcd_clear_screen", r = 0, g = 0, b = 0 });
Console.WriteLine("Done.");
return 0;

// Sends one command and waits for the robot's reply, as RobotConnection.SendCommandAsync does.
async Task Send(object command)
{
    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(command, jsonOptions);
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
