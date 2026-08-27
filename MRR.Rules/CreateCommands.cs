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
using System.Data;


namespace MRR
{

    #region Enums

    #endregion Enums

    public class CreateCommands 
    {

        private readonly TurnRequest _request;

        /// <summary>
        /// Deep copy of AllPlayers used for turn simulation. Rebuilt at the top of
        /// ExecuteTurn and discarded when the next turn is calculated; never saved back.
        /// </summary>
        private PlayerStates workingPlayers = new PlayerStates();

        #region Game Parameters & Configuration

        const int DamageSequence = 7; // number to use for damage & cannons within sequence

        public CreateCommands(TurnRequest request)
        {
            _request = request;
            g_BoardElements = request.Board;
        }

        // Every value the planner reads comes from the request. Keeping the property names
        // unchanged means the many uses of Turn / PhaseCount / AllPlayers and friends
        // throughout this file did not have to change.

        public string BoardFileName => _request.BoardFileName;

        public int BoardID => _request.BoardID;

        public int GameState => _request.GameState;

        public int PhaseCount => _request.PhaseCount;

        public int TotalFlags => _request.TotalFlags;

        public int LaserDamage => _request.LaserDamage;

        public PlayerStates AllPlayers => _request.Players;

        public CommandList ListOfCommands { get; set; } = [];

        public CardList GameCards => _request.GameCards;

        public OptionCardList OptionCards => _request.OptionCards;

        public Dictionary<int,string> OptionCardNames = [];

        public BoardElementCollection g_BoardElements { get; set; }

        public int Turn => _request.Turn;
        public int Phase => _request.Phase;

        public GameTypes GameType => _request.GameType;

        public int OptionsOnStartup => _request.OptionsOnStartup;

        public bool IsOptionsEnabled => _request.IsOptionsEnabled;

        /// <summary>Non-fatal problems noticed while planning; surfaced on the TurnPlan.</summary>
        private readonly List<string> _warnings = [];

        /// <summary>Spam cards consumed while planning, for the caller to retire.</summary>
        private readonly List<SpamCardUse> _spamConsumed = [];

        /// <summary>
        /// Next pre-drawn card for a robot, used when resolving Spam. Returns null when the
        /// pile is exhausted, which the caller reports rather than silently planning a
        /// shorter turn.
        /// </summary>
        private MoveCard? DrawNext(int robotID)
        {
            if (!_request.DrawPiles.TryGetValue(robotID, out var pile) || pile.Count == 0)
                return null;
            var card = pile[0];
            pile.RemoveAt(0);
            return card;
        }

        
        #endregion Game Parameters & Configuration


        #region Process Move

        //public void ProcessMove(MoveCard? p_movecard, PlayerStates workingPlayers)  //MoveCard.tCardType p_card, int p_player )
        public void ProcessMove(MoveCard? p_movecard)  //MoveCard.tCardType p_card, int p_player )
        {
            if (p_movecard == null) return;
            PlayerState? thisplayer = workingPlayers.GetPlayer(p_movecard.Owner);
            if (thisplayer == null) return;

            ListOfCommands.PhaseStep += 10;
            ListOfCommands.AddCommand(thisplayer, SquareAction.Card, p_movecard.ID);

            switch (p_movecard.Type)
            {
                case MoveCard.tCardType.LTurn:
                case MoveCard.tCardType.RTurn:
                case MoveCard.tCardType.UTurn:
                    // set new robot direction
                    ListOfCommands.AddCommand(thisplayer, SquareAction.SetPlayerStatus, 5);
                    ListOfCommands.AddCommand(thisplayer, SquareAction.StartBotMove, 1);
                    //RotateRobot(thisplayer, p_movecard.GetCardValue()) ; //,SquareAction.Rotate);
                    RotateRobot(thisplayer, GameCards.GetCardValue(p_movecard)); // p_movecard.GetCardValue()) ; //,SquareAction.Rotate);
                    ListOfCommands.AddCommand(thisplayer, SquareAction.StopBotMove,0);
                    ListOfCommands.AddCommand(thisplayer, SquareAction.SetPlayerStatus, 12);

                    break;
                case MoveCard.tCardType.Back1:
                case MoveCard.tCardType.Forward1:
                case MoveCard.tCardType.Forward2:
                case MoveCard.tCardType.Forward3:
                    // move robot...
                    // add this robot move
                    ListOfCommands.AddCommand(thisplayer, SquareAction.SetPlayerStatus, 5);
                    ListOfCommands.AddCommand(thisplayer, SquareAction.StartBotMove, 1);

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

                    BoardElement? l_CurrentSquare = g_BoardElements.GetSquare(thisplayer.CurrentPos.X, thisplayer.CurrentPos.Y);
                    if (l_CurrentSquare?.ActionList.Count(al => al.SquareAction == SquareAction.Water) > 0) // this square has water...
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
                    ListOfCommands.AddCommand(thisplayer, SquareAction.StopBotMove);
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

        public int CalcMoveDistance(PlayerState p_Player, int p_Distance, Direction p_Direction, SquareAction p_MoveType)
        {
            // check to see if this robot can move 1 square
            //   check for walls (2 checks)
            //   check for robot on target square
            //   check for damage on entering
            // check to see if this robot is pushing anything
            // check for remaining moves

            // Walls (this square)
            PlayerState thisplayer = p_Player; // workingPlayers.GetPlayer(p_Player);
            int PlayerX = thisplayer.CurrentPos.X;
            int PlayerY = thisplayer.CurrentPos.Y;

            BoardElement? l_CurrentSquare = g_BoardElements.GetSquare(PlayerX, PlayerY);

            // reverse direction to check for walls if moving backwards
            Direction l_ActualMoveDirection = (p_Distance > 0 ? p_Direction : RotationFunctions.Rotate(2, p_Direction));

            int l_CheckDirection = (int)RotationFunctions.Rotate(2, l_ActualMoveDirection); // calc 180 degrees out...

            int l_MoveDistance = Math.Sign(p_Distance); // could be +1 or -1 depending on forward/backward

            if (l_CurrentSquare?.ActionList.Count(al => ((al.SquareAction == SquareAction.BlockDirection) && (al.Parameter == l_CheckDirection))) > 0)
            {
                // path blocked by a wall
                ListOfCommands.AddCommand(thisplayer, SquareAction.BlockDirection);
                return 0;  // do not move
            }

            // calc new square
            //RobotLocation l_newsquare = thisplayer.CalcNewLocation(l_MoveDistance, p_Direction);
            RobotLocation l_newsquare = thisplayer.CurrentPos.CalcNewLocation(l_MoveDistance, p_Direction);
            // actions for new square
            BoardActionsCollection l_TargetActions = g_BoardElements.GetSquare(l_newsquare.X, l_newsquare.Y)?.ActionList ?? new BoardActionsCollection();

            if (l_TargetActions.Count(al => ((al.SquareAction == SquareAction.BlockDirection) && (al.Parameter == (int)l_ActualMoveDirection))) > 0)
            {
                ListOfCommands.AddCommand(thisplayer, SquareAction.BlockDirection);
                // path blocked by a wall
                return 0;
            }


            //   check for robot on target square
            PlayerState? l_PushPlayer = AllPlayers.GetPlayer(l_newsquare);
            if (l_PushPlayer != null)
            {
                ListOfCommands.AddCommand(l_PushPlayer, SquareAction.RobotPush, thisplayer.ID);
                AddDeathPoints(thisplayer, 1, l_PushPlayer);

                OptionCard? ramming = OptionCards.GetOption(tOptionCardCommandType.RammingGear, thisplayer);
                if (ramming != null) // this player has ramming gear
                {
                    ListOfCommands.AddCommand(thisplayer, ramming);
                    AddDamage(l_PushPlayer, 1, thisplayer);
                }

                //CommandItem l_PreTurn = ListOfCommands.AddSimpleTurnCommand(p_Phase, l_PushPlayer.ID, 0);

                int l_pushPlayerID = l_PushPlayer.ID;
                if (thisplayer.ID == l_pushPlayerID)
                {
                    //Console.WriteLine(l_pushPlayerID + " pushed by " + p_Player.ID + " at " + p_Player.CurrentPos.FullLocation + " ** Failure ** ");
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
                    && (workingPlayers.FirstOrDefault(wp => wp.CurrentPos.X == l_newsquare.X && wp.CurrentPos.Y == l_newsquare.Y)!=null)
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

        public void RotateRobot(PlayerState p_Robot, int p_Distance) //, SquareAction p_MoveType)
        {

            p_Robot.Rotate(p_Distance);  // rotate NextPos direction
            CommandItem turncommand = ListOfCommands.AddCommand(p_Robot, SquareAction.Rotate, p_Distance);
            //turncommand.PhaseStepSub = p_SubStep;
            p_Robot.SetLocation(); // update current to next

        }

        public bool MoveRobot(PlayerState p_Robot, RobotLocation p_NewLocation, int p_Distance, Direction p_Direction, SquareAction p_MoveType)
        {
            bool StillAlive = true;

            // move robot...
            p_Robot.NextPos.SetLocation(p_NewLocation); // end location?

            if (p_Distance >= 0)
            {
                ListOfCommands.AddCommand(p_Robot, p_Distance, 1, p_Direction, p_MoveType);
            }
            else // move backwards
            {
                ListOfCommands.AddCommand(p_Robot, -p_Distance, 3, RotationFunctions.Rotate(2, p_Direction), p_MoveType);
            }

            //   check for damage on entering

            BoardActionsCollection l_TargetActions = g_BoardElements.GetSquare(p_NewLocation.X, p_NewLocation.Y)?.ActionList ?? new BoardActionsCollection();

            BoardAction? mineAction = l_TargetActions.FirstOrDefault(ta => ta.SquareAction == SquareAction.Mine);
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
            PlayerState? blockingPlayer = AllPlayers.FirstOrDefault(wp => wp.CurrentPos.X == currentX && wp.CurrentPos.Y == currentY);
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
        public TurnPlan ExecuteTurn()
        {
            //GameState = DBConn.UpdateGameState();

            if (GameState != 6)
            {
                return new TurnPlan { Planned = false, Summary = "Wrong State:" + GameState };
            }

            // check all robots, and set their new state to "done moving"
            //if (!CheckPlayersReady()) return "Execute Failed: PlayerStates not ready";
            
            //Console.WriteLine("PlayerStates: " + AllPlayers.Count.ToString());

            // The board and the player states arrive in the request; Master refreshes them
            // before asking for a plan.
            g_BoardElements = _request.Board;

            ListOfCommands.Clear(); // = new CommandList();

            // ✅ Create working copy for simulation (will be discarded)
            workingPlayers = AllPlayers.DeepCopy();

            //Console.WriteLine("Check Rules Version");

            // update priority of card based on owner; sort by (-)
            foreach(MoveCard thiscard in GameCards)
            {
                thiscard.Priority = workingPlayers.GetPlayer(thiscard.Owner)?.Priority ?? 0;
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

            //PlayerStates PlayerDirections = new PlayerStates(AllPlayers);
            // Note: Don't refresh real AllPlayers; we're working on copy only

            foreach (PlayerState thisplayer in workingPlayers) // Use working copy, not real AllPlayers)
            {
                CommandItem? lastcommand = null;
                int lastphase = 0;
                int laststep = -1;

                //PlayerState thisplayer = new PlayerState(EndingPlayer); // copy this player

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

                if (!thisplayer.IsDead)
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

            foreach (PlayerState thisplayer in workingPlayers)
            {
                //thisplayer.FutureCards = workingPlayers.First(wp => wp.ID == thisplayer.ID).TotalCards();
                PlayerState? futureplayer = workingPlayers.GetPlayer(thisplayer.ID);
                //if ((CircuitBreaker.Owner == thisplayer.ID) && (futureplayer.Damage > 2) && (futureplayer.Damage <10))
                OptionCard? CircuitBreaker = OptionCards.GetOption(tOptionCardCommandType.CircuitBreaker, thisplayer);
                if ((CircuitBreaker != null) && (futureplayer?.Damage > 2) && (futureplayer?.Damage < 10))

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
                if (Turn > turncount)
                {
                    if (turncount > 5)
                    {
                        ListOfCommands.AddCommand((PlayerState?)null, SquareAction.SetGameState, 13); // shut down game (don't just end)
                    }
                    else
                    {
                        ListOfCommands.AddCommand((PlayerState?)null, SquareAction.EndOfGame);
                    }
                }
            }

            ListOfCommands.AddCommand(10,2); // set game state to next turn

            SequenceCommands();

            // ✅ Discard working copy here (workingPlayers goes out of scope)
            // Working copy is NOT saved back to database or AllPlayers
            // Only CommandList was written to database above

            //SendGameMessage(8,"Added " + ListOfCommands.Count + " commands"); // set to state 8, ready to start running commands
            Console.WriteLine("Planned " + ListOfCommands.Count + " commands");

            // The caller stores the commands and applies the state change. Previously this
            // method wrote CommandList itself and then ran
            //   UPDATE CurrentGameData SET iValue = 7 WHERE iKey = 10
            // to advance the state machine -- a planner reaching into two Master-owned
            // tables. Both are now results, not side effects.
            return new TurnPlan
            {
                Planned       = true,
                Commands      = ListOfCommands,
                NextGameState = 7,
                Summary       = "Added " + ListOfCommands.Count + " commands",
                Warnings      = _warnings,
                SpamConsumed  = _spamConsumed,
            };

 
        }

        /// <summary>
        /// Assigns command ids, running counters and normal/express sequence numbers, and
        /// fills the fields the executor reads. Pure: it orders the list, it does not store
        /// it. Master persists the result (see TurnPlan).
        /// </summary>
        public void SequenceCommands()
        {
            int commandID = 0;
            int lastCommandID = -1;
            int lastBot = -1;
            int RunningCommand = 0;
            //int ExpressCounter = 0;


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
                    expressList?.Select(el => el.ExpressSequence = RunningCommand).ToList();
                }
                else
                {
                    seq++;
                    RunningCommand = seq;
                }
            }
            

            foreach (CommandItem thisCommand in ListOfCommands)
            {
                thisCommand.Turn = Turn;
                //thisCommand.StatusID = thisCommand.StatusID;
                //thisCommand.PositionRow = thisCommand.EndPos.Y;
                //thisCommand.PositionCol = thisCommand.EndPos.X;
                //thisCommand.PositionDir = (int)thisCommand.EndPos.Direction;

                thisCommand.BTCommand = thisCommand.StringCommand;
                thisCommand.CommandCatID = (int)thisCommand.Category;
            }
        }


        public int TurnRobot(PlayerState p_thisplayer, CommandItem? p_OnMove, tCommandSequence p_Sequence)
        {
            // return number of commands added

            if (p_OnMove == null) return 0;

            Direction targetdir = p_OnMove.CommandDirection;
            if (p_Sequence == tCommandSequence.After) targetdir = p_OnMove.EndPos.Direction;
            int newdir = RotationFunctions.RotationDifference(p_thisplayer.CurrentPos.Direction, targetdir);
            switch (newdir)
            {
                case 0:
                    return 0;
                case -1:
                    p_OnMove.ValueB = 4;
                    return 0;
                case 1:
                    p_OnMove.ValueB = 2;
                    return 0;
                case 2:
                    p_OnMove.ValueB = 3;
                    return 0;
            }

            return 0;


        }


        #endregion Execute Turn (calculate turn)

        #region Process Robots
        /// <summary>
        /// This function replaces the LoadPlayersFromFile function
        /// </summary>


//        public PlayerState LoadOneRobot(int RobotID)
//        {
//            return new PlayerStates(RobotID).FirstOrDefault();
//        }


        #endregion Process Robots

        #region Run Phase

        public void ExecutePhase(int p_PhaseNumber, bool AllowOptions = true)
        {

            //ListOfCommands.AddCommand("Execute Phase" + p_PhaseNumber.ToString());

            ListOfCommands.Phase = p_PhaseNumber;
            // find first player on the list and give them the Next Phase button
            var firstplayer = workingPlayers.OrderBy(ob=>ob.Priority).FirstOrDefault();
            
            ListOfCommands.AddCommand("Run Phase " + p_PhaseNumber.ToString(),firstplayer);  // set button text & wait for click
            //ListOfCommands.AddCommand(3,p_PhaseNumber);
            ListOfCommands.AddCommand((PlayerState?)null, SquareAction.PhaseStart, p_PhaseNumber);
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

            //List<MoveCard> FullList = [];

            /*
             * this code should be ready for randomizer
             */
            // find any player on a randomizer...
            // player is active && does not have any random cards && current square contains random action
            //IEnumerable<PlayerState> activePlayers = AllPlayers.Where(ap=>(ap.Active ));
            //IEnumerable<BoardElement> playersSquare = AllPlayers.Where(ap => (ap.Active)).Select(ap => g_BoardElements.GetSquare(ap.CurrentPos.X, ap.CurrentPos.Y));
            /*
             * IEnumerable<PlayerState> randomizers = AllPlayers.Where(ap => (ap.Active &&
                g_BoardElements.GetSquare(ap.CurrentPos.X, ap.CurrentPos.Y).ActionList.Any(al=>al.SquareAction == SquareAction.Randomizer)));
            foreach (BoardElement thisplayer in playersSquare)
            {
                Console.WriteLine(thisplayer.ToString());
                //thisplayer.Active = true;
            }*/

            foreach (PlayerState thisplayer in workingPlayers.Where(ap => (ap.Active &&
                g_BoardElements.GetSquare(ap.CurrentPos.X, ap.CurrentPos.Y)?.ActionList.Any(al=>al.SquareAction == SquareAction.Randomizer) == true)))
            {
                PlayerState? currentPlayer = workingPlayers.GetPlayer(thisplayer.ID);
                if (currentPlayer == null) continue;
                MoveCard currentcard = currentPlayer.CardsPlayed!.First(pc => pc.PhasePlayed == p_PhaseNumber);
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
                        PlayerState? EMPlayer = workingPlayers.GetPlayer(EMP.Owner);
                        if (EMPlayer != null && UseOption(EMPlayer, EMP))
                        {
                            // clear cards and shut down all players
                            var AllOtherCards = GameCards.Where(mc => mc.Owner != EMPlayer.ID); // this will clear flywheel cards that are in memory

                            foreach (MoveCard eachcard in AllOtherCards)
                            {
                                eachcard.PhasePlayed = -1; // unplay card
                            }

                            var OtherPlayers = workingPlayers.Where(wp => wp.ID != EMPlayer.ID);
                            foreach (PlayerState notEMP in OtherPlayers)
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
                        PlayerState? eraseDamagePlayer = workingPlayers.GetPlayer(Eraser.Owner);
                        if (eraseDamagePlayer != null && UseOption(eraseDamagePlayer, Eraser))
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
                    PlayerState? currentPlayer = AllPlayers.GetPlayer(currentCard.Owner);
                    if (currentPlayer != null && currentPlayer.IsRunning)
                    {
                        BoardElement? currentBoardSquare = g_BoardElements.GetSquare(currentPlayer.CurrentPos);
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
                                    currentBoardSquare?.AddAction(new BoardAction(currentCard));
                                    break;

                                case tOptionCardCommandType.BridgeLayer:
                                    BoardElement? FacingSquare = g_BoardElements.GetSquare(currentPlayer.CurrentPos.CalcNewLocation()); // location of square in front of robot
                                    if (FacingSquare?.ActionList.Count(al => al.SquareAction == SquareAction.Archive) == 0) // no Archive on this square
                                    {
                                        BoardElement? blankSquare = g_BoardElements.GetSquare(SquareType.Blank);
                                        if (blankSquare != null) FacingSquare?.SetSquare(blankSquare);
                                    }
                                    break;

                                default:
                                    break;
                            }
                        }
                    }
                }

            }

            foreach (MoveCard thiscard in GameCards.Where(gc => gc.PhasePlayed == p_PhaseNumber).OrderBy(gc => gc.Priority))
            {
                PlayerState? thisplayer = workingPlayers.GetPlayer(thiscard.Owner);
                if (thisplayer != null)
                {
                    if (thisplayer.IsRunning) // player not dead
                    {
                        MoveCard? newcard = thiscard;   // null once a draw pile runs dry
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
                        // Spam resolves by drawing replacements until a non-Spam card turns up,
                        // and Spam can chain. Cards come from the pre-drawn pile in the request:
                        // drawing from the database here would mutate mid-plan and reshuffle the
                        // discard pile, so the same turn would not replan the same way.
                        while (newcard != null && newcard.Type == MoveCard.tCardType.Spam)
                        {
                            ListOfCommands.AddCommand(thisplayer, SquareAction.Card, newcard.ID);
                            _spamConsumed.Add(new SpamCardUse(thiscard.Owner, newcard.ID));
                            newcard = DrawNext(thiscard.Owner);
                            if (newcard == null)
                            {
                                _warnings.Add($"Robot {thiscard.Owner} ran out of cards resolving Spam in phase {p_PhaseNumber}.");
                                break;
                            }
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
            ListOfCommands.AddCommand((PlayerState?)null, SquareAction.BeginBoardEffects);

            while (true)
            {
                /// ActiveSquares is a list of board squares which have robots on them
                /// containing an ActionList which has actions that are active this phase
                /// and take place after "CurrentAction"

                var ActiveSquares = from be in g_BoardElements.BoardElements
                                    join ap in workingPlayers.Where(ap=>ap.Active)
                                    on be.Location equals ap.CurrentPos.Location
                                    select new { PlayerID = ap.ID, X = be.BoardCol, Y=be.BoardRow, ActionList = be.ActionList.Where(al=>al.PhaseActive(p_PhaseNumber)).Where(al=>al.ActionSequence > CurrentAction) };

                ///
                /// activeactions is a single list of all actions
                /// including the x,y location, and the robot on the square
                ///

                BoardActionsCollection l_activeactions = [];

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
                    //IEnumerable<PlayerState> liveplayers = AllPlayers.Where(wp => wp.IsRunning);

                    // need to create a list of players to iderate through, but add to while iderating
                    List<PlayerState> liveplayers = [];
                    
                    foreach (PlayerState thisplayer in AllPlayers.Where(wp => wp.IsRunning))
                    {
                        liveplayers.Add(thisplayer);
                        OptionCard? RearLaser = OptionCards.GetOption(tOptionCardCommandType.RearLaser, thisplayer);
                        if (RearLaser != null) // add another player for rear laser
                        {
                            PlayerState rearPlayer = new PlayerState(thisplayer);
                            rearPlayer.CurrentPos.Direction = RotationFunctions.Rotate(2, rearPlayer.CurrentPos.Direction);
                            liveplayers.Add(rearPlayer);
                        }

                    }

                    foreach (PlayerState thisplayer in liveplayers) // robots only shoot if they are running
                    {
                        int RemainingPower = 1;
                        OptionCard? RearLaser = OptionCards.GetOption(tOptionCardCommandType.RearLaser, thisplayer);
                        if (thisplayer.CurrentPos.Direction == workingPlayers.GetPlayer(thisplayer.ID)?.CurrentPos.Direction) RearLaser = null;
                        OptionCard? HighPowerLaser = OptionCards.GetOption(tOptionCardCommandType.HighPowerLaser, thisplayer);
                        if (HighPowerLaser != null)
                        {
                            // increase damage
                            RemainingPower = 2;
                            //ListOfCommands.AddCommand(thisplayer, HighPowerLaser);
                        }
                        Direction canndir = thisplayer.CurrentPos.Direction;
                        OptionCard? Turret = OptionCards.GetOption(tOptionCardCommandType.Turret, thisplayer);
                        if (Turret != null)
                        {
                            if (Turret.OptionDirection != Direction.Up ) // if turret is not facing up, use it.  Otherwise, ignore it.
                            {
                                canndir = RotationFunctions.GetOptionDirection(thisplayer.CurrentPos.Direction, Turret);
                            }
                            else
                            {
                                Turret = null;
                            }
                            //ListOfCommands.AddCommand(thisplayer, Turret);
                        }
                        Direction canndir2 = RotationFunctions.Rotate(2, canndir);
                        var (AddX, AddY) = RotationFunctions.MovementOffset(canndir);
                        //Func<PlayerState, bool> playerFilter = null;

                        int CheckX = thisplayer.CurrentPos.X;
                        int CheckY = thisplayer.CurrentPos.Y;
                        while ((CheckX > 0)  && (CheckY > 0) && (CheckX < g_BoardElements.BoardCols-1) && (CheckY < g_BoardElements.BoardRows-1))
                        {
                            /// check wall in same square
                            if (g_BoardElements.GetSquare(CheckX, CheckY)?.ActionList.Count(al => ((al.SquareAction == SquareAction.BlockDirection) && (al.Parameter == (int)canndir2))) > 0)
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
                            if (g_BoardElements.GetSquare(CheckX, CheckY)?.ActionList.Count(al => ((al.SquareAction == SquareAction.BlockDirection) && (al.Parameter == (int)canndir))) > 0)
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
                            PlayerState? shootPlayer = workingPlayers.GetPlayer(new RobotLocation(Direction.None, CheckX, CheckY)); //.Where(wp=>!wp.IsDead)
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

                                    OptionCard? PowerDownShield = OptionCards.GetOption(tOptionCardCommandType.PowerDownShield, shootPlayer);
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

                                    OptionCard? Shield = OptionCards.GetOption(tOptionCardCommandType.Shield, shootPlayer);
                                    if (Shield != null)
                                    {
                                        if (RotationFunctions.GetOptionDirection(shootPlayer.CurrentPos.Direction, Shield,true) == canndir)
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
                                    OptionCard? reflector = OptionCards.GetOption(tOptionCardCommandType.Reflector, shootPlayer);
                                    if (reflector != null)
                                    {
                                        if (RotationFunctions.GetOptionDirection(shootPlayer.CurrentPos.Direction, reflector, true ) == canndir)
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
                    PlayerState? thisplayer = AllPlayers.GetPlayer(thisaction.RobotID);
                    if (thisplayer == null) continue;
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
                            foreach (PlayerState oneplayer in AllPlayers.Where(op => op.LastFlag > 0))
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
                            OptionCard? optGyroscopicStabilizer = OptionCards.GetOption(tOptionCardCommandType.GyroscopicStabilizer, thisplayer,p_PhaseNumber);
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
                            //PlayerState usingPlayer = AllPlayers.GetPlayer(usingcard.Owner);
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
                        List<CommandItem> BM2 = []; // = BadMoves.Select(bm => new { cmd = bm}); //= new IEnumerable<CommandItem>();

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
                                PlayerState? thisWorkingPlayer = AllPlayers.GetPlayer(thisplayer.PlayerID);
                                thisWorkingPlayer?.SetLocation(firstmove.StartPos);
                                thisWorkingPlayer?.NextPos.SetLocation(thisWorkingPlayer.CurrentPos);
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
                CommandList MovedCommands = [];
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
                    thisCommand = ListOfCommands.FirstOrDefault(lc => (lc.CompareTo(thisCommand) > 0) && (SecondCommand != lc))!; // matching, but direction doesn't matter
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
        
        public bool UseOption(PlayerState? currentPlayer, OptionCard currentCard)
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
        
        public bool SetNextFlagForPlayer(PlayerState p_thisplayer, int nextFlagID = 0)
        {
            int flagid = p_thisplayer.LastFlag + 1;
            if (nextFlagID != 0) flagid = nextFlagID;

            BoardElement? nextflag = g_BoardElements.GetFlagSquare(flagid);
            if (nextflag != null)
            {
                //p_thisplayer.NextFlag.SetLocation(new RobotLocation(nextflag));
                p_thisplayer.NextFlag = new RobotLocation(nextflag);
            }
            else
            {
                nextflag = g_BoardElements.GetFlagSquare(1);
                if (nextflag != null)
                {

                    p_thisplayer.NextFlag = new RobotLocation(nextflag);
                }
                
            }
            return true;
        }


        public bool AddDeathPoints(PlayerState p_thisplayer, int AddCount, PlayerState? p_DamagedPlayer = null)
        {
            // if (p_DamagedPlayer != null)
            // {
            //     p_DamagedPlayer.DamagedBy = p_thisplayer.ID;
            // }
            // p_thisplayer.DamagePoints += AddCount;
            // ListOfCommands.AddCommand(p_thisplayer, SquareAction.DeathPoints, p_thisplayer.DamagePoints);
            return true;
        }

        /// <summary>
        /// Advances the player's flag count. Returns true if that wins the game.
        /// </summary>
        public bool AddFlag(PlayerState p_thisplayer, int AddCount)
        {
            p_thisplayer.LastFlag += AddCount;
            ListOfCommands.AddCommand(p_thisplayer, SquareAction.Flag, p_thisplayer.LastFlag);
            AddDeathPoints(p_thisplayer, p_thisplayer.LastFlag * 5);

            // TotalFlags is game-wide (CurrentGameData iKey 7), set from the board at game
            // start. >= rather than == so an overshoot still wins instead of being missed.
            return p_thisplayer.LastFlag >= TotalFlags;
        }

        public bool AddDamage(PlayerState p_thisrobot, int p_Damage, PlayerState? p_DamagingRobot = null)
        {
            
            if (p_DamagingRobot != null)
            {
                //AddDeathPoints(p_DamagingRobot, p_Damage, p_thisrobot);
                ListOfCommands.AddCommand(p_DamagingRobot, SquareAction.SetPlayerStatus, 14);
            }

            if (p_Damage > 0)
            {
                OptionCard? DestroyOption = OptionCards.GetOptionToDestroy(p_thisrobot);
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
                OptionCard? Ablative = OptionCards.GetOption(tOptionCardCommandType.AblativePaint, p_thisrobot, ListOfCommands.Phase);
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
                    AddDeathPoints(AllPlayers.GetPlayer(p_thisrobot.DamagedBy)!, 10);
                }

                // if died by pushing, credit others in DM game.
                int pushedPlayer = p_thisrobot.ID;
                //int pushedPhase = ListOfCommands.Max(lc => lc.Phase);
                List<int> pushedPlayerList = [];

                do
                {
                    // need to make sure the robot hasn't already been pushed by another robot
                    // also need to get all the robots that pushed the current robot, not just the first


                    // here, pushedPlayer was pushed by the robot listed in the ID/value
                    CommandItem? pushCommand = ListOfCommands.FirstOrDefault(lc => lc.RobotID == pushedPlayer && lc.CommandType == SquareAction.RobotPush && lc.Phase <= pushedPhase);
                    if (pushCommand == null) break;
                    pushedPlayer = pushCommand.Value;
                    pushedPhase = pushCommand.Phase;  // must have happened before or during the same phase
                    if (pushedPlayerList.Contains(pushedPlayer)) break;
                    pushedPlayerList.Add(pushedPlayer);
                    //AddFlag(AllPlayers.GetPlayer(pushedPlayer), 2); // pushing caused this player to die

                } while (true);

                if (pushedPlayer != p_thisrobot.ID)
                {
                    
                    AddDeathPoints(AllPlayers.GetPlayer(pushedPlayer)!, 10); // pushing caused this player to die
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

        public void DamageAtSquare(RobotLocation DamageSquare, PlayerState CausedDamage) // new RobotLocation(0, X, Y, Damage)
        {
            List<RobotLocation> DamageSquareList = [];
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

        public void DamageSpread(List<RobotLocation> DamageSquareList, RobotLocation DamageSquare)
        {
            if (DamageSquare.Index == 0) return;
            // set damage in this square

            if ((DamageSquare.X < 0) || (DamageSquare.X > g_BoardElements.BoardCols) || (DamageSquare.Y < 0) || (DamageSquare.Y > g_BoardElements.BoardRows)) // check this array for (-1)
            {
                return; // out of range
            }


            // check to make sure this square is not already on the list
            RobotLocation? MatchingSquare = DamageSquareList.FirstOrDefault(dsl => dsl.Location == DamageSquare.Location);
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

        #endregion Board Commands


    }

}
