
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel; //ObservableCollection
using System.ComponentModel; //INotifyPropertyChanged
//using System.IO;
using System.IO.Ports; // serial port

//using System.Windows.Media;  // brushes
//using System.Windows.Media.Imaging;
//using System.Windows.Controls;
using System.Windows;
using System.Xml.Serialization;
//using System.Windows.Data;
using System.Globalization;
using MySqlConnector;
using MRR.Services;
using System.Data;

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
        //[Description("Connected")]Connected,

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
        private DataService _dataService;

        public Players(DataService dataservice)
        {
            _dataService = dataservice;
        }



        public Players(int RobotID = 0) // 0 = all
        {
            // ensure a DataService exists for callers that use the parameterless/new Players()
            if (_dataService == null) _dataService = new DataService();
            string strSQL = "Select RobotID,CurrentFlag,Lives,Damage,ShutDown,Status,CurrentPosRow,CurrentPosCol,CurrentPosDir,Priority,Energy,PlayerSeat from Robots where Status <> 10 ";
            if (RobotID > 0) strSQL += " and RobotID=" + RobotID ;
            strSQL += ";";

            var loadplayers = _dataService.GetQueryResults(strSQL);
            foreach (DataRow row in loadplayers.Rows)
            {
                this.Add(new Player()
                {
                    ID = (int)row["RobotID"],
                    LastFlag = (int)row["CurrentFlag"],
                    Lives = (int)row["Lives"],
                    Damage = (int)row["Damage"],
                    ShutDown = (tShutDown)((int)row["ShutDown"]),
                    PlayerStatus = (tPlayerStatus)((int)row["Status"]),
                    CurrentPos = new RobotLocation((Direction)(int)row["CurrentPosDir"], (int)row["CurrentPosCol"], (int)row["CurrentPosRow"]),
                    Priority = (int)row["Priority"],
                    Energy = (int)row["Energy"],
                    PlayerSeat = (int)row["PlayerSeat"],
                    Name = "Robo-" + row["RobotID"].ToString(),
                    Active = ((int)row["Status"] != 10),
                    DamagePoints = 0
                });
            }

        }

        public Player GetPlayer(int p_PlayerID)
        {
            return GetPlayer(pl => pl.ID == p_PlayerID);
        }

        public Player GetPlayer(RobotLocation p_Square)
        {
            return GetPlayer(ap => ((ap.CurrentPos.X == p_Square.X) && (ap.CurrentPos.Y == p_Square.Y) && (ap.Active)));
        }

        public Player GetPlayer(Func<Player,bool> filter)
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
    public class Player
    {

        const int conTotalDamage = 10;
        const int conTotalLives = 3;

        #region Player Constructors

        // main constructor

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
            bool p_ComputerPlayer,
            int p_DamagePoints)
        {
            //MainGame = mainGame;
            ID = p_ID;
            ShutDown = p_ShutDown;

            CurrentPos = new RobotLocation(p_CurrentPos);
            NextPos = new RobotLocation(p_NextPos);
            ArchivePos = new RobotLocation(p_Archive);
            //NextFlag = new RobotLocation(p_CurrentPos);
            NextFlag = new RobotLocation(p_CurrentPos);
    
            Damage = p_StartingDamage;
            Lives = p_Lives;
            LastFlag = p_LastFlag;

            Name = p_Name;

            /// placement of this is critical
            Active = p_Active;
            ///

            PositionValid = false;

            ComputerPlayer = p_ComputerPlayer;
            DamagePoints = p_DamagePoints;

            DamagedBy = -1;

            return this;
        }

    /// <summary>
    /// Initialize player
    /// </summary>
    /// <param name="p_ID"></param>
        public Player(int p_ID)
        {
            int currentlives = conTotalLives;
            SetPlayer(p_ID, ToString(), new RobotLocation(), new RobotLocation(), new RobotLocation(), conTotalDamage, currentlives, 0, tShutDown.None, true, false,0);
        }

        public Player()
            :this(-1)
        {
        }

        public Player(Player p_Player)
            //: this(p_Player.ID, p_Player.CurrentPos, p_Player.NextPos, p_Player.ArchivePos, p_Player.Damage, p_Player.Lives, p_Player.LastFlag, p_Player.TotalFlags)
        {
            CopyPlayer(p_Player);
        }

        public Player CopyPlayer(Player p_Player)
        {
            SetPlayer( p_Player.ID, p_Player.Name, p_Player.CurrentPos, p_Player.NextPos, p_Player.ArchivePos, p_Player.Damage, p_Player.Lives, p_Player.LastFlag, p_Player.ShutDown, p_Player.Active, p_Player.ComputerPlayer,p_Player.DamagePoints);
            //GameType = p_Player.GameType;
            NextFlag = p_Player.NextFlag;
            Operator = p_Player.Operator;
            Priority = p_Player.Priority;
            Energy = p_Player.Energy;
            PlayerSeat = p_Player.PlayerSeat;

            return this;
        }

        #endregion

//        public RRGame MainGame { get; set; }

        public int ID { get; set; }
        public string Name { get; set; }
        public string Operator { get; set; }

        public int TotalFlags
        {
            get { return 5 ; } //MainGame.TotalFlags; }
            set { }
        }

        [XmlIgnore]
        public RobotLocation NextPos { get; set; }

        [XmlIgnore]
        public RobotLocation NextFlag { get; set; }

        [XmlIgnore]
        public bool PositionValid { get; set; }

        public RobotLocation ArchivePos { get; set; }

        public RobotLocation CurrentPos { get; set; }

        public tShutDown ShutDown { get; set; }

        // running is used for a robot that will receive cards
        // active is a robot that's in the game, and will receive damage and be affected by the board (shut down?)

        //private bool l_running = false;
        [XmlIgnore]
        public bool IsRunning
        {
            get
            {
                return Active && !(ShutDown == tShutDown.Currently);
                //return l_running;
            }
        }


        public bool Active { get; set; } // (currently in game?)

        public int Priority { get; set; }
        public int Energy { get; set; }
        public int PlayerSeat { get; set; }

        public bool ComputerPlayer { get; set; }

        //[XmlIgnore]
        //public int CardsNeeded
        //{
        //    get { return CardsAfterTurn - PlayerCards.Where(pc => pc.Owner == ID).Count(pc=>!pc.Locked) ; } // calc cards already in hand
        //    //get { return CardsAfterTurn - PlayerCardCount; } // calc cards already in hand
        //    set {  }
        //}


        public int Lives { get; set; }

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
                    value = conTotalDamage; // need to make sure we can repair 1 point, and NOT return to life
                    Active = false;
                }
                l_damage = value;

            }
        }


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
                //return Math.Abs(CurrentPos.X - NextFlag.X) + Math.Abs(CurrentPos.Y - NextFlag.Y); 
            }

        }

        public int DamagePoints { get; set; }
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

        public RobotLocation CalcNewLocation(int p_distance, Direction p_direction) //RobotLocation p_CurrentLocation)
        {
            // check direction
            // move p_distance based on direction
            return CurrentPos.CalcNewLocation(p_distance, p_direction);

        }

        [XmlIgnore]
        public int CardsPlayedCount { get { return CardsPlayed.Count(); } }

        /// <summary>
        /// Cards Played is a list of only cards already played
        /// </summary>
        [XmlIgnore]
        public CardList CardsPlayed
        {
            //get { return (CardList)(CardsPlayer.Where(gc => gc.PhasePlayed > 0).OrderBy(pc => pc.PhasePlayed)); }
            get { return new CardList(CardsPlayer.Where(gc => gc.PhasePlayed > 0).OrderBy(pc => pc.PhasePlayed)); }
        }

        private CardList hiddenCardsPlayer;
        //[XmlIgnore]
        public CardList CardsPlayer
        {
            get
            {
                if (hiddenCardsPlayer != null) return hiddenCardsPlayer;
                //if (MainGame == null) return null;
                //return new CardList(MainGame.GameCards.Where(gc => gc.Owner == ID )); }
                return null;}
            set { hiddenCardsPlayer = value; }
        }

        private OptionCardList hiddenOptionCards;
        //[XmlIgnore]
        public OptionCardList OptionCards
        {
            get
            {
                if (hiddenOptionCards != null) return hiddenOptionCards;
                //if (MainGame == null) return null;

                //return new OptionCardList(MainGame.OptionCards.Where(gc => gc.Owner == ID));
                return null ; 
            }
            set { hiddenOptionCards = value; }
        }

        public bool HasOptionCard(tOptionCardCommandType OptionID)
        {
            if (!this.IsRunning) return false;
            return false;
            //OptionCard thiscard = MainGame.OptionCards.GetOption(OptionID, this);
            //return (thiscard != null);
        }

        public int LastFlag { get; set; }

        public tPlayerStatus PlayerStatus { get; set; }

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
