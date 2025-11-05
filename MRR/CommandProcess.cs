using System.Threading.Tasks;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Net.Cache;
using MRR.Services;
using System.Data;

namespace MRR
{

// command id
// command type
// robot id
// command to send
// status id
// command cat id

// check for connection to each active robot
// get list of active commands

    public class PendingCommands : List<PendingCommand>
    {
        private readonly DataService _dataService;

        public PendingCommands(DataService dataService)
        {
            _dataService = dataService;

            //RobotList = new Players(); // load robot list from db
        }


        private Players RobotList => _dataService.AllPlayers;

//CREATE  OR REPLACE VIEW `viewCommandListActive` AS
//select * from CommandList where StatusID>=2 and StatusID <=4;

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
            int activeCommands = MarkCommandsReady(); //
            //Console.WriteLine("functionMarkCommandsReady - any commands");
            if (activeCommands == 0) return 0;
            Console.WriteLine("functionMarkCommandsReady:" + activeCommands);

            string strSQL = "select CommandID,CommandTypeID,RobotID,BTCommand,StatusID,CommandCatID from viewCommandListActive;";

            var commanddata = _dataService.GetQueryResults(strSQL);
            foreach (DataRow command in commanddata.Rows)
            {
                this.Add(new PendingCommand(
                    (int)command["CommandID"],
                    (int)command["CommandTypeID"],
                    RobotList.GetPlayer((int)command["RobotID"]),
                    (string)command["BTCommand"],
                    (int)command["StatusID"],
                    (int)command["CommandCatID"]
                ));
                Console.WriteLine("UpdateCommandList:" + (int)command["CommandID"]);
            }
            
            return this.Count;
        }

        public bool ProcessCommands()
        {
            //Console.WriteLine("Process Commands State: " + lGame.GameState);
            //if (lGame.GameState != 8) return true; // all commands are processed
            bool stillRunning = true;

            // are there commands that need processing?
          
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
                        _dataService.ExecuteSQL("select funcProcessCommand(" + onecommand.CommandID + ",4)");
                        // send command to robot here..
                        return true;
                    }
                    return false;
                case 2:
                case 3:
                    _dataService.ExecuteSQL("select funcProcessCommand(" + onecommand.CommandID + ",5)");
                    return true;
                case 6:
                    if (onecommand.StatusID < 4)
                    {
                        _dataService.ExecuteSQL("update Robots set MessageCommandID = " + onecommand.CommandID + " where RobotID = " + onecommand.RobotConnection.ID + "; ");
                        _dataService.ExecuteSQL("update CommandList set StatusID=4 where CommandID = " + onecommand.CommandID + "; ");
                        return true;
                    }
                    return false;

                default:
        
                    _dataService.ExecuteSQL("select funcProcessCommand(" + onecommand.CommandID + ",5)");
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
            result = _dataService.GetIntFromDB("Select count(*) from CommandList where  StatusID>=2 and StatusID <=4;");
            if (result > 0) return result; 
            
            var resultset = _dataService.GetIntList("select count(CommandID), min(CommandSequence) from CommandList where StatusID=1;");
            
            if (resultset[0] == 0) return 0; //# no commands waiting

            return _dataService.ExecuteSQL("Update CommandList set StatusID=2 where CommandSequence=" + resultset[1] + ";");
            
        }


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
