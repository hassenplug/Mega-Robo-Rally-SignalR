using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using System.ComponentModel.DataAnnotations.Schema;

namespace MRR
{

    #region Player Collection
    public class Players : List<Player>
    {
        public Players()
        {
        }

        public Player? GetPlayer(int p_PlayerID)
        {
            return GetPlayer(pl => pl.ID == p_PlayerID);
        }

        public Player? GetPlayer(RobotLocation p_Square)
        {
            return GetPlayer(ap => ((ap.CurrentPos.X == p_Square.X) && (ap.CurrentPos.Y == p_Square.Y) && (ap.Active)));
        }

        public Player? GetPlayer(Func<Player,bool> filter)
        {
            return this.FirstOrDefault(filter);
        }

        public void SetArchiveToCurrent()
        {
            this.Select(ts => { ts.ArchivePos.SetLocation(ts.CurrentPos); return ts; }).ToList();
        }

        /// <summary>
        /// Creates a deep copy of all players for turn simulation.
        /// The copy is used for physics/collision checks during turn planning.
        /// Working copy is discarded after turn execution (not saved back).
        /// </summary>
        public Players DeepCopy()
        {
            var copy = new Players();
            foreach (var player in this)
            {
                copy.Add(new Player(player));  // Player already has copy constructor
            }
            return copy;
        }

    }


    #endregion

    /// <summary>
    /// A robot as the host sees it: its game state (inherited from PlayerState, which lives
    /// in MRR.Contracts) plus the transport used to drive the physical VEX AIM robot.
    /// Everything below this line is I/O -- sockets, LCD, LEDs, camera, IMU.
    /// Slated to move behind IRobotTransport; see API_DECOMPOSITION_DESIGN.md section 5.5.
    /// </summary>
    public class Player : PlayerState
    {
        public Player() : base() { }

        /// <summary>
        /// Copy constructor. Copies game state only — the new instance has no sockets and is
        /// not connected. That is what turn simulation and the rear-laser stand-in want: a
        /// state snapshot, never a second handle on the physical robot.
        /// </summary>
        public Player(PlayerState p_Player) : base(p_Player) { }

        private ClientWebSocket? wsCmd;
        private ClientWebSocket? wsStatus;
        private ClientWebSocket? wsImage;
        public bool isConnected { get; set; }
        private CancellationTokenSource? _statusCts;
        // Guards concurrent access to wsStatus from both ListenStatusAsync and GetStatusAsync
        private readonly SemaphoreSlim _statusSocketSemaphore = new SemaphoreSlim(1, 1);
        /// <summary>
        /// The robot screen UI instance for this player. Only populated when
        /// GameController.UseRobotScreen is true and the robot is connected.
        /// </summary>
        [NotMapped]
        [XmlIgnore]
        public RobotScreenUI? ScreenUI { get; set; }

        public async Task<Player?> Connect(string ipAddress = "")
        {
            if (ipAddress != "")
            {
                if (ipAddress != null && ipAddress != IPAddress)
                {
                    IPAddress = ipAddress;
                }
            }
            if (IPAddress == null || IPAddress == "")
            {
                return null;
            }

            await ConnectAsync();
            return this;
        }

        public bool SendColorStatus(int Status = 1)
        {
            if (!isConnected) return false;

            switch (Status)
            {
                case 0: // off
                    SetLedAsync("all", 0,0,0).Wait(); // off
                    break;
                case 1: // Normal
                    var (r, g, b) = ColorHelper.ParseHex(Color);
                    SetLedAsync("all", r, g, b).Wait(); // robot color
                    break;
                case 2: // running
                    SetLedAsync("all", 0, 255, 0).Wait(); // green
                    break;
                case 3: // error
                    SetLedAsync("all", 255, 0, 0).Wait(); // red
                    break;
                default:
                    SetLedAsync("all", 255, 255, 0).Wait(); // yellow
                    break;
            }

            return true;
        }


        // ── AIMRobot methods (merged from AIMRobot.cs) ───────────────────────

        public async Task ConnectAsync()
        {
            wsCmd = new ClientWebSocket();
            wsStatus = new ClientWebSocket();
            wsImage = new ClientWebSocket();

            var (bgR, bgG, bgB) = ColorHelper.ParseHex(Color);
            var (fgR, fgG, fgB) = ColorHelper.ParseHex(ForeColor, 255, 255, 255);

            try
            {
                await wsCmd.ConnectAsync(new Uri($"ws://{IPAddress}:80/ws_cmd"), CancellationToken.None);
                await wsStatus.ConnectAsync(new Uri($"ws://{IPAddress}:80/ws_status"), CancellationToken.None);
                //await wsImage.ConnectAsync(new Uri($"ws://{IPAddress}:80/ws_img"), CancellationToken.None);

                isConnected = true;

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

                // Draw forward-pointing arrow in robot color on forecolor background

                //await SendCommandAsync(new { cmd_id = "lcd_draw_image_from_file", filename = $"arrow_{ForeColor}.png", x = 0, y = 0 });
                //await SendCommandAsync(new { cmd_id = "lcd_draw_image_from_file", filename = $"arrow_{ForeColor}", x = 0, y = 0 });

                //await SendCommandAsync(new { cmd_id = "lcd_set_font", fontname = "MONO60" });  //This doesn't seem to work
                await SetCursorAsync(6, Math.Max(0, (15 - Name.Length) / 2));
                await PrintAsync(Name);
                SendColorStatus();

                _statusCts = new CancellationTokenSource();
                //_ = ListenStatusAsync(_statusCts.Token);

            }
            catch (Exception ex)
            {
                isConnected = false;
                Console.WriteLine($"[{Name}] Connection failed: {ex.Message}");
            }
        }

        public async Task SendCommandAsync(object command)
        {
            if (!isConnected || wsCmd == null)
            {
                isConnected = false;
                return;
            }

            //Console.WriteLine("Sending command: " + JsonSerializer.Serialize(command));

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

                Console.WriteLine($"[{Name}] cmd ACK: {response}");

                if (responseObj != null && responseObj.ContainsKey("status"))
                {
                    var status = responseObj["status"].ToString();
                    if (status == "in_progress")
                    {}
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
            if (!isConnected || wsStatus == null || wsStatus.State != WebSocketState.Open)
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
                Console.WriteLine($"[{Name}] status: {json}");
                var status = JsonSerializer.Deserialize<RobotStatus>(json) ?? new RobotStatus();
                //Console.WriteLine($"[Status] flags:{status.Robot.Flags} battery:{status.Robot.Battery} x:{status.Robot.RobotX} y:{status.Robot.RobotY} isMoving:{status.Robot.isMoving}");
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


        public async ValueTask DisposeAsync()
        {
            _statusCts?.Cancel();
            _statusCts?.Dispose();
            _statusCts = null;

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


        public async Task SendRobotCommandAsync(CommandItem cmd)
        {
            int moveType = cmd.CommandMoveType;
            switch (moveType)
            {
                case 1: // Move — sends drive_for; isMoving set from ack; caller polls isMoving via StatusID==3
                    await MoveAndWaitAsync(cmd.Value, cmd.ValueB);
                    break;
                case 2: // Turn — sends turn_for; isMoving set from ack; caller polls isMoving via StatusID==3
                    await TurnAndWaitAsync(cmd.Value);
                    break;
                case 3: // Set Color    
                    SendColorStatus(cmd.Value);
                    break;
                case 0: // Stop
                    break;
                default:
                    break;
            }
            cmd.StatusID = 4;
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
        // Logs a warning if displacement is more than 10 mm short (stall/slip).

        // distance is in squares, angle is direction
        public async Task MoveAndWaitAsync(int distance, int angle)
        {
            const int mmPerSquare = 77;
            const int mmShortMove = mmPerSquare-1;
            //const double stallThresholdMm = 10.0;

            // set this to the target pose (not 0, 0)
            // then adjust to the actual target after the move
            var pre = await GetStatusAsync();
            int preHeading = (int)Math.Round(pre.Robot.Heading);

            //var (startingX, startingY) = RotationFunctions.MovementOffset((Direction)angle);

            //await SetPoseAsync(startingX * distance * mmPerSquare,
            //                   startingY * distance * mmPerSquare);
            var newDir = (RotationFunctions.Degrees(angle) + preHeading) * Math.PI / 180.0;
            int newX_mm = (int)(distance * mmPerSquare * Math.Sin(newDir));
            int newY_mm = (int)(distance * mmPerSquare * Math.Cos(newDir));
            //Console.WriteLine($"[MoveAndWait] newDir: {newDir}, newX_mm: {newX_mm}, newY_mm: {newY_mm}");
            await SetPoseAsync(-newX_mm, -newY_mm);

            // main move
            await MoveAsync(distance * mmShortMove, RotationFunctions.Degrees(angle), preHeading);

            // wait for the move to complete
            var post = await WaitForStopAsync();

            // second move — angle is robot-relative, so subtract preHeading from world angle
            int correctionAngle = (int)post.Robot.DirToOrigin - preHeading;
            //Console.WriteLine($"[MoveAndWait] correctionAngle: {correctionAngle} distance: {post.Robot.DistToOrigin}, dir: {post.Robot.DirToOrigin}");
            //await MoveAsync((int)post.Robot.DistToOrigin, correctionAngle, preHeading);

            // wait for the second move to complete
            //await WaitForStopAsync();
            //Console.WriteLine($"[MoveAndWait] Final: {correctionAngle} distance: {post.Robot.DistToOrigin}, dir: {post.Robot.DirToOrigin}");

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
                //File.WriteAllBytes(filename, imageData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[align] Could not save image: {ex.Message}");
            }
        }

        internal void RefreshCards()
        {
            /*
            var dt = GetQueryResults(
                $"SELECT CardsDealt, CardsPlayed FROM Robots WHERE RobotID = {ID};");
            if (dt.Rows.Count == 0) return;

            CardsDealtStr  = dt.Rows[0]["CardsDealt"]?.ToString()  ?? "";
            CardsPlayedStr = dt.Rows[0]["CardsPlayed"]?.ToString() ?? "0,0,0,0,0";
            */
        }

        internal void UpdateStatusLEDs()
        {
            int CPCount = CardsPlayedStr.Split(',').Count(s => s != "0" && s != "") ;
            SendColorStatus(CPCount==5?0:1);
            //SendColorStatus(CPCount==5?0:1);
        }
    }
}
