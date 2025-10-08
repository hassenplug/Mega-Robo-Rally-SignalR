using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel; //ObservableCollection
using System.ComponentModel;//INotifyPropertyChanged
//using System.Windows.Media; // brushes
using System.Xml.Serialization; // serializer
//using System.Windows.Data; // iconverter
using MySqlConnector;


///command item sub steps
/// 0 connect
/// 1 Start Move
/// 2 turn
/// 3-6 move/Fire?
/// 7 Unturn
/// 8 Stop Move
/// 9 disconnect

namespace MRR_CLG
{

    #region Command List
    public class CommandList : ObservableCollection<CommandItem>
    {
        public CommandList()
            : base()
        {
            Phase = 0;
        }


        public CommandList(Database mydb):this()
        {
            // load command list from db

        }

/*
                public void AddOneCommandToDB(CommandItem thisCommand) //, int commandID, int RunningCommand)
        {
            string strSQL = "insert into CommandList " +
                "(CommandID, Turn, Phase, CommandSequence, CommandSubSequence, " +
                " CommandTypeID, Parameter, ParameterB, RobotID, StatusID, BTCommand, Description, PositionRow, PositionCol, PositionDir,CommandCatID) " +
                " values (" + thisCommand.NormalSequence.ToString() + "," + CurrentTurn.ToString() + "," + thisCommand.Phase + "," + thisCommand.NormalSequence + "," + thisCommand.RunningCounter + "," +
                thisCommand.CommandTypeInt + "," + thisCommand.Value + "," + thisCommand.ValueB + "," + thisCommand.RobotID + "," + (int)thisCommand.Status + ",'" + thisCommand.StringCommand + "','" +
                thisCommand.Description + "'," + thisCommand.EndPos.Y + "," + thisCommand.EndPos.X + "," + (int)thisCommand.EndPos.Direction + "," + (int)thisCommand.Category
                + ")";

            DBConn.Command(strSQL);

        }
*/

  //      public CommandItem AddCommand()
  //      {
  //          return AddCommand();

 //       }


        public CommandItem AddCommand(CommandItem p_InsertBefore, Player p_Player, SquareAction p_Action, tCommandSequence p_Sequence)
        {
            // insert turning if this move is a turn... (turn "insert before" move)  (or do it at end of phase generation)

            CommandItem newCommand = new CommandItem(p_InsertBefore.Phase, p_InsertBefore.PhaseStep, p_Player, p_InsertBefore.Value, p_InsertBefore.ValueB, p_InsertBefore.CommandDirection, p_Action);

            int location = this.IndexOf(p_InsertBefore);
            if (p_Sequence == tCommandSequence.After) location++;

            this.Insert(location, newCommand);
            //this.Add(newCommand);
            //newCommand.RunningCounter = this.Count();
            return newCommand;
        }

        public CommandItem AddCommand(Player p_Player, SquareAction p_Action, int p_Value = 0, int p_ValueB = 0)
        {
            Direction holddir = Direction.None;
            if (p_Player != null) holddir = p_Player.CurrentPos.Direction;
            return AddCommand(p_Player, p_Value, p_ValueB, holddir, p_Action);
        }

        public CommandItem AddCommand(Player p_Player, OptionCard p_OptionCard, SquareAction p_action = SquareAction.PlayOptionCard)
        {
            if (p_OptionCard == null) return null;
            switch (p_action)
            {
                case SquareAction.OptionCountSet:
                    return AddCommand(p_Player, p_OptionCard.ID,0, (Direction)p_OptionCard.Quantity, p_action);
                case SquareAction.PlayOptionCard:
                    return AddCommand(p_Player, p_OptionCard.ID,0, p_OptionCard.OptionDirection, p_action);
                default:
                    return null;
            }

        }

        public CommandItem AddCommand(Player p_Player, tOptionCardCommandType p_OptionCardType)
        {
            return AddCommand(p_Player, SquareAction.PlayOptionCard, (int)p_OptionCardType);
        }

        /// <summary>
        /// Add "move" command
        /// </summary>
        /// <param name="p_Player"></param>
        /// <param name="p_Value"></param>
        /// <param name="p_Direction"></param>
        /// <param name="p_Action"></param>
        /// <returns></returns>
        public CommandItem AddCommand(Player p_Player, int p_Value, int p_ValueB, Direction p_Direction, SquareAction p_Action)
        {
            CommandItem newCommand = new CommandItem(Phase, PhaseStep, p_Player, p_Value, p_ValueB, p_Direction, p_Action);
            this.Add(newCommand);
            //newCommand.RunningCounter = this.Count();
            //if (p_Player.ID ==
            return newCommand;
        }

        /// <summary>
        /// Set CurrentGameData for ParameterA to ParameterB
        /// </summary>
        /// <param name="p_Value"></param>
        /// <param name="p_ValueB"></param>
        /// <returns></returns>
        public CommandItem AddCommand(int p_Value, int p_ValueB)
        {
            CommandItem newCommand = new CommandItem(Phase, PhaseStep, null, p_Value, p_ValueB, Direction.None, SquareAction.SetCurrentGameData);
            this.Add(newCommand);
            return newCommand;
        }

        public CommandItem AddCommand(string p_buttonText,Player p_Robot = null)
        {
            CommandItem newCommand = new CommandItem(Phase, PhaseStep, p_Robot, 0, 0, Direction.None, SquareAction.SetButtonText);
            newCommand.text = p_buttonText;
            this.Add(newCommand);
            return newCommand;
        }


//        public CommandItem SetPhase(int p_NewPhase)
//        {
//            Phase = p_NewPhase;
//            return AddCommand(null, SquareAction.PhaseStart, p_NewPhase);
//        }

        public CommandItem SetEnergy(Player p_player, int newEnergy)
        {
            p_player.Energy=newEnergy;
            return AddCommand(p_player, SquareAction.SetEnergy, p_player.Energy);
        }

        public int PhaseStep { get; set; }

        private int l_phase = 0;
        public int Phase
        {
            get { return l_phase; }
            set
            {
                l_phase = value;
                PhaseStep = 0;
                //OnPropertyChanged("Phase");
            }
        }

    }
    #endregion


    #region Command Item
    public class CommandItem : IComparable
    {

        public CommandItem() // this is required to seralize the class
        :this(0,0,null,0,0,Direction.None,SquareAction.None)
        {
        }


        //public CommandItem(int p_Phase, int p_PhaseStep, Player p_Robot, int p_Value, SquareAction p_Type)
        //    :this(p_Phase, p_PhaseStep,2, p_Robot, p_Value, Direction.None, p_Type)
        //{
        //}

        /// <summary>
        /// Create complete command item
        /// </summary>
        /// <param name="p_Phase"></param>
        /// <param name="p_PhaseStep"></param>
        /// <param name="p_PhaseSubStep"></param>
        /// <param name="p_Robot"></param>
        /// <param name="p_Value"></param>
        /// <param name="p_Direction"></param>
        /// <param name="p_Type"></param>
        public CommandItem(int p_Phase, int p_PhaseStep, Player p_Robot, int p_Value, int p_ValueB, Direction p_Direction, SquareAction p_Type) //, RRGame p_mainGame)
        {
            Phase = p_Phase;
            PhaseStep = p_PhaseStep;

            CommandType = p_Type;
            Value = p_Value;
            ValueB = p_ValueB;

            CommandDirection = p_Direction;
            PhaseStepAdder = 5;

			Status = CommandStatus.Waiting;

            if (p_Robot != null)
            {
                Robot = p_Robot;
                //RobotID = p_Robot.ID;
                StartPos = new RobotLocation(p_Robot.CurrentPos);
                EndPos = new RobotLocation(p_Robot.NextPos);
                //RobotDirection = StartPos.Direction;
                //RobotName = p_Robot.ToString();
            }
            else
            {
                Robot = new Player();
                //RobotID = -1;
                StartPos = new RobotLocation();
                EndPos = new RobotLocation();
                //RobotName = "no robot";
            }

            //Status = CommandStatus.Complete;
        }

        [XmlIgnore]
        public Player Robot { get; set; }
        public int RobotID { get { return Robot.ID; } set { } } // load robot from list of robots
        //public int RobotID { get { return Robot.ID; } set; }

        public Direction CommandDirection { get; set; }


        public RobotLocation StartPos { get; set; }
        public RobotLocation EndPos { get; set; }

        public int Phase { get; set; }
        public int PhaseStep { get; set; }
        public int PhaseStepAdder { get; set; }
        //public Sequences Sequence { get; set; } // order within the step
        //public SequenceSubCommand SequenceSubCommand { get; set; }

        //public int OrderBy { get { return Phase * 10000000 + PhaseStep * 10000 + (int)Sequence * 1000 + RunningCounter; } }
        //public int OrderBy { get { return Phase * 10000000 + PhaseStep * 10000 + RunningCounter; } }  // +(int)Sequence * 1000 + RunningCounter; } }

        public int RunningCounter { get; set; }
        public int NormalSequence { get; set; }
        public int ExpressSequence { get; set; }
        //public int ExpressCounter { get; set; }

        public SquareAction CommandType { get; set; }
        public int CommandTypeInt { get { return (int)CommandType; } set { } }
        public int Value { get; set; }
        public int ValueB { get; set; }
        public string text { get; set; }
        public CommandStatus Status { get; set; }

        private string GetOptionName()
        {
            // if (MainGame.OptionCardNames.ContainsKey(Value))
            // {
            //     return MainGame.OptionCardNames[Value].Replace("[quantity]", ValueB.ToString());
            // }
            // else
            // {
            //     return "invalid option:[" + Value + "]";
            // }
            return "";
        }
        
        private string GetRobotName(int p_RobotID)
        {
            return "";
            // return MainGame.AllPlayers.GetPlayer(p_RobotID).Name;
        }

        public CommandCategories Category { get { return GetCommandDetails.Category; }}

        public int CommandSequence { get { return GetCommandDetails.CommandSequence; }}

        public string Description { get { return GetCommandDetails.Description; }}

        public string StringCommand { get { return GetCommandDetails.BTCommand; }}


        public SquareActionDetails GetCommandDetails
        {
            get 
            {
                int l_Sequence = Phase * 10000 + PhaseStep * 10;
                switch (CommandType)
                {
                    case SquareAction.BoardMove: // distance + 1; back 1 = 0, forward 3 = 4
                    case SquareAction.PushedMove:
                    case SquareAction.Move:
                        return new SquareActionDetails(CommandCategories.RobotwReply, "moves " + Value + " from " + StartPos + " to " + EndPos, l_Sequence + PhaseStepAdder,"1," + (Value + 1));

                    case SquareAction.BoardRotate:// 0-3, 0=left, 1=none, 2=right, 3=uturn
                    case SquareAction.BoardMoveRotate:
                    case SquareAction.PushedMoveRotate:
                    case SquareAction.Rotate:
                        return new SquareActionDetails(CommandCategories.RobotwReply,"turns " + Value + " from " + StartPos + " to " + EndPos, l_Sequence + 3,"2," + (Value + 1));

                    case SquareAction.FireCannon:return new SquareActionDetails(CommandCategories.RobotNoReply,"fires laser at " + text , 0,"3,2");

                    case SquareAction.StartBotMove:return new SquareActionDetails(CommandCategories.RobotNoReply,"Start Move", l_Sequence + 2,"3,1");
                    case SquareAction.StopBotMove:return new SquareActionDetails(CommandCategories.RobotNoReply,"Stop Move", l_Sequence + 4,"3,0");

                    case SquareAction.BTDisconnect:return new SquareActionDetails(CommandCategories.Connection, "disconnect");
                    case SquareAction.BTConnect:return new SquareActionDetails(CommandCategories.Connection,"connect " + (Value==1?"R":""), l_Sequence + 1);

                    case SquareAction.Archive:return new SquareActionDetails(CommandCategories.DB,"archive set to " + StartPos.Location);
                    case SquareAction.Damage:return new SquareActionDetails(CommandCategories.RobotNoReply,"was damaged (" + Value + ")",0,"3,4");

                    case SquareAction.Flag:
                    case SquareAction.TouchFlag:
                    case SquareAction.TouchKotHFlag:
                    case SquareAction.TouchLastManFlag:
                        return new SquareActionDetails(CommandCategories.RobotNoReply,"tag flag: " + Value,0,"3,5");

                    case SquareAction.GameWinner:return new SquareActionDetails(CommandCategories.RobotNoReply, "wins",0,"3,7");

                    case SquareAction.DeletedMove:return new SquareActionDetails(CommandCategories.DB, "move from " + StartPos + " to " + EndPos + " CANCELED");
                    case SquareAction.PhaseStart:return new SquareActionDetails(CommandCategories.DB,"Start Phase: " + Phase,-1); //,Phase * 10000-1);
                    case SquareAction.Dead:return new SquareActionDetails(CommandCategories.RobotNoReply,"is dead",0,"3,8");
                        //return "35";
                    case SquareAction.LostLife:return new SquareActionDetails(CommandCategories.DB,"lost a life");
                    case SquareAction.RobotPush:return new SquareActionDetails(CommandCategories.DB,"pushed by " + GetRobotName(Value));
                    case SquareAction.PlayerLocation:return new SquareActionDetails(CommandCategories.DB,"is at " + StartPos);
                    case SquareAction.BlockDirection:return new SquareActionDetails(CommandCategories.DB,"is blocked by a wall");

                    case SquareAction.Water: return new SquareActionDetails(CommandCategories.DB,"lost 1 move in water");

                    case SquareAction.LogData:return new SquareActionDetails(CommandCategories.DB,"logged data");
                    case SquareAction.PlayOptionCard: return new SquareActionDetails(CommandCategories.RobotNoReply,"Activate " + GetOptionName(),0,"3,6");
                    case SquareAction.None:return new SquareActionDetails(CommandCategories.DB,"No Command");
                    case SquareAction.Option:return new SquareActionDetails(CommandCategories.DB,"Deal option");
                    case SquareAction.DealSpamCard:return new SquareActionDetails(CommandCategories.DB,"Deal Spam Card");
                    case SquareAction.PlayerStart:return new SquareActionDetails(CommandCategories.DB,"start");
                    case SquareAction.BoardDimension:return new SquareActionDetails(CommandCategories.DB,"Board Dimension");
                    case SquareAction.SquareLocation:return new SquareActionDetails(CommandCategories.DB,"Square Location");
                    case SquareAction.SquareTemplate:return new SquareActionDetails(CommandCategories.DB,"Template");
                    //case SquareAction.Card:return new SquareActionDetails(CommandCategories.DB, "played card: ") ; //+ MainGame.GameCards.FirstOrDefault(gc=>gc.ID == Value).Text + "");
                    case SquareAction.Card:
                        if (Value==99) return new SquareActionDetails(CommandCategories.DB, "played card: SPAM"); // + MainGame.GameCards.FirstOrDefault(gc=>(gc.ID == Value) && (gc.Owner==RobotID)).Text + "");
                        return new SquareActionDetails(CommandCategories.DB, "played card: " + "");
                        // return new SquareActionDetails(CommandCategories.DB, "played card: " + MainGame.GameCards.FirstOrDefault(gc=>(gc.ID == Value) && (gc.Owner==RobotID)).Text + "");
                    case SquareAction.Randomizer:return new SquareActionDetails(CommandCategories.DB, "gets random card");
                    case SquareAction.BeginBoardEffects: return new SquareActionDetails(CommandCategories.DB, "begin board effects");
                    case SquareAction.SetPlayerStatus: return new SquareActionDetails(CommandCategories.DB, "Status: " + Value);
                    case SquareAction.DeathPoints: return new SquareActionDetails(CommandCategories.DB, "damage points: " + Value);
                    case SquareAction.DestroyOptionCard: return new SquareActionDetails(CommandCategories.DB, "destroy " + GetOptionName());
                    case SquareAction.SetGameState: return new SquareActionDetails(CommandCategories.DB, "set game state to:" + Value);
                    case SquareAction.OptionCountSet: return new SquareActionDetails(CommandCategories.DB, "set Count " + GetOptionName());
                    case SquareAction.SetDamagePointTotal: return new SquareActionDetails(CommandCategories.DB, "set total to " + Value);
                    case SquareAction.SetShutDownMode: return new SquareActionDetails(CommandCategories.RobotNoReply, "set shut down: " + (tShutDown)Value,0,"3,8");
                    case SquareAction.SetEnergy: return new SquareActionDetails(CommandCategories.RobotNoReply, "set Energy: " + Value,0,"3,9");
                    case SquareAction.SetCurrentGameData: return new SquareActionDetails(CommandCategories.DB, "Set Game Data " + Value + " to " + ValueB);
                    case SquareAction.SetButtonText: return new SquareActionDetails(CommandCategories.UserInput, text,-1);
                    case SquareAction.Unknown:
                    default:
                        return new SquareActionDetails(CommandCategories.DB,"");

                }
            }
        }


        //public string Description { get {return ToString();} set; }

        // commands
        // move
        // turn
        // end of phase
        // end of turn
        // text description
        // direction
        // distance


        public bool IsRobotMoveCommand
        {
            get
            {
                switch (CommandType)
                {
                    case SquareAction.BoardMove:
                    case SquareAction.PushedMove:
                    case SquareAction.Move:
                        return true;
                    case SquareAction.BoardMoveRotate:
                    case SquareAction.BoardRotate:
                    case SquareAction.Rotate:
                        return true;
                }
                return false;
            }
        }

        public bool IsRobotCommand()
        {
            //return (StringCommand().Length > 0);
            CommandCategories cat = Category;
            return (cat == CommandCategories.RobotwReply || cat == CommandCategories.RobotNoReply);
        }

        //commands
        // 6x = move
        // 7x = turn
        // 30 Stop Move
        // 31 Start Move
        // 32 Fire cannon
        // 33 Take Damage
        // 34 Touch Flag
        // 35 Dead

        public override string ToString()
        {
            string outstring = "P:" + Phase + " S:" + PhaseStep + "; ";
            outstring += Robot.Name + " " ;
            outstring += Description + " ";
            if (StringCommand != "") outstring += "{" + StringCommand + "} ";
            if (CommandSequence!=0) outstring += "seq:" + CommandSequence + " ";
            outstring += "cmd:" + CommandType.ToString();
            return outstring;
        }
        #endregion

        public class SquareActionDetails
        {
            //CommandSequence

            public SquareActionDetails(CommandCategories category=CommandCategories.DB,string description="",int commandSequence=0, string btcommand="")
            {
                CommandSequence = commandSequence;
                Description = description;
                BTCommand = btcommand;
                Category = category;
            }
            public string Description {get;set;}
            public string BTCommand {get;set;}
            public CommandCategories Category {get;set;}
            public int CommandSequence {get;set;}
            
        }


        #region Compare Function

        /// <summary>
        ///
        /// </summary>
        /// <param name="that"> item to be renumbered</param>
        /// <returns></returns>
        public bool CheckForConflict(CommandItem that)
        {
            //CommandItem that = (CommandItem)otherCommand;
            //if (this == that) return false;
            if (!this.IsRobotMoveCommand) return false; // move
            if (!that.IsRobotMoveCommand) return false; // also move
            //if (this.CommandSequence != that.CommandSequence) return false; // same sequence
            if (this.EndPos.Location != that.StartPos.Location) return false; // start and end are equal

            return true;
        }

        public int CompareTo(object otherCommand)
        {
            CommandItem that = (CommandItem)otherCommand;
            if (this == that) return 0;
            if (this.CommandType != SquareAction.BoardMove) return 0; // move
            if (that.CommandType != SquareAction.BoardMove) return 0; // also move
            if (this.CommandSequence != that.CommandSequence) return 0; // same sequence
            //if (this.EndPos != that.StartPos) return 0; // start and end are equal
            if (this.EndPos.Location != that.StartPos.Location) return 0; // start and end are equal

            // close
            if (this.CommandDirection == that.CommandDirection) return 1; // Direction is the same

            return 2;  // direction is different
        }

        //public static bool operator ==(CommandItem com1, CommandItem com2)
        //{
        //    return (com1.
        //    return true;
        //}

        public class ItemSequenceCompare : IEqualityComparer<CommandItem>
        {
            public bool Equals(CommandItem First, CommandItem Second)
            {

                if (First.CommandType != SquareAction.BoardMove) return false;
                if (First.CommandSequence != Second.CommandSequence) return false;
                if (First.CommandDirection == Second.CommandDirection) return false;
                if (First.EndPos != Second.StartPos) return false;

                return true;

            }

            public int GetHashCode(CommandItem First)
            {
                return First.ToString().GetHashCode();
            }
        }
    }
    #endregion

    #region Command Enums
    public enum CommandTypes
    {
        None,
        Move,
        Turn,
        Required,
        Phase,
        Logging,
    }

    public enum CommandStatus
    {
        Unknown, // = 0
        Waiting, // = 1
        Ready, // = 2 // ready to process
        ScriptCommand, // = 3  // will be processed by script
        InProgress, // = 4 // script is processing (waiting for reply)
        ScriptComplete, // = 5 // script complete, update settings
        Complete, // = 6 // command complete
        //Ignore, // = 7
        //Deleted, // = 8
        //Sending, // = 3
        //WaitingForReply, // = 5 // script is waiting for reply
    }

    public enum CommandCategories
    {
        RobotwReply = 1,
        RobotNoReply = 2,
        DB = 3,
        PI = 4,
        Node = 5,
        UserInput = 6,
        Connection = 7,
    }

    public enum ConnectionFunctions
    {
        None,
        Connect,
        Disconnect,
    }

    public enum tCommandSequence
    {
        Before,
        After
    }


    //public enum tSequenceSubCommand
    //{
    //    Connect = 0,
    //    Command = 1,
    //    Disconnect = 2,
    //    Damage = 3
    //}

    //public enum tSequences
    //{
    //    StartBotMove = 0,
    //    Turn = 1,
    //    Command = 2,
    //    Move2 = 3,
    //    Move3 = 4,
    //    Move4 = 5,
    //    Unturn = 6,
    //    StopBotMove = 7
    //}

    #endregion


}
