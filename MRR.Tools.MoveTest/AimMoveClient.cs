using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MRR.Tools.MoveTest;

/// <summary>The ws_status fields the movement experiments care about.</summary>
public sealed record Pose(double X, double Y, double Heading, double Rotation, double Yaw, uint Flags, int Battery)
{
    public bool IsMoving => (Flags & 0xFF) != 0;

    public static Pose Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement.GetProperty("robot");
        double D(string name) =>
            r.TryGetProperty(name, out var el) && double.TryParse(
                el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
        uint flags = r.TryGetProperty("flags", out var f) ? Convert.ToUInt32(f.GetString() ?? "0x0", 16) : 0;
        int battery = r.TryGetProperty("battery", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetInt32() : 0;
        return new Pose(D("robot_x"), D("robot_y"), D("heading"), D("rotation"), D("yaw"), flags, battery);
    }

    public override string ToString() =>
        FormattableString.Invariant($"x={X,7:F1} y={Y,7:F1} heading={Heading,6:F1} rotation={Rotation,7:F1}");
}

/// <summary>
/// Standalone ws_cmd + ws_status client for the Phase 0 movement experiments
/// (documents/ROBOT_MOVEMENT_HANDOFF.md). Modeled on MRR.Tools.FirmwareCompare/AimTestClient;
/// deliberately independent of the game's RobotConnection, so it must only be used while the
/// game host is not holding the robot's sockets.
/// </summary>
public sealed class AimMoveClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly ClientWebSocket _cmd = new();
    private readonly ClientWebSocket _status = new();
    private readonly SemaphoreSlim _cmdLock = new(1, 1);

    public AimMoveClient(string host) => _host = host;

    public async Task ConnectAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _cmd.ConnectAsync(new Uri($"ws://{_host}:80/ws_cmd"), cts.Token);
        await _status.ConnectAsync(new Uri($"ws://{_host}:80/ws_status"), cts.Token);
        await SendAsync(new { cmd_id = "program_init" });
    }

    /// <summary>Sends one command and returns the raw ACK text. Serialized: one command in flight at a time.</summary>
    public async Task<string> SendAsync(object command)
    {
        await _cmdLock.WaitAsync();
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command));
            await _cmd.SendAsync(bytes, WebSocketMessageType.Binary, true, CancellationToken.None);
            var buffer = new byte[8192];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await _cmd.ReceiveAsync(buffer, cts.Token);
            return Encoding.UTF8.GetString(buffer, 0, result.Count);
        }
        finally
        {
            _cmdLock.Release();
        }
    }

    public async Task<Pose> GetPoseAsync()
    {
        await _status.SendAsync(new byte[] { 0x01 }, WebSocketMessageType.Binary, true, CancellationToken.None);
        var buffer = new byte[8192];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await _status.ReceiveAsync(buffer, cts.Token);
        return Pose.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
    }

    /// <summary>
    /// Polls every 50 ms (as the game's WaitForStopAsync does) until the motion flags clear.
    /// On timeout or cancellation it sends a stop and throws, so a robot is never left driving.
    /// </summary>
    public async Task<Pose> WaitForStopAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        await Task.Delay(50, CancellationToken.None);
        while (true)
        {
            var pose = await GetPoseAsync();
            if (!pose.IsMoving) return pose;
            if (ct.IsCancellationRequested || DateTime.UtcNow > deadline)
            {
                await StopAsync();
                throw new OperationCanceledException(ct.IsCancellationRequested
                    ? "Cancelled by Ctrl+C; stop sent."
                    : $"Robot still moving after {timeout.TotalSeconds:F0} s; stop sent.");
            }
            await Task.Delay(50, CancellationToken.None);
        }
    }

    /// <summary>The game's StopAsync: drive at speed 0.</summary>
    public Task<string> StopAsync() =>
        SendAsync(new { cmd_id = "drive", angle = 0.0, speed = 0.0, stacking_type = 0 });

    public async ValueTask DisposeAsync()
    {
        try { if (_cmd.State == WebSocketState.Open) await StopAsync(); } catch { /* robot may be gone */ }
        foreach (var ws in new[] { _cmd, _status })
        {
            try { if (ws.State == WebSocketState.Open) await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
            catch { /* robot may already be gone -- not exceptional */ }
            ws.Dispose();
        }
    }
}
