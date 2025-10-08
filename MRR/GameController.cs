// start app
// reset game state
// start web server
// process active commands
// create command list
// load/edit cards
// edit/load/save boards
// edit database

namespace MRR_CLG
{
    public partial class GameController 
    {
        public GameController()
        {
            DBConn = new Database();
            Comm = new Communication(DBConn);
        }

        public Database DBConn {get;set;}
        public Communication Comm {get;set;}


        public void SystemStartup()
        {
            GameState = UpdateGameState();

            // reset game state
            DBConn.ResetGameState();

            // start web server
            Comm.StartServer();

        }

        public int UpdateGameState()
        {
            if (DBConn.Conn.State == System.Data.ConnectionState.Open)
            {
                string strSQL = "Select iKey, sKey, iValue, sValue from CurrentGameData;";
                MySqlConnector.MySqlDataReader reader = DBConn.Exec(strSQL);
                while (reader.Read())
                {
                    //Console.WriteLine("key:" + reader[1]);
                    switch ((int)reader[0])
                    {
                        case 10: GameState = (int)reader[2];break;
                        case 2: CurrentTurn = (int)reader[2];break;
                        case 16: PhaseCount = (int)reader[2];break;
                        //case 24: AutoExecute = (int)reader[2];break;
                        case 27: RulesVersion = (int)reader[2];break;
                        case 20: BoardID = (int)reader[2]; 
                            if (reader[3] != System.DBNull.Value) BoardFileName =  (string)reader[3];
                            break;

                        case  6: LaserDamage = (int)reader[2];break;
                        case  1: GameType = (GameTypes)reader[2];break;
                        case 22: OptionsOnStartup = (int)reader[2];break;
                    
                    }
                }
                reader.Close();
            }
            return GameState;
        }
        public int GameState { get; set; }

        public int CurrentTurn      {get;set;}
        public int PhaseCount       {get;set;}
        public int RulesVersion     {get;set;}
        public int LaserDamage      {get;set;}
        public GameTypes GameType   {get;set;}
        public int BoardID  {get;set;}
        public int OptionsOnStartup {get;set;}
        public string BoardFileName {get;set;}

        public void SetupGame() // pass board elements and players
        {

            BoardElementCollection g_BoardElements = DBConn.BoardLoadFromDB(BoardID);

            IEnumerable<BoardElement> StartList = g_BoardElements.BoardElements.Where(be => be.ActionList.Count(al => al.SquareAction == SquareAction.PlayerStart) > 0);

            int robotCount = 0;

            Players lAllPlayers = new Players(DBConn);

            foreach (Player thisplayer in lAllPlayers)
            {
                // set current location to next starting point...
                BoardElement thisSquare = StartList.FirstOrDefault(be => be.ActionList.First(al => al.SquareAction == SquareAction.PlayerStart).Parameter == thisplayer.ID);
                if (thisSquare != null)
                {
                    int pRow = thisSquare.BoardRow;
                    int pCol = thisSquare.BoardCol;
                    int pDir = (int)thisSquare.Rotation;

                    DBConn.Command("Update Robots set CurrentPosRow=" + pRow + ", CurrentPosCol=" + pCol + ",CurrentPosDir=" + pDir + ",ArchivePosRow=" + pRow + ",ArchivePosCol=" + pCol + ",ArchivePosDir=" + pDir + "  where RobotID=" + thisplayer.ID + ";");
                    // add "connect" command, here

                    //DBConn.Command("call procRobotConnectionStatus(" + thisplayer.ID + ",70);");

                    // insert options here...
                    if (OptionsOnStartup > 0)
                    {
                        for (int opt = 0; opt < OptionsOnStartup; opt++)
                        {
                            DBConn.Command("call procDealOptionToRobot(" + thisplayer.ID + ");");
                        }
                    }

                    robotCount++;
                }
                else
                {
                    // remove player from game
                    DBConn.Command("delete from Robots where RobotID=" + thisplayer.ID + ";");
                }

            }


            //SendGameMessage(2,"Start for " + robotCount.ToString() + " robots");
        }




    }
}
