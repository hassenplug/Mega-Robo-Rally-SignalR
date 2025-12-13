// start app
// reset game state
// start web server
// process active commands
// create command list
// load/edit cards
// edit/load/save boards
// edit database
using MRR.Services;
using Microsoft.AspNetCore.SignalR;
using MRR.Hubs;
using MRR.Data;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
//using MRR.Data.Entities;

namespace MRR.Controller
{
    public partial class GameController
    {
        private readonly DataService _dataService;
        private readonly IHubContext<DataHub> _hubContext;

        public GameController(DataService dataService, IHubContext<DataHub> hubContext)
        {
            _dataService = dataService;
            _hubContext = hubContext;
            LoadCurrentGame();
        }


        public int RobotsActive => _dataService.RobotsActive;
        public Players AllPlayers => _dataService.AllPlayers;

        public int GameState => _dataService.GameState;

        public int UpdateGameState()
        {
            int gamestate = _dataService.UpdateGameState();
            var allDataJson = _dataService.GetAllDataJson();
            // Notify connected SignalR clients using the hub context from background thread
            _hubContext.Clients.All.SendAsync("AllDataUpdate", allDataJson).GetAwaiter().GetResult();
            return gamestate;
        }

        public async Task ExecuteTurn()
        {
            CreateCommands createCommands = new CreateCommands(_dataService);
            var exeResult = createCommands.ExecuteTurn();
            Console.WriteLine("Execute Turn Result: " + exeResult);
        }

        private Thread? _processCommandsThread = null;
        private readonly object _processCommandsLock = new object();
        private PendingCommands? _pendingCommands = null;

        public void StartProcessCommandsThread()
        {
            if (GameState != 8)
            {
                Console.WriteLine("Wrong State.  Should be 8  Actual State:" + GameState);
                return;
                //return ("Wrong State:" + GameState.ToString());
            }

            lock (_processCommandsLock)
            {
                if (_processCommandsThread == null || !_processCommandsThread.IsAlive)
                {
                    // Clean up previous PendingCommands if any
                    if (_pendingCommands != null)
                    {
                        _pendingCommands.Dispose();
                        _pendingCommands = null;
                    }
                    _pendingCommands = new PendingCommands(_dataService, _hubContext);
                    _processCommandsThread = new Thread(() =>
                    {
                        try
                        {
                            var result = _pendingCommands.ProcessCommands();
                            //Console.WriteLine("Process Commands Result: " + result);
                        }
                        finally
                        {
                            lock (_processCommandsLock)
                            {
                                _pendingCommands?.Dispose();
                                _pendingCommands = null;
                                _processCommandsThread = null;
                            }
                        }
                    });
                    _processCommandsThread.IsBackground = true;
                    _processCommandsThread.Start();
                }
                else
                {
                    Console.WriteLine("ProcessCommands thread is already running.");
                }
            }
        }

        public void StartGame(int startGameID = 0) // pass board elements and players // find start positions for each player
        {
            if (startGameID > 0)
            {
                 _dataService.ExecuteSQL("Update CurrentGameData set iValue = " + startGameID + " where iKey = 26;");  // set game state
            }

            _dataService.ExecuteSQL("Update CurrentGameData set iValue = 0 where iKey = 10;");  // set state to 0

            NextState();

            BoardElementCollection g_BoardElements = _dataService.BoardLoadFromDB(_dataService.BoardID);

            IEnumerable<BoardElement> StartList = g_BoardElements.BoardElements.Where(be => be.ActionList.Count(al => al.SquareAction == SquareAction.PlayerStart) > 0);

            int robotCount = 0;

            foreach (Player thisplayer in AllPlayers)
            {
                // set current location to next starting point...
                // Use Any(...) to avoid calling First(...) inside the predicate which can throw if no matching action exists.
                BoardElement? thisSquare = StartList.FirstOrDefault(be => be.ActionList.Any(al => al.SquareAction == SquareAction.PlayerStart && al.Parameter == thisplayer.ID));
                if (thisSquare != null)
                {
                    int pRow = thisSquare.BoardRow;
                    int pCol = thisSquare.BoardCol;
                    int pDir = (int)thisSquare.Rotation;

                    _dataService.ExecuteSQL("Update Robots set CurrentPosRow=" + pRow + ", CurrentPosCol=" + pCol + ",CurrentPosDir=" + pDir + ",ArchivePosRow=" + pRow + ",ArchivePosCol=" + pCol + ",ArchivePosDir=" + pDir + "  where RobotID=" + thisplayer.ID + ";");
                    // add "connect" command, here
                    // connect to robot
                    //thisplayer.RobotConnection = new Robots.AIMRobot(thisplayer.IPAddress);

                    //DBConn.Command("call procRobotConnectionStatus(" + thisplayer.ID + ",70);");

                    // insert options here...
                    if (_dataService.OptionsOnStartup > 0)
                    {
                        for (int opt = 0; opt < _dataService.OptionsOnStartup; opt++)
                        {
                            _dataService.ExecuteSQL("call procDealOptionToRobot(" + thisplayer.ID + ");");
                        }
                    }

                    robotCount++;
                }
                else
                {
                    // remove player from game
                    _dataService.ExecuteSQL("delete from Robots where RobotID=" + thisplayer.ID + ";");
                }

            }

            LoadCurrentGame();

            //SendGameMessage(2,"Start for " + robotCount.ToString() + " robots");
        }


        public string NextState()
        {
            int newstate;
            do
            {
//                GameState = newstate;
                newstate = GameState;

                Console.WriteLine("Current State:" + GameState.ToString());

                switch (GameState)
                {
                    case 0: // start game
            			//call procGameNew();
                        //startnewgame
                        StartGame();
                        SetGameState(2);
                        break;
                    case 2: //			#Next Turn
                        _dataService.ExecuteSQL("call procResetPlayers();");
                        _dataService.ExecuteSQL("call procMoveCardsShuffleAndDeal();");
                        _dataService.ExecuteSQL("update CurrentGameData set iValue=iValue+1 where iKey=2;"); // next turn
                        //call procResetPlayers();
                        //#call procUpdateShutDown();
                        //call procMoveCardsShuffleAndDeal();
                        //set cState = 3; #verify position
                        //update CurrentGameData set iValue=iValue+1 where iKey=2; # next turn
                        SetGameState(3);
                        break;

                    case 3: // Verify Position
                        //select count(*) into cResult from Robots where PositionValid=0;
                        //if cResult = 0 then 
                        //    set cState = 4;
                        //end if;
                        SetGameState(4);
                        break;
                    case 4: //# still programming
                        //Select Count(*) into cResult from Robots where (Status <> 4 and Status < 9) ; # not programmed & still active
                        //if cResult = 0 then
                        //    set cState = 5;
                        //end if;
                        int playersProgramming = _dataService.GetIntFromDB("Select Count(*) from  Robots where (Status <> 4 and Status < 9)");
                        if (playersProgramming==0)
                        {
                            SetGameState(5);
                        }
                        break;
                    case 5: //ready to execute turn
                        //Update Robots set `Status` = 13; // don't allow other programming changes
                        //call procCurrentPosSave();
                        _dataService.ExecuteSQL("Update Robots set `Status` = 13;"); // don't allow player changes to programs
                        SetGameState(6);
                        break;
                    case 6: // execute turn
                        //ExecuteTurn().Wait;
                        //Console.WriteLine("Executing turn...");
                        Task.Run(async () => await ExecuteTurn());
                        //Task.Delay(10);
                        //Console.WriteLine("Executing turn Done");
                        //ExecuteTurn();
                        break;
                    case 7: // executing turn
                        SetGameState(8);
                        break;
                    case 8: //#running phase  
                        StartProcessCommandsThread();
                        // set to 9 when done running
                        break;
                    case 9:// #continue (prompt)
                        SetGameState(8);
                        break;
                    case 10: // remove robot
                        // prompt
                        SetGameState(8);
                        break;
                    case 11: // game winner
                        // prompt
                        SetGameState(8);
                        break;
                    case 12: // End of game
                        // prompt
                        SetGameState(2);
                        break;
                    case 13: // Exit game (disconnect all robots)
                        // prompt
                        //# remove all connect commands from Command List
                        //Delete from CommandList where CommandTypeID = 70;
                        //#Exit game
                        SetGameState(0);
                        break;
                    case 14: // Reset board
                        // prompt
                        //#reset board (move robots)
                        //#set cState = 0;
                        SetGameState(0);
                        break;
                    case 15: // Create program
                        // prompt
                        //#Create programs
                        SetGameState(4);
                        break;
                    case 16: // Reload Position
                        // prompt
                        //#restore robot positions from previous turn
                        //# restore saved cards from previous turn
                        //call procCurrentPosLoad();
                        SetGameState(3);
                        break;
                    
                    default:
                        Console.WriteLine("NextStateError: Current State=" + GameState);
                        SetGameState(7);
                        break;
                }


                //newstate = 
                _dataService.GetIntFromDB("select funcGetNextGameState(); ");

                UpdateGameState();
                //Console.WriteLine("next:" + GameState.ToString());
                
            } while (GameState != newstate);
            //UpdateGameState();
            return "State:" + GameState.ToString();
        }

        public bool SetGameState(int newstate)
        {
            _dataService.ExecuteSQL("Update CurrentGameData set iValue=" + newstate.ToString() + " where iKey=10;");
            _dataService.GameState = newstate;
            return true;
        }

        public string LoadCurrentGame()
        {
            // load current game data from database
            // connect to robots in current game

            UpdateGameState();
            
            if (RobotsActive != 0)
            {
                ConnectToAllRobots();
            }

            // reset commands in process
            _dataService.ExecuteSQL("Update CommandList set StatusID = 1 where StatusID=4 or StatusID=3;");
            return "";
        }

        public bool ConnectToAllRobots()
        {
//            Console.WriteLine("Connecting to robots ");

            foreach (Player thisplayer in AllPlayers)
            {
                thisplayer.Connect();
            }
            return true;
        }

        public bool ConnectToRobot(int playerID)
        {
            Player? thisplayer = AllPlayers.GetPlayer(playerID);
            thisplayer.Connect();
            return true;
        }

        public void LoadBoard()
        {
            _dataService.BoardFileRead("../install/Boards/6x6x6R4Fb.srx");
            _dataService.BoardSaveToDB(3,_dataService.g_BoardElements);
        }

    }
}



/*

DELIMITER $$
USE `rally`$$
CREATE FUNCTION `funcGetNextGameState` ()
RETURNS INT 
BEGIN
	DECLARE cState INT;
	DECLARE cTurn INT;
	DECLARE cPhase INT;
	DECLARE cResult INT;
    DECLARE cStartingState int;
    
    repeat
		select iValue into cState from CurrentGameData where sKey = 'GameState';
    
		set cStartingState = cState;

		CASE cState
		WHEN 0 THEN
			#New Game
			#load players
			call procGameNew();
			set cState = 2;
			update CurrentGameData set iValue=0 where iKey=2; # turn
			update CurrentGameData set iValue=0 where iKey=3; # phase
		WHEN 1 THEN
			#Waiting for C#. ## not used
			set cState = 2;
		WHEN 2 THEN
			#Next Turn
			call procResetPlayers();
			#call procUpdateShutDown();
			call procMoveCardsShuffleAndDeal();
			set cState = 3; #verify position
			update CurrentGameData set iValue=iValue+1 where iKey=2; # next turn

		WHEN 3 THEN # or 4 or 5 THEN
			#Verify Position
			select count(*) into cResult from Robots where PositionValid=0;
			if cResult = 0 then 
				set cState = 4;
			end if;
		WHEN 4 THEN
			# still programming
			Select Count(*) into cResult from Robots where (Status <> 4 and Status < 9) ; # not programmed & still active
			if cResult = 0 then
				set cState = 5;
			end if;
		WHEN 5 THEN
			#Execute Turn
            Update Robots set `Status` = 13;
			call procCurrentPosSave();
			set cState = 6;
		WHEN 6 THEN
			#Waiting for C#
			set cState = 6;
		WHEN 7 THEN
			# show message from currentgamedata
            # Just got input here...
			set cState = 8;
            
		WHEN 8 THEN
			#Running Phase
			begin
			end;
		WHEN 9 THEN
			#Continue Running Phase
			#call procKickstart();
			set cState = 8;
		WHEN 10 THEN
			#remove robot
			#call procKickstart();
			#set bKickstart = 1;
			set cState = 8;
		WHEN 11 THEN
			#game winner
			#call procKickstart();
			#set bKickstart = 1;
			set cState = 8;
		WHEN 12 THEN
			#End of game
			set cState = 2;
		WHEN 13 THEN
			# remove all connect commands from Command List
			Delete from CommandList where CommandTypeID = 70;
			#Exit game
			set cState = 0;
			set cStartingState = cState;
		WHEN 14 THEN
			#reset board (move robots)
			#set cState = 0;
			begin
			end;
		WHEN 15 THEN
			#Create programs
			#set cState = 0;
			begin
			end;
		WHEN 16 THEN
			#restore robot positions from previous turn
			# restore saved cards from previous turn
			call procCurrentPosLoad();
			set cState = 3;
			
		ELSE
			begin
			#set cState = 0;
			end;
		END CASE;
		
		update CurrentGameData set iValue=cState where sKey="GameState";
		#update CurrentGameData set iValue=cTurn where sKey="Turn";
		#update CurrentGameData set iValue=cPhase where sKey="Phase";
        
	until (cState = cStartingState)
	end repeat;

	return cState;
    
    */