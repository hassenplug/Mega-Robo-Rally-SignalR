
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

        [NotMapped]
        public Robots.AIMRobot? RobotConnection { get; set; }

        public Robots.AIMRobot? Connect(string ipAddress = "")
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

            if (RobotConnection != null)
            {
                //RobotConnection.DisconnectAsync().Wait();
                //RobotConnection = null;
                // ensure robot is connected here...
            }
            RobotConnection = new Robots.AIMRobot(IPAddress);
            RobotConnection.ConnectAsync().Wait();
            RobotConnection.PrintAsync(Name).Wait();
            SendColorStatus();
            return RobotConnection;
        }

        public bool SendColorStatus(int Status = 0)
        {
            if (RobotConnection == null) return false;

            int r = int.Parse(Color.Substring(0, 2), NumberStyles.HexNumber);
            int g = int.Parse(Color.Substring(2, 2), NumberStyles.HexNumber);
            int b = int.Parse(Color.Substring(4, 2), NumberStyles.HexNumber);

            switch (Status)
            {
                case 1: // programming
                    RobotConnection.SetLedAsync("all", 255, 255, 0).Wait(); // yellow
                    break;
                case 2: // running
                    RobotConnection.SetLedAsync("all", 0, 255, 0).Wait(); // green
                    break;
                case 3: // error
                    RobotConnection.SetLedAsync("all", 255, 0, 0).Wait(); // red
                    break;
                default:
                    RobotConnection.SetLedAsync("all", r, g, b).Wait(); // robot color
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
    }
    #endregion

}
