using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MRR.Tools.RobotSpriteDraw;

/// <summary>
/// Minimal, standalone AIM WebSocket client used only by this diagnostic tool -- deliberately
/// independent of MRR.Devices.RobotConnection (the game's single owner of a live robot socket)
/// so this can be run against a robot outside any game/DB context.
/// </summary>
public sealed class AIMWebSocketClient : IAsyncDisposable
{
    private readonly Uri _commandUri;
    private readonly Uri _statusUri;
    private readonly ClientWebSocket _commandSocket = new();
    private readonly ClientWebSocket _statusSocket = new();
    private readonly SemaphoreSlim _commandLock = new(1, 1);

    private CancellationTokenSource? _statusCancellation;
    private Task? _statusTask;

    public AIMWebSocketClient(string robotIp)
    {
        _commandUri = new Uri($"ws://{robotIp}/ws_cmd");
        _statusUri = new Uri($"ws://{robotIp}/ws_status");
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _commandSocket.ConnectAsync(_commandUri, cancellationToken);
        await _statusSocket.ConnectAsync(_statusUri, cancellationToken);

        _statusCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _statusTask = RunStatusLoopAsync(_statusCancellation.Token);

        await SendCommandAsync(
            new { cmd_id = "program_init" },
            cancellationToken
        );

        await SendCommandAsync(
            new { cmd_id = "lcd_set_pen_width", width = 0 },
            cancellationToken
        );
    }

    public async Task DrawRobotAsync(
        int originX,
        int originY,
        int scale = 1,
        CancellationToken cancellationToken = default)
    {
        await ClearScreenAsync(0, 0, 0, cancellationToken);

        foreach (SpriteRectangle rectangle in RobotSprite.Rectangles)
        {
            await DrawRectangleAsync(
                originX + rectangle.X * scale,
                originY + rectangle.Y * scale,
                rectangle.Width * scale,
                rectangle.Height * scale,
                rectangle.Color,
                cancellationToken
            );
        }
    }

    public Task ClearScreenAsync(
        int red,
        int green,
        int blue,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            new
            {
                cmd_id = "lcd_clear_screen",
                r = red,
                g = green,
                b = blue
            },
            cancellationToken
        );
    }

    public Task DrawRectangleAsync(
        int x,
        int y,
        int width,
        int height,
        RgbColor color,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            new
            {
                cmd_id = "lcd_draw_rectangle",
                x,
                y,
                width,
                height,
                r = color.Red,
                g = color.Green,
                b = color.Blue,
                b_transparency = false
            },
            cancellationToken
        );
    }

    private async Task SendCommandAsync(
        object command,
        CancellationToken cancellationToken)
    {
        await _commandLock.WaitAsync(cancellationToken);

        try
        {
            byte[] commandBytes =
                JsonSerializer.SerializeToUtf8Bytes(command);

            await _commandSocket.SendAsync(
                new ArraySegment<byte>(commandBytes),
                WebSocketMessageType.Binary,
                true,
                cancellationToken
            );

            string responseJson = await ReceiveMessageAsync(
                _commandSocket,
                cancellationToken
            );

            using JsonDocument response = JsonDocument.Parse(responseJson);
            JsonElement root = response.RootElement;

            string commandId = root.TryGetProperty("cmd_id", out JsonElement id)
                ? id.GetString() ?? "unknown"
                : "unknown";

            string status = root.TryGetProperty("status", out JsonElement state)
                ? state.GetString() ?? "unknown"
                : "unknown";

            if (commandId == "cmd_unknown" || status == "error")
            {
                throw new InvalidOperationException(
                    $"AIM rejected command '{commandId}'."
                );
            }
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private async Task RunStatusLoopAsync(CancellationToken cancellationToken)
    {
        byte[] statusRequest = { 1 };

        while (!cancellationToken.IsCancellationRequested &&
               _statusSocket.State == WebSocketState.Open)
        {
            await _statusSocket.SendAsync(
                new ArraySegment<byte>(statusRequest),
                WebSocketMessageType.Binary,
                true,
                cancellationToken
            );

            _ = await ReceiveMessageAsync(
                _statusSocket,
                cancellationToken
            );

            await Task.Delay(50, cancellationToken);
        }
    }

    private static async Task<string> ReceiveMessageAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];
        using MemoryStream message = new();

        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                cancellationToken
            );

            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("AIM closed the connection.");

            message.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
                break;
        }

        return Encoding.UTF8.GetString(
            message.GetBuffer(),
            0,
            checked((int)message.Length)
        );
    }

    public async ValueTask DisposeAsync()
    {
        if (_statusCancellation is not null)
            await _statusCancellation.CancelAsync();

        if (_statusTask is not null)
        {
            try { await _statusTask; }
            catch (OperationCanceledException) { }
        }

        _commandSocket.Dispose();
        _statusSocket.Dispose();
        _statusCancellation?.Dispose();
        _commandLock.Dispose();
    }
}
