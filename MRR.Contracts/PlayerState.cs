using System.ComponentModel;
using System.Reflection;
using System.Xml.Serialization;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MRR
{
    #region Player Enums

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class StatusInfoAttribute(string statusColor, string ledColor, string shortDescription) : Attribute
    {
        public string StatusColor      { get; } = statusColor;
        public string LEDColor         { get; } = ledColor;
        public string ShortDescription { get; } = shortDescription;
    }

    public enum tPlayerStatus
    {
        [StatusInfo("FFFFFF", "FFFFFF", "Unknown")]   Unknown          = 0,
        [StatusInfo("FFFFFF", "FFFFFF", "Wait")]      WaitingForCards  = 1,
        [StatusInfo("CCFFCC", "003333", "Program")]   ReadyToProgram   = 2,
        [StatusInfo("AAFFAA", "008888", "Program")]   Programming      = 3,
        [StatusInfo("00FF00", "00FF00", "Ready")]      ReadyToRun       = 4,
        [StatusInfo("0000FF", "0000FF", "Moving")]     MoveInProgress   = 5,
        [StatusInfo("0000FF", "0000FF", "Moving")]     Moving           = 6,
        [StatusInfo("FFA500", "FFA500", "Connect")]    ConnectionFailing = 7,
        [StatusInfo("AAAAFF", "000088", "Connect")]    Connected        = 8,
        [StatusInfo("FFFF00", "FFFF00", "Shut Down")] ShutDown         = 9,
        [StatusInfo("FF0000", "FF0000", "Inactive")]   NotActive        = 10,
        [StatusInfo("FF0000", "FF0000", "Dead")]        Dead             = 11,
        [StatusInfo("88FF88", "88FF88", "Done")]        MoveComplete     = 12,
        [StatusInfo("55FF55", "55FF55", "Locked In")]  ProgramLocked    = 13,
        [StatusInfo("FFFF00", "FFFF00", "Laser")]       LaserFired       = 14,
        [StatusInfo("0000FF", "0000FF", "Respawn")]     Respawn          = 15,

        // Robots.ConnectStatusID values -- whether we have a live WebSocket to the robot,
        // distinct from the rest of this enum (Robots.Status), which is the robot's *game*
        // state. Former tConnectStatus, folded in here so both columns share one enum type.
        // IDs 20-23 avoid the 0-14 range above; Unknown (0) is shared by both columns.
        // "Connected" is already taken above (id 8) so this one is RobotConnected.
        [StatusInfo("FF0000", "FF0000", "Not Conn")]    NotConnected  = 20,
        [StatusInfo("FFFF00", "FFFF00", "Connecting")]  Connecting    = 21,
        [StatusInfo("00FF00", "00FF00", "Connected")]   RobotConnected = 22,
        [StatusInfo("800080", "800080", "Searching")]   Searching     = 23,
    }

    public static class PlayerStatusExtensions
    {
        public static StatusInfoAttribute Info(this tPlayerStatus status)
        {
            var field = typeof(tPlayerStatus).GetField(status.ToString());
            return field?.GetCustomAttribute<StatusInfoAttribute>()
                ?? new StatusInfoAttribute("FFFFFF", "FFFFFF", status.ToString());
        }
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

    /// <summary>
    /// A robot's game state: position, damage, cards, status. Everything here is data the
    /// rules engine and the database care about.
    ///
    /// Deliberately knows nothing about how to talk to a physical robot -- no WebSocket, no
    /// RobotScreenUI. Player (in the host) derives from this and adds the transport, so any
    /// existing code that passes a Player where state is wanted still compiles unchanged.
    /// This is what lets CommandItem, CommandList and OptionCardList live in MRR.Contracts.
    /// See API_DECOMPOSITION_DESIGN.md section 5.5.
    /// </summary>
    [Table("Robots")]
    public class PlayerState
    {

        const int conTotalDamage = 10;

        #region Player Constructors

        public PlayerState()
        {
            ID = -1;
            ShutDown = tShutDown.None;
            CurrentPos = new RobotLocation();
            NextPos = new RobotLocation();
            ArchivePos = new RobotLocation();
            NextFlag = new RobotLocation();
            LastFlag = 0;
            Name = ToString();
            PositionValid = false;
            DamagePoints = 0;
            DamagedBy = -1;
        }


        public PlayerState(PlayerState p_Player)
            : this()
        {
            ID = p_Player.ID;
            Name = p_Player.Name;
            ShutDown = p_Player.ShutDown;
            CurrentPos = new RobotLocation(p_Player.CurrentPos);
            NextPos = new RobotLocation(p_Player.NextPos);
            ArchivePos = new RobotLocation(p_Player.ArchivePos);
            NextFlag = p_Player.NextFlag;
            LastFlag = p_Player.LastFlag;
            PositionValid = false;
            DamagePoints = p_Player.DamagePoints;
            DamagedBy = -1;
            Operator = p_Player.Operator;
            Priority = p_Player.Priority;
            Energy = p_Player.Energy;
            PlayerSeat = p_Player.PlayerSeat;
            RespawnID = p_Player.RespawnID;
        }



        #endregion

        [Key]
        [Column("RobotID")]
        public int ID { get; set; }

        [NotMapped]
        public string Name { get; set; } = "";

        [NotMapped]
        public string Operator { get; set; } = "";

        // TotalFlags is deliberately not a Player property — there is one flag count for the
        // whole game, held in CurrentGameData (iKey 7) and exposed as DataService.TotalFlags.

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
        public bool IsRunning // not dead or shut down
        {
            get
            {
                return this.PlayerStatus != tPlayerStatus.Dead && this.PlayerStatus != tPlayerStatus.ShutDown && this.RespawnID == 0;
            }
        }

        [NotMapped]
        public bool IsDead
        {
            get
            {
                return this.PlayerStatus == tPlayerStatus.Dead;
            }
        }

        [NotMapped]
        public bool IsShutDown
        {
            get
            {
                return this.PlayerStatus == tPlayerStatus.ShutDown;
            }
        }

        public int Priority { get; set; }
        public int Energy { get; set; }
        public int PlayerSeat { get; set; }

        // Which Respawn square (SquareAction.Respawn's Parameter -- 1=A, 2=B, 3=C, ...) this
        // robot last respawned at, set by DataService.RespawnRobotAtRebootToken. 0 when it
        // fell back to ArchivePos (no Respawn square on the board) or has never died.
        public int RespawnID { get; set; }

        public int? MessageCommandID { get; set; }
        public int Score { get; set; }


        public int CurrentPosRow { get => CurrentPos.Y; set => CurrentPos.Y = value; }
        public int CurrentPosCol { get => CurrentPos.X; set => CurrentPos.X = value; }
        public int CurrentPosDir { get => (int)CurrentPos.Direction; set => CurrentPos.Direction = (Direction)value; }

        public int ArchivePosRow { get => ArchivePos.Y; set => ArchivePos.Y = value; }
        public int ArchivePosCol { get => ArchivePos.X; set => ArchivePos.X = value; }
        public int ArchivePosDir { get => (int)ArchivePos.Direction; set => ArchivePos.Direction = (Direction)value; }

        [NotMapped]
        public string Color { get; set; } = "333333"; // hex color string RRGGBB

        [NotMapped]
        public string ForeColor { get; set; } = "FFFFFF"; // hex color string RRGGBB


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
        public CardList CardsPlayed =>
            [.. CardsPlayer.Where(gc => gc.PhasePlayed > 0).OrderBy(pc => pc.PhasePlayed)];

        [NotMapped]
        [XmlIgnore]
        public string CardsDealtStr =>
            string.Join(",", CardsPlayer.Where(c => c.CardLocation == 1).OrderByDescending(c => c.Type).Select(c => (int)c.Type));

        [NotMapped]
        [XmlIgnore]
        public string CardsPlayedStr =>
            string.Join(",", Enumerable.Range(1, 5).Select(phase =>
                (int)(CardsPlayer.FirstOrDefault(c => c.PhasePlayed == phase)?.Type ?? MoveCard.tCardType.Unknown)));


        [NotMapped]
        public CardList? AllGameCards { get; set; }

        [NotMapped]
        public CardList CardsPlayer => [.. (AllGameCards ?? []).Where(c => c.Owner == ID)];

        [NotMapped]
        public OptionCardList? OptionCards { get; set; }

        [Column("CurrentFlag")]
        public int LastFlag { get; set; }

        [Column("Status")]
        public tPlayerStatus PlayerStatus { get; set; }


        [NotMapped]
        public int PlayerViewDirection { get; set; }

        [NotMapped]
        public string StatusToShow
        {
            get
            {
                var cards = CardsPlayed;
                string? showCardsPlayed = cards.Count == 0 ? null
                    : string.Join("", cards.Select(c => c.Executed ? cards.GetCardText(c) : "X"));
                    //: string.Join(",", cards.Select(c => c.Executed ? cards.GetCardText(c) : "X"));
                return (showCardsPlayed == null || IsDead || IsShutDown)
                    ? PlayerStatus.Info().ShortDescription
                    : showCardsPlayed;
            }
        }

        [Column("PlayerMsg")]
        public string PlayerMsg { get; set; } = "";

        public string Password { get; set; } = "";

        [NotMapped]
        public string? IPAddress { get; set; }

        public RobotData ToRobotData() => new()
        {
            RobotID             = ID,
            RobotName           = Name,
            RobotColor          = Color,
            RobotColorFG        = ForeColor,
            CurrentFlag         = LastFlag,
            StatusColor         = PlayerStatus.Info().StatusColor,
            LEDColor            = PlayerStatus.Info().LEDColor,
            PlayerStatus        = PlayerStatus.Info().ShortDescription,
//            StatusColor         = (isConnected ? PlayerStatus : tPlayerStatus.NotActive).Info().StatusColor,
//            LEDColor            = (isConnected ? PlayerStatus : tPlayerStatus.NotActive).Info().LEDColor,
//            PlayerStatus        = (isConnected ? PlayerStatus : tPlayerStatus.NotActive).Info().ShortDescription,
            StatusID            = (int)PlayerStatus,
            X                   = CurrentPos.X,
            Y                   = CurrentPos.Y,
            Dir                 = (int)CurrentPos.Direction,
            sDir                = CurrentPos.Direction.ToString(),
//            AX                  = ArchivePos.X,
//            AY                  = ArchivePos.Y,
//            Score               = Score,
            OperatorName        = Operator,
//            PositionValid       = PositionValid ? 1 : 0,
            Priority            = Priority,
            ShutDown            = (int)ShutDown,
            PlayerSeat          = PlayerSeat,
            Energy              = Energy,
            FlagEnergyCards     = $"{LastFlag}/{Energy}/{CardsPlayer.Count}",
            PlayerViewDirection = PlayerViewDirection,
            DirectionAdjustment = PlayerViewDirection,
            CardsDealt          = CardsDealtStr,
            CardsPlayed         = CardsPlayedStr,
            StatusToShow        = StatusToShow,
            PlayerMsg           = PlayerMsg,
            CardCount           = CardsPlayer.Count,
        };
        public override string ToString()
        {
            if (ID == -1)
            {
                return "-";
            }

            return "[" + ID.ToString() + "]" + CurrentPos;
        }
    }
}
