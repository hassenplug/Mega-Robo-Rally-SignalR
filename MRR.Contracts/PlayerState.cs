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
        const int conTotalLives = 3;

        #region Player Constructors

        public PlayerState()
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
        public int PlayerViewDirection { get; set; }

        [NotMapped]
        public string StatusToShow
        {
            get
            {
                var cards = CardsPlayed;
                string? showCardsPlayed = cards.Count == 0 ? null
                    : string.Join(",", cards.Select(c => c.Executed ? cards.GetCardText(c) : "X"));
                return (showCardsPlayed == null || !Active)
                    ? PlayerStatus.Info().ShortDescription
                    : showCardsPlayed;
            }
        }

        [NotMapped]
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
            Password            = Password,
            PlayerSeat          = PlayerSeat,
            Energy              = Energy,
            FlagEnergy          = $"{LastFlag}/{CardsPlayer.Count}",
            PlayerViewDirection = PlayerViewDirection,
            DirectionAdjustment = PlayerViewDirection,
            CardsDealt          = CardsDealtStr,
            CardsPlayed         = CardsPlayedStr,
            StatusToShow        = StatusToShow,
            msg                 = PlayerMsg,
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
