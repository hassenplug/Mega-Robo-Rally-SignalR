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
using MRR;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Threading;
//using MRR.Data.Entities;

namespace MRR.Controller
{
    public partial class GameController
    {
        private readonly DataService _dataService;
        private readonly IHubContext<DataHub> _hubContext;

        /// <summary>
        /// When true, each robot's LCD shows the programming UI and accepts touch input.
        /// Defaults to false; set via POST /api/settings/robot-screen.
        /// </summary>
        public static bool UseRobotScreen { get; set; } = false;

        // CancellationTokenSources for the per-robot touch polling tasks
        private readonly Dictionary<int, CancellationTokenSource> _screenUiCts = new();

        public GameController(DataService dataService, IHubContext<DataHub> hubContext)
        {
            _dataService = dataService;
            _hubContext = hubContext;
            LoadCurrentGame();
        }


        public int RobotsActive => _dataService.RobotsActive;
        public bool IsRunning => _dataService.IsRunning;
        public Players AllPlayers => _dataService.AllPlayers;

        public int GameState => _dataService.GameState;

        public int UpdateGameState()
        {
            //int gamestate = _dataService.UpdateGameState();
            var allDataJson = _dataService.GetAllDataJson();
            // Notify connected SignalR clients using the hub context from background thread
            _hubContext.Clients.All.SendAsync("AllDataUpdate", allDataJson).GetAwaiter().GetResult();
            return GameState;
        }

        public async Task ExecuteTurn()
        {
            if (Interlocked.CompareExchange(ref _executeTurnRunningFlag, 1, 0) == 1)
            {
                Console.WriteLine("ExecuteTurn already running; call ignored.");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    // Master assembles the input, the planner reads only that, Master applies
                    // the result. Nothing in between touches the database.
                    TurnRequest request = _dataService.BuildTurnRequest();
                    CreateCommands createCommands = new CreateCommands(request);
                    TurnPlan plan = createCommands.ExecuteTurn();
                    Console.WriteLine("Execute Turn Result: " + plan.Summary);
                    foreach (var warning in plan.Warnings) Console.WriteLine("  warning: " + warning);

                    if (!plan.Planned) return;

                    // Master owns both of these tables, so Master applies the plan: store the
                    // commands, then advance the state machine. The planner used to do both
                    // itself.
                    _dataService.PersistCommands(plan.Commands, _dataService.Turn);
                    _dataService.RetireSpamCards(plan.SpamConsumed);
                    _dataService.GameState = plan.NextGameState;
                });

                // Sync C# state from DB so NextState() sees state 7, not stale 6
                _dataService.UpdateGameState();
            }
            finally
            {
                Interlocked.Exchange(ref _executeTurnRunningFlag, 0);
                NextState();
            }
        }

        private Thread? _processCommandsThread = null;
        private readonly object _processCommandsLock = new object();
        private PendingCommands? _pendingCommands = null;
        // guard flag to prevent re-entrant NextState() calls
        private int _nextStateRunningFlag = 0;
        // guard to ensure ExecuteTurn runs only one instance at a time
        private int _executeTurnRunningFlag = 0;

        public void StartProcessCommandsThread()
        {
            if (GameState != 8)
            {
                Console.WriteLine("Wrong State.  Should be 8  Actual State:" + GameState);
                return;
                //return ("Wrong State:" + GameState.ToString());
            }

            Console.WriteLine("Starting Process Commands Thread...");
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

        /// <summary>
        /// GM recovery action (e.g. "clearpause"): forces stuck commands of one type from one
        /// status to another. Routed through the live PendingCommands instance when one
        /// exists, so its in-memory command list is updated too -- otherwise the loop's next
        /// SaveChanges() on its stale copy silently reverts this fix. Falls back to a direct
        /// DB update when no ProcessCommands loop is running.
        /// </summary>
        public int ClearPausedCommands(SquareAction commandType, int fromStatus, int toStatus)
        {
            lock (_processCommandsLock)
            {
                if (_pendingCommands != null)
                    return _pendingCommands.ClearStuckCommands(commandType, fromStatus, toStatus);
            }

            return _dataService.ExecuteSQL(
                $"UPDATE CommandList SET StatusID = {toStatus} WHERE CommandTypeID = {(int)commandType} AND StatusID = {fromStatus}");
        }

        /// <summary>
        /// Completes one command by ID (e.g. a player's "continue" click on a User Input
        /// command). Routed through the live PendingCommands instance when one exists, for
        /// the same reason as ClearPausedCommands above.
        /// </summary>
        public int ProcessDbCommand(int commandId, int newStatus)
        {
            lock (_processCommandsLock)
            {
                if (_pendingCommands != null)
                    return _pendingCommands.ProcessDbCommand(commandId, newStatus);
            }

            return _dataService.ProcessDbCommand(commandId, newStatus);
        }

        /// <summary>
        /// Stops the turn in progress. Returns false if nothing was running.
        ///
        /// Commands already sent to a robot cannot be recalled -- the robot is physically
        /// moving -- so this stops dispatch and retires what had not started. The state
        /// machine is left where it is: use "Reload Position" (state 16) to put the robots
        /// back where the turn began, or "Create Program" (state 15) to reprogram.
        /// </summary>
        public bool AbortTurn()
        {
            lock (_processCommandsLock)
            {
                if (_pendingCommands == null) return false;
                Console.WriteLine("Abort requested for the running turn.");
                _pendingCommands.Abort();
                return true;
            }
        }

        public void LoadGameData(int gameDataID)
        {
            // Copy all GameData fields into CurrentGameData
            _dataService.ExecuteSQL(
                "UPDATE CurrentGameData " +
                "INNER JOIN GameData ON GameData.GameDataID = " + gameDataID + " " +
                "SET CurrentGameData.iValue = " +
                "  CASE CurrentGameData.sKey " +
                "    WHEN 'GameDataID'   THEN GameData.GameDataID " +
                "    WHEN 'GameState'    THEN 0 " +
                "    WHEN 'GameType'     THEN GameData.GameType " +
                "    WHEN 'LaserDamage'  THEN GameData.LaserDamage " +
                "    WHEN 'TotalFlags'   THEN GameData.TotalFlags " +
                "    WHEN 'PhaseCount'   THEN GameData.PhaseCount " +
                "    WHEN 'BoardCols'    THEN GameData.BoardCols " +
                "    WHEN 'BoardRows'    THEN GameData.BoardRows " +
                "    WHEN 'BoardID'      THEN GameData.BoardID " +
                "    WHEN 'OptionCount'  THEN GameData.OptionCount " +
                "    WHEN 'PlayerListID' THEN GameData.PlayerListID " +
                "    ELSE CurrentGameData.iValue " +
                "  END;");
            //_dataService.ExecuteSQL("call procResetGame();");

        }

        public void StartGame() // pass board elements and players // find start positions for each player
        {
            _dataService.IsRunning = true;

            _dataService.ExecuteSQL("Update CurrentGameData set iValue=0 where sKey='GameState';");
            _dataService.ExecuteSQL("Update CurrentGameData set iValue=0 where sKey='Turn';");
            _dataService.ExecuteSQL("Update CurrentGameData set iValue=0 where sKey='Phase';");
            _dataService.ExecuteSQL("Delete from MoveCards;");
            _dataService.ExecuteSQL("Delete from CommandList;");        
            _dataService.ExecuteSQL("Delete from RobotOptions;");
            _dataService.ExecuteSQL("Delete from StatusLEDs;");
            _dataService.ExecuteSQL("Delete from Robots;");

            // Populate Robots from the active OperatorData list so positions can be set below.
            // Joins to RobotBases/RobotBodies/SeatOrientation denormalize display/lookup fields
            // onto Robots so later reads don't need to re-join every time.
            _dataService.ExecuteSQL(
                "insert into Robots (RobotID, OperatorName, RobotBaseID, RobotBodyID, `Status`, Priority, `Password`, PlayerSeat, " +
                "RobotName, RobotColor, RobotColorFG, IPAddress, DirectionAdjustment) " +
                "Select od.RobotID, od.OperatorName, od.RobotID, od.RobotBodyID, 1, od.PlayerSeat, od.`Password`, od.PlayerSeat, " +
                "rbody.Name, rbody.Color, rbody.ColorFG, rbase.IPAddress, so.Direction " +
                "from OperatorData od " +
                "inner join CurrentGameData pl on od.OperatorListID = pl.iValue and pl.sKey = 'PlayerListID' " +
                "inner join RobotBodies rbody on od.RobotBodyID = rbody.RobotBodyID " +
                "inner join RobotBases rbase on od.RobotID = rbase.RobotBaseID " +
                "inner join SeatOrientation so on od.PlayerSeat = so.SeatID " +
                "where od.IsActive > 0;");

            //NextState();

            BoardElementCollection g_BoardElements = _dataService.BoardLoadFromDB(_dataService.BoardID);

            // One TotalFlags for the whole game, taken from the board being played.
            // The setter writes through to CurrentGameData (iKey 7), which is the source of
            // truth from here on — UpdateGameState() reloads it after a restart.
            _dataService.TotalFlags = g_BoardElements.CalcTotalFlags();

            IEnumerable<BoardElement> StartList = g_BoardElements.BoardElements.Where(be => be.ActionList.Count(al => al.SquareAction == SquareAction.PlayerStart) > 0);

            int robotCount = 0;

            //foreach (Player thisplayer in AllPlayers)
            for(int pid=1; pid<9; pid++)
            {
                //int pid = thisplayer.ID;
                // set current location to next starting point...
                // Use Any(...) to avoid calling First(...) inside the predicate which can throw if no matching action exists.
                BoardElement? thisSquare = StartList.FirstOrDefault(be => be.ActionList.Any(al => al.SquareAction == SquareAction.PlayerStart && al.Parameter == pid));
                if (thisSquare != null)
                {
                    int pRow = thisSquare.BoardRow;
                    int pCol = thisSquare.BoardCol;
                    int pDir = (int)thisSquare.Rotation;

                    _dataService.ExecuteSQL("Update Robots set CurrentPosRow=" + pRow + ", CurrentPosCol=" + pCol + ",CurrentPosDir=" + pDir + ",ArchivePosRow=" + pRow + ",ArchivePosCol=" + pCol + ",ArchivePosDir=" + pDir + "  where RobotID=" + pid + ";");
                    // add "connect" command, here
                    // connect to robot
                    //thisplayer.RobotConnection = new Robots.AIMRobot(thisplayer.IPAddress);

                    //DBConn.Command("call procRobotConnectionStatus(" + thisplayer.ID + ",70);");

                    // insert options here...
                    if (_dataService.OptionsOnStartup > 0)
                    {
                        for (int opt = 0; opt < _dataService.OptionsOnStartup; opt++)
                        {
                            _dataService.DealOptionToRobot(pid);
                        }
                    }

                    robotCount++;
                }
                else
                {
                    // remove player from game
                    _dataService.ExecuteSQL("delete from Robots where RobotID=" + pid + ";");
                }

            }

            _dataService.GameNewAddCards();
            _dataService.UpdatePlayerPriority(null, 1);

            // Refresh C# state so BoardID etc. reflect the new values before board load
            //_dataService.UpdateGameState();
            _dataService.GetAllPlayers(true); // force refresh of player list after DB changes
            //_dataService.UpdateGameState();
//            NextState();
            LoadCurrentGame();

            //SendGameMessage(2,"Start for " + robotCount.ToString() + " robots");
        }

        public string NextState()
        {
            // Ensure only one thread can run NextState at a time
            if (Interlocked.CompareExchange(ref _nextStateRunningFlag, 1, 0) == 1)
            {
                Console.WriteLine("NextState() already running; call ignored.");
                return "State:" + GameState.ToString();
            }

            try
            {
                int newstate;
                do
                {
                    newstate = GameState;

                    Console.WriteLine("Current State:" + GameState.ToString());

                    switch (GameState)
                    {
                        case 0: // start game
                            StartGame();
                            SetGameState(2);
                            break;
                        case 2: // Next Turn
                            _dataService.ResetPlayers();
                            _dataService.MoveCardsShuffleAndDeal();
                            //_dataService.ExecuteSQL("call procUpdateRobotCards();");
                            //UpdateGameState(); // ensure DB changes are visible before next command
                            _dataService.ExecuteSQL("update CurrentGameData set iValue=iValue+1 where iKey=2;"); // next turn
                            foreach (var p in AllPlayers) p.UpdateStatusLEDs();
                            ScreenUiLoadHand(2);
                            SetGameState(3);
                            break;
                        case 3: // Verify Position
                            ScreenUiLoadHand(3);
                            SetGameState(4);
                            break;
                        case 4: // still programming
                            int playersProgramming = _dataService.GetIntFromDB("Select Count(*) from  Robots where (Status <> 4 and Status < 9)");
                            if (playersProgramming == 0)
                            {
                                SetGameState(5);
                            }
                            else
                            {
                                // Show hand on robot screens during programming
                                ScreenUiLoadHand(4);
                            }
                            break;
                        case 5: // ready to execute turn
                            _dataService.ExecuteSQL("Update Robots set `Status` = 13;"); // don't allow player changes to programs
                            _dataService.CurrentPosSave();
                            ScreenUiLock();
                            SetGameState(6);
                            break;
                        case 6: // execute turn
                            ScreenUiRenderIdle(6);
                            Task.Run(async () => await ExecuteTurn());
                            break;
                        case 7: // executing turn
                            ScreenUiRenderIdle(7);
                            SetGameState(8);
                            break;
                        case 8: // running phase
                            ScreenUiRenderIdle(8);
                            StartProcessCommandsThread();
                            break;
                        case 9: // continue (prompt)
                        case 10: // remove robot
                        case 11: // game winner
                            ScreenUiRenderIdle(GameState);
                            SetGameState(8);
                            break;
                        case 12: // End of game
                            ScreenUiRenderIdle(12);
                            SetGameState(2);
                            break;
                        case 13: // Exit game (disconnect all robots)
                        case 14: // Reset board
                            ScreenUiRenderIdle(GameState);
                            SetGameState(0);
                            break;
                        case 15: // Create program
                            SetGameState(4);
                            break;
                        case 16: // Reload Position
                            // restore saved positions from previous turn
                            _dataService.CurrentPosLoad();
                            SetGameState(3);
                            break;
                        default:
                            Console.WriteLine("NextStateError: Current State=" + GameState);
                            SetGameState(7);
                            break;
                    }

                    // update from DB whether state should advance further
                    //_dataService.GetIntFromDB("select funcGetNextGameState(); ");

                    UpdateGameState();

                } while (GameState != newstate);

                return "State:" + GameState.ToString();
            }
            finally
            {
                Interlocked.Exchange(ref _nextStateRunningFlag, 0);
            }
        }


        public bool SetGameState(int newstate)
        {
            _dataService.GameState = newstate;
            return true;
        }

        public string LoadCurrentGame()
        {
            // load current game data from database
            // connect to robots in current game

            //UpdateGameState();
            //_dataService.UpdateGameState(); // ensure C# state reflects any DB changes from UpdateGameState logic
            _dataService.ReloadAllData();
            
            if (RobotsActive != 0 && IsRunning)
            {
                ConnectToAllRobots();
            }

            // reset commands in process
            _dataService.ExecuteSQL("Update CommandList set StatusID = 2 where StatusID=4 or StatusID=3;");
            return "";
        }

        /// <summary>Connects every robot. Awaits all of them together (not fire-and-forget --
        /// see DisconnectAllRobots for why that silently drops DB writes on failure) so the
        /// call doesn't return until every robot's ConnectStatusID reflects the outcome.</summary>
        public bool ConnectToAllRobots()
        {
            Task.WhenAll(AllPlayers.Select(ConnectPlayerWithScreen)).Wait();
            return true;
        }

        private async Task ConnectPlayerWithScreen(Player player)
        {
            SetRobotConnectStatus(player.ID, tConnectStatus.Connecting);
            await player.Connect();
            SetRobotConnectStatus(player.ID, player.isConnected ? tConnectStatus.Connected : tConnectStatus.NotConnected);

            if (UseRobotScreen && player.isConnected)
            {
                InitScreenUI(player);
            }
        }

        /// <summary>Writes through to Robots.ConnectStatusID -- and, in the same statement,
        /// ConnectStatusColor/ConnectStatusDesc from the matching RobotStatus row -- so the
        /// connection screen reflects whether we actually have a live socket to the robot.
        /// Sets all three itself rather than calling RefreshRobotDenormalizedFields: that method
        /// recomputes the *gameplay* denormalized columns (StatusColor, LEDColor, PlayerStatus,
        /// sDir, FlagEnergy, StatusToShow, PlayerMsg) for every robot via several joins, which is
        /// far more than a single robot's connect-status change needs. Broadcasts immediately so
        /// the Connecting -> Connected/NotConnected transition is visible live rather than only
        /// on the next unrelated broadcast (see install/todo.md Section 9).</summary>
        private void SetRobotConnectStatus(int robotID, tConnectStatus status)
        {
            _dataService.ExecuteSQL($@"
                UPDATE Robots r
                JOIN RobotStatus cs ON cs.RobotStatusID = {(int)status}
                SET r.ConnectStatusID    = {(int)status},
                    r.ConnectStatusColor = cs.StatusColor,
                    r.ConnectStatusDesc  = cs.ShortDescription
                WHERE r.RobotID = {robotID};");
            UpdateGameState();
        }

        /// <summary>Batch form of SetRobotConnectStatus for the connection screen's "Connect
        /// All"/"Disconnect All"/"Search" (install/todo.md Section 9). Each robot gets its own
        /// SetRobotConnectStatus call -- and so its own broadcast -- rather than one shared
        /// UPDATE across all of them: this is a between-games action (game setup/robot
        /// assignment), not a per-turn one, so the API_DECOMPOSITION_DESIGN.md tempo table (§2)
        /// puts its latency budget in seconds and treats extra hops/round-trips here as cheap,
        /// unlike the per-command tempo GameController's turn-execution path has to protect.</summary>
        public void SetAllConnectStatus(IEnumerable<int> robotIDs, tConnectStatus status)
        {
            foreach (var robotID in robotIDs)
                SetRobotConnectStatus(robotID, status);
        }

        /// <summary>
        /// Creates a RobotScreenUI for the player and starts the touch polling loop.
        /// Cancels any existing polling task for this player first.
        /// </summary>
        private void InitScreenUI(Player player)
        {
            // Cancel any previous polling task
            if (_screenUiCts.TryGetValue(player.ID, out var oldCts))
            {
                oldCts.Cancel();
                oldCts.Dispose();
                _screenUiCts.Remove(player.ID);
            }

            var ui = new RobotScreenUI(player, _dataService, _hubContext);
            player.ScreenUI = ui;

            var cts = new CancellationTokenSource();
            _screenUiCts[player.ID] = cts;

            // Start polling as a background task
            _ = Task.Run(async () =>
            {
                try { await ui.StartPollingAsync(cts.Token); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ScreenUI {player.ID}] Polling task faulted: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Triggers LoadHand on all player ScreenUIs (states 2/3/4 entry).
        /// </summary>
        private void ScreenUiLoadHand(int gameState)
        {
            if (!UseRobotScreen) return;
            foreach (var player in AllPlayers)
            {
                if (player.ScreenUI != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await player.ScreenUI.LoadHand(gameState); }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ScreenUI {player.ID}] LoadHand error: {ex.Message}");
                        }
                    });
                }
                else if (player.isConnected && UseRobotScreen)
                {
                    // Robot connected but ScreenUI not yet created — create it now
                    InitScreenUI(player);
                    _ = Task.Run(async () =>
                    {
                        try { await player.ScreenUI!.LoadHand(gameState); }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ScreenUI {player.ID}] LoadHand error: {ex.Message}");
                        }
                    });
                }
            }
        }

        /// <summary>
        /// Triggers LockAsync on all player ScreenUIs (state 5 entry).
        /// </summary>
        private void ScreenUiLock()
        {
            if (!UseRobotScreen) return;
            foreach (var player in AllPlayers)
            {
                if (player.ScreenUI != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await player.ScreenUI.LockAsync(); }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ScreenUI {player.ID}] LockAsync error: {ex.Message}");
                        }
                    });
                }
            }
        }

        /// <summary>
        /// Triggers RenderIdleAsync on all player ScreenUIs (states 6–11 and 12–16).
        /// </summary>
        private void ScreenUiRenderIdle(int gameState)
        {
            if (!UseRobotScreen) return;
            foreach (var player in AllPlayers)
            {
                if (player.ScreenUI != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await player.ScreenUI.RenderIdleAsync(gameState); }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ScreenUI {player.ID}] RenderIdleAsync error: {ex.Message}");
                        }
                    });
                }
            }
        }

        /// <summary>
        /// Refreshes and re-renders the ScreenUI for one player.
        /// Called after procUpdateCardPlayed fires from DataHub (phone UI tap).
        /// </summary>
        public void RefreshPlayerScreenUI(int playerId)
        {
            if (!UseRobotScreen) return;
            var player = AllPlayers.GetPlayer(playerId);
            if (player?.ScreenUI == null) return;

            _dataService.RefreshPlayerCards(playerId);
            _ = Task.Run(async () =>
            {
                try { await player.ScreenUI.RenderAsync(); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ScreenUI {playerId}] RefreshPlayerScreenUI error: {ex.Message}");
                }
            });
        }

        public bool ConnectToRobot(int playerID)
        {
            Player? thisplayer = AllPlayers.GetPlayer(playerID);
            SetRobotConnectStatus(playerID, tConnectStatus.Connecting);
            thisplayer?.Connect().Wait();
            SetRobotConnectStatus(playerID, thisplayer?.isConnected == true ? tConnectStatus.Connected : tConnectStatus.NotConnected);
            return true;
        }

        /// <summary>Ends the current game: clears IsRunning (iKey 9) so a restart won't try to
        /// reconnect to robots, and disconnects from them now.</summary>
        public void EndGame()
        {
            _dataService.IsRunning = false;
            DisconnectAllRobots();
        }

        /// <summary>Disconnects every robot. Awaits all of them together rather than
        /// fire-and-forget: the previous "_ = DisconnectPlayer(...)" returned before the
        /// disconnect finished, and if DisposeAsync threw (e.g. closing a socket to a robot
        /// that already dropped off WiFi) the exception was never observed and
        /// ConnectStatusID was never written -- this is why "disconnect does not update the
        /// database" could happen with no visible error.</summary>
        public bool DisconnectAllRobots()
        {
            Task.WhenAll(AllPlayers.Select(DisconnectPlayer)).Wait();
            return true;
        }

        public bool DisconnectRobot(int playerID)
        {
            Player? thisplayer = AllPlayers.GetPlayer(playerID);
            if (thisplayer == null)
            {
                // No live Player object for this robot (e.g. never connected this process
                // lifetime) -- there's nothing to dispose, but the row should still say
                // Not Connected rather than silently leaving whatever it said before.
                SetRobotConnectStatus(playerID, tConnectStatus.NotConnected);
                return true;
            }
            DisconnectPlayer(thisplayer).Wait();
            return true;
        }

        /// <summary>Always writes ConnectStatusID, even if closing the sockets throws --
        /// DisposeAsync (unlike Player.Connect/ConnectAsync) has no internal try/catch, so a
        /// robot that already dropped its connection could throw here on close, and the write
        /// below would never run without this try/finally.</summary>
        private async Task DisconnectPlayer(Player player)
        {
            try
            {
                await player.DisposeAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{player.Name}] DisposeAsync error during disconnect: {ex.Message}");
            }
            finally
            {
                SetRobotConnectStatus(player.ID, tConnectStatus.NotConnected);
            }
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