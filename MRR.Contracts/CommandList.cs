using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


///command item sub steps
/// 0 connect
/// 1 Start Move
/// 2 turn
/// 3-6 move/Fire?
/// 7 Unturn
/// 8 Stop Move
/// 9 disconnect

namespace MRR
{

    #region Command List
    public class CommandList : List<CommandItem>
    {
        /// <summary>Add a fully specified command at the current phase and step.</summary>
        public CommandItem AddCommand(PlayerState? p_Player, int p_Value, int p_ValueB, Direction p_Direction, SquareAction p_Action)
        {
            CommandItem newCommand = new CommandItem(Phase, PhaseStep, p_Player, p_Value, p_ValueB, p_Direction, p_Action);
            Add(newCommand);
            return newCommand;
        }

        /// <summary>Add a command that faces the way the robot is currently facing (None if no robot).</summary>
        public CommandItem AddCommand(PlayerState? p_Player, SquareAction p_Action, int p_Value = 0, int p_ValueB = 0)
            => AddCommand(p_Player, p_Value, p_ValueB, p_Player?.CurrentPos.Direction ?? Direction.None, p_Action);

        /// <summary>Add a pre-built command, stamping it with the current phase and step.</summary>
        public CommandItem AddCommand(CommandItem newcommand)
        {
            newcommand.Phase = Phase;
            newcommand.PhaseStep = PhaseStep;
            Add(newcommand);
            return newcommand;
        }

        /// <summary>Add an option-card command; null if there is no card or the action isn't one for option cards.</summary>
        public CommandItem? AddCommand(PlayerState? p_Player, OptionCard? p_OptionCard, SquareAction p_action = SquareAction.PlayOptionCard)
        {
            if (p_OptionCard == null) return null;
            return p_action switch
            {
                SquareAction.OptionCountSet => AddCommand(p_Player, p_OptionCard.ID, 0, (Direction)p_OptionCard.Quantity, p_action),
                SquareAction.PlayOptionCard => AddCommand(p_Player, p_OptionCard.ID, 0, p_OptionCard.OptionDirection, p_action),
                _ => null,
            };
        }

        public CommandItem AddCommand(PlayerState p_Player, tOptionCardCommandType p_OptionCardType)
            => AddCommand(p_Player, SquareAction.PlayOptionCard, (int)p_OptionCardType);

        /// <summary>Add a "move" command in an absolute board direction.</summary>
        public CommandItem AddCommand(PlayerState p_Player, Direction p_Direction, SquareAction p_Action)
        {
            // holddir is p_Direction (the move's absolute board direction) expressed relative
            // to the robot's own facing -- 1=straight ahead, 2=right, 3=back, 4=left -- using
            // the same reconciliation TurnRobot performs (CreateCommands.cs TurnRobot()).
            int holddir = (int)RotationFunctions.Rotate(
                RotationFunctions.RotationDifference(p_Player.CurrentPos.Direction, p_Direction),
                Direction.Up);
            return AddCommand(p_Player, 1, holddir, p_Direction, p_Action);
        }

        /// <summary>Set CurrentGameData key p_Value to p_ValueB.</summary>
        public CommandItem AddCommand(int p_Value, int p_ValueB)
            => AddCommand(null, p_Value, p_ValueB, Direction.None, SquareAction.SetCurrentGameData);

        /// <summary>Add a button-text / message command.</summary>
        public CommandItem AddCommand(string p_buttonText, PlayerState? p_Robot = null)
        {
            CommandItem newCommand = AddCommand(p_Robot, 0, 0, Direction.None, SquareAction.SetButtonText);
            newCommand.text = p_buttonText;
            return newCommand;
        }

        public CommandItem SetEnergy(PlayerState p_player, int newEnergy)
        {
            p_player.Energy = newEnergy;
            return AddCommand(p_player, SquareAction.SetEnergy, p_player.Energy);
        }

        public int PhaseStep { get; set; }

        // Changing the phase restarts the step count.
        private int _phase;
        public int Phase
        {
            get => _phase;
            set
            {
                _phase = value;
                PhaseStep = 0;
            }
        }
    }
    #endregion


    #region Command Item
    [Table("CommandList")]
    public class CommandItem : IComparable
    {

        public CommandItem() // this is required to seralize the class
        :this(0,0,null,0,0,Direction.None,SquareAction.None)
        {
        }


        /// <summary>Create a complete command item.</summary>
        public CommandItem(int p_Phase, int p_PhaseStep, PlayerState? p_Robot, int p_Value, int p_ValueB, Direction p_Direction, SquareAction p_Type)
        {
            Phase = p_Phase;
            PhaseStep = p_PhaseStep;

            CommandType = p_Type;
            Value = p_Value;
            ValueB = p_ValueB;

            CommandDirection = p_Direction;
            PhaseStepAdder = 5;

            StatusID = (int)CommandStatus.Waiting;

            if (p_Robot != null)
            {
                Robot = p_Robot;
                StartPos = new RobotLocation(p_Robot.CurrentPos);
                // NextPos always reflects the robot's true state at this point in planning:
                // equal to CurrentPos when no move is in flight (DataService.
                // GetPlayerStatesFromDB now loads it that way), or the real destination
                // once MoveRobot/RotateRobot has set it for this step. This used to special-
                // case "NextPos.X==0 && NextPos.Y==0" as meaning "not set yet" and fall back
                // to CurrentPos -- but (0,0) is also a real board square, so any command
                // whose robot was genuinely moving to/rotating on square (0,0) got the wrong
                // EndPos (the OLD position), which is what CommandProcess/ProcessDbCommand
                // then wrote into Robots.CurrentPosRow/Col.
                EndPos = new RobotLocation(p_Robot.NextPos);
            }
            else
            {
                Robot = new PlayerState();
                StartPos = new RobotLocation();
                EndPos = new RobotLocation();
            }
        }

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int CommandID { get; set; }
        public int Turn { get; set; }

        private PlayerState? _Robot;
        /// <summary>
        /// The robot this command belongs to, or null if it has not been attached.
        ///
        /// This used to be resolved implicitly: the RobotID setter looked the player up in a
        /// <c>static Players? AllPlayers</c> on this class, so an EF-materialized command
        /// silently acquired a Robot — and silently had none if that static was unset or the
        /// object crossed a process boundary. The static is gone; whoever loads commands
        /// attaches the robot explicitly (see PendingCommands' constructor). RobotID remains
        /// the persisted source of truth.
        /// </summary>
        [NotMapped]
        public PlayerState? Robot {
            get { return _Robot; }
            set
            {
                _Robot = value;
                if (value != null && value.ID > 0) _RobotID = value.ID;
            }
        }
        private int _RobotID;

        public int RobotID {
            get { return _RobotID; }
            set { _RobotID = value; }
        }

        [NotMapped]
        public Direction CommandDirection { get; set; }

        [NotMapped]
        public RobotLocation StartPos { get; set; }
        [NotMapped]
        public RobotLocation EndPos { get; set; } = new RobotLocation();

        public int Phase { get; set; }
        [NotMapped]
        public int PhaseStep { get; set; }
        [NotMapped]
        public int PhaseStepAdder { get; set; }

        [Column("CommandSubSequence")]
        public int RunningCounter { get; set; }
        [Column("CommandSequence")]
        public int NormalSequence { get; set; }
        [NotMapped]
        public int ExpressSequence { get; set; }

        [Column("CommandTypeID")]
        public SquareAction CommandType { get; set; }

        [Column("Parameter")]
        public int Value { get; set; }
        [Column("ParameterB")]
        public int ValueB { get; set; }
        [NotMapped]
        public string text { get; set; } = "";
        [Column("StatusID")]
        public int StatusID { get; set; }
        [NotMapped]
        public CommandStatus Status { get => (CommandStatus)StatusID; set => StatusID = (int)value; }
        [Column("BTCommand")]
        public string BTCommand { get; set; } = "";
        public int PositionRow { get => EndPos.Y; set => EndPos.Y = value; }
        public int PositionCol { get => EndPos.X; set => EndPos.X = value; }
        public int PositionDir { get => (int)EndPos.Direction; set => EndPos.Direction = (Direction)value; }
        public int CommandCatID { get; set; }

        [NotMapped]
        public CommandCategories Category { get { return GetCommandDetails.Category; }}

        [NotMapped]
        public int CommandSequence { get { return GetCommandDetails.CommandSequence; }}

        private string? _dbDescription;
        [Column("Description")]
        public string Description
        {
            get => _dbDescription ?? GetCommandDetails.Description;
            set => _dbDescription = value;
        }

        [NotMapped]
        public string StringCommand { get { return GetCommandDetails.BTCommand; }}


        [NotMapped]
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
                    case SquareAction.Dead:return new SquareActionDetails(CommandCategories.RobotNoReply,"is dead",0,"3,3");
                    case SquareAction.LostLife:return new SquareActionDetails(CommandCategories.DB,"lost a life");
                    case SquareAction.RobotPush:return new SquareActionDetails(CommandCategories.DB,"pushed by ");
                    case SquareAction.PlayerLocation:return new SquareActionDetails(CommandCategories.DB,"is at " + StartPos);
                    case SquareAction.BlockDirection:return new SquareActionDetails(CommandCategories.DB,"is blocked by a wall");

                    case SquareAction.Water: return new SquareActionDetails(CommandCategories.DB,"lost 1 move in water");

                    case SquareAction.LogData:return new SquareActionDetails(CommandCategories.DB,"logged data");
                    case SquareAction.PlayOptionCard: return new SquareActionDetails(CommandCategories.RobotNoReply,"Activate ",0,"3,6");
                    case SquareAction.None:return new SquareActionDetails(CommandCategories.DB,"No Command");
                    case SquareAction.Option:return new SquareActionDetails(CommandCategories.DB,"Deal option");
                    case SquareAction.DealSpamCard:return new SquareActionDetails(CommandCategories.DB,"Deal Spam Card");
                    case SquareAction.PlayerStart:return new SquareActionDetails(CommandCategories.DB,"start");
                    case SquareAction.BoardDimension:return new SquareActionDetails(CommandCategories.DB,"Board Dimension");
                    case SquareAction.SquareLocation:return new SquareActionDetails(CommandCategories.DB,"Square Location");
                    case SquareAction.SquareTemplate:return new SquareActionDetails(CommandCategories.DB,"Template");
                    case SquareAction.Card:
                        if (Value==99) return new SquareActionDetails(CommandCategories.DB, "played card: SPAM");
                        return new SquareActionDetails(CommandCategories.DB, "played card: " + (Robot?.CardsPlayer?.FirstOrDefault(gc => gc.ID == Value)?.Text ?? Value.ToString()));
                    case SquareAction.Randomizer:return new SquareActionDetails(CommandCategories.DB, "gets random card");
                    case SquareAction.BeginBoardEffects: return new SquareActionDetails(CommandCategories.DB, "begin board effects");
                    case SquareAction.SetPlayerStatus: return new SquareActionDetails(CommandCategories.DB, "Status: " + Value);
                    case SquareAction.DeathPoints: return new SquareActionDetails(CommandCategories.DB, "damage points: " + Value);
                    case SquareAction.DestroyOptionCard: return new SquareActionDetails(CommandCategories.DB, "destroy ");
                    case SquareAction.SetGameState: return new SquareActionDetails(CommandCategories.DB, "set game state to:" + Value);
                    case SquareAction.OptionCountSet: return new SquareActionDetails(CommandCategories.DB, "set Count ");
                    case SquareAction.SetDamagePointTotal: return new SquareActionDetails(CommandCategories.DB, "set total to " + Value);
                    case SquareAction.SetShutDownMode: return new SquareActionDetails(CommandCategories.RobotNoReply, "set shut down: " + (tShutDown)Value,0,"3,8");
                    case SquareAction.SetEnergy: return new SquareActionDetails(CommandCategories.RobotNoReply, "set Energy: " + Value,0,"3,9");
                    case SquareAction.SetCurrentGameData: return new SquareActionDetails(CommandCategories.DB, "Set Game Data " + Value + " to " + ValueB);
                    case SquareAction.SetButtonText: return new SquareActionDetails(CommandCategories.UserInput, text,-1);
                    case SquareAction.SetFlash: return new SquareActionDetails(CommandCategories.RobotNoReply, "" + (Value != 0 ? text : "Flash off"));
                    case SquareAction.Unknown:
                    default:
                        return new SquareActionDetails(CommandCategories.DB,"");

                }
            }
        }


        [NotMapped]
        public bool IsRobotMoveCommand => CommandType is
            SquareAction.BoardMove or SquareAction.PushedMove or SquareAction.Move or
            SquareAction.BoardMoveRotate or SquareAction.BoardRotate or SquareAction.Rotate;

        [NotMapped]
        public int CommandMoveType => CommandType switch
        {
            SquareAction.BoardMove or SquareAction.PushedMove or SquareAction.Move => 1,
            SquareAction.BoardMoveRotate or SquareAction.BoardRotate or SquareAction.Rotate => 2,
            SquareAction.StartBotMove or SquareAction.StopBotMove => 3,
            SquareAction.SetFlash => 4,
            _ => 0,
        };

        public bool IsRobotCommand()
            => Category is CommandCategories.RobotwReply or CommandCategories.RobotNoReply;

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
            outstring += (Robot?.Name ?? "robot " + RobotID) + " " ;
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
        /// 0 = not comparable; 1 = same sequence, this move ends where <paramref name="otherCommand"/>
        /// starts, same direction; 2 = same, but a different direction.
        /// </summary>
        public int CompareTo(object? otherCommand)
        {
            if (otherCommand == null) return 0;
            CommandItem that = (CommandItem)otherCommand;
            if (this == that) return 0;
            if (this.CommandType != SquareAction.BoardMove) return 0;
            if (that.CommandType != SquareAction.BoardMove) return 0;
            if (this.CommandSequence != that.CommandSequence) return 0;
            if (this.EndPos.Location != that.StartPos.Location) return 0;

            return this.CommandDirection == that.CommandDirection ? 1 : 2;
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


    #endregion


}
