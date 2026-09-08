using System.Xml.Serialization;
using System.ComponentModel.DataAnnotations.Schema;
using MRR.Devices;

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
    /// in MRR.Contracts) plus a handle to the live transport used to drive the physical VEX AIM
    /// robot. The transport itself -- sockets, LCD, LEDs, camera, IMU -- lives in
    /// <see cref="RobotConnection"/> (MRR/Devices), owned by the shared
    /// <see cref="RobotConnections"/> registry rather than by this object, so rebuilding the
    /// player list (DataService.GetAllPlayers) never touches a live connection. Every method
    /// below is a thin forward onto <see cref="Connection"/>. See
    /// API_DECOMPOSITION_DESIGN.md section 5.5.
    /// </summary>
    public class Player : PlayerState
    {
        public Player() : base() { }

        /// <summary>
        /// Copy constructor. Copies game state only — the new instance has no Connection and is
        /// not connected. That is what turn simulation and the rear-laser stand-in want: a
        /// state snapshot, never a second handle on the physical robot.
        /// </summary>
        public Player(PlayerState p_Player) : base(p_Player) { }

        /// <summary>
        /// The live transport for this robot, attached by DataService.GetAllPlayers() from the
        /// shared RobotConnections registry. Null until a connection registry entry exists for
        /// this robot's ID.
        /// </summary>
        [NotMapped]
        [XmlIgnore]
        public RobotConnection? Connection { get; set; }

        public bool isConnected
        {
            get => Connection?.IsConnected ?? false;
            set { if (Connection != null) Connection.IsConnected = value; }
        }

        /// <summary>
        /// The robot screen UI instance for this player. Only populated when
        /// GameController.UseRobotScreen is true and the robot is connected.
        /// </summary>
        [NotMapped]
        [XmlIgnore]
        public RobotScreenUI? ScreenUI { get; set; }

        public async Task<Player?> Connect(string ipAddress = "")
        {
            if (Connection == null || string.IsNullOrEmpty(Connection.IPAddress))
            {
                return null;
            }

            await ConnectAsync();
            return this;
        }

        public bool SendColorStatus(int Status = 1)
        {
            if (!isConnected || Connection == null) return false;

            switch (Status)
            {
                case 0: // off
                    Connection.SetLedAsync("all", 0, 0, 0).Wait(); // off
                    break;
                case 1: // Normal
                    var (r, g, b) = ColorHelper.ParseHex(Color);
                    Connection.SetLedAsync("all", r, g, b).Wait(); // robot color
                    break;
                case 2: // running
                    Connection.SetLedAsync("all", 0, 255, 0).Wait(); // green
                    break;
                case 3: // error
                    Connection.SetLedAsync("all", 255, 0, 0).Wait(); // red
                    break;
                default:
                    Connection.SetLedAsync("all", 255, 255, 0).Wait(); // yellow
                    break;
            }

            return true;
        }

        // ── Transport forwarders (real implementation moved to RobotConnection) ──────

        public Task ConnectAsync() =>
            Connection == null ? Task.CompletedTask : Connection.ConnectAsync(Name, Color, ForeColor);

        public Task SendCommandAsync(object command) =>
            Connection == null ? Task.CompletedTask : Connection.SendCommandAsync(command);

        public Task<RobotStatus> GetStatusAsync() =>
            Connection == null ? Task.FromResult(new RobotStatus()) : Connection.GetStatusAsync();

        public Task<RobotStatus> WaitForStopAsync() =>
            Connection == null ? Task.FromResult(new RobotStatus()) : Connection.WaitForStopAsync();

        public ValueTask DisposeAsync() =>
            Connection == null ? ValueTask.CompletedTask : Connection.DisposeAsync();

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
            Connection == null ? Task.CompletedTask : Connection.MoveAsync(distance, angle, final_heading, drive_speed);

        public Task SetPoseAsync(int x = 0, int y = 0) =>
            Connection == null ? Task.CompletedTask : Connection.SetPoseAsync(x, y);

        public Task MoveAndWaitAsync(int distance, int angle) =>
            Connection == null ? Task.CompletedTask : Connection.MoveAndWaitAsync(distance, angle);

        public Task TurnAndWaitAsync(int direction) =>
            Connection == null ? Task.CompletedTask : Connection.TurnAndWaitAsync(direction);

        public Task MoveUnlimitedAsync(double angle, double speed) =>
            Connection == null ? Task.CompletedTask : Connection.MoveUnlimitedAsync(angle, speed);

        public Task StopAsync() =>
            Connection == null ? Task.CompletedTask : Connection.StopAsync();

        public Task TurnAsync(int direction) =>
            Connection == null ? Task.CompletedTask : Connection.TurnAsync(direction);

        public Task PrintAsync(string text) =>
            Connection == null ? Task.CompletedTask : Connection.PrintAsync(text);

        public Task SetCursorAsync(int row, int col) =>
            Connection == null ? Task.CompletedTask : Connection.SetCursorAsync(row, col);

        public Task ClearScreenAsync() =>
            Connection == null ? Task.CompletedTask : Connection.ClearScreenAsync();

        public Task SetLedAsync(string led, int r, int g, int b) =>
            Connection == null ? Task.CompletedTask : Connection.SetLedAsync(led, r, g, b);

        public Task ShowAIAsync() =>
            Connection == null ? Task.CompletedTask : Connection.ShowAIAsync();

        public Task<GridLineAnalysis> AlignAsync(int maxIterations = 10) =>
            Connection == null
                ? Task.FromResult(new GridLineAnalysis(false, 0, 0, -1, -1, false))
                : Connection.AlignAsync(maxIterations);

        public Task<byte[]?> GetCameraImageAsync(int timeoutMs = 5000) =>
            Connection == null ? Task.FromResult<byte[]?>(null) : Connection.GetCameraImageAsync(timeoutMs);

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
            Console.WriteLine($"Player {ID} played {CPCount} cards {CardsPlayedStr}");
            SendColorStatus(CPCount==5?1:0);
        }
    }
}
