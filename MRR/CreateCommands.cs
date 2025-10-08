using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.ComponentModel;//INotifyPropertyChanged
using System.Threading;
using System.Xml.Serialization;  // serializer
using System.Reflection;
using System.IO;
using System.Collections.ObjectModel; // needed for enum?


namespace MRR_CLG
{

    #region Enums

    public enum GameTypes
    {
        Standard = 0,
        KingOfTheHill = 1,
        StandardV2 = 2,
    }

    #endregion Enums

    public class CreateCommands // : INotifyPropertyChanged
    {

        #region Game Parameters & Configuration

        const int DamageSequence = 7; // number to use for damage & cannons within sequence

        public CreateCommands(Database lDBConn)
        {

            DBConn = lDBConn;

            GameCards = new CardList();

            OptionCards = new OptionCardList();

            ListOfCommands = new CommandList();

            AllPlayers = new Players(DBConn);

            g_BoardElements = new BoardElementCollection(0, 0);
        }

        public string BoardFileName { get; set; }

        public int BoardID { get; set; }

        public int GameState { get; set; }

        public int RulesVersion { get; set; }

        public int PhaseCount { get; set; }

        public Players AllPlayers { get; set; }

        public CommandList ListOfCommands { get; set; }

        public CardList GameCards { get; set; }

        public Database DBConn { get; set; }

        public OptionCardList OptionCards { get; set; }

        public Dictionary<int,string> OptionCardNames = new Dictionary<int, string>();

        //public OptionCardList MasterOptionCardList { get; set; }

        public BoardElementCollection g_BoardElements { get; set; } // = new BoardElementCollection(0, 0);

        public int CurrentTurn  { get; set; } = 0;

        public GameTypes GameType { get;set; }

        public bool IsOptionsEnabled
        {
            get
            {
                return (OptionsOnStartup > -1);
            }
            set
            {
                if (value)
                {
                    OptionsOnStartup = 1;
                }
                else
                {
                    OptionsOnStartup = -1;
                }
            }
        }

        public int OptionsOnStartup  { get; set; } = -1;

        public int LaserDamage  { get; set; } = 1;

        public int TotalFlags  { get; set; } = 4;
        
        #endregion Game Parameters & Configuration

        #region Game Config


        #endregion


        #region Process Move

        public void ProcessMove(MoveCard p_movecard)  //MoveCard.tCardType p_card, int p_player )
        {
            Player thisplayer = AllPlayers.GetPlayer(p_movecard.Owner);

            ListOfCommands.PhaseStep += 10;
            ListOfCommands.AddCommand(thisplayer, SquareAction.Card, p_movecard.ID);

            switch (p_movecard.Type)
            {
                case MoveCard.tCardType.LTurn:
                case MoveCard.tCardType.RTurn:
                case MoveCard.tCardType.UTurn:
                    // set new robot direction
                    ListOfCommands.AddCommand(thisplayer, SquareAction.SetPlayerStatus, 5);
                    ListOfCommands.AddCommand(thisplayer, SquareAction.StartBotMove, 0, thisplayer.PlayerScore);
                    //RotateRobot(thisplayer, p_movecard.GetCardValue()) ; //,SquareAction.Rotate);
                    RotateRobot(thisplayer, GameCards.GetCardValue(p_movecard)); // p_movecard.GetCardValue()) ; //,SquareAction.Rotate);
                    ListOfCommands.AddCommand(thisplayer, SquareAction.StopBotMove,0,thisplayer.PlayerScore);
                    ListOfCommands.AddCommand(thisplayer, SquareAction.SetPlayerStatus, 12);

                    break;
                case MoveCard.tCardType.Back1:
                case MoveCard.tCardType.Forward1:
                case MoveCard.tCardType.Forward2:
                case MoveCard.tCardType.Forward3:
                    // move robot...
                    // add this robot move
                    ListOfCommands.AddCommand(thisplayer, SquareAction.SetPlayerStatus, 5);
                    ListOfCommands.AddCommand(thisplayer, SquareAction.StartBotMove, 0, thisplayer.PlayerScore);

                    int l_MoveDistance = GameCards.GetCardValue(p_movecard);
                    // check for water...

                    // check for breaks, reverse gear, and 4th gear
                    int checkPhase = p_movecard.PhasePlayed;

                    switch (p_movecard.Type)
                    {
                        case MoveCard.tCardType.Back1:
                            if (OptionCards.GetOption(tOptionCardCommandType.ReverseGears, thisplayer, checkPhase) != null)
                            {
                                l_MoveDistance--; // back up farther
                                ListOfCommands.AddCommand(thisplayer, SquareAction.PlayOptionCard, (int)tOptionCardCommandType.ReverseGears);
                            }
                            break;
                        case MoveCard.tCardType.Forward1:
                            if (OptionCards.GetOption(tOptionCardCommandType.Brakes, thisplayer, checkPhase) != null)
                            {
                                l_MoveDistance = 0; // stop
                                ListOfCommands.AddCommand(thisplayer, SquareAction.PlayOptionCard, (int)tOptionCardCommandType.Brakes);
                            }
                            break;
                        case MoveCard.tCardType.Forward3:
                            if (OptionCards.GetOption(tOptionCardCommandType.FourthGear, thisplayer, checkPhase) != null)
                            {
                                l_MoveDistance++; // Fourth Gear
                                ListOfCommands.AddCommand(thisplayer, SquareAction.PlayOptionCard, (int)tOptionCardCommandType.FourthGear);
                            }
                            break;
                    }

                    BoardElement l_CurrentSquare = g_BoardElements.GetSquare(thisplayer.CurrentPos.X, thisplayer.CurrentPos.Y);
                    if (l_CurrentSquare.ActionList.Count(al => al.SquareAction == SquareAction.Water) > 0) // this square has water...
                    {
                        l_MoveDistance -= Math.Sign(l_MoveDistance); // move one closer to 0
                        ListOfCommands.AddCommand(thisplayer, SquareAction.Water);
                    }


                    if (l_MoveDistance != 0)
                    {
                        CalcMoveDistance(thisplayer, l_MoveDistance, thisplayer.CurrentPos.Direction, SquareAction.Move);
                    }

                    // before water
                    //CalcMoveDistance(thisplayer, GameCards.GetCardValue(p_movecard), thisplayer.CurrentPos.Direction, SquareAction.Move);
                    ListOfCommands.AddCommand(thisplayer, SquareAction.StopBotMove, 0, thisplayer.PlayerScore);
                    if (!thisplayer.IsDead)
                    {
                        ListOfCommands.AddCommand(thisplayer, SquareAction.SetPlayerStatus, 12);
                    }

                    // turn all robots to correct direction...

                    break;
                case MoveCard.tCardType.PowerUp:
                    ListOfCommands.SetEnergy(thisplayer, thisplayer.Energy+1);
                    break;
                case MoveCard.tCardType.Option:
                case MoveCard.tCardType.Unknown:
                default:
                    // don't do anything
                    break;
            }
        }

        public int CalcMoveDistance(Player p_Player, int p_Distance, Direction p_Direction, SquareAction p_MoveType)
        {
            // check to see if this robot can move 1 square
            //   check for walls (2 checks)
            //   check for robot on target square
            //   check for damage on entering
            // check to see if this robot is pushing anything
            // check for remaining moves

            // Walls (this square)
            Player thisplayer = p_Player; // AllPlayers.GetPlayer(p_Player);
            int PlayerX = thisplayer.CurrentPos.X;
            int PlayerY = thisplayer.CurrentPos.Y;

            BoardElement l_CurrentSquare = g_BoardElements.GetSquare(PlayerX, PlayerY);

            // reverse direction to check for walls if moving backwards
            Direction l_ActualMoveDirection = (p_Distance > 0 ? p_Direction : RotationFunctions.Rotate(2, p_Direction));

            int l_CheckDirection = (int)RotationFunctions.Rotate(2, l_ActualMoveDirection); // calc 180 degrees out...

            int l_MoveDistance = Math.Sign(p_Distance); // could be +1 or -1 depending on forward/backward

            if (l_CurrentSquare.ActionList.Count(al => ((al.SquareAction == SquareAction.BlockDirection) && (al.Parameter == l_CheckDirection))) > 0)
            {
                // path blocked by a wall
                ListOfCommands.AddCommand(thisplayer, SquareAction.BlockDirection);
                return 0;  // do not move
            }

            // calc new square
            //RobotLocation l_newsquare = thisplayer.CalcNewLocation(l_MoveDistance, p_Direction);
            RobotLocation l_newsquare = thisplayer.CurrentPos.CalcNewLocation(l_MoveDistance, p_Direction);
            // actions for new square
            BoardActionsCollection l_TargetActions = g_BoardElements.GetSquare(l_newsquare.X, l_newsquare.Y).ActionList;

            if (l_TargetActions.Count(al => ((al.SquareAction == SquareAction.BlockDirection) && (al.Parameter == (int)l_ActualMoveDirection))) > 0)
            {
                ListOfCommands.AddCommand(thisplayer, SquareAction.BlockDirection);
                // path blocked by a wall
                return 0;
            }


            //   check for robot on target square
            Player l_PushPlayer = AllPlayers.GetPlayer(l_newsquare);
            if (l_PushPlayer != null)
            {
                ListOfCommands.AddCommand(l_PushPlayer, SquareAction.RobotPush, thisplayer.ID);
                AddDeathPoints(thisplayer, 1, l_PushPlayer);

                OptionCard ramming = OptionCards.GetOption(tOptionCardCommandType.RammingGear, thisplayer);
                if (ramming != null) // this player has ramming gear
                {
                    ListOfCommands.AddCommand(thisplayer, ramming);
                    AddDamage(l_PushPlayer, 1, thisplayer);
                }

                //CommandItem l_PreTurn = ListOfCommands.AddSimpleTurnCommand(p_Phase, l_PushPlayer.ID, 0);

                int l_pushPlayerID = l_PushPlayer.ID;
                if (thisplayer.ID == l_pushPlayerID)
                {
                    Console.WriteLine(l_pushPlayerID + " pushed by " + p_Player.ID + " at " + p_Player.CurrentPos.FullLocation + " ** Failure ** ");
                    return 0;
                }

                int l_pushdistance = CalcMoveDistance(l_PushPlayer, l_MoveDistance, p_Direction, SquareAction.PushedMove);
                if (l_pushdistance == 0) // do not move
                {
                    return 0; // do not move
                }
                else // move both
                {
                }
            }
            else if ((l_newsquare.X == 0 
                    || l_newsquare.Y == 0 
                    || l_newsquare.X == g_BoardElements.BoardCols - 1 
                    || l_newsquare.Y == g_BoardElements.BoardRows - 1)
                    && (AllPlayers.FirstOrDefault(wp => wp.CurrentPos.X == l_newsquare.X && wp.CurrentPos.Y == l_newsquare.Y)!=null)
                    ) // edge of board, with player
            {
                 // no live robot on this square.  Is this an edge?  Is there a bot there?
                //when moving on a square, if there is a dead robot, move him

                int xChange = 0;  
                int yChange = 0;  
                Direction dChange = Direction.None;

                // move robot sideways from current spot  (is this robot on the x edge?)
                if ((l_newsquare.X == 0) || (l_newsquare.X == g_BoardElements.BoardCols - 1))
                {
                    yChange = -1;
                    dChange = Direction.Up;
                }
                else
                {
                    xChange = -1;
                    dChange = Direction.Left;
                }

                // move
                ClearThisSpot(l_newsquare.X, l_newsquare.Y, xChange, yChange, dChange);

            }


            // move robot...  (make actual move)
            if (!MoveRobot(thisplayer, l_newsquare, l_MoveDistance, p_Direction, p_MoveType))
            {
                // robot died
                return l_MoveDistance;
            }

            ////   check for damage on entering
            //int l_Damage = l_TargetActions.Where(ta => ta.SquareAction == SquareAction.Damage).Sum(ta => ta.Parameter);
            //if (l_Damage > 0)
            //{
            //    thisplayer.Damage += l_Damage;
            //    ListOfCommands.AddCommand(thisplayer, l_Damage, SquareAction.Damage);
            //    //ListOfCommands.AddCommand(thisplayer + " took " + l_Damage + " damage");
            //    if (thisplayer.IsDead)
            //    {
            //        ListOfCommands.AddCommand(thisplayer, SquareAction.Dead);
            //        //ListOfCommands.AddCommand(thisplayer + " is dead");
            //        return l_MoveDistance;
            //    }
            //}

            // check for remaining moves


            int remainingDistance = p_Distance - l_MoveDistance;
            if (remainingDistance != 0) // and this move is OK
            {
                // check again
                remainingDistance = CalcMoveDistance(p_Player, remainingDistance, p_Direction, p_MoveType); // next sub step
            }

            return remainingDistance + l_MoveDistance;
        }

        public void RotateRobot(Player p_Robot, int p_Distance) //, SquareAction p_MoveType)
        {

            p_Robot.Rotate(p_Distance);  // rotate NextPos direction
            CommandItem turncommand = ListOfCommands.AddCommand(p_Robot, SquareAction.Rotate, p_Distance);
            //turncommand.PhaseStepSub = p_SubStep;
            p_Robot.SetLocation(); // update current to next

        }

        public bool MoveRobot(Player p_Robot, RobotLocation p_NewLocation, int p_Distance, Direction p_Direction, SquareAction p_MoveType)
        {
            bool StillAlive = true;

            // move robot...
            p_Robot.NextPos.SetLocation(p_NewLocation); // end location?

            if (p_Distance >= 0)
            {
                ListOfCommands.AddCommand(p_Robot, p_Distance, p_Robot.PlayerScore, p_Direction, p_MoveType);
            }
            else // move backwards
            {
                ListOfCommands.AddCommand(p_Robot, -p_Distance, p_Robot.PlayerScore, RotationFunctions.Rotate(2, p_Direction), p_MoveType);
            }

            //   check for damage on entering

            BoardActionsCollection l_TargetActions = g_BoardElements.GetSquare(p_NewLocation.X, p_NewLocation.Y).ActionList;

            BoardAction mineAction = l_TargetActions.FirstOrDefault(ta => ta.SquareAction == SquareAction.Mine);
            if (mineAction != null)
            {
                DamageAtSquare(new RobotLocation(0, mineAction.SquareX, mineAction.SquareY, mineAction.Parameter),p_Robot);
                // remove damage from square
                mineAction.SquareAction = SquareAction.None;

                ListOfCommands.AddCommand(p_Robot, SquareAction.Mine);
            }

            int l_Damage = l_TargetActions.Where(ta => ta.SquareAction == SquareAction.Damage).Sum(ta => ta.Parameter);
            if (l_Damage > 0)
            {
                if (!AddDamage(p_Robot, l_Damage))
                {
                    StillAlive = false;
                }
            }

            p_Robot.SetLocation(); // do move

            return StillAlive;
        }


        public void ClearThisSpot(int currentX, int currentY, int changeX, int changeY, Direction changeD)
        {
            Player blockingPlayer = AllPlayers.FirstOrDefault(wp => wp.CurrentPos.X == currentX && wp.CurrentPos.Y == currentY);
            if (blockingPlayer!= null)
            {
                if (currentX + changeX < 0)
                {
                    if (currentY == 0)
                    {
                        changeY = 1;
                        changeX = 0;
                        changeD = Direction.Down;
                    }
                    else
                    {
                        changeY = -1;
                        changeX = 0;
                        changeD = Direction.Up;
                    }
                }

                if (currentY + changeY < 0)
                {
                    if (currentX == 0)
                    {
                        changeY = 0;
                        changeX = 1;
                        changeD = Direction.Right;
                    }
                    else
                    {
                        changeY = 0;
                        changeX = -1;
                        changeD = Direction.Left;
                    }
                }

                // there is a player here.  Move him the direction needed
                bool rotated = false;
                if (blockingPlayer.CurrentPos.Direction != changeD)
                {
                    int newdir = RotationFunctions.RotationDifference(blockingPlayer.CurrentPos.Direction, changeD);
                    RotateRobot(blockingPlayer, newdir);
                    rotated = true;
                }
                // check where this player will move to
                ClearThisSpot(currentX + changeX, currentY + changeY, changeX, changeY, changeD);
                // move one
                MoveRobot(blockingPlayer, new RobotLocation(changeD, currentX + changeX, currentY + changeY), 1, changeD, SquareAction.PushedMove);
                if (rotated)
                {
                    // insert step
                    ListOfCommands.PhaseStep += 10;
                }
            }

        }

        #endregion Process Move


        #region Execute Turn (calculate turn)

        /// <summary>
        /// calculate command list, given cards and player positions
        /// </summary>
        public string ExecuteTurn()
        {
            //GameState = DBConn.UpdateGameState();

            if (GameState != 6) 
            {
                return ("Wrong State:" + GameState.ToString());
            }

            // check all robots, and set their new state to "done moving"
            //if (!CheckPlayersReady()) return "Execute Failed: Players not ready";

            LoadBoard();

            LoadRobots();

            //GameCards.LoadCardList();
            LoadGameCardsFromDatabase();

            LoadOptionCardsFromDatabase();

            //MasterOptionCardList = (OptionCardList)LoadFile(typeof(OptionCardList), "" + "OptionList.xml");

            //LoadRobots();

            ListOfCommands.Clear(); // = new CommandList();

            // update priority of card based on owner; sort by (-)
            if (RulesVersion==1)
            {
                foreach(MoveCard thiscard in GameCards)
                {
                    thiscard.Priority = -AllPlayers.GetPlayer(thiscard.Owner).Priority;
                }

            }

            // save AllPlayers here...
            // set button text
//            ListOfCommands.AddCommand("Phase 1");

            // begin moves
            for (int RunningPhase = 1; RunningPhase < PhaseCount + 1; RunningPhase++)
            {
                ExecutePhase(RunningPhase);
            }


            /// 1) search MOVE list for each robot to make sure all robots are facing correct direction for move and insert starting turns
            /// 2) change all steps between turns to new direction
            /// 3) return all robots to correct direction
            ///

            Players PlayerDirections = new Players(AllPlayers);

            foreach (Player thisplayer in PlayerDirections) // AllPlayers)
            {
                CommandItem lastcommand = null;
                int lastphase = 0;
                int laststep = -1;

                //Player thisplayer = new Player(EndingPlayer); // copy this player

                //foreach (CommandItem thiscommand in ListOfCommands.Where(loc => (loc.RobotID == thisplayer.ID) && (loc.IsRobotCommand())))
                IEnumerable<CommandItem> playercommands = ListOfCommands.Where(loc => (loc.RobotID == thisplayer.ID) && (loc.IsRobotCommand()));

                for (int counter1 = 0; counter1 < playercommands.Count(); counter1++)
                {
                    CommandItem thiscommand = playercommands.ElementAt(counter1);

                    if ((lastphase != thiscommand.Phase) || (laststep != thiscommand.PhaseStep))
                    {
                        counter1 += TurnRobot(thisplayer, lastcommand, tCommandSequence.After);
                        lastphase = thiscommand.Phase;
                        laststep = thiscommand.PhaseStep;
                        lastcommand = null;
                    }

                    switch (thiscommand.CommandType)
                    {
                        /// robot is rotating
                        case SquareAction.BoardMoveRotate:
                        case SquareAction.BoardRotate:
                        case SquareAction.Rotate:
                        case SquareAction.PushedMoveRotate:
                            counter1 += TurnRobot(thisplayer, lastcommand, tCommandSequence.After); // turn robot to correct direction AFTER this move
                            //thisplayer.SetLocation(new RobotLocation(thiscommand.EndPos.Direction, thisplayer.CurrentPos.X, thisplayer.CurrentPos.Y)); // move robot to new location
                            thisplayer.SetLocation(thiscommand.EndPos); // move robot to new location
                            lastcommand = null;
                            break;
                        /// robot is moving
                        case SquareAction.Move:
                        case SquareAction.BoardMove:
                        case SquareAction.PushedMove:
                            counter1 += TurnRobot(thisplayer, thiscommand, tCommandSequence.Before); // turn robot to new direction for this move
                            thisplayer.SetLocation(new RobotLocation(thisplayer.CurrentPos.Direction, thiscommand.EndPos.X, thiscommand.EndPos.Y)); // move robot to new location
                            lastcommand = thiscommand;
                            break;
                        /// robot is doing something else
                        default:
                            break;
                    }

                }

                //if (!thisplayer.IsDead)
                {
                    TurnRobot(thisplayer, lastcommand, tCommandSequence.After);
                }
                lastcommand = null;
            }



            // post process command list
            /// remove unneeded turns
            /// add connect/disconnect flags to commands
            ///

            /// 1 Setup list of moves (group moves where possible)
            /// 2 remove unneeded turns
            /// 3 Add connect/disconnect flags

            /// list of commands
            ///

            /// 1 find multiple forced moves by single robot in single step
            ///

            /// assign a running counter to each command
            //int RunningCommandID = 0;
            //ListOfCommands.Select(loc => { loc.RunningCounter = RunningCommandID+=10; return loc; }).ToList();

            foreach (Player thisplayer in AllPlayers)
            {
                //thisplayer.FutureCards = AllPlayers.First(wp => wp.ID == thisplayer.ID).TotalCards();
                Player futureplayer = AllPlayers.GetPlayer(thisplayer.ID);
                //if ((CircuitBreaker.Owner == thisplayer.ID) && (futureplayer.Damage > 2) && (futureplayer.Damage <10))
                OptionCard CircuitBreaker = OptionCards.GetOption(tOptionCardCommandType.CircuitBreaker, thisplayer);
                if ((CircuitBreaker != null) && (futureplayer.Damage > 2) && (futureplayer.Damage < 10))

                {
                    ListOfCommands.AddCommand(thisplayer, CircuitBreaker);
                    thisplayer.ShutDown = tShutDown.NextTurn;
                }

            }
            
            // add damagepoint total to start of turn
            // if (IsOptionsEnabled) // if we are using options, show damage points
            // {
            //     int maxd = AllPlayers.Max(wp=>wp.DamagePoints);
            //     ListOfCommands.AddCommand(null, SquareAction.SetDamagePointTotal,maxd );
                
            // }

            // if board includes touch koth or touch last man, set type to done 
            if (GameType == GameTypes.KingOfTheHill)
            {
                int turncount = 2;
                if (PhaseCount < 2) // 10 turns in a 1 phase game; 3 turns in a 5 phase game
                {
                    turncount = 9;
                }
                if (CurrentTurn > turncount)
                {
                    if (turncount > 5)
                    {
                        ListOfCommands.AddCommand(null, SquareAction.SetGameState, 13); // shut down game (don't just end)
                    }
                    else
                    {
                        ListOfCommands.AddCommand(null, SquareAction.EndOfGame);
                    }
                }
            }

            ListOfCommands.AddCommand(10,2); // set game state to next turn

            AddCommandsToDatabase();

            //SendGameMessage(8,"Added " + ListOfCommands.Count + " commands"); // set to state 8, ready to start running commands
            Console.WriteLine("Added " + ListOfCommands.Count + " commands");
            return ("Added " + ListOfCommands.Count + " commands");


        }

        public void AddCommandsToDatabase()
        {
            int commandID = 0;
            int lastCommandID = -1;
            int lastBot = -1;
            int RunningCommand = 0;
            //int ExpressCounter = 0;

            string cTurn = CurrentTurn.ToString();

            string strSQL = "Delete from CommandList where Turn=" + cTurn + " and Phase>0;";
            DBConn.Command(strSQL);


            // process sequence for list of commands
            // if previous command sequence contains a

            foreach (CommandItem thisCommand in ListOfCommands)
            {
                //Console.WriteLine(thisCommand.ToString() );
                if ((lastCommandID != thisCommand.CommandSequence) || (thisCommand.RobotID == lastBot)) // || thisCommand.CommandTypeInt == 92) // start/end phase
                {
                    commandID++;

                    // 1 Look at everything in this CommandSequence
                    // 2 Look at everything in the ExpressCounter sequence
                    // 3 if they share any squares, do not combine them.
                    // 4 ExpressCounter++;

                    //ExpressCounter++; // figure out when NOT to bump this value...

                    lastCommandID = thisCommand.CommandSequence;
                    RunningCommand = 0;
                }

                RunningCommand++;
                lastBot = thisCommand.RobotID;
                thisCommand.RunningCounter = RunningCommand; // commands in this sequence
                thisCommand.NormalSequence = commandID;      // sequence of commands (groups that can execute together)
                //thisCommand.ExpressSequence = ExpressCounter;  // groups that can all execute at the same time...
                //thisCommand.ExpressSequence = commandID;  // groups that can all execute at the same time...
                //thisCommand.ExpressCounter = RunningCommand; // counter for express commands
            }

            // go through all the commands (above) and set the command ID and express sequence
            // then go again and combine express sequences where possible
            RunningCommand = 1;
            
            for (int seq=2;seq<commandID ; seq++)
            {
                // find all items in ExpressSequence = seq
                // find all items in ExpressSequence = ExpressCounter
                // if no items overlap, renumber seq items to ExpressCounter
                // overlap = seq ends where ExpressCounter starts
                // else change ExpressCounter to next seq & bump seq


                //ListOfCommands.Join(ListOfCommands, seqList => seqList.ExpressSequence, EL => EL.ExpressSequence,);
                bool renumberList = false;

                IEnumerable<CommandItem> currentList = ListOfCommands.Where(cl => cl.ExpressSequence == RunningCommand && cl.IsRobotMoveCommand);
                IEnumerable<CommandItem> expressList = ListOfCommands.Where(el => el.ExpressSequence == seq && el.IsRobotMoveCommand);
                if (currentList==null || expressList == null)
                {

                }
                else
                {
                    IEnumerable<CommandItem> combinedList = from cl in currentList join el in expressList on cl.StartPos.Location equals el.EndPos.Location select cl;
                    if (combinedList==null)
                    {
                        renumberList = true;
                    }
                    else
                    {
                        renumberList = (combinedList.Count() == 0);
                    }

                }

                if (renumberList)
                {
                    // renumber expressList
                    expressList.Select(el => el.ExpressSequence = RunningCommand).ToList();
                }
                else
                {
                    seq++;
                    RunningCommand = seq;
                }
            }
            

            foreach (CommandItem thisCommand in ListOfCommands)
            {

                /*
                 * CommandID
                 * GameDataID
                 * Turn
                 * Phase
                 * CommandSequence
                 * CommandSubSequence
                 * CommandTypeID
                 * Parameter
                 * RobotID
                 * StatusID
                 * BTCommand
                 * BTReply
                 * Description
                 *
                 * Follows Command
                 *
                 *
                 */

                AddOneCommandToDB(thisCommand); //, commandID, RunningCommand);

            }

            //Console.WriteLine("Added " + ListOfCommands.Count + " commands to the database");

        }
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

        public void AddDisconnectsToDB()
        {

            foreach (Player thisplayer in AllPlayers)
            {
                DBConn.Command("call procRobotConnectionStatus(" + thisplayer.ID + ",71);");
            }

            DBConn.Command("Update CurrentGameData set GameState=0, Message='Exit Game'; ");
        }

        public void LoadGameCardsFromDatabase()
        {
            GameCards.Clear();

            string strSQL = "Select CardID, CardTypeID, Owner, PhasePlayed from MoveCards;";
            MySqlConnector.MySqlDataReader reader = DBConn.Exec(strSQL);
            while (reader.Read())
            {
                MoveCard newCard = new MoveCard((int)reader[0],(int)reader[1]);
                newCard.Owner = (int)reader[2];
                newCard.PhasePlayed = (int)reader[3];

                GameCards.Add(newCard);
            }

            reader.Close();
        }

        public void LoadOptionCardsFromDatabase()
        {
            OptionCards.Clear();
            string strSQL = "Select RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive,PhasePlayed, DataValue, Damage, Name,EditorType from viewRobotOptions;";
            //string strSQL = "Select CardID, CardTypeID, Owner, PhasePlayed from MoveCards;";
            MySqlConnector.MySqlDataReader reader = DBConn.Exec(strSQL);
            while (reader.Read())
            {
                //OptionCard newCard = new OptionCard((int)reader[0], (tOptionCardCommandType)reader[1], (int)reader[2], (int)reader[3], (int)reader[4], (int)reader[5], (int)reader[6], (int)reader[7], (string)reader[8]);
                OptionCard newCard = new OptionCard()
                {
                    Owner = (int)reader[0],
                    ID = (int)reader[1],
                    DestroyWhenDamaged = ((int)reader[2] == 1),
                    Quantity = (int)reader[3],
                    PhasePlayed = (int)reader[5],
                    DataValue = (int)reader[6],
                    Damage = (int)reader[7],
                    Name = (string)reader[8],
                    EditorType = (tOptionEditorType)reader[9]
                };

                OptionCards.Add(newCard);

                if (!OptionCardNames.ContainsKey(newCard.ID))
                {
                    OptionCardNames.Add(newCard.ID, newCard.Name);
                }
            }

            reader.Close();
        }


        public int TurnRobot(Player p_thisplayer, CommandItem p_OnMove, tCommandSequence p_Sequence)
        {
            // return number of commands added

            if (p_OnMove == null) return 0;

            Direction targetdir = p_OnMove.CommandDirection;
            if (p_Sequence == tCommandSequence.After) targetdir = p_OnMove.EndPos.Direction;
            int newdir = RotationFunctions.RotationDifference(p_thisplayer.CurrentPos.Direction, targetdir);
            switch (newdir)
            {
                case 0: // already there
                    return 0;

                case -1: // must turn right or left...
                case 1:
                    //if (!p_thisplayer.Active)
                    //    return 0;

                    p_thisplayer.Rotate(newdir);

                    CommandItem startingmove;
                    if (p_Sequence == tCommandSequence.Before)
                    {
                        startingmove = ListOfCommands.First(loc => ((loc.Phase == p_OnMove.Phase) && (loc.PhaseStep == p_OnMove.PhaseStep) && (loc.IsRobotCommand()) && (loc.CommandType != SquareAction.StartBotMove)));
                    }
                    else
                    {
                        startingmove = ListOfCommands.Last(loc => ((loc.Phase == p_OnMove.Phase) && (loc.PhaseStep == p_OnMove.PhaseStep) && (loc.IsRobotCommand()) && (loc.CommandType != SquareAction.StopBotMove)));
                    }

                    //CommandItem turncommand = ListOfCommands.AddCommand(p_OnMove, p_thisplayer, SquareAction.Rotate, p_Sequence);
                    CommandItem turncommand = ListOfCommands.AddCommand(startingmove, p_thisplayer, SquareAction.Rotate, p_Sequence);
                    turncommand.Value = newdir;

                    p_thisplayer.SetLocation();

                    return 1;

                case 2:
                    // if direction is off 180, reverse move, and do not turn
                    // reverse direction of move command
                    // ONLY reverse direction on turn moves, not Unturn moves
                    if (p_OnMove.Value > 0)
                    {
                        p_OnMove.Value = -p_OnMove.Value;
                    }
                    //if ((p_Sequence == tCommandSequence.Before) && (p_OnMove.Value > 0))
                    //{
                    //    p_OnMove.Value = -p_OnMove.Value;
                    //}
                    return 0;
            }
            return 0;
        }

        // not used
        // public bool CheckPlayersReady()
        // {
        //     string strSQL = "Select count(RobotID) rid from Robots where Status != 4 ;";
        //     MySqlConnector.MySqlDataReader reader = DBConn.Exec(strSQL);
        //     while (reader.Read())
        //     {
        //         //Console.WriteLine("not ready status: " + reader[0]);
        //         if ((long)reader[0] > 0) return false;
        //     }
        //     reader.Close();

        //     strSQL = "Update Robots set Status = 12 ;";
        //     return DBConn.Command(strSQL);

        // }

        #endregion Execute Turn (calculate turn)

        #region Process Robots
        /// <summary>
        /// This function replaces the LoadPlayersFromFile function
        /// </summary>
        public void LoadRobots()
        {
            AllPlayers = new Players(DBConn);
        }

        public Player LoadOneRobot(int RobotID)
        {
            return new Players(DBConn,RobotID).FirstOrDefault();
        }


        #endregion Process Robots

        #region Run Phase

        public void ExecutePhase(int p_PhaseNumber, bool AllowOptions = true)
        {

            //ListOfCommands.AddCommand("Execute Phase" + p_PhaseNumber.ToString());

            ListOfCommands.Phase = p_PhaseNumber;
            // find first player on the list and give them the Next Phase button
            var firstplayer = AllPlayers.OrderBy(ob=>ob.Priority).FirstOrDefault();
            
            ListOfCommands.AddCommand("Run Phase " + p_PhaseNumber.ToString(),firstplayer);  // set button text & wait for click
            //ListOfCommands.AddCommand(null, SquareAction.PhaseStart, p_PhaseNumber);
            //ListOfCommands.AddCommand(10,7); // set game state to waiting for input


//            ListOfCommands.SetPhase(p_PhaseNumber);
            // calculate sequence of all moves, including board effects

            // 1 execute cards (check for dead)
            // 2 board moves (check for dead)
            // 3 Laser/Cannon fire (check for dead)
            // 4 touch checkpoints
            // 5 repair damage

            // get list of cards to execute
            //foreach (MoveCard thiscard in GameCards.Where(gc => gc.PhasePlayed == p_PhaseNumber).OrderByDescending(gc => gc.Priority))

            List<MoveCard> FullList = new List<MoveCard>();

            /*
             * this code should be ready for randomizer
             */
            // find any player on a randomizer...
            // player is active && does not have any random cards && current square contains random action
            //IEnumerable<Player> activePlayers = AllPlayers.Where(ap=>(ap.Active ));
            //IEnumerable<BoardElement> playersSquare = AllPlayers.Where(ap => (ap.Active)).Select(ap => g_BoardElements.GetSquare(ap.CurrentPos.X, ap.CurrentPos.Y));
            /*
             * IEnumerable<Player> randomizers = AllPlayers.Where(ap => (ap.Active &&
                g_BoardElements.GetSquare(ap.CurrentPos.X, ap.CurrentPos.Y).ActionList.Any(al=>al.SquareAction == SquareAction.Randomizer)));
            foreach (BoardElement thisplayer in playersSquare)
            {
                Console.WriteLine(thisplayer.ToString());
                //thisplayer.Active = true;
            }*/

            foreach (Player thisplayer in AllPlayers.Where(ap => (ap.Active &&
                g_BoardElements.GetSquare(ap.CurrentPos.X, ap.CurrentPos.Y).ActionList.Any(al=>al.SquareAction == SquareAction.Randomizer))))
            {
                Player currentPlayer = AllPlayers.GetPlayer(thisplayer.ID);
                MoveCard currentcard = currentPlayer.CardsPlayed.First(pc => pc.PhasePlayed == p_PhaseNumber);
                if (!currentcard.Random) // already a random card?
                {
                    // clear previous card
                    currentcard.PhasePlayed = -1;
                    GameCards.First(gc => gc.ID == currentcard.ID).PhasePlayed = -1;
                    //currentPlayer.PlayedCards.Remove(currentcard);
                    
                    // pick new random card, and mark it as selected (random)
                    // pick a random card that no one else owns, or has played
                    MoveCard thiscard = GameCards.OrderBy(gc => gc.CurrentOrder).First(gc => ((gc.Owner == -1) && (gc.PhasePlayed == -1)));
                    thiscard.Owner = thisplayer.ID;
                    thiscard.PhasePlayed = p_PhaseNumber;
                    thiscard.Random = true;
                    //currentPlayer.PlayedCards.AddCard(thiscard);
                }

            }

            if (AllowOptions)
            {
                //EMP
                if (p_PhaseNumber == 1)
                {
                    //OptionCardList EMPOptionList = OptionCards.GetOptions(tOptionCardCommandType.EMP, 0);
                    var EMPOptionList = OptionCards.Where(oc => oc.ID == (int)tOptionCardCommandType.EMP && oc.PhasePlayed > 0);
                    if (EMPOptionList.Count() == 1) // only work if only one is being set off
                    {
                        OptionCard EMP = EMPOptionList.First();
                        Player EMPlayer = AllPlayers.GetPlayer(EMP.Owner);
                        if (UseOption(EMPlayer, EMP))
                        {
                            // clear cards and shut down all players
                            var AllOtherCards = GameCards.Where(mc => mc.Owner != EMPlayer.ID); // this will clear flywheel cards that are in memory

                            foreach (MoveCard eachcard in AllOtherCards)
                            {
                                eachcard.PhasePlayed = -1; // unplay card
                            }

                            var OtherPlayers = AllPlayers.Where(wp => wp.ID != EMPlayer.ID);
                            foreach (Player notEMP in OtherPlayers)
                            {
                                notEMP.ShutDown = tShutDown.Currently;
                                ListOfCommands.AddCommand(notEMP, SquareAction.SetShutDownMode, (int)tShutDown.Currently);
                            }

                            // at EOT, shut down and don't repair
                            // set shut down mode to 3
                            EMPlayer.ShutDown = tShutDown.WithoutReset;
                            ListOfCommands.AddCommand(EMPlayer, SquareAction.SetShutDownMode, (int)tShutDown.WithoutReset);

                        }
                    }

                    while (OptionCards.Where(oc => oc.ID == (int)tOptionCardCommandType.DamageEraser && oc.PhasePlayed > 0).Any())
                    {
                        OptionCard Eraser = OptionCards.First(oc => oc.ID == (int)tOptionCardCommandType.DamageEraser && oc.PhasePlayed > 0);
                        Player eraseDamagePlayer = AllPlayers.GetPlayer(Eraser.Owner); ;
                        if (UseOption(eraseDamagePlayer, Eraser))
                        {
                            // erase damage
                            AddDamage(eraseDamagePlayer, -eraseDamagePlayer.Damage);
                        }
                    }
                }



                //// the big one must be store in the board -- this is not possible, yet
                //if (p_PhaseNumber == 1)
                //{
                //    bool CheckingForBombs = true;
                //    while (CheckingForBombs)
                //    {
                //        // check for bombs
                //        BoardElement bombSquare = g_BoardElements.GetSquare(be => be.ActionList.Count(al => (al.SquareAction == SquareAction.PlayOptionCard) && (al.Parameter == (int)tOptionCardCommandType.TheBigOne)) > 0);
                //        CheckingForBombs = (bombSquare != null);
                //        if (CheckingForBombs)
                //        {
                //            RobotLocation bombPoint2 = new RobotLocation(bombSquare);
                //            OptionCard TheBigOneA = MasterOptionCardList.FirstOrDefault(uc => uc.ID == (int)tOptionCardCommandType.TheBigOne);  // return that card

                //            bombPoint2.Index = TheBigOneA.Damage;
                //            DamageAtSquare(bombPoint2);
                //            // remove big one from board
                //            BoardAction bombAction = bombSquare.ActionList.First(al => al.Parameter == (int)tOptionCardCommandType.TheBigOne);
                //            bombSquare.ActionList.Remove(bombAction);
                //        }
                //    }
                //}


                // create a list of actions that should take place, now..
                OptionCardList LocalOptionList = OptionCards.GetOptions(tOptionCardCommandType.SelfDestruct, p_PhaseNumber);
                //LocalOptionList.AddOptionsToList(OptionCards.GetOptions(tOptionCardCommandType.TheBigOne, p_PhaseNumber));
                //LocalOptionList.AddOptionsToList(OptionCards.GetOptions(tOptionCardCommandType.MineLayer, p_PhaseNumber));
                //LocalOptionList.AddOptionsToList(OptionCards.GetOptions(tOptionCardCommandType.BridgeLayer, p_PhaseNumber));
                //LocalOptionList.AddOptionsToList(OptionCards.GetOptions(tOptionCardCommandType.ScramblerBomb, p_PhaseNumber));

                foreach (OptionCard currentCard in LocalOptionList)
                {
                    Player currentPlayer = AllPlayers.GetPlayer(currentCard.Owner);
                    if (currentPlayer.IsRunning)
                    {
                        BoardElement currentBoardSquare = g_BoardElements.GetSquare(currentPlayer.CurrentPos);
                        RobotLocation currentLocation = new RobotLocation(currentPlayer.CurrentPos);
                        if (UseOption(currentPlayer,currentCard))
                        {
                            switch ((tOptionCardCommandType)currentCard.ID)
                            {
                                case tOptionCardCommandType.SelfDestruct:
                                    currentLocation.Index = currentCard.Damage;
                                    DamageAtSquare(currentLocation, currentPlayer);
                                    break;

                                // goo dropper
                                // portable teleporter
                                // proximity mine
                                case tOptionCardCommandType.TheBigOne:
                                case tOptionCardCommandType.MineLayer:
                                case tOptionCardCommandType.ScramblerBomb:

                                    currentCard.PhasePlayed = 1;
                                    currentBoardSquare.AddAction(new BoardAction(currentCard));
                                    break;

                                case tOptionCardCommandType.BridgeLayer:
                                    BoardElement FacingSquare = g_BoardElements.GetSquare(currentPlayer.CurrentPos.CalcNewLocation()); // location of square in front of robot
                                    if (FacingSquare.ActionList.Count(al => al.SquareAction == SquareAction.Archive) == 0) // no Archive on this square
                                    {
                                        FacingSquare.SetSquare(g_BoardElements.GetSquare(SquareType.Blank));
                                    }
                                    break;

                                default:
                                    break;
                            }
                        }
                    }
                }

            }

            foreach (MoveCard thiscard in GameCards.Where(gc => gc.PhasePlayed == p_PhaseNumber).OrderByDescending(gc => gc.Priority))
            {
                Player thisplayer = AllPlayers.GetPlayer(thiscard.Owner);
                if (thisplayer != null)
                {
                    if (thisplayer.IsRunning) // player not dead
                    {
                        MoveCard newcard = thiscard;
                        if(thiscard.Type==MoveCard.tCardType.Again)
                        {
                            if(p_PhaseNumber>1)
                            {
                                // find previous card for this player
                                ProcessMove( GameCards.FirstOrDefault(gc => gc.PhasePlayed == p_PhaseNumber - 1 && gc.Owner == thiscard.Owner ));
                                continue;
                            }
                            else
                            {
                                // treat as spam
                                ListOfCommands.AddCommand(thisplayer, SquareAction.Card, thiscard.ID);
                                newcard = new MoveCard(thiscard,MoveCard.tCardType.Spam);
                                
                            }
                        }
                        while(newcard.Type==MoveCard.tCardType.Spam)
                        {
                            ListOfCommands.AddCommand(thisplayer, SquareAction.Card, newcard.ID);
                            //newcard = new MoveCard(thiscard,(MoveCard.tCardType)DBConn.GetIntFromDB("select funcGetNextCard(" + thiscard.Owner + "," + newcard.ID + ")"));
                            int newcardID = (int)DBConn.GetIntFromDB("select funcGetNextCard(" + thiscard.Owner + "," + newcard.ID + ")");
                            //newcard = new MoveCard(thiscard,(MoveCard.tCardType)newcardtype);
                            newcard = GameCards.FirstOrDefault(gc=>gc.ID == newcardID && gc.Owner == thiscard.Owner);
                            //Console.WriteLine("Got new card for " + thisplayer.Name + "="+newcard.ID + ":"+newcardID.ToString());
                        }
                        ProcessMove(newcard);
                    }
                }
            }

            // loop through all squares "active" on this part of the phase
            //IEnumerable<BoardElement> StartList = g_BoardElements.Where(be => be.Type == SquareType.StartSquare).OrderBy(be => be.ActionList.First(al => al.SquareAction == SquareAction.PlayerStart).Parameter);
            //BoardActionsCollection l_TargetActions = g_BoardElements.GetSquare(l_newsquare.X, l_newsquare.Y).ActionList;
            //var dependmoves = from rmc in RobotMoveCommands join dep in nxtcomm.dependencies on rmc.movenumber equals dep select rmc;

            // list of completed dependencies
            //var completelist = from rmc in dependmoves where rmc.MoveIsComplete select rmc.movenumber;

            // get list of squares with a robot on them.
            //IEnumerable<BoardElement>
            //var ActiveSquares = g_BoardElements.Join(AllPlayers.Where(ap => ap.Active),
            //    be => be.BoardRow + "-" + be.BoardCol,
            //    ap => ap.WorkingPos.Y + "-" + ap.WorkingPos.X,
            //    (be,ap) => be);

            int CurrentAction = 0;
            ListOfCommands.AddCommand(null, SquareAction.BeginBoardEffects);

            while (true)
            {
                /// ActiveSquares is a list of board squares which have robots on them
                /// containing an ActionList which has actions that are active this phase
                /// and take place after "CurrentAction"

                var ActiveSquares = from be in g_BoardElements.BoardElements
                                    join ap in AllPlayers.Where(ap=>ap.Active)
                                    on be.Location equals ap.CurrentPos.Location
                                    select new { PlayerID = ap.ID, X = be.BoardCol, Y=be.BoardRow, ActionList = be.ActionList.Where(al=>al.PhaseActive(p_PhaseNumber)).Where(al=>al.ActionSequence > CurrentAction) };

                ///
                /// activeactions is a single list of all actions
                /// including the x,y location, and the robot on the square
                ///

                BoardActionsCollection l_activeactions = new BoardActionsCollection();

                foreach(var thissquare in ActiveSquares)
                {
                    foreach(BoardAction thisaction in thissquare.ActionList)
                    {
                        BoardAction newAction = new BoardAction(thisaction, thissquare.PlayerID,thissquare.X,thissquare.Y);
                        l_activeactions.Add(newAction);
                    }
                }

                // add any option effects to the list, here...
                IEnumerable<OptionCard> ActiveOptions = OptionCards.Where(oc=> (oc.IsActive(p_PhaseNumber)) && (oc.ActionSequence > CurrentAction));
                foreach (OptionCard thisCard in ActiveOptions)
                {
                    BoardAction newAction = new BoardAction(thisCard);
                    l_activeactions.Add(newAction);
                }

                int holdcurrent = DamageSequence + 1;

                // no remaining actions; exit loop
                if (l_activeactions.Count() == 0)
                {
                    if (CurrentAction > DamageSequence)
                    {
                        break;
                    }
                }
                else
                {
                    holdcurrent = l_activeactions.Min(aa => aa.ActionSequence);
                }

                if ((CurrentAction < DamageSequence) && (holdcurrent > DamageSequence)) holdcurrent = DamageSequence; // insert damage check in sequence

                /// find minimum Current Action (Next action)
                CurrentAction = holdcurrent;
                ListOfCommands.PhaseStep = CurrentAction + 100; // *10 + 1000;

                if (CurrentAction == DamageSequence)
                {

                    /// laser file
                    /// search for opponent bots

                    //checked for laser file from all robots
                    // and other sources...
                    // 1 calc direction of robot's cannon
                    // 2 make list of squares
                    // 3 limit list by walls
                    // 4 limit list by robots
                    // 5 inflict damage (fire & damage bot)
                    // 6 repeat
                    //IEnumerable<Player> liveplayers = AllPlayers.Where(wp => wp.IsRunning);
                    Players liveplayers = new Players();
                    foreach (Player thisplayer in AllPlayers.Where(wp => wp.IsRunning))
                    {
                        liveplayers.Add(thisplayer);
                        OptionCard RearLaser = OptionCards.GetOption(tOptionCardCommandType.RearLaser, thisplayer);
                        if (RearLaser != null)
                        {
                            Player rearPlayer = new Player(thisplayer);
                            rearPlayer.CurrentPos.Direction = RotationFunctions.Rotate(2, rearPlayer.CurrentPos.Direction);
                            liveplayers.Add(rearPlayer);
                        }

                    }

                    foreach (Player thisplayer in liveplayers) // robots only shoot if they are running
                    {
                        int RemainingPower = 1;
                        OptionCard RearLaser = OptionCards.GetOption(tOptionCardCommandType.RearLaser, thisplayer);
                        if (thisplayer.CurrentPos.Direction == AllPlayers.GetPlayer(thisplayer.ID).CurrentPos.Direction) RearLaser = null;
                        OptionCard HighPowerLaser = OptionCards.GetOption(tOptionCardCommandType.HighPowerLaser, thisplayer);
                        if (HighPowerLaser != null)
                        {
                            // increase damage
                            RemainingPower = 2;
                            //ListOfCommands.AddCommand(thisplayer, HighPowerLaser);
                        }
                        Direction canndir = thisplayer.CurrentPos.Direction;
                        OptionCard Turret = OptionCards.GetOption(tOptionCardCommandType.Turret, thisplayer);
                        if (Turret != null)
                        {
                            if (Turret.OptionDirection != Direction.Up ) // if turret is not facing up, use it.  Otherwise, ignore it.
                            {
                                canndir = RotationFunctions.GetOptionDirection(thisplayer, Turret);
                            }
                            else
                            {
                                Turret = null;
                            }
                            //ListOfCommands.AddCommand(thisplayer, Turret);
                        }
                        Direction canndir2 = RotationFunctions.Rotate(2, canndir);
                        int AddX = RotationFunctions.MovementOffsetX(canndir);
                        int AddY = RotationFunctions.MovementOffsetY(canndir);
                        //Func<Player, bool> playerFilter = null;

                        int CheckX = thisplayer.CurrentPos.X;
                        int CheckY = thisplayer.CurrentPos.Y;
                        while ((CheckX > 0)  && (CheckY > 0) && (CheckX < g_BoardElements.BoardCols-1) && (CheckY < g_BoardElements.BoardRows-1))
                        {
                            /// check wall in same square
                            if (g_BoardElements.GetSquare(CheckX, CheckY).ActionList.Count(al => ((al.SquareAction == SquareAction.BlockDirection) && (al.Parameter == (int)canndir2))) > 0)
                            {
                                RemainingPower--;
                                if (RemainingPower == 0)
                                {
                                    break; // done searching
                                }
                                else
                                {
                                    //ListOfCommands.AddCommand(thisplayer, HighPowerLaser);
                                }
                            }

                            CheckX += AddX;
                            CheckY += AddY;
                            // check for close wall
                            // check for opponent
                            // check for far wall
                            //BoardElement currentsquare = g_BoardElements.GetSquare(CheckX, CheckY);

                            /// check wall in close edge of next square
                            if (g_BoardElements.GetSquare(CheckX, CheckY).ActionList.Count(al => ((al.SquareAction == SquareAction.BlockDirection) && (al.Parameter == (int)canndir))) > 0)
                            {
                                RemainingPower--;
                                if (RemainingPower == 0)
                                {
                                    break; // done searching
                                }
                                else
                                {
                                    //ListOfCommands.AddCommand(thisplayer, HighPowerLaser);
                                }
                            }

                            /// check for opponent
                            Player shootPlayer = AllPlayers.GetPlayer(new RobotLocation(Direction.None, CheckX, CheckY)); //.Where(wp=>!wp.IsDead)
                            if (shootPlayer != null)
                            {
                                if (!shootPlayer.IsDead)
                                {
                                    // fire cannon
                                    ListOfCommands.AddCommand(thisplayer, Turret);
                                    ListOfCommands.AddCommand(thisplayer, SquareAction.FireCannon, shootPlayer.ID);
                                    ListOfCommands.AddCommand(thisplayer, HighPowerLaser);
                                    ListOfCommands.AddCommand(thisplayer, RearLaser);

                                    //int realdamage = g_BoardElements.LaserDamage;

                                    int LaserCount = 1 + OptionCards.Count(uc => (uc.ID == (int)tOptionCardCommandType.DoubleBarrelLaser || uc.ID == (int)tOptionCardCommandType.AdditionalLaser) && (uc.Owner == thisplayer.ID));
                                    int realdamage = LaserDamage * LaserCount;
                                    //OptionCard DoubleLaser = OptionCards.GetOption(tOptionCardCommandType.DoubleBarrelLaser, thisplayer);
                                    //if (DoubleLaser != null)
                                    //{
                                    //    realdamage = realdamage * 2;
                                    //    ListOfCommands.AddCommand(thisplayer, DoubleLaser);
                                    //}

                                    OptionCard PowerDownShield = OptionCards.GetOption(tOptionCardCommandType.PowerDownShield, shootPlayer);
                                    if (PowerDownShield != null)
                                    {
                                        if (shootPlayer.ShutDown == tShutDown.Currently)
                                        {
                                            if (realdamage > 0)
                                            {
                                                realdamage -= 1;
                                            }
                                            ListOfCommands.AddCommand(shootPlayer, PowerDownShield);
                                        }
                                    }

                                    OptionCard Shield = OptionCards.GetOption(tOptionCardCommandType.Shield, shootPlayer);
                                    if (Shield != null)
                                    {
                                        if (RotationFunctions.GetOptionDirection(shootPlayer, Shield,true) == canndir)
                                        {
                                            if (realdamage > 0)
                                            {
                                                realdamage -= 1;
                                            }
                                            ListOfCommands.AddCommand(shootPlayer, Shield);
                                        }
                                    }

                                    AddDamage(shootPlayer, realdamage, thisplayer);
                                    if (shootPlayer.IsDead)
                                    {
                                        //AddDeathPoints(thisplayer, 8);
                                    }

                                    // reflector
                                    OptionCard reflector = OptionCards.GetOption(tOptionCardCommandType.Reflector, shootPlayer);
                                    if (reflector != null)
                                    {
                                        if (RotationFunctions.GetOptionDirection(shootPlayer, reflector, true ) == canndir)
                                        {
                                            // return fire
                                            ListOfCommands.AddCommand(shootPlayer, reflector);
                                            Direction cann3 = canndir;
                                            canndir = canndir2;
                                            canndir2 = canndir;
                                            AddX *= -1;
                                            AddY *= -1;
                                            continue;
                                        }
                                    }
                                    RemainingPower--;
                                    if (RemainingPower == 0)
                                    {
                                        break; // done searching
                                    }
                                    else
                                    {
                                        //ListOfCommands.AddCommand(thisplayer, HighPowerLaser);
                                    }

                                }
                            }
                        }
                    }
                }

                /// create a list of actions which will take place now
                IEnumerable<BoardAction> l_CurrentActions = l_activeactions.Where(aa => aa.ActionSequence == CurrentAction);

                foreach (BoardAction thisaction in l_CurrentActions)
                {
                    Player thisplayer = AllPlayers.GetPlayer(thisaction.RobotID);
                    switch (thisaction.SquareAction)
                    {
                        case SquareAction.Archive:
                            thisplayer.ArchivePos.SetLocation(thisplayer.CurrentPos);
                            thisplayer.NextPos.SetLocation(thisplayer.CurrentPos);
                            ListOfCommands.AddCommand(thisplayer, SquareAction.Archive);
                            break;
                        case SquareAction.Damage:
                            if (thisaction.Parameter < 0 && thisplayer.Damage == 0 && IsOptionsEnabled) // would repair, but player is not damaged
                            {
                                ListOfCommands.AddCommand(thisplayer, SquareAction.Option);
                            }
                            else
                            {
                                AddDamage(thisplayer, thisaction.Parameter);
                            }

                            break;

                            ///
                            /// todo: add options
                            /// at this point, a double wrench repairs two points of damage
                            ///

                        case SquareAction.Option:
                            if (IsOptionsEnabled)
                            {
                                ListOfCommands.AddCommand(thisplayer, SquareAction.Option);

                            }
                            else
                            {
                                AddDamage(thisplayer, -1);
                            }

                            break;
                        case SquareAction.Flag:
                            break;
                        case SquareAction.TouchFlag:
                            if (thisplayer.LastFlag + 1 == thisaction.Parameter)
                            {
                                if (AddFlag(thisplayer, 1))
                                {
                                    //ListOfCommands.AddCommand(thisplayer, SquareAction.GameWinner);
                                    ListOfCommands.AddCommand("Game Winner:" + thisplayer.Name,thisplayer); // , SquareAction.GameWinner);
                                }
                                else
                                {
                                    // set next flag...
                                    SetNextFlagForPlayer(thisplayer);

                                }
                            }
                           break;
                        case SquareAction.TouchKotHFlag:
                            AddFlag(thisplayer, thisaction.Parameter);
                            break;
                        case SquareAction.TouchLastManFlag:
                            foreach (Player oneplayer in AllPlayers.Where(op => op.LastFlag > 0))
                            {
                                ListOfCommands.AddCommand(oneplayer, SquareAction.Flag, 0);
                                oneplayer.LastFlag = 0;
                            }

                            AddFlag(thisplayer, thisaction.Parameter);
                            // clear flag for all other players
                            break;
                        case SquareAction.Move:
                            // move robot...
                            MoveRobot(thisplayer, thisplayer.CalcNewLocation(1, (Direction)thisaction.Parameter), 1, (Direction)thisaction.Parameter, SquareAction.BoardMove); // sub step = 2
                            //MoveRobot(thisplayer, thisplayer.CurrentPos.CalcNewLocation(1, (Direction)thisaction.Parameter), 1, (Direction)thisaction.Parameter, SquareAction.BoardMove); // sub step = 2

                            break;
                        case SquareAction.Rotate: // board rotation...
                            OptionCard optGyroscopicStabilizer = OptionCards.GetOption(tOptionCardCommandType.GyroscopicStabilizer, thisplayer,p_PhaseNumber);
                            if (optGyroscopicStabilizer != null)
                            {
                                ListOfCommands.AddCommand(thisplayer, optGyroscopicStabilizer);
                                break; // GyroscopicStabilizer Active
                            }

                            int turndirection = (thisaction.Parameter == 1 ? 1 : -1);

                            RotateRobot(thisplayer, turndirection); //,SquareAction.BoardRotate);

                            break;
                        case SquareAction.PlayOptionCard:
                            if (!AllowOptions) break;

                            //OptionCard usingcard = OptionCards.GetOption(thisaction.Parameter);
                            //Player usingPlayer = AllPlayers.GetPlayer(usingcard.Owner);
                            //BoardElement usingBoardSquare = g_BoardElements.GetSquare(usingPlayer.CurrentPos);
                            //switch (usingcard.CommandType)
                            //{
                            //    case tOptionCardCommandType.MineLayer:
                            //        if (usingcard.GetStillWorking() && AllowOptions)
                            //        {
                            //            // place mine on robot square
                            //            BoardAction NewMine = new BoardAction(SquareAction.Mine, usingcard.Damage, DamageSequence, 0);

                            //            usingBoardSquare.ActionList.Add(NewMine);
                            //            ListOfCommands.AddCommand(thisplayer, usingcard);
                            //            //ListOfCommands.AddCommand(thisplayer, SquareAction.PlayOptionCard);
                            //        }
                            //        break;
                            //    case tOptionCardCommandType.BridgeLayer:
                            //        if (usingcard.GetStillWorking())
                            //        {
                            //            // place bridge in front of robot
                            //            // find square
                            //            BoardElement FacingSquare = g_BoardElements.GetSquare(usingPlayer.CurrentPos.CalcNewLocation()); // location of square in front of robot
                            //            // remove all square actions from square
                            //            FacingSquare.SetSquare(g_TemplateElements.GetSquare(SquareType.Blank));
                            //            ListOfCommands.AddCommand(thisplayer, usingcard);
                            //            //ListOfCommands.AddCommand(thisplayer, SquareAction.PlayOptionCard);
                            //        }
                            //        break;
                            //    case tOptionCardCommandType.ScramblerBomb:
                            //        if (usingcard.GetStillWorking() && AllowOptions)
                            //        {
                            //            // place scrambler
                            //            BoardAction NewMine = new BoardAction(SquareAction.Damage,0,0,0);
                            //            ListOfCommands.AddCommand(thisplayer, usingcard);

                            //        }
                            //        break;
                            //}
                            break;
                        case SquareAction.Mine:
                            DamageAtSquare(new RobotLocation(0, thisaction.SquareX, thisaction.SquareY,thisaction.Parameter), thisplayer);
                            // remove damage from square
                            thisaction.SquareAction = SquareAction.None;

                            ListOfCommands.AddCommand(thisplayer, SquareAction.Mine);
                            // remove mine

                            break;
                        case SquareAction.SetEnergy:
                            ListOfCommands.SetEnergy(thisplayer,thisplayer.Energy+1);
                            break;
                        case SquareAction.None:
                        case SquareAction.BlockDirection:
                        case SquareAction.PlayerStart:
                        case SquareAction.Card:

                        case SquareAction.BoardDimension:
                        case SquareAction.SquareLocation:
                        case SquareAction.SquareTemplate:
                        case SquareAction.Unknown:
                        default:
                            break;
                    }


                }

                // if any robots are on the same square, delete the movement for those robots

                // 1) find overlapping robots
                // 2) Find moves for overlapping robots.
                // 3) revert WorkingPOS
                // 4) Undo (delete) moves
                // 5) Repeat

                int problemMoveCount = 0;
                do
                {

                    var OverlappingRobots = from rob in AllPlayers.Where(wr=>wr.Active)
                                            join rob2 in AllPlayers.Where(wr => wr.Active) on rob.CurrentPos.Location equals rob2.CurrentPos.Location
                                            select new { PlayerID = rob.ID, Player2ID = rob2.ID, CurrentPos = rob.CurrentPos };

                    var OL2 = OverlappingRobots.Where(olr => olr.PlayerID != olr.Player2ID);

                    problemMoveCount = OL2.Count();

                    /// OL2 is a list of robots that overlap after board movements
                    if ((OL2 != null) && (OL2.Count() > 0))
                    {
                        IEnumerable<CommandItem> BadMoves = from rob in OL2
                                                            join mov in ListOfCommands.Where(lc => ((lc.Phase == p_PhaseNumber)
                                                                && (lc.PhaseStep == CurrentAction + 100)
                                                                && ((lc.CommandType == SquareAction.BoardMove)
                                                                  || (lc.CommandType == SquareAction.BoardMoveRotate))
                                                                ))
                                                            on rob.PlayerID equals mov.RobotID
                                                            select mov;

                        //on rob.CurrentPos.Location equals mov.EndPos.Location
                        // revert
                        List<CommandItem> BM2 = new List<CommandItem>(); // = BadMoves.Select(bm => new { cmd = bm}); //= new IEnumerable<CommandItem>();

                        foreach (CommandItem bm in BadMoves)
                        {
                            // remove move from list
                            bm.CommandType = SquareAction.DeletedMove;
                            BM2.Add(bm);

                            //bm.Status = CommandStatus.Deleted;
                        }

                        problemMoveCount = BM2.Count();

                        foreach (var thisplayer in OL2)
                        {
                            // todo: I don't really like this...
                            //CommandItem firstmove = BadMoves.First(bm => bm.RobotID == thisplayer.PlayerID);
                            if (BM2.Count(bm => bm.RobotID == thisplayer.PlayerID) > 0)
                            {
                                CommandItem firstmove = BM2.First(bm => bm.RobotID == thisplayer.PlayerID);
                                Player thisWorkingPlayer = AllPlayers.GetPlayer(thisplayer.PlayerID);
                                thisWorkingPlayer.SetLocation(firstmove.StartPos);
                                thisWorkingPlayer.NextPos.SetLocation(thisWorkingPlayer.CurrentPos);
                            }
                        }

                    }
                    else // count == 0 exit loop
                    {
                        problemMoveCount = 0;
                    }

                } while (problemMoveCount>0);
            }

            // resequence belt moves onto turn belts with a robot on them

            while(true)
            {

                var TurnMoves = from firstmove in ListOfCommands
                                from secondmove in ListOfCommands
                                where (firstmove.CompareTo(secondmove)) == 2
                                select new { First = firstmove, Second = secondmove };
                if (TurnMoves.Count() == 0)
                {
                    break;
                }

                //var TurnOne = TurnMoves.First();
                //CommandItem FirstCommand = TurnOne.First;
                //CommandItem SecondCommand = TurnOne.Second;
                CommandList MovedCommands = new CommandList(DBConn);
                CommandItem thisCommand = TurnMoves.First().First;
                CommandItem SecondCommand = TurnMoves.First().Second;
                int CommandSequence = thisCommand.CommandSequence;

                do
                {

                    MovedCommands.Add(thisCommand); // add to the list of moved commands

                    ListOfCommands.Remove(thisCommand); // remove from current list
                    //thisCommand.PhaseStepAdder++;

                    // find any other matching commands
                    //thisCommand = ListOfCommands.FirstOrDefault(lc => (thisCommand.CompareTo(lc) > 0) && (SecondCommand != lc)); // matching, but direction doesn't matter
                    thisCommand = ListOfCommands.FirstOrDefault(lc => (lc.CompareTo(thisCommand) > 0) && (SecondCommand != lc)); // matching, but direction doesn't matter
                    //thisCommand = ListOfCommands.FirstOrDefault(lc => (lc.CompareTo(thisCommand) > 0) ); // matching, but direction doesn't matter

                } while (thisCommand != null);

                int LastCommand = ListOfCommands.IndexOf(ListOfCommands.Last(lc => lc.CommandSequence == CommandSequence)) + 1;

                foreach (CommandItem moved in MovedCommands)
                {
                    moved.PhaseStepAdder++;
                    ListOfCommands.Insert(LastCommand, moved);
                }

            }

        }

        #endregion  Run Phase

        #region Helper Functions
        
        public bool UseOption(Player currentPlayer, OptionCard currentCard)
        {
            if (currentCard.Use())
            {
                if (currentPlayer == null) currentPlayer = AllPlayers.GetPlayer(currentCard.Owner);
                ListOfCommands.AddCommand(currentPlayer, currentCard);
                ListOfCommands.AddCommand(currentPlayer, currentCard.ID, currentCard.Quantity, Direction.None, SquareAction.OptionCountSet);
                if (currentCard.Quantity==0)
                {
                    OptionCards.ClearFromPlayer(currentCard, currentPlayer);
                }
                return true;
            }
            return false;
        }
        
        public bool SetNextFlagForPlayer(Player p_thisplayer, int nextFlagID = 0)
        {
            int flagid = p_thisplayer.LastFlag + 1;
            if (nextFlagID != 0) flagid = nextFlagID;

            BoardElement nextflag = g_BoardElements.GetFlagSquare(flagid);
            if (nextflag != null)
            {
                //p_thisplayer.NextFlag.SetLocation(new RobotLocation(nextflag));
                p_thisplayer.NextFlag = new RobotLocation(nextflag);
            }
            else
            {
                nextflag = g_BoardElements.GetFlagSquare( 1);
                if (nextflag != null)
                {

                    p_thisplayer.NextFlag = new RobotLocation(nextflag);
                }
                
            }
            return true;
        }


        public bool AddDeathPoints(Player p_thisplayer, int AddCount, Player p_DamagedPlayer = null)
        {
            // if (p_DamagedPlayer != null)
            // {
            //     p_DamagedPlayer.DamagedBy = p_thisplayer.ID;
            // }
            // p_thisplayer.DamagePoints += AddCount;
            // ListOfCommands.AddCommand(p_thisplayer, SquareAction.DeathPoints, p_thisplayer.DamagePoints);
            return true;
        }

        public bool AddFlag(Player p_thisplayer, int AddCount)
        {
            p_thisplayer.LastFlag += AddCount;
            ListOfCommands.AddCommand(p_thisplayer, SquareAction.Flag, p_thisplayer.LastFlag);
            AddDeathPoints(p_thisplayer, p_thisplayer.LastFlag * 5);

            if (p_thisplayer.LastFlag > p_thisplayer.TotalFlags)
            {
                TotalFlags = p_thisplayer.LastFlag;

            }
            else if (p_thisplayer.LastFlag == p_thisplayer.TotalFlags)
            {
                return true;
            }

            return false;
        }

        public bool AddDamage(Player p_thisrobot, int p_Damage, Player p_DamagingRobot = null)
        {
            /*
            if (p_DamagingRobot != null)
            {
                AddDeathPoints(p_DamagingRobot, p_Damage, p_thisrobot);
            }*/

            if (p_Damage > 0)
            {
                OptionCard DestroyOption = OptionCards.GetOptionToDestroy(p_thisrobot);
                if (DestroyOption != null)
                {
                    OptionCards.ClearFromPlayer(DestroyOption, p_thisrobot);
                    //ListOfCommands.AddCommand(p_thisrobot, -1, SquareAction.Damage);  // Should this still take place, if options is destroyed
                    //ListOfCommands.AddCommand(p_thisrobot, DestroyOption); // destroy this option

                    //p_Damage--;
                    if (DestroyOption.Damage > 0) // this option explodes
                    {
                        RobotLocation damagesquare = new RobotLocation(p_thisrobot.CurrentPos);
                        damagesquare.Index = DestroyOption.Damage;
                        DamageAtSquare(damagesquare,p_thisrobot);
                    }

                    return AddDamage(p_thisrobot, p_Damage - 1); // check for more destroyable options
                }
                
                // check if any options prevent this damage
                OptionCard Ablative = OptionCards.GetOption(tOptionCardCommandType.AblativePaint, p_thisrobot, ListOfCommands.Phase);
                if (Ablative != null) // active for this player
                {
                    if(UseOption(p_thisrobot, Ablative))
                    {
                        return AddDamage(p_thisrobot, p_Damage - 1);
                    }
                }
            }

            // inflict damage
            if (!p_thisrobot.IsDead)
            {
                if (p_thisrobot.Damage + p_Damage > 9)
                { 
                    p_thisrobot.Damage += p_Damage;   // he's about to be dead
                }
                else
                {
                    ListOfCommands.AddCommand(p_thisrobot, SquareAction.DealSpamCard, 0);                
                }
                //p_thisrobot.Damage += p_Damage;
                //ListOfCommands.AddCommand(p_thisrobot, SquareAction.Damage, p_thisrobot.Damage);
            }

            // check for dead
            if (p_thisrobot.IsDead)
            {
                if (ListOfCommands.Count(lc => (lc.RobotID == p_thisrobot.ID) && (lc.CommandType == SquareAction.Dead)) > 0)
                {
                    return false;  // not alive
                }
                //int pushedPhase = ListOfCommands.AddCommand(p_thisrobot, SquareAction.Dead).Phase;
                int pushedPhase = ListOfCommands.AddCommand(p_thisrobot, SquareAction.SetPlayerStatus,11).Phase;
                ListOfCommands.AddCommand("Remove Robot: " + p_thisrobot.Name,p_thisrobot);  // set button text & wait for click

                // lose points for dying
                AddDeathPoints(p_thisrobot, -10);
                if (p_thisrobot.DamagedBy > 0)
                {
                    AddDeathPoints(AllPlayers.GetPlayer(p_thisrobot.DamagedBy),10);
                }

                // if died by pushing, credit others in DM game.
                int pushedPlayer = p_thisrobot.ID;
                //int pushedPhase = ListOfCommands.Max(lc => lc.Phase);
                List<int> pushedPlayerList = new List<int>();

                do
                {
                    // need to make sure the robot hasn't already been pushed by another robot
                    // also need to get all the robots that pushed the current robot, not just the first


                    // here, pushedPlayer was pushed by the robot listed in the ID/value
                    CommandItem pushCommand = ListOfCommands.FirstOrDefault(lc => lc.RobotID == pushedPlayer && lc.CommandType == SquareAction.RobotPush && lc.Phase <= pushedPhase);
                    if (pushCommand == null) break;
                    pushedPlayer = pushCommand.Value;
                    pushedPhase = pushCommand.Phase;  // must have happened before or during the same phase
                    if (pushedPlayerList.Contains(pushedPlayer)) break;
                    pushedPlayerList.Add(pushedPlayer);
                    //AddFlag(AllPlayers.GetPlayer(pushedPlayer), 2); // pushing caused this player to die

                } while (true);

                if (pushedPlayer != p_thisrobot.ID)
                {
                    
                    AddDeathPoints(AllPlayers.GetPlayer(pushedPlayer), 10); // pushing caused this player to die
                }

/*
                // move to edge of board...
                if ((p_thisrobot.NextPos.X != 0) && (p_thisrobot.NextPos.Y != 0) && (p_thisrobot.NextPos.X != g_BoardElements.BoardCols-1) && (p_thisrobot.NextPos.Y != g_BoardElements.BoardRows-1))
                {
                    // not already on edge
                    //ListOfCommands.AddCommand(p_thisrobot, SquareAction.LogData);
                    //ListOfCommands.AddCommand(p_thisrobot, SquareAction.SetGameState, 10); // remove dead robot from board
                    //ListOfCommands.AddCommand("Remove Robot:" + p_thisrobot.Name); // remove dead robot from board
                }
                else
                {
                    // clear this spot...
                    //ClearThisEdge(p_thisrobot.NextPos);

                }
*/

                return false; // not still alive
            }

            return true; // still alive
        }

        public void DamageAtSquare(RobotLocation DamageSquare, Player CausedDamage) // new RobotLocation(0, X, Y, Damage)
        {
            ObservableCollection<RobotLocation> DamageSquareList = new ObservableCollection<RobotLocation>();
            //RobotLocation DamageThisSquare = new RobotLocation(0, 1, 2, 3);

            // calc squares to damage
            DamageSpread(DamageSquareList,  DamageSquare);

            // damage anything on those squares

            //var PlayerJoin = from DS in DamageSquareList join WP in AllPlayers on DS equals WP.CurrentPos select new { WP, DS.Index };

            //foreach (var PlayerDamage in PlayerJoin)
            //{
            //    AddDamage(PlayerDamage.WP, PlayerDamage.Index);
            //}

            var DamagedPlayerList = AllPlayers.Join(DamageSquareList, player => player.CurrentPos.Location, ds => ds.Location, (player, ds) => new { WPlayer = player, Damage = ds.Index });

            foreach (var PlayerDamage in DamagedPlayerList)
            {
                AddDamage(PlayerDamage.WPlayer, PlayerDamage.Damage, CausedDamage);
            }
        }

        public void DamageSpread(ObservableCollection<RobotLocation> DamageSquareList, RobotLocation DamageSquare)
        {
            if (DamageSquare.Index == 0) return;
            // set damage in this square

            if ((DamageSquare.X < 0) || (DamageSquare.X > g_BoardElements.BoardCols) || (DamageSquare.Y < 0) || (DamageSquare.Y > g_BoardElements.BoardRows)) // check this array for (-1)
            {
                return; // out of range
            }


            // check to make sure this square is not already on the list
            RobotLocation MatchingSquare = DamageSquareList.FirstOrDefault(dsl => dsl.Location == DamageSquare.Location);
            //if (!(MatchingSquare.Equals(null))) // already on list.  Is this better?
            if (MatchingSquare != null) // already on list.  Is this better?
            //if (DamageSquareList.Count(dsl => dsl == DamageSquare) > 0)
            {
                //RobotLocation MatchingSquare = DamageSquareList.FirstOrDefault(dsl => dsl == DamageSquare);
                if (MatchingSquare.Index > DamageSquare.Index) // already has better value
                {
                    return;
                }
                DamageSquareList.Remove(MatchingSquare);
            }

            DamageSquareList.Add(DamageSquare);

            // add checking for walls...

            // search for 4 adjacent squares and call again
            DamageSpread(DamageSquareList, new RobotLocation(0, DamageSquare.X - 1, DamageSquare.Y, DamageSquare.Index / 2));
            DamageSpread(DamageSquareList, new RobotLocation(0, DamageSquare.X + 1, DamageSquare.Y, DamageSquare.Index / 2));
            DamageSpread(DamageSquareList, new RobotLocation(0, DamageSquare.X , DamageSquare.Y - 1, DamageSquare.Index / 2));
            DamageSpread(DamageSquareList, new RobotLocation(0, DamageSquare.X , DamageSquare.Y + 1, DamageSquare.Index / 2));

        }

        #endregion Helper Functions


        #region Board Commands

        public string LoadBoard()
        {
            try {
                //UpdateGameState();
                Console.WriteLine("Loading:" + BoardFileName);

                //if (BoardID > 0)
                //{
                    DBConn.BoardLoadFromDB(BoardID);
                //}
                //else
                //{
                //    BoardFileRead(BoardFileName);
                //}
            }
            catch
            {
                Console.WriteLine("Board Load Failed:" + BoardID);
            }

            return BoardFileName;
        }

        public void BoardFileRead(string p_Filename)
        {

            if (p_Filename.Contains(".jpg")) p_Filename = p_Filename.Replace(".jpg", ".srx");
            if (p_Filename.Contains(".srx"))
            {
                //g_BoardElements = (BoardElementCollection)LoadFile(typeof(BoardElementCollection), p_Filename);
            }

            if (g_BoardElements != null)
            {
                TotalFlags = g_BoardElements.BoardElements.Count(be => be.ActionList.Count(al => al.SquareAction == SquareAction.Flag) > 0);
                LaserDamage = g_BoardElements.LaserDamage;
                //GameType = g_BoardElements.BoardType;
            }
            else
            {
                Console.WriteLine("Load Board Failed:" + p_Filename);
            }

        }


        public void LoadXMLBoards()
        {
            string startingDirectory = "../Boards/";
            // 0 find boards not in list (get max ID)
            var rmax = DBConn.GetIntFromDB( "Select Max(BoardID)+1 from Boards;");

            var files = Directory.EnumerateFiles(startingDirectory, "*.srx", SearchOption.TopDirectoryOnly);
            foreach(var file in files)
            {
                var count = DBConn.GetIntFromDB("Select count(*) from Boards where BoardName like '%" + file + "'" );
                if (count==0)
                {
                    string strSQL1 = "insert into Boards (BoardID, BoardName) values (" + rmax.ToString() + ",'" + file + "');";
                    DBConn.Command(strSQL1);
                    Console.WriteLine(file);
                    rmax++;
                }
            }

            var boardlist = new Dictionary<int, string>();
            // 1 find boards that are not loaded
            string strSQL = "Select * from Boards;";
            MySqlConnector.MySqlDataReader reader = DBConn.Exec(strSQL);
            while (reader.Read())
            {
                boardlist.Add((int)reader[0], (string)reader[1]);
            }

            reader.Close();

            foreach(var board in boardlist)
            {
                Console.WriteLine(board.Value);
                BoardFileRead(board.Value);
                // 2 load board from xml file

                // 3 save current board to db
                if (g_BoardElements != null)
                    DBConn.BoardSaveToDB(board.Key, g_BoardElements);
            }


            // set state to new game
            //SendGameMessage(0,"loaded " + boardlist.Count + "boards");

        }

        #endregion Board Commands


    }

}
