using System.Threading.Tasks;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Net.Cache;
using System.Collections.ObjectModel; //ObservableCollection

namespace MRR_CLG
{

/*

Reset status = 1 where status = 4
newstatus = -1
Get all commands where status >= 3 and status <= 4
    If status = 3 
        if robot command
            Send command to robot
            NewStatus = 4 (wait) or 5 (do not wait)
    else //status = 4
        // Check for reply from robots
        if received, newstatus = 5

    if newstatus >-1, update status in database

*/

// command id
// command type
// robot id
// command to send
// status id
// command cat id

// check for connection to each active robot
// get list of active commands

    public class PendingCommands : ObservableCollection<PendingCommand>
    {
        private Database DBConn;
        private Players RobotList;

        public PendingCommands(Database ldb)
        {
            DBConn = ldb;

            RobotList = new Players(DBConn); // load robot list from db

        }

        /// <summary>
        /// get current commands that need processed
        /// </summary>
        public int UpdateCommandList()
        {
            // call function to mark ready
            // funcMarkCommandsReady()

            Console.WriteLine("UpdateCommandList");

            this.Clear();

            // if funcMarkCommandsReady == 0, return 0
            int activeCommands = MarkCommandsReady(); // DBConn.GetIntFromDB("Select funcMarkCommandsReady();");  //procGetReadyCommands`;
            //Console.WriteLine("functionMarkCommandsReady - any commands");
            if (activeCommands == 0) return 0;
            Console.WriteLine("functionMarkCommandsReady:" + activeCommands);

            string strSQL = "select CommandID,CommandTypeID,RobotID,BTCommand,StatusID,CommandCatID from viewCommandListActive;";
            
            MySqlConnector.MySqlDataReader reader = DBConn.Exec(strSQL);

            while (reader.Read())
            {
                this.Add(new PendingCommand( (int)reader.GetValue(0), // command id
                                            (int)reader.GetValue(1),  // command type
                                            RobotList.GetPlayer((int)reader.GetValue(2)),  // robot id
                                            (string)reader.GetValue(3),  // bt command
                                            (int)reader.GetValue(4),  // status id
                                            (int)reader.GetValue(5)  // command cat id
                ));
                Console.WriteLine("UpdateCommandList:" + (int)reader.GetValue(0));
            }

            reader.Close();

            return this.Count;
        }

        public bool ProcessCommands()
        {
            //Console.WriteLine("Process Commands State: " + lGame.GameState);
            //if (lGame.GameState != 8) return true; // all commands are processed
            bool stillRunning = true;

            // are there commands that need processing?
            //while(DBConn.GetIntFromDB("select count(*) from viewCommandList where StatusID <6")>0)
            //{
                while (UpdateCommandList() > 0 && stillRunning)
                {
                    Console.WriteLine("Active Commands:" + this.Count());
                    stillRunning = false;
                    foreach(PendingCommand onecommand in this)
                    {
                        stillRunning = stillRunning || ExecuteCommand(onecommand);
                    }
                }
            //}
            Console.WriteLine("Process Commands:Done ");
            return false;
        }

/*
1	Robot wReply	1	0	0
2	Robot No Reply	1	0	0
3	DB	0	1	0
4	PI	0	0	1
5	Node 	0	0	0
6	User Input	0	0	0
7	Connection	1	0	0
				
*/
        public bool ExecuteCommand(PendingCommand onecommand)
        {
            Console.WriteLine("Process Command(" + onecommand.CommandID + ")["+ onecommand.CommandCatID +"]{" + onecommand.CommandType + "}" + onecommand.CommandString);
            switch(onecommand.CommandCatID)
            {
                case 1:
                    if (onecommand.StatusID == 2)
                    {
                        DBConn.Command("select funcProcessCommand(" + onecommand.CommandID + ",4)");
                        // send command to robot here..
                        return true;
                    }
                    return false;
                case 2:
                case 3:
                    DBConn.Command("select funcProcessCommand(" + onecommand.CommandID + ",5)");
                    return true;
                case 6:
                    if (onecommand.StatusID < 4)
                    {
                        DBConn.Command("update Robots set MessageCommandID = " + onecommand.CommandID + " where RobotID = " + onecommand.RobotConnection.ID + "; ");
                        DBConn.Command("update CommandList set StatusID=4 where CommandID = " + onecommand.CommandID + "; ");
                        return true;
                    }
                    return false;

                default:
        
                    DBConn.Command("select funcProcessCommand(" + onecommand.CommandID + ",5)");
                    break;
            }
            // process command here
            // update commandstatus

            return false;
        }

        public int MarkCommandsReady()
        {
            int result;

            // check to see if any others are still incomplete.  If so, exit
            result = DBConn.GetIntFromDB("Select count(*) from CommandList where  StatusID>=2 and StatusID <=4;");
            if (result > 0) return result; 
            
            var resultset = DBConn.GetIntList("select count(CommandID), min(CommandSequence) from CommandList where StatusID=1;");
            
            if (resultset[0] == 0) return 0; //# no commands waiting

            return DBConn.Command("Update CommandList set StatusID=2 where CommandSequence=" + resultset[1] + ";");
            
        }


        // function to wait for reply from robots
        // wait for reply from robots

        // wait for input from users

        // process all commands
        //public string ProcessCommands()
        //{
            // get database state
            // switch & process it

            // start thread to call function
            //Thread CommandThread = new Thread(new ThreadStart(WebSocket.RunWebSocket));
            //CommandThread.Name = "RunningWebSocket"; // +Name;
            //CommandThread.Start();
            //Thread.Sleep(1);

            //worker = new QueueWorker(queue,Game);
            //Thread t = new Thread(new ThreadStart(worker.Work));
            //t.Start();

            //worker = new WebSocketClass(queue,Game);
            //Thread t = new Thread(new ThreadStart(worker.Start));
            //t.Start();

        //    return null;
        //}

    }

    public class PendingCommand  //: IComparable
    {

        public PendingCommand(int pcommandid, int pcommandType, Player pplayer, string pbtcommand, int pstatusid, int pcomcat) 
        {
            CommandID = pcommandid;
            CommandType = pcommandType;
            RobotConnection = pplayer;
            CommandString = pbtcommand;
            StatusID = pstatusid;
            CommandCatID = pcomcat;
        }

        public int CommandID {get;set;}
        public int CommandType {get;set;}
        public Player RobotConnection {get;set;}
        public string CommandString {get;set;}
        public int StatusID {get;set;}
        public int CommandCatID {get;set;} 

        public bool ExecuteCommand()
        {
            return false;
        }

    }

}

/*

		if (cRobotID = 0) then
			update CurrentGameData set iValue = 7, sValue = cDescription where iKey = 10;  # go to wait for input state and set button text
		else
			if (cStatus<4) then
	#			update Robots set MessageCommandID = cParameter, MessageString = cDescription where RobotID = cRobotID;  # go to wait for input state and set button text
				update Robots set MessageCommandID = p_CommandID where RobotID = cRobotID;  
				set p_NewStatus = 4; # In progress; waiting for input
			#else 
				#update Robots set MessageCommandID = null where RobotID = cRobotID;  
			end if;
        end if;
*/