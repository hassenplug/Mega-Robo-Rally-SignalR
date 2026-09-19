# VEX AIM Robot Sprite and WebSocket Development

> Visible conversation transcript compiled on September 19, 2026. Internal system instructions and tool logs are excluded.

---

## User

Give me 5 frames of this robot rolling, remove the grey base. Make it 8-bit fun.

Reference image: `8366eb2d-7895-403b-812d-3b571342359a.png`

## Assistant

Created a five-frame 8-bit rolling sprite sequence with the gray base removed and a transparent background.

Generated sprite sheet: `generated_images/exec-7fdc8e19-166c-4fca-825a-48c83c006776.png`

---

## User

Give me a GIF.

## Assistant

[Download the looping 8-bit robot GIF](twonky_8bit_rolling.gif)

---

## User

Give me one frame made out just rectangles and give me a table of top-left bottom-right coordinates.

## Assistant

![Rectangle-built Twonky frame](twonky_rectangle_frame.png)

Built entirely from 27 axis-aligned rectangles on a transparent 64 × 64 canvas. Bottom-right coordinates mark the rectangle boundary, so width = BR X − TL X.

| Rectangle | Color | Top-left | Bottom-right |
|---|---:|---:|---:|
| shell-top | `#d93ad7` | (24, 6) | (44, 8) |
| shell-main | `#ed43e6` | (18, 8) | (50, 18) |
| shell-highlight | `#ff9bfa` | (24, 9) | (38, 11) |
| front-light | `#ffffff` | (12, 12) | (18, 18) |
| shell-stripe | `#e8e8ee` | (16, 18) | (52, 22) |
| stripe-shadow | `#a7a7b0` | (18, 21) | (50, 23) |
| rear-body | `#751071` | (46, 22) | (54, 38) |
| rear-accent-1 | `#d52fcf` | (50, 24) | (54, 28) |
| rear-accent-2 | `#d52fcf` | (50, 31) | (54, 35) |
| link-left | `#77777f` | (20, 18) | (24, 31) |
| link-top | `#77777f` | (24, 16) | (40, 20) |
| link-right | `#77777f` | (40, 18) | (44, 31) |
| pivot | `#f4f4f8` | (29, 17) | (35, 23) |
| wheel-top | `#202025` | (22, 26) | (46, 30) |
| wheel-upper | `#202025` | (16, 30) | (52, 34) |
| wheel-middle | `#202025` | (12, 34) | (56, 52) |
| wheel-lower | `#202025` | (16, 52) | (52, 56) |
| wheel-bottom | `#202025` | (22, 56) | (46, 60) |
| hub-top | `#f3f3f7` | (24, 34) | (44, 37) |
| hub-main | `#f3f3f7` | (20, 37) | (48, 49) |
| hub-bottom | `#f3f3f7` | (24, 49) | (44, 52) |
| spoke-top | `#b61cad` | (32, 34) | (36, 40) |
| spoke-right | `#b61cad` | (42, 42) | (48, 46) |
| spoke-bottom | `#b61cad` | (32, 47) | (36, 52) |
| spoke-left | `#b61cad` | (20, 42) | (26, 46) |
| hub-ring | `#b61cad` | (27, 39) | (41, 49) |
| hub-center | `#ffffff` | (30, 41) | (38, 47) |

Files:

- [Editable rectangle SVG](twonky_rectangle_frame.svg)
- [Transparent PNG](twonky_rectangle_frame.png)
- [Coordinate table CSV](twonky_rectangle_coordinates.csv)

---

## User

```python
import time

# Mock robot client - replace with your actual VEX AIM WebSocket client
class RobotScreen:
    def clear(self): pass
    def draw_text(self, text, x, y): pass
    def draw_rect(self, x, y, w, h): pass

robot = RobotScreen()

# --- CONFIGURE THROTTLING ---
DRAW_INTERVAL = 0.20  # Limit drawing to once every 200ms (5 FPS)
last_draw_time = 0.0

# --- STATE TRACKING TO AVOID REDUNDANT DRAWS ---
last_sent_x = None
last_sent_y = None

def update_robot_display(current_x, current_y, force_redraw=False):
    global last_draw_time, last_sent_x, last_sent_y
    current_time = time.time()
    
    # 1. Time Check: Skip if we drew too recently
    if (current_time - last_draw_time) < DRAW_INTERVAL:
        return
        
    # 2. Change Check: Skip if coordinates haven't actually changed
    if not force_redraw and (current_x == last_sent_x and current_y == last_sent_y):
        return

    # 3. Safe Execution: Perform the slow drawing steps
    # Keep strings short to speed up processing
    robot.clear()
    robot.draw_text(f"X:{current_x}", 10, 10)
    robot.draw_text(f"Y:{current_y}", 10, 30)
    robot.draw_rect(current_x, current_y, 20, 20)
    
    # Update tracking timestamps and state
    last_draw_time = current_time
    last_sent_x = current_x
    last_sent_y = current_y

# --- MAIN EXECUTION LOOP ---
# Your main robot navigation or line tracking logic runs at maximum speed
mock_robot_x = 0
mock_robot_y = 0

print("Starting high-speed processing loop...")
for _ in range(1000):
    # Simulate rapid sensor processing or course corrections
    mock_robot_x += 1 
    time.sleep(0.01)  # Core math/logic is running ultra-fast (100Hz)
    
    # This function is called constantly, but the throttle safeguards the hardware
    update_robot_display(mock_robot_x, mock_robot_y)
```

Fit it into this logic.

## Assistant

This replaces the single 20 × 20 rectangle with the rectangle-built robot. The robot coordinates remain relative to `current_x, current_y`, and the entire display is still limited to 5 FPS.

```python
import time


# Mock robot client - replace with your actual VEX AIM WebSocket client
class RobotScreen:
    def clear(self):
        pass

    def draw_text(self, text, x, y):
        pass

    def draw_rect(self, x, y, w, h):
        pass

    # Replace this with the actual color method, if supported.
    def set_fill_color(self, color):
        pass


robot = RobotScreen()


# -----------------------------
# DISPLAY CONFIGURATION
# -----------------------------

DRAW_INTERVAL = 0.20  # 5 FPS
SPRITE_SCALE = 1

last_draw_time = 0.0
last_sent_x = None
last_sent_y = None


# -----------------------------
# RECTANGLE ROBOT SPRITE
# -----------------------------

ROBOT_RECTS = [
    (24, 6, 20, 2, "#D93AD7"),
    (18, 8, 32, 10, "#ED43E6"),
    (24, 9, 14, 2, "#FF9BFA"),
    (12, 12, 6, 6, "#FFFFFF"),
    (16, 18, 36, 4, "#E8E8EE"),
    (18, 21, 32, 2, "#A7A7B0"),
    (46, 22, 8, 16, "#751071"),
    (50, 24, 4, 4, "#D52FCF"),
    (50, 31, 4, 4, "#D52FCF"),
    (20, 18, 4, 13, "#77777F"),
    (24, 16, 16, 4, "#77777F"),
    (40, 18, 4, 13, "#77777F"),
    (29, 17, 6, 6, "#F4F4F8"),
    (22, 26, 24, 4, "#202025"),
    (16, 30, 36, 4, "#202025"),
    (12, 34, 44, 18, "#202025"),
    (16, 52, 36, 4, "#202025"),
    (22, 56, 24, 4, "#202025"),
    (24, 34, 20, 3, "#F3F3F7"),
    (20, 37, 28, 12, "#F3F3F7"),
    (24, 49, 20, 3, "#F3F3F7"),
    (32, 34, 4, 6, "#B61CAD"),
    (42, 42, 6, 4, "#B61CAD"),
    (32, 47, 4, 5, "#B61CAD"),
    (20, 42, 6, 4, "#B61CAD"),
    (27, 39, 14, 10, "#B61CAD"),
    (30, 41, 8, 6, "#FFFFFF"),
]


def draw_filled_rect(x, y, width, height, color):
    robot.set_fill_color(color)
    robot.draw_rect(x, y, width, height)


def draw_robot_sprite(origin_x, origin_y, scale=1):
    for offset_x, offset_y, width, height, color in ROBOT_RECTS:
        draw_filled_rect(
            origin_x + offset_x * scale,
            origin_y + offset_y * scale,
            width * scale,
            height * scale,
            color,
        )


def update_robot_display(current_x, current_y, force_redraw=False):
    global last_draw_time, last_sent_x, last_sent_y

    current_time = time.monotonic()

    if not force_redraw:
        if (current_time - last_draw_time) < DRAW_INTERVAL:
            return

        if current_x == last_sent_x and current_y == last_sent_y:
            return

    robot.clear()
    robot.draw_text(f"X:{current_x}", 4, 4)
    robot.draw_text(f"Y:{current_y}", 4, 18)

    draw_robot_sprite(
        origin_x=current_x,
        origin_y=current_y,
        scale=SPRITE_SCALE,
    )

    last_draw_time = current_time
    last_sent_x = current_x
    last_sent_y = current_y


mock_robot_x = 0
mock_robot_y = 40

print("Starting high-speed processing loop...")

for _ in range(1000):
    mock_robot_x += 1
    mock_robot_x %= 184
    time.sleep(0.01)
    update_robot_display(mock_robot_x, mock_robot_y)
```

The sprite is approximately `56 × 60` screen units at `SPRITE_SCALE = 1`. If the display can handle it, set `SPRITE_SCALE = 2` for a `112 × 120` robot.

The only part likely needing adjustment for the real client is:

```python
robot.set_fill_color(color)
robot.draw_rect(x, y, width, height)
```

If `draw_rect()` creates outlines instead of filled rectangles, replace it with the client’s filled-rectangle command.

---

## User

How long would this take on a VEX AIM to send this over Bluetooth?

## Assistant

If you are using the AIM WebSocket client shown in your example, it is sent over Wi-Fi, not Bluetooth. VEX documents the AIM WebSocket connection as a remote Wi-Fi connection. Bluetooth is used primarily for transferring programs and robot-to-robot communication.

For one frame:

- 27 rectangle draws
- About 8 color changes if grouped by color
- 1 screen clear
- 2 text draws
- Approximately 38 commands total as currently organized

Estimated completion time:

| Execution method | Likely time per frame |
|---|---:|
| Code running directly on AIM | roughly 20–100 ms |
| Wi-Fi, commands queued without individual acknowledgements | roughly 50–200 ms |
| Wi-Fi, separate acknowledged request per command | roughly 250–800 ms |
| Hypothetical separate Bluetooth commands | probably 500 ms to several seconds |

Therefore, the requested 5 FPS should work if the code runs directly on the AIM. It may work over Wi-Fi if commands are efficiently queued, but probably will not maintain 5 FPS if every rectangle causes a separate network round trip.

Measurement example:

```python
start_time = time.monotonic()

update_robot_display(
    mock_robot_x,
    mock_robot_y,
    force_redraw=True
)

elapsed_ms = (time.monotonic() - start_time) * 1000
print(f"Frame completed in {elapsed_ms:.1f} ms")
```

Update `last_draw_time` after drawing finishes:

```python
last_draw_time = time.monotonic()
```

Practical expectation over WebSocket was estimated at approximately **300–600 ms per frame**, or roughly **2–3 FPS**, unless commands are batched or pipelined.

---

## User

Over Wi-Fi.

## Assistant

Over Wi-Fi using the AIM WebSocket client, the initial budget was **about 150–500 ms per complete frame** for the 27-rectangle robot.

| Frame time | Practical rate |
|---:|---:|
| 100 ms | 10 FPS |
| 200 ms | 5 FPS |
| 333 ms | 3 FPS |
| 500 ms | 2 FPS |

The raw data is tiny. The issue is the number of WebSocket commands: 27 rectangles, potentially 27 color commands, one clear, and two text commands. Passing the color directly to `draw_rectangle()` reduces this to approximately 30 calls. A starting interval of `0.33` seconds, or roughly 3 FPS, was recommended until measured.

---

## User

Show me the full code with this optimization with comments.

## Assistant

The optimized version used the native AIM screen API and passed each color directly into `draw_rectangle()`, eliminating separate `set_fill_color()` commands. The important implementation was:

```python
import time
from vex import *

robot = Robot()

DRAW_INTERVAL = 0.33
SPRITE_WIDTH = 64
SPRITE_HEIGHT = 64
SPRITE_SCALE = 1
SCREEN_WIDTH = 240
SCREEN_HEIGHT = 240

last_draw_time = 0.0
last_sent_x = None
last_sent_y = None

SHELL_TOP = Color("#D93AD7")
SHELL_MAIN = Color("#ED43E6")
SHELL_HIGHLIGHT = Color("#FF9BFA")
FRONT_LIGHT = Color("#FFFFFF")
SHELL_STRIPE = Color("#E8E8EE")
STRIPE_SHADOW = Color("#A7A7B0")
REAR_BODY = Color("#751071")
REAR_ACCENT = Color("#D52FCF")
LINKAGE = Color("#77777F")
PIVOT = Color("#F4F4F8")
WHEEL = Color("#202025")
HUB = Color("#F3F3F7")
WHEEL_ACCENT = Color("#B61CAD")
CENTER = Color("#FFFFFF")

ROBOT_RECTS = (
    (24, 6, 20, 2, SHELL_TOP),
    (18, 8, 32, 10, SHELL_MAIN),
    (24, 9, 14, 2, SHELL_HIGHLIGHT),
    (12, 12, 6, 6, FRONT_LIGHT),
    (16, 18, 36, 4, SHELL_STRIPE),
    (18, 21, 32, 2, STRIPE_SHADOW),
    (46, 22, 8, 16, REAR_BODY),
    (50, 24, 4, 4, REAR_ACCENT),
    (50, 31, 4, 4, REAR_ACCENT),
    (20, 18, 4, 13, LINKAGE),
    (24, 16, 16, 4, LINKAGE),
    (40, 18, 4, 13, LINKAGE),
    (29, 17, 6, 6, PIVOT),
    (22, 26, 24, 4, WHEEL),
    (16, 30, 36, 4, WHEEL),
    (12, 34, 44, 18, WHEEL),
    (16, 52, 36, 4, WHEEL),
    (22, 56, 24, 4, WHEEL),
    (24, 34, 20, 3, HUB),
    (20, 37, 28, 12, HUB),
    (24, 49, 20, 3, HUB),
    (32, 34, 4, 6, WHEEL_ACCENT),
    (42, 42, 6, 4, WHEEL_ACCENT),
    (32, 47, 4, 5, WHEEL_ACCENT),
    (20, 42, 6, 4, WHEEL_ACCENT),
    (27, 39, 14, 10, WHEEL_ACCENT),
    (30, 41, 8, 6, CENTER),
)

def draw_robot_sprite(origin_x, origin_y, scale=1):
    for offset_x, offset_y, width, height, color in ROBOT_RECTS:
        robot.screen.draw_rectangle(
            origin_x + offset_x * scale,
            origin_y + offset_y * scale,
            width * scale,
            height * scale,
            color
        )

def update_robot_display(current_x, current_y, force_redraw=False):
    global last_draw_time, last_sent_x, last_sent_y

    check_time = time.monotonic()

    if not force_redraw:
        if check_time - last_draw_time < DRAW_INTERVAL:
            return None

        if current_x == last_sent_x and current_y == last_sent_y:
            return None

    scaled_width = SPRITE_WIDTH * SPRITE_SCALE
    scaled_height = SPRITE_HEIGHT * SPRITE_SCALE

    display_x = max(0, min(current_x, SCREEN_WIDTH - scaled_width))
    display_y = max(0, min(current_y, SCREEN_HEIGHT - scaled_height))

    frame_start = time.monotonic()

    robot.screen.clear_screen(BLACK)
    robot.screen.print_at(f"X:{current_x}", x=8, y=18)
    robot.screen.print_at(f"Y:{current_y}", x=8, y=34)

    draw_robot_sprite(display_x, display_y, SPRITE_SCALE)

    frame_finished = time.monotonic()
    frame_duration_ms = (frame_finished - frame_start) * 1000.0

    last_draw_time = frame_finished
    last_sent_x = current_x
    last_sent_y = current_y

    return frame_duration_ms

robot.screen.set_pen_width(0)
robot.screen.set_pen_color(WHITE)
robot.screen.clear_screen(BLACK)

mock_robot_x = 0
mock_robot_y = 50
completed_frame_count = 0

try:
    for _ in range(1000):
        mock_robot_x += 1
        maximum_sprite_x = SCREEN_WIDTH - SPRITE_WIDTH * SPRITE_SCALE

        if mock_robot_x > maximum_sprite_x:
            mock_robot_x = 0

        time.sleep(0.01)

        frame_time_ms = update_robot_display(mock_robot_x, mock_robot_y)

        if frame_time_ms is not None:
            completed_frame_count += 1

            if completed_frame_count % 10 == 0:
                estimated_fps = 1000.0 / frame_time_ms if frame_time_ms > 0 else 0.0
                print(
                    f"Frame: {frame_time_ms:.1f} ms | "
                    f"Theoretical max: {estimated_fps:.1f} FPS"
                )

except KeyboardInterrupt:
    print("Display loop stopped by user.")

finally:
    robot.screen.clear_screen(BLACK)
    robot.screen.print_at("Stopped", x=78, y=120)
```

The AIM API supports passing a fill color directly as the fifth argument to `draw_rectangle(x, y, width, height, color)`.

---

## User

Can I put all of this and have WebSockets trigger it from Wi-Fi to the robot?

## Assistant

Yes. Run the complete Python program on the computer, connect it to the AIM over Wi-Fi with the WebSocket library, and call the drawing function whenever the robot screen should update.

```text
Python program → trigger → AIM WebSocket client → Wi-Fi → AIM screen
```

The rectangle data and logic remain on the computer. Each trigger sends the screen commands over Wi-Fi. This does not upload the whole drawing routine to the AIM and then send a tiny “play” command. If the robot must continue after Wi-Fi disconnects, the drawing code must run locally on the AIM.

---

## User

Can I trigger code on the robot via WebSockets?

## Assistant

Not directly. The AIM WebSocket API cannot call an arbitrary Python function already loaded and running on the robot.

It supports:

```text
Computer function → WebSocket commands → AIM hardware
```

It does not officially support:

```text
WebSocket message: "run_animation"
             ↓
AIM executes a stored run_animation() function locally
```

For a true one-command trigger, the program would need to be uploaded to the AIM and triggered with a locally supported event such as a screen press, controller button, timer, vision detection, or sensor condition.

---

## User

C#.

## Assistant

If the control application is written in C#, C# cannot run directly on the AIM. The initially recommended architecture was:

```text
C# application → one trigger → Python AIM bridge → Wi-Fi → AIM
```

The C# application would send an HTTP trigger to a small Python service. The Python service would maintain the official AIM WebSocket connection and execute the screen commands.

---

## User

Eliminate the Python bridge, go direct from C#.

## Assistant

Yes. C# can connect directly using `ClientWebSocket`. VEX does not publish an official C# SDK, but the official open-source Python client shows the protocol:

- Command endpoint: `ws://<robot-ip>/ws_cmd`
- Status endpoint: `ws://<robot-ip>/ws_status`
- Commands are compact UTF-8 JSON sent as binary WebSocket messages.
- Each command receives a JSON response.
- A remote session begins with `{"cmd_id":"program_init"}`.

The direct C# implementation used this structure:

```csharp
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

const string RobotIp = "192.168.4.1";

using CancellationTokenSource shutdown = new();
await using AIMWebSocketClient aim = new(RobotIp);

await aim.ConnectAsync(shutdown.Token);

Console.WriteLine("Press Enter to draw Twonky.");
Console.ReadLine();

await aim.DrawRobotAsync(
    originX: 88,
    originY: 88,
    scale: 1,
    cancellationToken: shutdown.Token
);

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

public static class RobotSprite
{
    public static readonly SpriteRectangle[] Rectangles =
    {
        new(24, 6, 20, 2, RgbColor.FromHex("D93AD7")),
        new(18, 8, 32, 10, RgbColor.FromHex("ED43E6")),
        new(24, 9, 14, 2, RgbColor.FromHex("FF9BFA")),
        new(12, 12, 6, 6, RgbColor.FromHex("FFFFFF")),
        new(16, 18, 36, 4, RgbColor.FromHex("E8E8EE")),
        new(18, 21, 32, 2, RgbColor.FromHex("A7A7B0")),
        new(46, 22, 8, 16, RgbColor.FromHex("751071")),
        new(50, 24, 4, 4, RgbColor.FromHex("D52FCF")),
        new(50, 31, 4, 4, RgbColor.FromHex("D52FCF")),
        new(20, 18, 4, 13, RgbColor.FromHex("77777F")),
        new(24, 16, 16, 4, RgbColor.FromHex("77777F")),
        new(40, 18, 4, 13, RgbColor.FromHex("77777F")),
        new(29, 17, 6, 6, RgbColor.FromHex("F4F4F8")),
        new(22, 26, 24, 4, RgbColor.FromHex("202025")),
        new(16, 30, 36, 4, RgbColor.FromHex("202025")),
        new(12, 34, 44, 18, RgbColor.FromHex("202025")),
        new(16, 52, 36, 4, RgbColor.FromHex("202025")),
        new(22, 56, 24, 4, RgbColor.FromHex("202025")),
        new(24, 34, 20, 3, RgbColor.FromHex("F3F3F7")),
        new(20, 37, 28, 12, RgbColor.FromHex("F3F3F7")),
        new(24, 49, 20, 3, RgbColor.FromHex("F3F3F7")),
        new(32, 34, 4, 6, RgbColor.FromHex("B61CAD")),
        new(42, 42, 6, 4, RgbColor.FromHex("B61CAD")),
        new(32, 47, 4, 5, RgbColor.FromHex("B61CAD")),
        new(20, 42, 6, 4, RgbColor.FromHex("B61CAD")),
        new(27, 39, 14, 10, RgbColor.FromHex("B61CAD")),
        new(30, 41, 8, 6, RgbColor.FromHex("FFFFFF"))
    };
}

public readonly record struct SpriteRectangle(
    int X,
    int Y,
    int Width,
    int Height,
    RgbColor Color
);

public readonly record struct RgbColor(int Red, int Green, int Blue)
{
    public static RgbColor FromHex(string value)
    {
        value = value.TrimStart('#');

        return new RgbColor(
            Convert.ToInt32(value[..2], 16),
            Convert.ToInt32(value.Substring(2, 2), 16),
            Convert.ToInt32(value.Substring(4, 2), 16)
        );
    }
}
```

The direct implementation uses no NuGet WebSocket package because `ClientWebSocket` is built into .NET. It talks to the AIM wire protocol directly, so a future firmware or official-client update could require adjustments.

Sources referenced during the discussion:

- [VEX AIM WebSocket documentation](https://api.vex.com/aim/home/websocket/index.html)
- [VEX AIM Screen API](https://api.vex.com/aim/home/python/Screen.html)
- [VEX AIM custom colors](https://api.vex.com/aim/home/python/Logic/Color_objects.html)
- [Official VEX AIM WebSocket Python client](https://github.com/VEX-Robotics/AIM_Websocket_Library)

---

## User

Make this entire chat a `.md` file.

---

## User

Make this chat into a `.md`.
