
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel; //ObservableCollection
using System.ComponentModel; //INotifyPropertyChanged
using System.IO.Ports; // serial port
using System.Windows;
using System.Xml.Serialization;
using System.Globalization;
using MySqlConnector;
using MRR.Services;
using System.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net.WebSockets;
using System.Text.Json;

// serializer

namespace MRR
{

    #region Player Enums
    //public enum tRobotStatus
    //{
    //    Stationary = 0,
    //    Moving = 1,
    //    OffCenter = 2,
    //    Turning = 3,
    //    CommandSent = 4,
    //    ReceivedReply = 5,
    //    Unknown = 6
    //}

    public enum tPlayerStatus
    {
        [Description("Unknown")]Unknown,
        [Description("Waiting For Cards")]WaitingForCards,
        [Description("Programming")]Programming,
        [Description("Ready To Run")]ReadyToRun,
        [Description("Move In Progress")]MoveInProgress,
        [Description("Moving")]Moving,
        [Description("Connection Failing")]ConnectionFailing,
        [Description("Connected")] Connected,
        [Description("Connected")] Connected1,
        [Description("Connected")] Connected2,
        [Description("Connected")] Connected3,
        [Description("Connected")] Connected4,
        [Description("Move Complete")] MoveComplete,
    }

    public enum tShutDown
    {
        [Description("No")]None,
        [Description("Next Turn")]NextTurn,
        [Description("Currently")]Currently,
        [Description("Without Reset")]WithoutReset,
        [Description("ClearDamage")]ClearDamage,
    }

    #endregion

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

    }


    #endregion

    #region Players
    [Table("Robots")]
    public class Player
    {

        const int conTotalDamage = 10;
        const int conTotalLives = 3;

        #region Player Constructors

        public Player()
        {
            ID = -1;
            ShutDown = tShutDown.None;
            CurrentPos = new RobotLocation();
            NextPos = new RobotLocation();
            ArchivePos = new RobotLocation();
            NextFlag = new RobotLocation();
            Damage = 0;
            Lives = conTotalLives;
            LastFlag = 0;
            Name = ToString();
            // placement of this is critical
            Active = true;
            PositionValid = false;
            DamagePoints = 0;
            DamagedBy = -1;
        }

        public Player(int p_ID)
            : this()
        {
            ID = p_ID;
            Name = ToString();
        }

        public Player(Player p_Player)
            : this()
        {
            ID = p_Player.ID;
            Name = p_Player.Name;
            ShutDown = p_Player.ShutDown;
            CurrentPos = new RobotLocation(p_Player.CurrentPos);
            NextPos = new RobotLocation(p_Player.NextPos);
            ArchivePos = new RobotLocation(p_Player.ArchivePos);
            NextFlag = p_Player.NextFlag;
            Damage = p_Player.Damage;
            Lives = p_Player.Lives;
            LastFlag = p_Player.LastFlag;
            // placement of this is critical
            Active = p_Player.Active;
            PositionValid = false;
            DamagePoints = p_Player.DamagePoints;
            DamagedBy = -1;
            Operator = p_Player.Operator;
            Priority = p_Player.Priority;
            Energy = p_Player.Energy;
            PlayerSeat = p_Player.PlayerSeat;
        }



        #endregion

        [Key]
        [Column("RobotID")]
        public int ID { get; set; }

        [NotMapped]
        public string Name { get; set; } = "";

        [NotMapped]
        public string Operator { get; set; } = "";

        [NotMapped]
        public int TotalFlags
        {
            get { return 5 ; }
            set { }
        }

        [NotMapped]
        [XmlIgnore]
        public RobotLocation NextPos { get; set; } = new RobotLocation();

        [NotMapped]
        [XmlIgnore]
        public RobotLocation NextFlag { get; set; } = new RobotLocation();

        [NotMapped]
        [XmlIgnore]
        public bool PositionValid { get; set; }

        [NotMapped]
        public RobotLocation ArchivePos { get; set; } = new RobotLocation();

        [NotMapped]
        public RobotLocation CurrentPos { get; set; } = new RobotLocation();

        public tShutDown ShutDown { get; set; }

        [NotMapped]
        [XmlIgnore]
        public bool IsRunning
        {
            get
            {
                return Active && !(ShutDown == tShutDown.Currently);
            }
        }

        [NotMapped]
        public bool Active { get; set; }

        public int Priority { get; set; }
        public int Energy { get; set; }
        public int PlayerSeat { get; set; }

        public int? MessageCommandID { get; set; }
        public int Score { get; set; }


        public int CurrentPosRow { get => CurrentPos.Y; set => CurrentPos.Y = value; }
        public int CurrentPosCol { get => CurrentPos.X; set => CurrentPos.X = value; }
        public int CurrentPosDir { get => (int)CurrentPos.Direction; set => CurrentPos.Direction = (Direction)value; }

        public int ArchivePosRow { get => ArchivePos.Y; set => ArchivePos.Y = value; }
        public int ArchivePosCol { get => ArchivePos.X; set => ArchivePos.X = value; }
        public int ArchivePosDir { get => (int)ArchivePos.Direction; set => ArchivePos.Direction = (Direction)value; }

        public int Lives { get; set; }

        [NotMapped]
        public string Color { get; set; } = "333333"; // hex color string RRGGBB

        [NotMapped]
        public string ForeColor { get; set; } = "FFFFFF"; // hex color string RRGGBB

        private ClientWebSocket? wsCmd;
        private ClientWebSocket? wsStatus;
        private ClientWebSocket? wsImage;
        public bool isConnected { get; set; }
        public int isMoving;
        private CancellationTokenSource? _statusCts;
        // Guards concurrent access to wsStatus from both ListenStatusAsync and GetStatusAsync
        private readonly SemaphoreSlim _statusSocketSemaphore = new SemaphoreSlim(1, 1);

        private int l_damage = 0;
        public int Damage
        {
            get
            {
                return l_damage;
            }
            set
            {
                if (value < 0) value = 0;
                if (value >= conTotalDamage)
                {
                    value = conTotalDamage;
                    Active = false;
                }
                l_damage = value;

            }
        }


        [NotMapped]
        [XmlIgnore]
        public bool IsDead
        {
            get
            {
                return (bool)(Damage >= conTotalDamage);
            }
            set { }
        }

        public int PlayerScore
        {
            get
            {
                int pscore = 0;
                //if (!Active) return 99;
                //pscore = LastFlag * 10000; // add flags

                // add 40-(distance to next flag)
                //pscore += !Active ? 0 : ((40 - Math.Abs(CurrentPos.X - NextFlag.X) + Math.Abs(CurrentPos.Y - NextFlag.Y)) * 100);

                //pscore += ((20 - DistanceToNextFlag) * 100);

                // add lives
                //pscore += (Lives * 10);

                // add damage
                //pscore += (10 - Damage);

                pscore += (( Math.Abs(NextPos.X - NextFlag.X) + Math.Abs(NextPos.Y - NextFlag.Y)) );
                return pscore;
            }

        }

        [NotMapped]
        public int DamagePoints { get; set; }

        [NotMapped]
        public int DamagedBy { get; set; }

        public Direction Rotate(int RotateDir)
        {
            NextPos.SetLocation(new RobotLocation(RotationFunctions.Rotate(RotateDir, CurrentPos.Direction),CurrentPos.X,CurrentPos.Y));

            return NextPos.Direction;
        }


        public void SetLocation(Direction p_NewDirection, int p_NewX, int p_NewY)
        {
            CurrentPos.Direction = p_NewDirection;
            CurrentPos.X = p_NewX;
            CurrentPos.Y = p_NewY;

        }

        public void SetLocation(RobotLocation p_NewLocation)
        {
            SetLocation(p_NewLocation.Direction, p_NewLocation.X, p_NewLocation.Y);
        }

        public void SetLocation()
        {
            SetLocation(NextPos);
        }

        public RobotLocation CalcNewLocation(int p_distance, Direction p_direction)
        {
            return CurrentPos.CalcNewLocation(p_distance, p_direction);

        }

        [XmlIgnore]
        public int CardsPlayedCount { get { return CardsPlayed.Count(); } }

        [NotMapped]
        [XmlIgnore]
        public CardList CardsPlayed
        {
            get { return new CardList((CardsPlayer ?? new CardList()).Where(gc => gc.PhasePlayed > 0).OrderBy(pc => pc.PhasePlayed)); }
        }

        /// <summary>
        /// Comma-separated TypeIDs of the 9 cards dealt this turn (e.g. "5,6,7,1,2,3,8,10,4").
        /// Populated by DataService.RefreshAllPlayers() from the Robots.CardsDealt DB column.
        /// </summary>
        [NotMapped]
        [XmlIgnore]
        public string CardsDealtStr { get; set; } = "";

        /// <summary>
        /// Comma-separated TypeIDs of the 5 program registers (e.g. "5,0,6,0,0"); 0 = empty.
        /// Populated by DataService.RefreshAllPlayers() from the Robots.CardsPlayed DB column.
        /// </summary>
        [NotMapped]
        [XmlIgnore]
        public string CardsPlayedStr { get; set; } = "0,0,0,0,0";

        /// <summary>
        /// The robot screen UI instance for this player. Only populated when
        /// GameController.UseRobotScreen is true and the robot is connected.
        /// </summary>
        [NotMapped]
        [XmlIgnore]
        public RobotScreenUI? ScreenUI { get; set; }

        [NotMapped]
        public CardList? CardsPlayer { get; set; }

        [NotMapped]
        public OptionCardList? OptionCards { get; set; }

        public bool HasOptionCard(tOptionCardCommandType OptionID)
        {
            if (!this.IsRunning) return false;
            return false;
        }

        [Column("CurrentFlag")]
        public int LastFlag { get; set; }

        [Column("Status")]
        public tPlayerStatus PlayerStatus { get; set; }

        [NotMapped]
        public string? IPAddress { get; set; }

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

        public override string ToString()
        {
            if (ID == -1)
            {
                return "-";
            }

            return "[" + ID.ToString() + "]" + CurrentPos;
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
            catch (Exception)
            {
                isConnected = false;
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

                //Console.WriteLine("Response: " + response);

                if (responseObj != null && responseObj.ContainsKey("status"))
                {
                    var status = responseObj["status"].ToString();
                    if (status == "in_progress" && isMoving == 1)
                        isMoving = 2;
                    else if (status == "error")
                    {
                        var errorInfo = responseObj.ContainsKey("error_info") ? responseObj["error_info"].ToString() : "Unknown error";
                        Console.WriteLine("Robot error: " + errorInfo);
                    }
                }
            }
        }

        private static readonly byte[] StatusPollRequest = [0x01];

        private async Task ListenStatusAsync(CancellationToken ct)
        {
            var buffer = new byte[4096];
            while (!ct.IsCancellationRequested && wsStatus?.State == WebSocketState.Open)
            {
                try
                {
                    await _statusSocketSemaphore.WaitAsync(ct);
                    try
                    {
                        // Send 0x01 to request a status snapshot — never pass ct to socket ops
                        // to avoid aborting the socket on cancellation.
                        await wsStatus.SendAsync(new ArraySegment<byte>(StatusPollRequest),
                            WebSocketMessageType.Binary, true, CancellationToken.None);

                        var result = await wsStatus.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close) break;

                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        ProcessStatusEvent(json);
                    }
                    finally
                    {
                        _statusSocketSemaphore.Release();
                    }

                    await Task.Delay(100, ct); // ct only here — clean exit between polls
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine("Status listener error: " + ex.Message);
                    if (wsStatus?.State != WebSocketState.Open) break;
                    try { await Task.Delay(100, ct); } catch { break; }
                }
            }
            Console.WriteLine("Status listener exited");
        }

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



        private void ProcessStatusEvent(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("robot", out var robot)) return;

                var flagsStr  = robot.TryGetProperty("flags",    out var fEl)  ? fEl.GetString()  ?? "0x0" : "0x0";
                var battery   = robot.TryGetProperty("battery",  out var bEl)  ? bEl.GetInt32()   : 0;
                var robotXStr = robot.TryGetProperty("robot_x",  out var xEl)  ? xEl.GetString()  ?? "0" : "0";
                var robotYStr = robot.TryGetProperty("robot_y",  out var yEl)  ? yEl.GetString()  ?? "0" : "0";
                var heading   = robot.TryGetProperty("heading",  out var hEl)  ? hEl.GetString()  ?? "?" : "?";
                var rotation  = robot.TryGetProperty("rotation", out var rEl)  ? rEl.GetString()  ?? "?" : "?";

                var PosX = double.TryParse(robotXStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) ? px : 0;
                var PosY = double.TryParse(robotYStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var py) ? py : 0;
                var DistToOrigin = Math.Sqrt(PosX * PosX + PosY * PosY);
                var DirToOrigin  = Math.Atan2(-PosY, -PosX) * 180.0 / Math.PI;

                var flags = Convert.ToUInt32(flagsStr, 16);

                //Console.WriteLine(json);
                if(ID == 1)
                    Console.WriteLine($"Flags={flagsStr} moving={isMoving} bat={battery}% x={PosX:F2} y={PosY:F2} dist={DistToOrigin:F2}mm dir={DirToOrigin:F1}° hdg={heading} rot={rotation}");

                if ((flags & 0xFF) != 0)
                {
                    // robot is physically moving
                    if (isMoving == 1) isMoving = 2;
                }
                else
                {
                    // robot is physically idle
                    if (isMoving == 2) isMoving = 3;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Status parse error: " + ex.Message);
            }
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

        public async Task RunTest()
        {
            await ConnectAsync();

            await ClearScreenAsync();
            await PrintAsync("Hello from C#!");
            await SetLedAsync("all", 0, 255, 0);

            await MoveAsync(76, 0);
            while (isMoving is > 0 and < 3) await Task.Delay(50);
            isMoving = 0;

            await TurnAsync(1);
            while (isMoving is > 0 and < 3) await Task.Delay(50);
            isMoving = 0;
        }

        public async Task SendRobotCommandAsync(CommandItem cmd)
        {
            //isMoving = 1;
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
            //const double stallThresholdMm = 10.0;

            // set this to the target pose (not 0, 0)
            // then adjust to the actual target after the move
            var (startingX, startingY) = RotationFunctions.MovementOffset((Direction)angle);

            await SetPoseAsync(-startingX, -startingY);

            var pre = await GetStatusAsync();
            int preHeading = (int)Math.Round(pre.Robot.Heading);

            // main move
            await MoveAsync(distance * mmPerSquare, RotationFunctions.Degrees(angle), preHeading);

            // wait for the move to complete
            var post = await WaitForStopAsync();
            
            // second move
            //await MoveAsync((int)post.Robot.DistToOrigin, (int)post.Robot.DirToOrigin, preHeading);

            // wait for the second move to complete
            //await WaitForStopAsync();

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
    #endregion

}
