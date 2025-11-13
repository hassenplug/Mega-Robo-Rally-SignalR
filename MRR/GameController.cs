// start app
// reset game state
// start web server
// process active commands
// create command list
// load/edit cards
// edit/load/save boards
// edit database
using MRR.Services;
using MRR.Data;
//using MRR.Data.Entities;

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
            return _dataService.UpdateGameState();
        }

        public async Task ExecuteTurn()
        {
            CreateCommands createCommands = new CreateCommands(_dataService);
            var exeResult = createCommands.ExecuteTurn();
            Console.WriteLine("Execute Turn Result: " + exeResult);
        }

        public async Task ProcessCommands()
        {
            PendingCommands commandProcess = new PendingCommands(_dataService);
            var procResult = commandProcess.ProcessCommands();
            Console.WriteLine("Process Commands Result: " + procResult);
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

    }
}