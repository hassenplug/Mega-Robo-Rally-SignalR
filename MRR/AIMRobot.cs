using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MRR.Data.Entities;

namespace MRR.Robots;

public class AIMRobot // : IAsyncDisposable
{
    private readonly string ipAddress;
    private ClientWebSocket? wsCmd;
    private ClientWebSocket? wsStatus;
    private ClientWebSocket? wsImage;
    private bool isConnected;
    private string robotColor { get; set; }

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
            //await wsStatus.ConnectAsync(new Uri($"ws://{ipAddress}:80/ws_status"), CancellationToken.None);
            //await wsImage.ConnectAsync(new Uri($"ws://{ipAddress}:80/ws_img"), CancellationToken.None);

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
        await SetLedAsync("all", 0, 255, 0); // Green front LED

        // Move forward
        await MoveAsync(-90, 100); // 0 degrees (forward), 100mm/s speed
        await Task.Delay(20); // Wait 2 seconds
        await StopAsync();

        await TurnAsync(90); // Turn right 90 degrees at 100mm/s
        await Task.Delay(20); // Wait 2 seconds
        await StopAsync();

    }

    public async Task SendRobotCommandAsync(PendingCommandEntity Command)
    {
        await SendRobotCommandAsync(Command.CommandID, Command.Parameter, Command.ParameterB, (Command.CommandCatID == 1) ? 1 : 0);
    }

    // command = move, turn, lcd_print, lcd_clear_screen, light_set, show_aivision, robot_command
    // p1 = distance
    // p2 = direction 0=forward, 1=backward

    public async Task SendRobotCommandAsync(int CommandID, int Param1 = 0, int Param2 = 0, int waitforcompletion = 1)
    {
        switch (CommandID)
        {
            case 1: // Move
                await MoveAsync(Param1, Param2); // forward
                break;
            case 2: // Turn
                await TurnAsync(Param1); // right
                break;
            case 3: // Stop
                await StopAsync();
                break;
            default:
                // Unknown command
                break;
        }

        if (waitforcompletion == 1)
        {
            // Simple wait to simulate completion
            await Task.Delay(500);
        }
    }


    /*        await SendCommandAsync(new
            {
                cmd_id = "robot_command",
                command_id = CommandID,
                param_1 = Param1,
                param_2 = Param2
            });
        }
    */

/*
    super().__init__("drive_for")
        self.distance = distance
        self.angle = angle
        self.drive_speed = drive_speed
        self.turn_speed = turn_speed
        self.final_heading = final_heading
        self.stacking_type = stacking_type
        
    def __init__(self, x=0, t=0, r=0):
        super().__init__("drive_with_vector")
        self.x = x
        self.t  = t
        self.r = r        

    def __init__(self, turn_rate=0.0, stacking_type=0):
        super().__init__("turn")
        self.turn_rate = turn_rate
        self.stacking_type = stacking_type        

    def __init__(self, turn_rate=0.0, stacking_type=0):
        super().__init__("turn")
        self.turn_rate = turn_rate
        self.stacking_type = stacking_type

    def __init__(self, heading=0.0, turn_rate=0.0, stacking_type=0):
        super().__init__("turn_to")
        self.heading = heading
        self.turn_rate = turn_rate
        self.stacking_type = stacking_type

    def __init__(self, angle=0, turn_rate=0.0, stacking_type=0):
        super().__init__("turn_for")
        self.angle = angle
        self.turn_rate = turn_rate
        self.stacking_type = stacking_type

    def __init__(self, vel1=0, vel2=0, vel3=0):
        super().__init__("spin_wheels")
        self.vel1 = vel1
        self.vel2 = vel2
        self.vel3 = vel3

    def __init__(self, x=0, y=0):
        super().__init__("set_pose")
        self.x = x
        self.y = y

    def __init__(self, string=""):
        super().__init__("lcd_print")
        self.string = string

    def __init__(self, string="", x=0, y=0, b_opaque=True):
        super().__init__("lcd_print_at")
        self.string = string
        self.x = x
        self.y = y
        self.b_opaque = b_opaque

    def __init__(self, row=0, col=0):
        super().__init__("lcd_set_cursor")
        self.row = row
        self.col = col

    def __init__(self, x=0, y=0):
        super().__init__("lcd_set_origin")
        self.x = x
        self.y = y

    def __init__(self):
        super().__init__("lcd_next_row")

    def __init__(self, row=0, r=0,g=0,b=0):
        super().__init__("lcd_clear_row")
        self.row = row
        self.r = r
        self.g = g
        self.b = b

    def __init__(self, r=0, g=0, b=0):
        super().__init__("lcd_clear_screen")
        self.r = r
        self.g = g
        self.b = b

    def __init__(self, fontname):
        super().__init__("lcd_set_font")
        self.fontname = fontname

    def __init__(self, width):
        super().__init__("lcd_set_pen_width")
        self.width = width

    def __init__(self, r=0, g=0, b=0):
        super().__init__("lcd_set_pen_color")
        self.r = r
        self.g = g
        self.b = b

    def __init__(self, r=0, g=0, b=0, transparent=False):
        super().__init__("lcd_set_fill_color")
        self.r = r
        self.g = g
        self.b = b
        self.b_transparency = transparent

    def __init__(self, x1=0, y1=0, x2=0, y2=0):
        super().__init__("lcd_draw_line")
        self.x1 = x1
        self.y1 = y1
        self.x2 = x2
        self.y2 = y2

    def __init__(self, x=0, y=0, width=0, height=0, r=0, g=0, b=0, transparent=False):
        super().__init__("lcd_draw_rectangle")
        self.x = x
        self.y = y
        self.width = width
        self.height = height
        self.r = r
        self.g = g
        self.b = b
        self.b_transparency = transparent

    def __init__(self, x=0, y=0, radius=0, r=0, g=0, b=0, transparent=False):
        super().__init__("lcd_draw_circle")
        self.x = x
        self.y = y
        self.radius = radius
        self.r = r
        self.g = g
        self.b = b
        self.b_transparency = transparent

    def __init__(self, x=0, y=0):
        super().__init__("lcd_draw_pixel")
        self.x = x
        self.y = y

    def __init__(self, filename="", x=0, y=0):
        super().__init__("lcd_draw_image_from_file")
        self.filename = filename
        self.x = x
        self.y = y

    def __init__(self, x=0, y=0, width=0, height=0):
        super().__init__("lcd_set_clip_region")
        self.x = x
        self.y = y
        self.width = width
        self.height = height

    def __init__(self, name=0, look=0):
        super().__init__("show_emoji")
        self.name = name
        self.look = look

    def __init__(self):
        super().__init__("hide_emoji")

    def __init__(self, name=0, look=0):
        super().__init__("show_aivision")

    def __init__(self, name=0, look=0):
        super().__init__("hide_aivision")

    def __init__(self):
        super().__init__("imu_calibrate")

    def __init__(self, sensitivity=0):
        super().__init__("imu_set_crash_threshold")
        self.sensitivity = sensitivity

    def __init__(self, kick_type=""):
        super().__init__(kick_type)

    def to_json(self):
        return super().to_json()
#endregion Kicker Commands

#region Sound Commands
    def __init__(self, name="", volume=0):
        super().__init__("play_sound")
        self.name = name
        self.volume = volume

    def __init__(self, name="", volume=0):
        super().__init__("play_file")
        self.name = name
        self.volume = volume

    def __init__(self, note=0, octave=0, duration=500, volume=0):
        super().__init__("play_note")
        self.note = note
        self.octave = octave
        self.duration = duration
        self.volume = volume

    def __init__(self):
        super().__init__("stop_sound")


#endregion Sound Commands

#region LED Commands
    def __init__(self, led="", r=0, g=0, b=0):
        super().__init__("light_set")
        self.led = led
        self.r = r
        self.g = g
        self.b = b

#endregion LED Commands

#region AiVision Commands
    def __init__(self, id, r, g, b, hangle, hdsat ):
        super().__init__("color_description")
        self.id = id
        self.r = r
        self.g = g
        self.b = b
        self.hdsat = hdsat
        self.hangle = hangle

    def __init__(self, id, c1, c2, *args):
        super().__init__("code_description")
        self.id = id
        self.c1 = c1.id
        self.c2 = c2.id
        self.c3 = -1
        self.c4 = -1
        self.c5 = -1
        if( len(args) > 0 ):
            self.c3 = args[0].id
        if( len(args) > 1 ):
            self.c3 = args[1].id
        if( len(args) > 2 ):
            self.c3 = args[2].id

    def __init__(self, enable=True):
        super().__init__("tag_detection")
        self.b_enable = enable

    def __init__(self, enable=True, merge=True):
        super().__init__("color_detection")
        self.b_enable = enable
        self.b_merge = merge

    def __init__(self, enable=True):
        super().__init__("model_detection")
        self.b_enable = enable




*/

    // Movement commands
    public Task MoveAsync(int distance, int angle) =>
        SendCommandAsync(new
        {
            cmd_id = "drive_for",
            angle,
            drive_speed = 100 * (distance >= 0 ? 1 : -1),
            turn_speed = 0,
            final_heading = 0,
            stacking_type = 0
        });

    public Task MoveUnlimitedAsync(double angle, double speed) =>
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

    public Task TurnAsync(int direction) =>
        SendCommandAsync(new
        {
            cmd_id = "turn_for",
            angle = direction * 90,
            turn_rate = 100,
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
    // {all, light1, light2, light3, light4, light5, light6}
    public Task SetLedAsync(string led, int r, int g, int b)
    {
        var ledData = new Dictionary<string, object>
        {
            { "cmd_id", "light_set" },
            { led, new { r, g, b } }
        };
        //Console.WriteLine("LED Data: " + JsonSerializer.Serialize(ledData));
        return SendCommandAsync(ledData);
    }

    //        SendCommandAsync(new

    public Task ShowAIAsync() =>
        SendCommandAsync(new
        {
            cmd_id = "show_aivision"
        });

}