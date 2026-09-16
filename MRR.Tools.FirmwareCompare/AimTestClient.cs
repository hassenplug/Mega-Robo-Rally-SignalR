using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MRR.Tools.FirmwareCompare;

/// <summary>
/// Minimal, standalone AIM WebSocket client used only by this diagnostic tool.
/// Deliberately independent of MRR.Devices.RobotConnection: that class is the game's
/// single owner of a live robot socket (see documents/RobotConnections.md) and is wired
/// to the rally DB, game state, and the full connect sequence (LCD arrow, name, etc).
/// This tool exists to compare two robots' raw firmware responses byte-for-byte, outside
/// any game context, so it talks ws_cmd/ws_status directly using the wire format documented
/// in .claude/agents/aim-robot-api.md.
/// </summary>
public sealed class AimTestClient : IAsyncDisposable
{
    private readonly string _ip;
    private ClientWebSocket? _cmd;
    private ClientWebSocket? _status;

    public string Label { get; }

    public AimTestClient(string label, string ip)
    {
        Label = label;
        _ip = ip;
    }

    public async Task ConnectAsync()
    {
        _cmd = new ClientWebSocket();
        _status = new ClientWebSocket();
        await _cmd.ConnectAsync(new Uri($"ws://{_ip}:80/ws_cmd"), CancellationToken.None);
        await _status.ConnectAsync(new Uri($"ws://{_ip}:80/ws_status"), CancellationToken.None);

        // Robot rejects all other commands until this is received.
        await SendAsync(new { cmd_id = "program_init" });
    }

    /// <summary>Sends one JSON command as a binary frame and returns the raw ACK text.</summary>
    public async Task<string> SendAsync(object command)
    {
        if (_cmd == null) throw new InvalidOperationException($"{Label}: not connected");

        var json = JsonSerializer.Serialize(command);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _cmd.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, CancellationToken.None);

        var buffer = new byte[8192];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await _cmd.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    /// <summary>Sends the single-byte 0x01 status poll and returns the raw status JSON text.</summary>
    public async Task<string> PollStatusAsync()
    {
        if (_status == null) throw new InvalidOperationException($"{Label}: not connected");

        await _status.SendAsync(new ArraySegment<byte>(new byte[] { 0x01 }), WebSocketMessageType.Binary, true, CancellationToken.None);

        var buffer = new byte[8192];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await _status.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    /// <summary>Polls status until robot.flags & 0xFF == 0 (per aim-robot-api.md §2.9) or the timeout elapses.</summary>
    public async Task<bool> WaitUntilStoppedAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var json = await PollStatusAsync();
            if (TryGetMotionFlags(json, out var flags) && (flags & 0xFF) == 0)
                return true;
            await Task.Delay(100);
        }
        return false;
    }

    private static bool TryGetMotionFlags(string statusJson, out uint flags)
    {
        flags = 0;
        try
        {
            using var doc = JsonDocument.Parse(statusJson);
            if (doc.RootElement.TryGetProperty("robot", out var robot) &&
                robot.TryGetProperty("flags", out var flagsEl))
            {
                flags = Convert.ToUInt32(flagsEl.GetString() ?? "0x0", 16);
                return true;
            }
        }
        catch
        {
            // Malformed/missing flags -- treat as "unknown", caller keeps polling until timeout.
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_cmd is { State: WebSocketState.Open })
        {
            try { await _cmd.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
            catch { /* robot may already be gone -- not exceptional */ }
        }
        if (_status is { State: WebSocketState.Open })
        {
            try { await _status.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
            catch { /* robot may already be gone -- not exceptional */ }
        }
        _cmd?.Dispose();
        _status?.Dispose();
    }
}
