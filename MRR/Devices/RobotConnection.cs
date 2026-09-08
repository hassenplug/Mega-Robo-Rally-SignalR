using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MRR;

namespace MRR.Devices
{
    /// <summary>
    /// One robot's live transport: the three AIM WebSockets (ws_cmd, ws_status, ws_img) and the
    /// commands that ride them. No game state (Energy, Damage, CardsPlayed, ...) and no SQL --
    /// see API_DECOMPOSITION_DESIGN.md section 5.5 ("Device Gateway ... transport only, no game
    /// rules, no database"). Owned exclusively by <see cref="RobotConnections"/>; <see
    /// cref="MRR.Player"/> forwards to whichever instance is attached to it rather than opening a
    /// socket itself.
    /// </summary>
    public class RobotConnection
    {
        public RobotConnection(int robotId, string? ipAddress)
        {
            RobotID = robotId;
            IPAddress = ipAddress;
        }

        public int RobotID { get; }
        public string? IPAddress { get; internal set; }
        public bool IsConnected { get; set; }

        private ClientWebSocket? wsCmd;
        private ClientWebSocket? wsStatus;
        private ClientWebSocket? wsImage;
        private CancellationTokenSource? _statusCts;
        // Guards concurrent access to wsStatus from both ListenStatusAsync and GetStatusAsync
        private readonly SemaphoreSlim _statusSocketSemaphore = new SemaphoreSlim(1, 1);

        // ── AIMRobot methods (moved from Players.cs) ─────────────────────────

        public async Task ConnectAsync(string name, string color, string foreColor)
        {
            wsCmd = new ClientWebSocket();
            wsStatus = new ClientWebSocket();
            wsImage = new ClientWebSocket();

            var (bgR, bgG, bgB) = ColorHelper.ParseHex(color);
            var (fgR, fgG, fgB) = ColorHelper.ParseHex(foreColor, 255, 255, 255);

            try
            {
                await wsCmd.ConnectAsync(new Uri($"ws://{IPAddress}:80/ws_cmd"), CancellationToken.None);
                await wsStatus.ConnectAsync(new Uri($"ws://{IPAddress}:80/ws_status"), CancellationToken.None);
                //await wsImage.ConnectAsync(new Uri($"ws://{IPAddress}:80/ws_img"), CancellationToken.None);

                IsConnected = true;

                await SendCommandAsync(new { cmd_id = "program_init" });
                await SendCommandAsync(new { cmd_id = "imu_calibrate" });
                await SendCommandAsync(new { cmd_id = "set_pose", x = 0, y = 0 });
                await SendCommandAsync(new { cmd_id = "lcd_clear_screen", r = bgR, g = bgG, b = bgB });
                await SetLedAsync("all", bgR, bgG, bgB);

                await SendCommandAsync(new { cmd_id = "lcd_set_pen_color", r = fgR, g = fgG, b = fgB });
                await SendCommandAsync(new { cmd_id = "lcd_set_fill_color", r = bgR, g = bgG, b = bgB, transparent = false });

                // Draw forward-pointing arrow in robot color on forecolor background
                for (int y = 30; y <= 99; y++)
                {
                    int halfWidth = (y - 30) * 60 / 70;
                    await SendCommandAsync(new { cmd_id = "lcd_draw_line", x1 = 120 - halfWidth, y1 = y, x2 = 120 + halfWidth, y2 = y });
                }
                await SendCommandAsync(new { cmd_id = "lcd_draw_rectangle", x = 95, y = 100, width = 50, height = 110, r = fgR, g = fgG, b = fgB, transparent = false });

                //await SendCommandAsync(new { cmd_id = "lcd_draw_image_from_file", filename = $"arrow_{foreColor}.png", x = 0, y = 0 });
                //await SendCommandAsync(new { cmd_id = "lcd_draw_image_from_file", filename = $"arrow_{foreColor}", x = 0, y = 0 });

                //await SendCommandAsync(new { cmd_id = "lcd_set_font", fontname = "MONO60" });  //This doesn't seem to work
                await SetCursorAsync(6, Math.Max(0, (15 - name.Length) / 2));
                await PrintAsync(name);
                await SetLedAsync("all", bgR, bgG, bgB); // robot-color LED, same as SendColorStatus()'s default case

                _statusCts = new CancellationTokenSource();
                //_ = ListenStatusAsync(_statusCts.Token);
            }
            catch (Exception ex)
            {
                IsConnected = false;
                Console.WriteLine($"[{RobotID}] Connection failed: {ex.Message}");
            }
        }

        public async Task SendCommandAsync(object command)
        {
            if (!IsConnected || wsCmd == null)
            {
                IsConnected = false;
                return;
            }

            var jsonCommand = JsonSerializer.Serialize(command);
            //Console.WriteLine($"[{RobotID}] cmd: {jsonCommand}");
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

                //Console.WriteLine($"[{RobotID}] cmd ACK: {response}");

                if (responseObj != null && responseObj.ContainsKey("status"))
                {
                    var status = responseObj["status"].ToString();
                    if (status == "in_progress")
                    { }
                    else if (status == "error")
                    {
                        var errorInfo = responseObj.ContainsKey("error_info") ? responseObj["error_info"].ToString() : "Unknown error";
                        Console.WriteLine("Robot error: " + errorInfo);
                    }
                }
            }
        }

        private static readonly byte[] StatusPollRequest = [0x01];

        /// <summary>
        /// Polls ws_status for a single snapshot and returns the parsed RobotStatus.
        /// Thread-safe: shares the status socket semaphore with ListenStatusAsync.
        /// </summary>
        public async Task<RobotStatus> GetStatusAsync()
        {
            if (!IsConnected || wsStatus == null || wsStatus.State != WebSocketState.Open)
                return new RobotStatus();

            await _statusSocketSemaphore.WaitAsync();
            try
            {
                await wsStatus.SendAsync(new ArraySegment<byte>(StatusPollRequest),
                    WebSocketMessageType.Binary, true, CancellationToken.None);

                var buffer = new byte[4096];
                var result = await wsStatus.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                    return new RobotStatus();

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                //Console.WriteLine($"[{RobotID}] status: {json}");
                var status = JsonSerializer.Deserialize<RobotStatus>(json) ?? new RobotStatus();
                return status;
            }
            finally
            {
                _statusSocketSemaphore.Release();
            }
        }

        public async Task<RobotStatus> WaitForStopAsync()
        {
            await Task.Delay(50);
            var status = await GetStatusAsync();
            while (status.Robot.isMoving)
            {
                await Task.Delay(50);
                status = await GetStatusAsync();
            }
            return status;
        }

        /// <summary>
        /// Closes all three sockets. Never throws -- a robot that already dropped off WiFi has
        /// its socket closed on the remote end without a close handshake, which makes
        /// CloseAsync throw; that is expected here, not exceptional, so each socket's close is
        /// wrapped individually rather than leaving the caller to guard the whole call.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            _statusCts?.Cancel();
            _statusCts?.Dispose();
            _statusCts = null;

            if (wsCmd != null)
            {
                try
                {
                    await StopAsync();
                    if (wsCmd.State == WebSocketState.Open)
                        await wsCmd.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{RobotID}] wsCmd close failed (robot likely already disconnected): {ex.Message}");
                }
                wsCmd.Dispose();
            }

            if (wsStatus != null)
            {
                try
                {
                    if (wsStatus.State == WebSocketState.Open)
                        await wsStatus.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{RobotID}] wsStatus close failed (robot likely already disconnected): {ex.Message}");
                }
                wsStatus.Dispose();
            }

            if (wsImage != null)
            {
                try
                {
                    if (wsImage.State == WebSocketState.Open)
                        await wsImage.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{RobotID}] wsImage close failed (robot likely already disconnected): {ex.Message}");
                }
                wsImage.Dispose();
            }

            IsConnected = false;
        }

        public Task MoveAsync(int distance, int angle, int final_heading = 0, int drive_speed = 200) =>
            SendCommandAsync(new
            {
                cmd_id = "drive_for",
                distance = distance,
                angle = angle,
                drive_speed = drive_speed,
                turn_speed = 0,
                final_heading = final_heading,
                stacking_type = 0
            });

        public Task SetPoseAsync(int x = 0, int y = 0) =>
            SendCommandAsync(new { cmd_id = "set_pose", x, y });

        // Like MoveAsync but zeros odometry first, then verifies the robot
        // actually traveled the expected distance after the move completes.

        // distance is in squares, angle is direction
        public async Task MoveAndWaitAsync(int distance, int angle)
        {
            const int mmPerSquare = 77;
            const int mmShortMove = mmPerSquare - 1;

            // set this to the target pose (not 0, 0)
            // then adjust to the actual target after the move
            var pre = await GetStatusAsync();
            int preHeading = (int)Math.Round(pre.Robot.Heading);

            var newDir = (RotationFunctions.Degrees(angle) + preHeading) * Math.PI / 180.0;
            int newX_mm = (int)(distance * mmPerSquare * Math.Sin(newDir));
            int newY_mm = (int)(distance * mmPerSquare * Math.Cos(newDir));
            await SetPoseAsync(-newX_mm, -newY_mm);
            //Console.WriteLine($"[MoveAndWait] angle={angle}, preHeading={preHeading}, newDir={newDir}, newX_mm={newX_mm}, newY_mm={newY_mm}");

            // main move
            Console.WriteLine($"[MoveAndWait] MoveAsync(distance={distance * mmShortMove}, direction={RotationFunctions.Degrees(angle)}, preHeading={preHeading})");
            await MoveAsync(distance * mmShortMove, RotationFunctions.Degrees(angle), preHeading);

            // wait for the move to complete
            var post = await WaitForStopAsync();

            // second move — angle is robot-relative, so subtract preHeading from world angle
            int correctionAngle = (int)post.Robot.DirToOrigin - preHeading;
        }

        public async Task TurnAndWaitAsync(int direction)
        {
            await TurnAsync(direction);

            // wait for the turn to complete
            await WaitForStopAsync();
        }

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
                turn_rate = 200,
                stacking_type = 0
            });

        public Task PrintAsync(string text) =>
            SendCommandAsync(new
            {
                cmd_id = "lcd_print",
                @string = text
            });

        public Task SetCursorAsync(int row, int col) =>
            SendCommandAsync(new { cmd_id = "lcd_set_cursor", row, col });

        public Task ClearScreenAsync() =>
            SendCommandAsync(new
            {
                cmd_id = "lcd_clear_screen",
                b = 100,
                g = 0,
                r = 0
            });

        public Task SetLedAsync(string led, int r, int g, int b)
        {
            var ledData = new Dictionary<string, object>
            {
                { "cmd_id", "light_set" },
                { led, new { r, g, b } }
            };
            return SendCommandAsync(ledData);
        }

        public Task ShowAIAsync() =>
            SendCommandAsync(new
            {
                cmd_id = "show_aivision"
            });

        public Task<GridLineAnalysis> AlignAsync(int maxIterations = 10) =>
            GridAlignmentAgent.AlignAsync(this, maxIterations);

        // Connect to the robot's ws_img channel and receive one image frame.
        // Per AIM API: send 0x01 to start streaming; first frame arrives ~300 ms later.
        // Returns raw JPEG bytes or null on failure/timeout.
        public async Task<byte[]?> GetCameraImageAsync(int timeoutMs = 5000)
        {
            if (IPAddress == null) return null;
            using var ws = new ClientWebSocket();
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await ws.ConnectAsync(new Uri($"ws://{IPAddress}:80/ws_img"), cts.Token);

                // Trigger the stream — robot sends nothing until it receives 0x01.
                await ws.SendAsync(new ArraySegment<byte>([0x01]),
                    WebSocketMessageType.Binary, true, cts.Token);

                var segments = new List<byte[]>();
                var buffer = new byte[65536];
                while (ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    }
                    catch (WebSocketException)
                    {
                        // Robot closed the TCP connection without a WS close frame —
                        // normal for a one-shot image response. Keep whatever arrived.
                        break;
                    }
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    segments.Add(buffer[..result.Count]);
                    if (result.EndOfMessage) break;
                }
                // Robot already closed the connection; no CloseAsync needed.

                if (segments.Count == 0)
                {
                    Console.WriteLine($"[ws_img] {IPAddress}: no data received");
                    return null;
                }
                int total = segments.Sum(s => s.Length);
                var combined = new byte[total];
                int pos = 0;
                foreach (var seg in segments) { seg.CopyTo(combined, pos); pos += seg.Length; }

                Console.WriteLine($"[ws_img] {IPAddress}: {total} bytes, first={BitConverter.ToString(combined[..Math.Min(4, total)])}");
                SaveAlignImage(combined);
                return combined;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ws_img error for {IPAddress}: {ex.Message}");
                return null;
            }
        }

        private void SaveAlignImage(byte[] imageData)
        {
            try
            {
                var dir = Path.Combine("images", "align");
                Directory.CreateDirectory(dir);
                var safe = (IPAddress ?? "unknown").Replace('.', '_');
                var filename = $"align_{safe}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg";
                File.WriteAllBytes(Path.Combine(dir, filename), imageData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[align] Could not save image: {ex.Message}");
            }
        }
    }
}
