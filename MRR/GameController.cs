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


        public int GameState { get; set; }
        public int CurrentTurn { get; set; }
        public int CurrentPhase { get; set; }
        public int PhaseCount { get; set; }
        public int RulesVersion { get; set; }
        public int LaserDamage { get; set; }
        public GameTypes GameType { get; set; }
        public int BoardID { get; set; }
        public int OptionsOnStartup { get; set; }
        public string? BoardFileName { get; set; }
        public int RobotsActive { get; set; }
        public Players AllPlayers { get; set; } = new Players();

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
                    switch (key)
                    {
                        case 1: GameType = (GameTypes)value; break;
                        case 2: CurrentTurn = value; break;
                        case 3: CurrentPhase = value; break;
                        case 6: LaserDamage = value; break;
                        case 8: RobotsActive = value; break;
                        case 10: GameState = value; break;
                        case 16: PhaseCount = value; break;
                        case 20:
                            BoardID = value;
                            if (row[3] != System.DBNull.Value) BoardFileName = row[3].ToString();
                            break;
                        case 22: OptionsOnStartup = value; break;
                        case 27: RulesVersion = value; break;
                    }
                }
            }
            return GameState;
        }

        public void StartGame(int startGameID = 1) // pass board elements and players // find start positions for each player
        {

            _dataService.ExecuteSQL("Update CurrentGameData set iValue = " + startGameID + " where iKey = 26;");  // set game state
            _dataService.ExecuteSQL("Update CurrentGameData set iValue = 0 where iKey = 10;");  // set state to 0

            NextState();

            BoardElementCollection g_BoardElements = _dataService.BoardLoadFromDB(BoardID);

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
                    if (OptionsOnStartup > 0)
                    {
                        for (int opt = 0; opt < OptionsOnStartup; opt++)
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
            ConnectToAllRobots();
            UpdateGameState();
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