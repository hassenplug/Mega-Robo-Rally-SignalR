// start app
// reset game state
// start web server
// process active commands
// create command list
// load/edit cards
// edit/load/save boards
// edit database
using MRR.Services;

namespace MRR
{
    public partial class GameController
    {
        private readonly DataService _dataService;

        public GameController(DataService dataService)
        {
            _dataService = dataService;
            // Communication expects a DataService now; create and assign
            Comm = new Communication(_dataService);
        }

        public Communication Comm { get; set; }


        public void SystemStartup()
        {
            GameState = UpdateGameState();

            // reset game state
            _dataService.ResetGameState();

            // start web server
            Comm.StartServer();

        }

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
                    switch (key)
                    {
                        case 10: GameState = Convert.ToInt32(row[2]); break;
                        case 2: CurrentTurn = Convert.ToInt32(row[2]); break;
                        case 16: PhaseCount = Convert.ToInt32(row[2]); break;
                        case 27: RulesVersion = Convert.ToInt32(row[2]); break;
                        case 20:
                            BoardID = Convert.ToInt32(row[2]);
                            if (row[3] != System.DBNull.Value) BoardFileName = row[3].ToString();
                            break;
                        case 6: LaserDamage = Convert.ToInt32(row[2]); break;
                        case 1: GameType = (GameTypes)Convert.ToInt32(row[2]); break;
                        case 22: OptionsOnStartup = Convert.ToInt32(row[2]); break;
                    }
                }
            }
            return GameState;
        }
        public int GameState { get; set; }

        public int CurrentTurn { get; set; }
        public int PhaseCount { get; set; }
        public int RulesVersion { get; set; }
        public int LaserDamage { get; set; }
        public GameTypes GameType { get; set; }
        public int BoardID { get; set; }
        public int OptionsOnStartup { get; set; }
        public string BoardFileName { get; set; }

        public void SetupGame() // pass board elements and players
        {

            BoardElementCollection g_BoardElements = _dataService.BoardLoadFromDB(BoardID);

            IEnumerable<BoardElement> StartList = g_BoardElements.BoardElements.Where(be => be.ActionList.Count(al => al.SquareAction == SquareAction.PlayerStart) > 0);

            int robotCount = 0;

            Players lAllPlayers = new Players();

            foreach (Player thisplayer in lAllPlayers)
            {
                // set current location to next starting point...
                BoardElement thisSquare = StartList.FirstOrDefault(be => be.ActionList.First(al => al.SquareAction == SquareAction.PlayerStart).Parameter == thisplayer.ID);
                if (thisSquare != null)
                {
                    int pRow = thisSquare.BoardRow;
                    int pCol = thisSquare.BoardCol;
                    int pDir = (int)thisSquare.Rotation;

                    _dataService.ExecuteSQL("Update Robots set CurrentPosRow=" + pRow + ", CurrentPosCol=" + pCol + ",CurrentPosDir=" + pDir + ",ArchivePosRow=" + pRow + ",ArchivePosCol=" + pCol + ",ArchivePosDir=" + pDir + "  where RobotID=" + thisplayer.ID + ";");
                    // add "connect" command, here

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


            //SendGameMessage(2,"Start for " + robotCount.ToString() + " robots");
        }




    }
}
