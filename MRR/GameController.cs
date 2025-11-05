// start app
// reset game state
// start web server
// process active commands
// create command list
// load/edit cards
// edit/load/save boards
// edit database
using MRR.Services;

namespace MRR.Controller
{
    public partial class GameController
    {
        private readonly DataService _dataService;
        public GameController(DataService dataService)
        {
            _dataService = dataService;
            LoadCurrentGame();
        }


        public int RobotsActive => _dataService.RobotsActive;
        public Players AllPlayers => _dataService.AllPlayers;

        public int GameState => _dataService.GameState;

        public int UpdateGameState()
        {
            // Query current game data
            string strSQL = "Select iKey, sKey, iValue, sValue from CurrentGameData;";
            var dt = _dataService.GetQueryResults(strSQL);
            if (dt != null && dt.Rows.Count > 0)
            {
                foreach (System.Data.DataRow row in dt.Rows)
                {
                    var key = Convert.ToInt32(row[0]);
                    var value = Convert.ToInt32(row[2]);
//                    Console.WriteLine("GameState Key:" + key.ToString() + " Value:" + value.ToString());
                    switch (key)
                    {
                        case 1: _dataService.GameType = (GameTypes)value; break;
                        case 2: _dataService.CurrentTurn = value; break;
                        case 3: _dataService.CurrentPhase = value; break;
                        case 6: _dataService.LaserDamage = value; break;
                        case 8: _dataService.RobotsActive = value; break;
                        case 10: _dataService.GameState = value; break;
                        case 16: _dataService.PhaseCount = value; break;
                        case 20:
                            _dataService.BoardID = value;
                            if (row[3] != System.DBNull.Value) _dataService.BoardFileName = row[3].ToString();
                            break;
                        case 22: _dataService.OptionsOnStartup = value; break;
                        case 27: _dataService.RulesVersion = value; break;
                    }
                }
            }
            return _dataService.GameState;
        }

        public async Task ExecuteTurn()
        {
            CreateCommands createCommands = new CreateCommands(_dataService);
            var exeResult = createCommands.ExecuteTurn();
            Console.WriteLine("Execute Turn Result: " + exeResult);
        }

        public void StartGame(int startGameID = 1) // pass board elements and players // find start positions for each player
        {

            _dataService.ExecuteSQL("Update CurrentGameData set iValue = " + startGameID + " where iKey = 26;");  // set game state
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
                    thisplayer.RobotConnection = new Robots.AIMRobot(thisplayer.IPAddress);

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
            var newstate = _dataService.GetIntFromDB("select funcGetNextGameState(); ");
            UpdateGameState();
            Console.WriteLine("next:" + newstate.ToString());
            return "State:" + newstate.ToString();
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
            return "";
        }

        public bool ConnectToAllRobots()
        {
//            Console.WriteLine("Connecting to robots ");

            foreach (Player thisplayer in AllPlayers)
            {
                ConnectToRobot(thisplayer);
            }
            return true;
        }

        public bool ConnectToRobot(int playerID)
        {
            Player? thisplayer = AllPlayers.GetPlayer(playerID);
            ConnectToRobot(thisplayer);
            return true;
        }

        public bool ConnectToRobot(Player player)
        {
            if (player.RobotConnection == null)
            {
                string strSQL = "Select MacID from RobotBases where RobotBaseID=" + player.ID + ";";
                var dt = _dataService.GetQueryResults(strSQL);
                if (dt != null && dt.Rows.Count > 0)
                {
                    foreach (System.Data.DataRow row in dt.Rows)
                    {
                        Console.WriteLine("Connecting to robot " + player.ID.ToString() + " at " + row[0].ToString());
                        player.Connect(row[0].ToString());
                    }
                }
            }
            return true;
        }
    }
}