using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MySqlConnector;
using MRR;

namespace MRR.Devices
{
    /// <summary>
    /// One robot's live transport: the three AIM WebSockets (ws_cmd, ws_status, ws_img) and the
    /// commands that ride them. Owned exclusively by <see cref="RobotConnections"/>; <see
    /// cref="MRR.Player"/> forwards to whichever instance is attached to it rather than opening a
    /// socket itself.
    ///
    /// Connects itself: the constructor polls the <c>Robots</c> table (joined to
    /// <c>RobotBodies</c>/<c>RobotBases</c>) for this robot's name, color, and IP address --
    /// the caller passes nothing but the <see cref="RobotID"/> -- and then dials the robot.
    /// <see cref="ConnectAsync"/> is private for exactly this reason: it is never called again
    /// after construction. To reconnect a robot, replace its <see cref="RobotConnection"/> (see
    /// <see cref="RobotConnections.Reconnect"/>) rather than calling back into an existing one.
    /// </summary>
    public class RobotConnection
    {
        public RobotConnection(int robotId, string connectionString)
        {
            RobotID = robotId;
            _connectionString = connectionString;
            Ready = InitializeAsync();
        }

        /// <summary>
        /// Completes once the constructor's DB poll and connect attempt are done (success or
        /// failure) -- await this after creating a <see cref="RobotConnection"/> to know when
        /// <see cref="IsConnected"/> reflects the outcome, without <see cref="ConnectAsync"/>
        /// ever being callable directly.
        /// </summary>
        public Task Ready { get; }

        private readonly string _connectionString;
        private string _name = "";
        private string _color = "FFFFFF";
        private string _foreColor = "000000";
        private string _seatArrow = "";
        // Robots.PlayerMsg/ShutDown as of the last LoadFromDatabase -- acted on only when this
        // connection is created (InitializeAsync).
        private string _playerMsg = "";
        private tShutDown _dbShutDown = tShutDown.None;

        public int RobotID { get; }
        public string? Hostname { get; private set; }
        public bool IsConnected { get; set; }

        private bool _shutDown;

        /// <summary>
        /// True while this robot is shut down for the turn: the screen is redrawn black with gray
        /// text and the LEDs go off; false redraws it in the robot's own colors. Setting the value
        /// it already has changes nothing. Set by the turn's RobotShutDown command
        /// (Player.SendRobotCommandAsync), cleared by DataService.ResetPlayers.
        /// </summary>
        public bool ShutDown
        {
            get => _shutDown;
            set
            {
                if (_shutDown == value) return;
                _shutDown = value;
                _ = RefreshIdentityDisplayAsync();
            }
        }

        // The screen's background/text colors: the robot's own, or black/gray while shut down.
        private string DisplayColor => _shutDown ? "000000" : _color;
        private string DisplayForeColor => _shutDown ? "808080" : _foreColor;

        private async Task InitializeAsync()
        {
            if (!LoadFromDatabase())
            {
                Console.WriteLine($"[{RobotID}] RobotConnection: no matching Robots row -- not connecting.");
                return;
            }

            // Pick up where the game is for this robot: already shut down (set straight on the
            // field so ConnectAsync's first draw is the dark one), and any message it is waiting
            // on a press for.
            _shutDown = _dbShutDown == tShutDown.Currently;

            await ConnectAsync();

            // Shown the same way the SetFlash command shows a prompt, so the SetFlash-off that
            // follows the press restores the name screen as usual.
            if (IsConnected && _playerMsg != "")
            {
                ScreenText = _playerMsg;
                Flash = true;
            }
        }

        /// <summary>
        /// Polls <c>Robots</c>' own denormalized RobotName/RobotColor/RobotColorFG/Hostname
        /// columns for this robot alone -- the only value read from anywhere else is <see
        /// cref="RobotID"/>. Reads straight off Robots rather than joining RobotBodies/RobotBases
        /// (as DataService.GetAllPlayers() also no longer does, same reason): those were INNER
        /// JOINs, so a StartGame() placeholder row (RobotBodyID NULL, not yet claimed via
        /// DataService.SelectSeat) would never match, and this robot would silently never
        /// connect during setup (install/todo.md "Operator Data Setup"). Also reads PlayerMsg and
        /// ShutDown, which only InitializeAsync uses.
        /// </summary>
        private bool LoadFromDatabase()
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var cmd = new MySqlCommand(@"
                SELECT RobotName, RobotColor, RobotColorFG, Hostname, SeatArrow, PlayerMsg, ShutDown
                FROM Robots
                WHERE RobotID = @id", connection);
            cmd.Parameters.AddWithValue("@id", RobotID);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return false;

            _name = reader["RobotName"]?.ToString() ?? "";
            _color = reader["RobotColor"]?.ToString() ?? "FFFFFF";
            _foreColor = reader["RobotColorFG"]?.ToString() ?? "000000";
            Hostname = reader["Hostname"]?.ToString();
            _seatArrow = reader["SeatArrow"]?.ToString() ?? "";
            _playerMsg = reader["PlayerMsg"] == DBNull.Value ? "" : reader["PlayerMsg"].ToString() ?? "";
            _dbShutDown = reader["ShutDown"] == DBNull.Value ? tShutDown.None : (tShutDown)Convert.ToInt32(reader["ShutDown"]);
            return true;
        }

        /// <summary>
        /// Whether LEDs should currently be on. Mirrors DataService.UpdateCardPlayed's
        /// on-while-programming / off-once-full rule: if this robot is mid-programming
        /// (RobotStatus.Programming=1 -- covers "Waiting For Cards" through "Ready to Run"),
        /// lights reflect how many of its PhaseCount registers (MoveCards.CardLocation=2) are
        /// already filled rather than defaulting to on. Any other status (running,
        /// connected-only, shut down, ...) just defaults to on. Used by
        /// RefreshIdentityDisplayAsync, both right after connecting and whenever a placeholder's
        /// assigned body changes.
        /// </summary>
        private bool ShouldLightsBeOn()
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            using var statusCmd = new MySqlCommand(
                "SELECT rs.Programming " +
                "FROM Robots r " +
                "INNER JOIN RobotStatus rs ON r.`Status` = rs.RobotStatusID " +
                "WHERE r.RobotID = @id", connection);
            statusCmd.Parameters.AddWithValue("@id", RobotID);
            var statusResult = statusCmd.ExecuteScalar();
            bool inProgramming = statusResult != null && statusResult != DBNull.Value
                && Convert.ToInt32(statusResult) == 1;
            if (!inProgramming) return true;

            using var countCmd = new MySqlCommand(
                "SELECT COUNT(*) FROM MoveCards WHERE Owner = @id AND CardLocation = 2", connection);
            countCmd.Parameters.AddWithValue("@id", RobotID);
            int programCount = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);

            using var phaseCmd = new MySqlCommand(
                "SELECT iValue FROM CurrentGameData WHERE sKey = 'PhaseCount'", connection);
            int phaseCount = Convert.ToInt32(phaseCmd.ExecuteScalar() ?? 5);

            return programCount < phaseCount;
        }

        private ClientWebSocket? wsCmd;
        private ClientWebSocket? wsStatus;
        private ClientWebSocket? wsImage;
        private CancellationTokenSource? _statusCts;
        private CancellationTokenSource? _flashCts;
        private Task? _flashTask;
        private Task? _promptTask;
        private string _screenText = "";
        // One text draw at a time: ShowTextAsync is several commands, and two interleaved
        // (a prompt and the name restore) would leave a mix of both on screen.
        private readonly SemaphoreSlim _textLock = new SemaphoreSlim(1, 1);
        // Guards concurrent access to wsStatus from both ListenStatusAsync and GetStatusAsync
        private readonly SemaphoreSlim _statusSocketSemaphore = new SemaphoreSlim(1, 1);
        // Guards DisposeAsync itself: RobotConnections.Refresh()/Reconnect()/ReconnectAll() and
        // GameController's DisconnectRobot/DisconnectAllRobots can all reach the same
        // RobotConnection instance (Player.Connection and the RobotConnections registry entry
        // are the same object) from different threads with no coordination between them.
        // Without this, a second concurrent call re-cancels/re-disposes _statusCts and throws
        // ObjectDisposedException, uncaught, which crashes the process.
        private readonly SemaphoreSlim _disposeLock = new SemaphoreSlim(1, 1);
        private bool _disposed;

        // ── AIMRobot methods (moved from Players.cs) ─────────────────────────

        /// <summary>
        /// Dials the robot using the name/color/address <see cref="LoadFromDatabase"/> just
        /// read. Private: only <see cref="InitializeAsync"/> (i.e. construction) ever calls
        /// this, so a live connection is never re-dialed in place -- see the class remarks.
        /// </summary>
        private async Task ConnectAsync()
        {
            wsCmd = new ClientWebSocket();
            wsStatus = new ClientWebSocket();
            wsImage = new ClientWebSocket();

            try
            {
                await wsCmd.ConnectAsync(new Uri($"ws://{Hostname}:80/ws_cmd"), CancellationToken.None);
                await wsStatus.ConnectAsync(new Uri($"ws://{Hostname}:80/ws_status"), CancellationToken.None);
                //await wsImage.ConnectAsync(new Uri($"ws://{Hostname}:80/ws_img"), CancellationToken.None);

                IsConnected = true;

                await SendCommandAsync(new { cmd_id = "program_init" });
                await SendCommandAsync(new { cmd_id = "imu_calibrate" });
                await SendCommandAsync(new { cmd_id = "set_pose", x = 0, y = 0 });

                await RefreshIdentityDisplayAsync();

                _statusCts = new CancellationTokenSource();
                //_ = ListenStatusAsync(_statusCts.Token);
            }
            catch (Exception ex)
            {
                IsConnected = false;
                Console.WriteLine($"[{RobotID}] Connection failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets this robot's LCD screen (background color, forward-pointing arrow, name) and
        /// LEDs together -- re-reads RobotName/RobotColor/RobotColorFG (LoadFromDatabase) first,
        /// so both reflect whichever body is currently assigned. Called once from ConnectAsync
        /// right after its own one-time program_init/imu_calibrate/set_pose, and again whenever
        /// DataService.SelectSeat() changes a placeholder's assigned body (Program.cs's
        /// /api/setup/select), so the robot's screen and LEDs pick up the body the player just
        /// picked without repeating that connection setup.
        /// </summary>
        public async Task RefreshIdentityDisplayAsync()
        {
            LoadFromDatabase();

            if (!IsConnected || wsCmd == null)
            {
                IsConnected = false;
                return;
            }

            string name = _name;
            var (bgR, bgG, bgB) = ColorHelper.ParseHex(DisplayColor);
            var (fgR, fgG, fgB) = ColorHelper.ParseHex(DisplayForeColor, 255, 255, 255);
            Console.WriteLine($"[{RobotID}] RefreshIdentityDisplayAsync: name={name}, color={_color} ({bgR},{bgG},{bgB}), foreColor={_foreColor} ({fgR},{fgG},{fgB})");

            await SendCommandAsync(new { cmd_id = "lcd_clear_screen", r = bgR, g = bgG, b = bgB });
            await SetLightsAsync(ShouldLightsBeOn());

            await SendCommandAsync(new { cmd_id = "lcd_set_pen_color", r = fgR, g = fgG, b = fgB });
            await SendCommandAsync(new { cmd_id = "lcd_set_fill_color", r = bgR, g = bgG, b = bgB, transparent = false });

            // Draw forward-pointing arrow in robot color on forecolor background
            for (int y = 30; y <= 99; y++)
            {
                int halfWidth = (y - 30) * 60 / 70;
                await SendCommandAsync(new { cmd_id = "lcd_draw_line", x1 = 120 - halfWidth, y1 = y, x2 = 120 + halfWidth, y2 = y });
            }
            await SendCommandAsync(new { cmd_id = "lcd_draw_rectangle", x = 95, y = 100, width = 50, height = 110, r = fgR, g = fgG, b = fgB, transparent = false });

            await ShowTextAsync(name);

            //await ShowSeatArrowAsync(_seatArrow);
        }

        /// <summary>
        /// Prints the seat arrow (Robots.SeatArrow: ^ > v <, up = the way the robot faces, toward
        /// its player) centered under the name, small, in the robot's color on a background of
        /// its foreground color. Draws nothing for an empty arrow (seat or facing not known yet).
        /// Sets the pen, fill and font itself, so it can be called on its own after the arrow
        /// changes -- RefreshIdentityDisplayAsync calls it at the end of its redraw.
        /// </summary>
        public async Task ShowSeatArrowAsync(string arrow)
        {
            if (string.IsNullOrEmpty(arrow)) return;

            var (r, g, b) = ColorHelper.ParseHex(DisplayColor);
            var (fgR, fgG, fgB) = ColorHelper.ParseHex(DisplayForeColor, 255, 255, 255);
            const int charWidth = 10; // mono20: half the 20px width mono40 uses for the name above

            await SendCommandAsync(new { cmd_id = "lcd_set_pen_color", r, g, b });
            await SendCommandAsync(new { cmd_id = "lcd_set_fill_color", r = fgR, g = fgG, b = fgB, transparent = false });
            await SendCommandAsync(new { cmd_id = "lcd_set_font", fontname = "mono20" });
            await SendCommandAsync(new { cmd_id = "lcd_print_at", @string = arrow, x = Math.Max(0, (240 - arrow.Length * charWidth) / 2), y = 204, b_opaque = true });
        }

        public async Task SendCommandAsync(object command)
        {
            if (!IsConnected || wsCmd == null)
            {
                IsConnected = false;
                return;
            }

            // One send+ack at a time: effects (RobotEffects) and the flash loop now send while a
            // drive/turn command is in flight, and two overlapping ReceiveAsync calls on one
            // socket throw.
            await _cmdLock.WaitAsync();
            try
            {
                var jsonCommand = JsonSerializer.Serialize(command);
                //Console.WriteLine($"[{RobotID}] cmd: {jsonCommand}");
                var bytes = Encoding.UTF8.GetBytes(jsonCommand);

                long sentAt = System.Diagnostics.Stopwatch.GetTimestamp(); // after the lock: network + robot only
                await wsCmd.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Binary,
                    true,
                    CancellationToken.None);

                var buffer = new byte[4096];
                var result = await wsCmd.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    CancellationToken.None);
                CommandTiming.RecordAck(RobotID, System.Diagnostics.Stopwatch.GetElapsedTime(sentAt).TotalMilliseconds);

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
            catch (Exception ex)
            {
                IsConnected = false;
                Console.WriteLine($"[{RobotID}] SendCommandAsync failed: {ex.Message}");
                //throw;
            }
            finally
            {
                _cmdLock.Release();
            }
        }

        private readonly SemaphoreSlim _cmdLock = new SemaphoreSlim(1, 1);

        /// <summary>Plays one of the robot's built-in sounds (see aim-robot-api.md section 5).</summary>
        public Task PlaySoundAsync(string name, int volume) =>
            SendCommandAsync(new { cmd_id = "play_sound", name, volume });

        /// <summary>
        /// Puts the ring back the way the game wants it after an effect: on in the robot's
        /// colour, or off if it has finished programming (same rule as at connect time).
        /// </summary>
        public Task RestoreLightsAsync() => SetLightsAsync(ShouldLightsBeOn());

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
                long sentAt = System.Diagnostics.Stopwatch.GetTimestamp();
                await wsStatus.SendAsync(new ArraySegment<byte>(StatusPollRequest),
                    WebSocketMessageType.Binary, true, CancellationToken.None);

                var buffer = new byte[4096];
                var result = await wsStatus.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                CommandTiming.RecordStatus(RobotID, System.Diagnostics.Stopwatch.GetElapsedTime(sentAt).TotalMilliseconds);
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
        /// Idempotent and safe to call concurrently from multiple callers (e.g. a manual
        /// disconnect racing RobotConnections.Refresh() tearing down the same connection) --
        /// only the first call actually runs; the rest are no-ops.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await _disposeLock.WaitAsync();
            try
            {
                if (_disposed) return;
                _disposed = true;
            }
            finally
            {
                _disposeLock.Release();
            }

            _flashCts?.Cancel();
            _flashCts = null;

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
            preHeading = 0; // ignore heading for now, since the robot's odometry is not accurate enough to rely on it

            var newDir = (RotationFunctions.Degrees(angle) + preHeading) * Math.PI / 180.0;
            int newX_mm = (int)(distance * mmPerSquare * Math.Sin(newDir));
            int newY_mm = (int)(distance * mmPerSquare * Math.Cos(newDir));
            await SetPoseAsync(-newX_mm, -newY_mm);
            //Console.WriteLine($"[MoveAndWait] angle={angle}, preHeading={preHeading}, newDir={newDir}, newX_mm={newX_mm}, newY_mm={newY_mm}");

            // main move
            //Console.WriteLine($"[MoveAndWait] MoveAsync(distance={distance * mmShortMove}, direction={RotationFunctions.Degrees(angle)}, preHeading={preHeading})");
            await MoveAsync(distance * mmShortMove, RotationFunctions.Degrees(angle), preHeading);

            // wait for the move to complete
            var post = await WaitForStopAsync();
            SaveBattery(post.Robot.Battery);

            // second move — angle is robot-relative, so subtract preHeading from world angle
            int correctionAngle = (int)post.Robot.DirToOrigin - preHeading;
        }

        public async Task TurnAndWaitAsync(int direction)
        {
            await TurnAsync(direction);

            // wait for the turn to complete
            var post = await WaitForStopAsync();
            SaveBattery(post.Robot.Battery);
        }

        /// <summary>
        /// Records the robot's battery (ws_status robot.battery) in RobotBases.BatteryStatus --
        /// the battery belongs to the physical base, so it stays with the base if the GM swaps
        /// robots onto different bases mid-game. Called when a move or turn completes. A reading
        /// of 0 is the empty status GetStatusAsync returns when the socket is gone, not a real
        /// battery level, so it is skipped. Off the move's path and never throws: a database
        /// hiccup must not hold up or fail a robot move.
        /// </summary>
        private void SaveBattery(int battery)
        {
            if (battery <= 0) return;
            _ = Task.Run(() =>
            {
                try
                {
                    using var connection = new MySqlConnection(_connectionString);
                    connection.Open();
                    using var cmd = new MySqlCommand(
                        @"UPDATE RobotBases b JOIN Robots r ON r.RobotBaseID = b.RobotBaseID
                          SET b.BatteryStatus = @battery WHERE r.RobotID = @id", connection);
                    cmd.Parameters.AddWithValue("@battery", battery);
                    cmd.Parameters.AddWithValue("@id", RobotID);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{RobotID}] could not save battery: {ex.Message}");
                }
            });
        }

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

        /// <summary>
        /// Turns all LEDs on (in this robot's own color) or off. Always off while shut down.
        /// </summary>
        public Task SetLightsAsync(bool on)
        {
            var (r, g, b) = on && !_shutDown ? ColorHelper.ParseHex(_color) : (0, 0, 0);
            return SetLedAsync("all", r, g, b);
        }

        // Ring order per .claude/agents/aim-robot-api.md's LED angle table (315°, 265°, 210°,
        // 155°, 100°, 45°) -- light1 through light6 going around the robot one direction.
        private static readonly string[] RingLeds = { "light1", "light2", "light3", "light4", "light5", "light6" };
        private const int FlashStepDelayMs = 20;

        /// <summary>
        /// Setting this true starts a background loop that cycles the six ring LEDs (light1..
        /// light6) on (robot color) then off, one at a time, repeating until set back to false
        /// -- a no-op if already running/stopped. The setter itself never blocks: false just
        /// cancels the loop, which stops after finishing whichever LED it's on.
        /// </summary>
        public bool Flash
        {
            get => _flashCts != null;
            set
            {
                if (value)
                {
                    if (_flashCts != null) return; // already running
                    _flashCts = new CancellationTokenSource();
                    _flashTask = RunFlashLoopAsync(_flashCts.Token);
                    if (_screenText.Length > 0) _promptTask = ShowTextAsync(_screenText);
                }
                else
                {
                    if (_flashCts == null) return; // not flashing: screen was never replaced
                    _flashCts.Cancel();
                    _flashCts = null;
                    _ = RestoreIdentityAfterFlashAsync(_flashTask, _promptTask);
                    _promptTask = null;
                }
            }
        }

        /// <summary>
        /// The text shown on the robot's LCD for as long as <see cref="Flash"/> is true (the
        /// reason the player is being asked to press). Set it before setting Flash = true; when
        /// Flash goes false the robot's name display is redrawn instead.
        /// </summary>
        public string ScreenText
        {
            get => _screenText;
            set => _screenText = value;
        }

        // Same font and position as the robot's name in RefreshIdentityDisplayAsync.
        private const int NameCharWidth = 20;     // mono40
        private const int NameHeight = 40;
        private const int NameY = 165;            // lcd_print_at y is the text baseline
        private const int NameAscent = 32;        // mono40 glyphs reach this far above the baseline
        private const int NameBandY = NameY - NameAscent;

        /// <summary>
        /// The only place text (the robot's name, or the prompt shown while it flashes) is drawn
        /// on the LCD. Paints the whole text band (full width) in the robot color so nothing of
        /// the previous text is left, puts back the slice of the arrow's shaft (x 95..145) that
        /// runs through that band, then prints <paramref name="text"/> centered in the name's font
        /// and position. The rest of the screen is untouched.
        /// </summary>
        private async Task ShowTextAsync(string text)
        {
            await _textLock.WaitAsync();
            try
            {
                var (bgR, bgG, bgB) = ColorHelper.ParseHex(DisplayColor);
                var (fgR, fgG, fgB) = ColorHelper.ParseHex(DisplayForeColor, 255, 255, 255);

                // lcd_draw_rectangle outlines in the pen color, so match the pen to each fill or a
                // 1px border shows as lines across the screen.
                await SendCommandAsync(new { cmd_id = "lcd_set_pen_color", r = bgR, g = bgG, b = bgB });
                await SendCommandAsync(new { cmd_id = "lcd_draw_rectangle", x = 0, y = NameBandY, width = 240, height = NameHeight, r = bgR, g = bgG, b = bgB, transparent = false });
                await SendCommandAsync(new { cmd_id = "lcd_set_pen_color", r = fgR, g = fgG, b = fgB });
                await SendCommandAsync(new { cmd_id = "lcd_draw_rectangle", x = 95, y = NameBandY, width = 50, height = NameHeight, r = fgR, g = fgG, b = fgB, transparent = false });
                await SendCommandAsync(new { cmd_id = "lcd_set_fill_color", r = bgR, g = bgG, b = bgB, transparent = false });
                await SendCommandAsync(new { cmd_id = "lcd_set_font", fontname = "mono40" });
                await SendCommandAsync(new { cmd_id = "lcd_print_at", @string = text, x = Math.Max(0, (240 - text.Length * NameCharWidth) / 2), y = NameY, b_opaque = true });
            }
            finally
            {
                _textLock.Release();
            }
        }

        // Waits for the flash loop (so it can't relight an LED after the redraw) and the prompt
        // draw to finish, then draws the name back over the prompt.
        private async Task RestoreIdentityAfterFlashAsync(Task? flashTask, Task? promptTask)
        {
            try
            {
                if (flashTask != null) await flashTask;
                if (promptTask != null) await promptTask;

                await ShowTextAsync(_name);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{RobotID}] RestoreIdentityAfterFlashAsync failed: {ex.Message}");
            }
        }

        private async Task RunFlashLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                var (r, g, b) = ColorHelper.ParseHex(_color);
                while (!cancellationToken.IsCancellationRequested)
                {
                    // show text on the robot's LCD while flashing, so the players know why the robot is flashing
                    //await PrintAsync("Flashing");
                    foreach (var led in RingLeds)
                    {
                        await SetLedAsync(led, r, g, b);
                        await Task.Delay(FlashStepDelayMs, cancellationToken);

                        if (led == RingLeds.Last())
                        {
                            // clear the text after the last LED goes dark, so the players know the flashing is done
                            //await ClearScreenAsync();
                        }

                        await SetLedAsync(led, 0, 0, 0);
                        // do not delay when turning off, so the next LED lights up immediately after the previous one goes dark
                        // await Task.Delay(FlashStepDelayMs, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when Flash is set back to false mid-delay.
            }
        }

        public Task<PlusAnalysis> AlignAsync(int maxIterations = 10) =>
            GridAlignmentAgent.AlignAsync(this, maxIterations);

        // Connect to the robot's ws_img channel and receive one image frame.
        // Per AIM API: send 0x01 to start streaming; first frame arrives ~300 ms later.
        // Returns raw JPEG bytes or null on failure/timeout.
        public async Task<byte[]?> GetCameraImageAsync(int timeoutMs = 5000)
        {
            if (string.IsNullOrEmpty(Hostname)) return null;
            using var ws = new ClientWebSocket();
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await ws.ConnectAsync(new Uri($"ws://{Hostname}:80/ws_img"), cts.Token);

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
                    Console.WriteLine($"[ws_img] {Hostname}: no data received");
                    return null;
                }
                int total = segments.Sum(s => s.Length);
                var combined = new byte[total];
                int pos = 0;
                foreach (var seg in segments) { seg.CopyTo(combined, pos); pos += seg.Length; }

                Console.WriteLine($"[ws_img] {Hostname}: {total} bytes, first={BitConverter.ToString(combined[..Math.Min(4, total)])}");
                SaveAlignImage(combined);
                return combined;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ws_img error for {Hostname}: {ex.Message}");
                return null;
            }
        }

        private void SaveAlignImage(byte[] imageData)
        {
            try
            {
                var dir = Path.Combine("images", "align");
                Directory.CreateDirectory(dir);
                var safe = (Hostname ?? "unknown").Replace('.', '_');
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
