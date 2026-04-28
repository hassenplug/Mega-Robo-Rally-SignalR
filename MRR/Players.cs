
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

        public Player SetPlayer(
            int p_ID,
            string p_Name,
            RobotLocation p_CurrentPos,
            RobotLocation p_NextPos,
            RobotLocation p_Archive,
            int p_StartingDamage,
            int p_Lives,
            int p_LastFlag,
            tShutDown p_ShutDown,
            bool p_Active,
            int p_DamagePoints)
        {
            ID = p_ID;
            ShutDown = p_ShutDown;

            CurrentPos = new RobotLocation(p_CurrentPos);
            NextPos = new RobotLocation(p_NextPos);
            ArchivePos = new RobotLocation(p_Archive);
            NextFlag = new RobotLocation(p_CurrentPos);

            Damage = p_StartingDamage;
            Lives = p_Lives;
            LastFlag = p_LastFlag;

            Name = p_Name;

            /// placement of this is critical
            Active = p_Active;
            ///

            PositionValid = false;

            DamagePoints = p_DamagePoints;

            DamagedBy = -1;

            return this;
        }

        public Player(int p_ID)
        {
            int currentlives = conTotalLives;
            SetPlayer(p_ID, ToString(), new RobotLocation(), new RobotLocation(), new RobotLocation(), 0, currentlives, 0, tShutDown.None, true, 0);
        }

        public Player()
            :this(-1)
        {
        }

        public Player(Player p_Player)
        {
            CopyPlayer(p_Player);
        }

        public Player CopyPlayer(Player p_Player)
        {
            SetPlayer( p_Player.ID, p_Player.Name, p_Player.CurrentPos, p_Player.NextPos, p_Player.ArchivePos, p_Player.Damage, p_Player.Lives, p_Player.LastFlag, p_Player.ShutDown, p_Player.Active,p_Player.DamagePoints);
            NextFlag = p_Player.NextFlag;
            Operator = p_Player.Operator;
            Priority = p_Player.Priority;
            Energy = p_Player.Energy;
            PlayerSeat = p_Player.PlayerSeat;

            return this;
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
        public bool isConnected;
        public bool isMoving;
        private CancellationTokenSource? _statusCts;
        private TaskCompletionSource<bool>? _motionComplete;

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

        [NotMapped]
        public CardList? CardsPlayer
        {
            get
            {
                if (hiddenCardsPlayer != null) return hiddenCardsPlayer;
                return null;}
            set { hiddenCardsPlayer = value; }
        }
        private CardList? hiddenCardsPlayer;

        [NotMapped]
        public OptionCardList? OptionCards
        {
            get
            {
                if (hiddenOptionCards != null) return hiddenOptionCards;
                return null ;
            }
            set { hiddenOptionCards = value; }
        }
        private OptionCardList? hiddenOptionCards;

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

        public Player? Connect(string ipAddress = "")
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

            ConnectAsync().Wait();
            PrintAsync(Name).Wait();
            SendColorStatus();
            return this;
        }

        public bool SendColorStatus(int Status = 0)
        {
            if (!isConnected) return false;

            int r = int.Parse(Color.Substring(0, 2), NumberStyles.HexNumber);
            int g = int.Parse(Color.Substring(2, 2), NumberStyles.HexNumber);
            int b = int.Parse(Color.Substring(4, 2), NumberStyles.HexNumber);

            switch (Status)
            {
                case 1: // programming
                    SetLedAsync("all", 255, 255, 0).Wait(); // yellow
                    break;
                case 2: // running
                    SetLedAsync("all", 0, 255, 0).Wait(); // green
                    break;
                case 3: // error
                    SetLedAsync("all", 255, 0, 0).Wait(); // red
                    break;
                default:
                    SetLedAsync("all", r, g, b).Wait(); // robot color
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

            int bgR = int.Parse(Color[..2], NumberStyles.HexNumber);
            int bgG = int.Parse(Color[2..4], NumberStyles.HexNumber);
            int bgB = int.Parse(Color[4..6], NumberStyles.HexNumber);
            int fgR = int.Parse(ForeColor[..2], NumberStyles.HexNumber);
            int fgG = int.Parse(ForeColor[2..4], NumberStyles.HexNumber);
            int fgB = int.Parse(ForeColor[4..6], NumberStyles.HexNumber);

            try
            {
                await wsCmd.ConnectAsync(new Uri($"ws://{IPAddress!}:80/ws_cmd"), CancellationToken.None);
                await wsStatus.ConnectAsync(new Uri($"ws://{IPAddress!}:80/ws_status"), CancellationToken.None);

                isConnected = true;

                await SendCommandAsync(new { cmd_id = "program_init" });
                await SendCommandAsync(new { cmd_id = "lcd_clear_screen", r = bgR, g = bgG, b = bgB });
                await SendCommandAsync(new { cmd_id = "lcd_set_pen_color", r = fgR, g = fgG, b = fgB });
                await SetLedAsync("all", bgR, bgG, bgB );

                _statusCts = new CancellationTokenSource();
                _ = ListenStatusAsync(_statusCts.Token);
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

            Console.WriteLine("Sending command: " + JsonSerializer.Serialize(command));

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
                    }
                }
            }
        }

        public Task CheckMovingStatus()
        {
            if (!isConnected || wsStatus == null)
            {
                isConnected = false;
                isMoving = false;
            }
            return Task.CompletedTask;
        }

        private async Task ListenStatusAsync(CancellationToken ct)
        {
            var buffer = new byte[4096];
            while (!ct.IsCancellationRequested && wsStatus?.State == WebSocketState.Open)
            {
                try
                {
                    var result = await wsStatus.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine("Status event: " + json);
                    ProcessStatusEvent(json);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine("Status listener error: " + ex.Message);
                    break;
                }
            }
        }

        private void ProcessStatusEvent(string json)
        {
            try
            {
                var evt = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (evt == null) return;

                if (evt.TryGetValue("event", out var evtType) && evtType?.ToString() == "motion_complete")
                {
                    isMoving = false;
                    _motionComplete?.TrySetResult(true);
                }
                else if (evt.TryGetValue("is_moving", out var moving))
                {
                    isMoving = moving?.ToString() is "true" or "True" or "1";
                    if (!isMoving) _motionComplete?.TrySetResult(true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Status parse error: " + ex.Message);
            }
        }

        public async Task WaitForMotionCompleteAsync(int timeoutMs = 5000)
        {
            _motionComplete = new TaskCompletionSource<bool>();
            using var cts = new CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => _motionComplete.TrySetResult(false));
            await _motionComplete.Task;
            _motionComplete = null;
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
        }

        public async Task RunTest()
        {
            await ConnectAsync();

            await ClearScreenAsync();
            await PrintAsync("Hello from C#!");
            await SetLedAsync("all", 0, 255, 0);

            await MoveAsync(76, 0);
            await WaitForMotionCompleteAsync();

            await TurnAsync(1);
            await WaitForMotionCompleteAsync();
        }

        public async Task SendRobotCommandAsync(CommandItem cmd)
        {
            int moveType = cmd.CommandMoveType;
            switch (moveType)
            {
                case 1: // Move
                    Console.WriteLine($"move --- Value:{cmd.Value} ValueB:{cmd.ValueB} rotation: {RotationFunctions.Degrees(cmd.ValueB)}");
                    await MoveAsync(cmd.Value, cmd.ValueB);
                    break;
                case 2: // Turn
                    await TurnAsync(cmd.Value);
                    break;
                case 0: // Stop
                    await StopAsync();
                    break;
                default:
                    break;
            }

            //if (cmd.Category == CommandCategories.RobotwReply)
            //{
            //    await Task.Delay(1500);
            //}
        }

        public Task MoveAsync(int distance, int angle) =>
            SendCommandAsync(new
            {
                cmd_id = "drive_for",
                distance = distance * 77,
                angle = RotationFunctions.Degrees(angle),
                drive_speed = 100,
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

        public Task PrintAsync(string text) =>
            SendCommandAsync(new
            {
                cmd_id = "lcd_print",
                @string = text
            });

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

        // Connect to the robot's ws_img channel and receive one image frame.
        // Returns raw bytes (typically JPEG) or null on failure/timeout.
        public async Task<byte[]?> GetCameraImageAsync(int timeoutMs = 3000)
        {
            if (IPAddress == null) return null;
            using var ws = new ClientWebSocket();
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await ws.ConnectAsync(new Uri($"ws://{IPAddress}:80/ws_img"), cts.Token);
                var segments = new List<byte[]>();
                var buffer = new byte[65536];
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    segments.Add(buffer[..result.Count]);
                    if (result.EndOfMessage) break;
                }
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                if (segments.Count == 0) return null;
                int total = segments.Sum(s => s.Length);
                var combined = new byte[total];
                int pos = 0;
                foreach (var seg in segments) { seg.CopyTo(combined, pos); pos += seg.Length; }
                return combined;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ws_img error for {IPAddress}: {ex.Message}");
                return null;
            }
        }
    }
    #endregion

}
