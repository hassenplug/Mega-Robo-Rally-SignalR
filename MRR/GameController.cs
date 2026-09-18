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
using System.Data;
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

                    // HistoryRobotTurns: snapshot every robot's current (i.e. starting-this-turn)
                    // position + programmed cards straight from Robots, before the planner
                    // simulates the turn and moves them. Master-side, like PersistCommands/
                    // RetireSpamCards below -- the planner itself stays DB-free.
                    _dataService.SaveToHistory();

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
                        bool aborted;
                        try
                        {
                            _pendingCommands.ProcessCommands();
                            aborted = _pendingCommands.Aborted;
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

                        // CreateCommands always appends a SetGameState command at the end of
                        // the turn's command list, which lands GameState off 8 (e.g. back to
                        // 2) via ProcessDbCommand directly -- bypassing NextState()'s cascade.
                        // Drive that cascade here so the turn rolls into programming for the
                        // next one on its own, instead of leaving the GM to click Next State.
                        // An aborted turn deliberately leaves the state machine alone (see
                        // AbandonTurn's remarks) -- what happens next is the GM's call.
                        if (!aborted) NextState();
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
            _dataService.ExecuteSQL(
                "UPDATE CurrentGameData Set iValue  = " + gameDataID + " " +
                " where CurrentGameData.sKey = 'GameDataID'");

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
                "    WHEN 'Players'      THEN GameData.StartPositions " +
                "    ELSE CurrentGameData.iValue " +
                "  END;");
            //_dataService.ExecuteSQL("call procResetGame();");

            // The raw SQL above bypasses GameStateStore's cached properties (BoardID,
            // OptionsOnStartup, etc.), so without this, StartGame() -- called right after via
            // NextState()'s state-0 case -- reads stale values and builds the Robots table
            // against the previous board/option count. Refresh now so the very next StartGame()
            // call sees the new GameData, not just the one after that.
            _dataService.UpdateGameState();
        }

        public void StartGame() // pass board elements and players // find start positions for each player
        {
            // No explicit "now running" write needed: GameState is about to move off 25 (the
            // "not running" sentinel) via the raw SQL below and then case 0's SetGameState(2)
            // in NextState(), so GameState != 25 holds by the time this state finishes.

            _dataService.ExecuteSQL("Update CurrentGameData set iValue=0 where sKey='GameState';");
            _dataService.ExecuteSQL("Update CurrentGameData set iValue=0 where sKey='Turn';");
            _dataService.ExecuteSQL("Update CurrentGameData set iValue=0 where sKey='Phase';");
            _dataService.ExecuteSQL("Delete from MoveCards;");
            _dataService.ExecuteSQL("Delete from CommandList;");        
            _dataService.ExecuteSQL("Delete from RobotOptions;");
            _dataService.ExecuteSQL("Delete from StatusLEDs;");
            _dataService.ExecuteSQL("Delete from Robots;");

            // Bypasses OperatorData entirely (install/todo.md "Operator Data Setup", decided
            // 2026-09-17): one placeholder row per physical robot base, RobotID==RobotBaseID --
            // the same 1:1 convention the old OperatorData-driven insert relied on
            // (RobotBases rbase on od.RobotID = rbase.RobotBaseID). Position/direction come from
            // the board's own PlayerStart square for that base, same lookup the removed
            // LoadPlayersIntoGame() did per-player -- it just happens per-base now instead of
            // per-claimed-seat, since which square a base starts on doesn't depend on who ends up
            // piloting it. RobotBodyID is left unset (InsertPlaceholderRobot uses NULL, not the
            // column's schema default of 0, which has no matching RobotBodies row and would fail
            // the FK) until a player claims the seat -- see DataService.SelectSeat, called from
            // GameState==1 (below in NextState()).
            BoardElementCollection g_BoardElements = _dataService.BoardLoadFromDB(_dataService.BoardID);
            IEnumerable<BoardElement> StartList = g_BoardElements.BoardElements.Where(be => be.ActionList.Count(al => al.SquareAction == SquareAction.PlayerStart) > 0);

            var bases = _dataService.GetQueryResults("Select RobotBaseID, IPAddress from RobotBases order by RobotBaseID");
            foreach (DataRow baseRow in bases.Rows)
            {
                int baseId = Convert.ToInt32(baseRow["RobotBaseID"]);
                string ip = baseRow["IPAddress"] == DBNull.Value ? "" : (string)baseRow["IPAddress"];

                // Use Any(...) to avoid calling First(...) inside the predicate which can throw if no matching action exists.
                BoardElement? thisSquare = StartList.FirstOrDefault(be => be.ActionList.Any(al => al.SquareAction == SquareAction.PlayerStart && al.Parameter == baseId));
                if (thisSquare == null) continue; // no start square for this base on the current board -- skip it, same as the old code's "remove player from game" no-op

                _dataService.InsertPlaceholderRobot(baseId, ip, thisSquare.BoardRow, thisSquare.BoardCol, (int)thisSquare.Rotation);
            }

            //_dataService.GameNewAddCards();
            //_dataService.UpdatePlayerPriority(null, 1);

            _dataService.GetAllPlayers(true); // force refresh so the new rows are visible below
            if (_dataService.RobotsActive != 0) ConnectToAllRobots();
            _dataService.RefreshRobotDenormalizedFields(); // StatusColor/PlayerStatus/etc. for the new rows
            //LoadCurrentGame();
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
                            SetGameState(1);
                            break;
                        case 1: // waiting for every seat to be claimed (install/todo.md "Operator Data Setup")
                            int stillOpen = _dataService.GetIntFromDB("Select Count(*) from Robots where Status <> 1");
                            int totalSeats = _dataService.GetIntFromDB("Select Count(*) from Robots");
                            if (totalSeats > 0 && stillOpen == 0)
                            {
                                // Every placeholder row (StartGame) has been claimed via
                                // DataService.SelectSeat -- positions were already set at
                                // placeholder-creation time, so what's left is what
                                // LoadPlayersIntoGame() used to do once its bulk OperatorData
                                // insert finished: deal starting options, deal the deck, set
                                // initial turn order.
                                if (_dataService.OptionsOnStartup > 0)
                                {
                                    foreach (int robotId in _dataService.GetIntList("Select RobotID from Robots"))
                                        for (int opt = 0; opt < _dataService.OptionsOnStartup; opt++)
                                            _dataService.DealOptionToRobot(robotId);
                                }
                                _dataService.GameNewAddCards();
                                _dataService.UpdatePlayerPriority(null, 1);
                                _dataService.GetAllPlayers(true);
                                SetGameState(2);
                            }
                            // else: still waiting on GameConfig.PlayerToSelect -- no state change here, loop exits
                            break;
                        case 2: // Next Turn
                            _dataService.ResetPlayers();
                            _dataService.MoveCardsShuffleAndDeal();
                            //_dataService.ExecuteSQL("call procUpdateRobotCards();");
                            _dataService.ExecuteSQL("update CurrentGameData set iValue=iValue+1 where iKey=2;"); // next turn
                            // Raw SQL above bypasses the Turn property, so _dataService.Turn is
                            // still the old value -- refresh it (DataService.UpdateGameState(),
                            // not the broadcast-only GameController.UpdateGameState() below) so
                            // the broadcast at the end of this loop shows the right turn number
                            // instead of stale data for the whole programming phase.
                            _dataService.UpdateGameState();
                            //foreach (var p in AllPlayers) p.UpdateStatusLEDs();
                            ScreenUiLoadHand(2);
                            SetGameState(3);
                            break;
                        case 3: // Verify Position
                            ScreenUiLoadHand(3);
                            SetAllRobotLights(true);
                            SetGameState(4);
                            break;
                        case 4: // still programming
                            int playersProgramming = _dataService.GetIntFromDB("Select Count(*) from  Robots where (Status <> 4 and Status < 9)");
                            if (playersProgramming == 0 && _dataService.AllRobotDirectionsChosen())
                            {
                                _dataService.LockAllRobotDirections();
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
                            SetAllRobotLights(true);
                            ScreenUiRenderIdle(6);
                            Task.Run(async () => await ExecuteTurn());
                            break;
                        case 7: // executing turn
                            ScreenUiRenderIdle(7);
                            SetAllRobotLights(false);
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
                            SetAllRobotLights(true);
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

            if (RobotsActive != 0 && _dataService.GameState != 25)
            {
                ConnectToAllRobots();
            }

            // reset commands in process -- routed through the live PendingCommands instance
            // when one exists (same reason as ClearPausedCommands: otherwise its next
            // SaveChanges() on a stale in-memory copy would silently revert this reset), else a
            // direct DB update since there's no in-memory list to go stale.
            lock (_processCommandsLock)
            {
                if (_pendingCommands != null)
                    _pendingCommands.ResetStuckCommands();
                else
                    _dataService.ExecuteSQL("Update CommandList set StatusID = 2 where StatusID=4 or StatusID=3;");
            }
            return "";
        }

        /// <summary>Closes and reopens every robot's connection together -- RobotConnection's
        /// ConnectAsync is private and only ever runs from its own constructor (see
        /// RobotConnections.ReconnectAll), so "connect" always means "replace with a fresh,
        /// self-connecting instance," never "call back into the existing one." This is also
        /// what LoadCurrentGame() relies on at the end of StartGame() to guarantee no robot
        /// carries a stale socket, LED state, or LCD screen into a new game. Awaits every
        /// connect attempt before returning, then force-refreshes AllPlayers so each
        /// Player.Connection re-attaches to its fresh RobotConnection.</summary>
        public bool ConnectToAllRobots()
        {
            var ids = AllPlayers.Select(p => p.ID).ToList();
            foreach (var id in ids) SetRobotConnectStatus(id, tPlayerStatus.Connecting);

            var connections = _dataService.ReconnectAllRobots(ids);
            Task.WhenAll(connections.Select(c => c.Ready)).Wait();

            _dataService.GetAllPlayers(true); // re-attach Player.Connection to each fresh RobotConnection

            foreach (var id in ids)
            {
                var player = AllPlayers.GetPlayer(id);
                bool connected = player?.isConnected == true;
                SetRobotConnectStatus(id, connected ? tPlayerStatus.RobotConnected : tPlayerStatus.NotConnected);

                if (UseRobotScreen && connected)
                {
                    InitScreenUI(player!);
                }
            }
            return true;
        }

        /// <summary>Delegates the write to DataService.SetRobotConnectStatus (Robots.ConnectStatusID/
        /// ConnectStatusColor/ConnectStatusDesc only -- never the gameplay Status/StatusColor
        /// columns), then broadcasts immediately so the Connecting -> Connected/NotConnected
        /// transition is visible live rather than only on the next unrelated broadcast (see
        /// install/todo.md Section 8). CommandProcess.cs calls the DataService method directly
        /// (no GameController reference there) when it detects a robot dropped its connection
        /// mid-turn -- that path relies on the turn loop's own PublishSnapshot for the broadcast
        /// instead of duplicating one here.</summary>
        private void SetRobotConnectStatus(int robotID, tPlayerStatus status)
        {
            _dataService.SetRobotConnectStatus(robotID, status);
            UpdateGameState();
        }

        /// <summary>Batch form of SetRobotConnectStatus for the connection screen's "Connect
        /// All"/"Disconnect All"/"Search" (install/todo.md Section 8). Each robot gets its own
        /// SetRobotConnectStatus call -- and so its own broadcast -- rather than one shared
        /// UPDATE across all of them: this is a between-games action (game setup/robot
        /// assignment), not a per-turn one, so the API_DECOMPOSITION_DESIGN.md tempo table (§2)
        /// puts its latency budget in seconds and treats extra hops/round-trips here as cheap,
        /// unlike the per-command tempo GameController's turn-execution path has to protect.</summary>
        public void SetAllConnectStatus(IEnumerable<int> robotIDs, tPlayerStatus status)
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
        /// Turns every connected robot's LEDs on (in its own color) or off. Used to mark two
        /// phases visually: on when programming starts (states 3/15 entering state 4) and again
        /// at execute-turn (state 6 entry), off once execute-turn finishes (state 7 exit). Note
        /// individual robots may also flip their own LED off earlier, once that player finishes
        /// programming -- see DataService.Cards.cs.
        /// </summary>
        private void SetAllRobotLights(bool on)
        {
            foreach (var player in AllPlayers)
            {
                _ = Task.Run(async () =>
                {
                    try { await player.SetLightsAsync(on); }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{player.ID}] SetLightsAsync error: {ex.Message}");
                    }
                });
            }
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

            _ = Task.Run(async () =>
            {
                try { await player.ScreenUI.RenderAsync(); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ScreenUI {playerId}] RefreshPlayerScreenUI error: {ex.Message}");
                }
            });
        }

        /// <summary>Closes this robot's current connection (if any) and opens a fresh one --
        /// see ConnectToAllRobots for why reconnecting always means replacing the
        /// RobotConnection instance rather than calling back into it.</summary>
        public bool ConnectToRobot(int playerID)
        {
            SetRobotConnectStatus(playerID, tPlayerStatus.Connecting);
            var connection = _dataService.ReconnectRobot(playerID);
            connection.Ready.Wait();
            _dataService.GetAllPlayers(true); // re-attach Player.Connection to the fresh RobotConnection
            var thisplayer = AllPlayers.GetPlayer(playerID);
            SetRobotConnectStatus(playerID, thisplayer?.isConnected == true ? tPlayerStatus.RobotConnected : tPlayerStatus.NotConnected);
            return true;
        }

        /// <summary>Ends the current game: sets GameState=25 ("not running", was a separate
        /// IsRunning flag) so a restart won't try to reconnect to robots, and disconnects from
        /// them now.</summary>
        public void EndGame()
        {
            _dataService.GameState = 25;
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
                SetRobotConnectStatus(playerID, tPlayerStatus.NotConnected);
                return true;
            }
            DisconnectPlayer(thisplayer).Wait();
            return true;
        }

        /// <summary>Always writes ConnectStatusID, even if closing the sockets throws --
        /// DisposeAsync has no internal try/catch, so a robot that already dropped its
        /// connection could throw here on close, and the write below would never run without
        /// this try/finally.</summary>
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
                SetRobotConnectStatus(player.ID, tPlayerStatus.NotConnected);
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