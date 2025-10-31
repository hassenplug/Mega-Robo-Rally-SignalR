using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MRR.Robots;

public class AIMRobot // : IAsyncDisposable
{
    private readonly string ipAddress;
    private ClientWebSocket? wsCmd;
    private ClientWebSocket? wsStatus;
    private ClientWebSocket? wsImage;
    private bool isConnected;

    public AIMRobot(string ipAddress = "192.168.1.150")
    {
        this.ipAddress = ipAddress;
    }

    public async Task ConnectAsync()
    {
        wsCmd = new ClientWebSocket();
        wsStatus = new ClientWebSocket();
        //wsImage = new ClientWebSocket();

        try
        {
            await wsCmd.ConnectAsync(new Uri($"ws://{ipAddress}:80/ws_cmd"), CancellationToken.None);
            //await wsStatus.ConnectAsync(new Uri($"ws://{ipAddress}/ws_status"), CancellationToken.None);
            //await wsImage.ConnectAsync(new Uri($"ws://{ipAddress}/ws_img"), CancellationToken.None);

            isConnected = true;

            // Initialize the program
            await SendCommandAsync(new { cmd_id = "program_init" });

        }
        catch (Exception ex)
        {
            isConnected = false;
            //throw new Exception($"Failed to connect to AIM robot: {ex.Message}", ex);
        }
    }

    public async Task SendCommandAsync(object command)
    {
        if (!isConnected || wsCmd == null)
        {
            isConnected = false;
            return;
        }
        //    throw new InvalidOperationException("Not connected to AIM robot");

        var jsonCommand = JsonSerializer.Serialize(command);
        var bytes = Encoding.UTF8.GetBytes(jsonCommand);

        await wsCmd.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Binary,
            true,
            CancellationToken.None);

        var buffer = new byte[4096];
        var result = await wsCmd.ReceiveAsync(
            new ArraySegment<byte>(buffer),
            CancellationToken.None);

        if (result.MessageType == WebSocketMessageType.Binary)
        {
            var response = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var responseObj = JsonSerializer.Deserialize<Dictionary<string, object>>(response);

            Console.WriteLine("Response: " + response);

            if (responseObj != null && responseObj.ContainsKey("status"))
            {
                var status = responseObj["status"].ToString();
                if (status == "error")
                {
                    var errorInfo = responseObj.ContainsKey("error_info") ? responseObj["error_info"].ToString() : "Unknown error";
                    //throw new Exception($"Command failed: {errorInfo}");
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (wsCmd != null)
        {
            await StopAsync();
            if (wsCmd.State == WebSocketState.Open)
                await wsCmd.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None);
            wsCmd.Dispose();
        }

        if (wsStatus != null)
        {
            if (wsStatus.State == WebSocketState.Open)
                await wsStatus.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None);
            wsStatus.Dispose();
        }

        if (wsImage != null)
        {
            if (wsImage.State == WebSocketState.Open)
                await wsImage.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None);
            wsImage.Dispose();
        }
    }

    public async Task RunTest()
    {
        await ConnectAsync();

        // Example commands
        await ClearScreenAsync();
    //    await robot.ShowAIAsync();

        await PrintAsync("Hello from C#!");
        await SetLedAsync("LED1", 0, 255, 0); // Green front LED
        
        // Move forward
        await MoveAsync(270, 100); // 0 degrees (forward), 100mm/s speed
        await Task.Delay(20); // Wait 2 seconds
        await StopAsync();

        await TurnAsync(90, 100); // Turn right 90 degrees at 100mm/s
        await Task.Delay(20); // Wait 2 seconds
        await StopAsync();

    }


    // Movement commands
    public Task MoveAsync(double angle, double speed) =>
        SendCommandAsync(new
        {
            cmd_id = "drive",
            angle,
            speed,
            stacking_type = 0
        });

    public Task StopAsync() =>
        SendCommandAsync(new
        {
            cmd_id = "drive",
            angle = 0.0,
            speed = 0.0,
            stacking_type = 0
        });

    public Task TurnAsync(double angle, double speed) =>
        SendCommandAsync(new
        {
            cmd_id = "turn_for",
            angle,
            turn_rate = speed,
            stacking_type = 0
        });

    // Screen commands
    public Task PrintAsync(string text) =>
        //    {
        SendCommandAsync(new
        {
            cmd_id = "lcd_print",
            @string = text
        });
    //    }

    public Task ClearScreenAsync() =>
        SendCommandAsync(new
        {
            cmd_id = "lcd_clear_screen",
            b = 100,
            g = 0,
            r = 0

        });

    // LED commands
    public Task SetLedAsync(string led, int r, int g, int b)
    {
        var ledData = new Dictionary<string, object>
        {
            { "cmd_id", "light_set" },
            { led, new { r, g, b } }
        };
        Console.WriteLine("LED Data: " + JsonSerializer.Serialize(ledData));
        return SendCommandAsync(ledData);
    }

    //        SendCommandAsync(new

    public Task ShowAIAsync() =>
        SendCommandAsync(new
        {
            cmd_id = "show_aivision"
        });

}