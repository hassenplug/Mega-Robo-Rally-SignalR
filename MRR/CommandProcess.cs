using System.Threading.Tasks;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Net.Cache;
using MRR.Services;
using Microsoft.AspNetCore.SignalR;
using MRR.Hubs;
using MRR.Data;
using MRR.Data.Entities;
using Microsoft.EntityFrameworkCore;
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

    public class PendingCommands : IDisposable
    {
        private readonly DataService _dataService;
        private readonly IHubContext<DataHub> _hubContext;
        private MRRDbContext? _dbContext;

        public PendingCommands(DataService dataService, IHubContext<DataHub> hubContext)
        {
            _dataService = dataService;
            _hubContext = hubContext;
            _dbContext = _dataService.CreateDbContext();
        }

        //private Players RobotList => _dataService.AllPlayers;
        
        private MRRDbContext Db
        {
            get
            {
                if (_dbContext == null)
                {
                    _dbContext = _dataService.CreateDbContext();
                }
                return _dbContext;
            }
        }

        public void Dispose()
        {
            if (_dbContext != null)
            {
                _dbContext.Dispose();
                _dbContext = null;
            }
        }

        private List<PendingCommandEntity> GetActiveCommandList()
        {
            using var db = _dataService.CreateDbContext();
            return db.PendingCommands.Where(c => c.StatusID >= 2 && c.StatusID <= 4).ToList();
        }

        public bool ProcessCommands() // make sure state = 7 or 8
        {
            bool stillRunning = true;

            while (MarkCommandsReady() > 0 && stillRunning)
            {
                var active = GetActiveCommandList();
                while (active.Count > 0 && stillRunning)
                {
                    //Console.WriteLine("Active Commands:" + active.Count);
                    stillRunning = false;
                    foreach (PendingCommandEntity onecommand in active)
                    {
                        //Console.WriteLine("Processing Command ID: " + onecommand.ToString());
                        stillRunning = stillRunning || ProcessCommand(onecommand);
                    }
                    // refresh active set for the next inner loop iteration
                    active = GetActiveCommandList();
                    // send updates to clients
                    var allDataJson = _dataService.GetAllDataJson();
                    // Notify connected SignalR clients using the hub context from background thread
                    _hubContext.Clients.All.SendAsync("AllDataUpdate", allDataJson).GetAwaiter().GetResult();
                    //_hubContext .Clients.All.SendAsync("AllDataUpdate", allDataJson).GetAwaiter().GetResult();

                }

                //Console.WriteLine("Process Commands:Done ");
                // update to next state (post execute state)
            }
            return false;
        }

        public int MarkCommandsReady()
        {
            using (var db = _dataService.CreateDbContext())
            {
                var result = db.PendingCommands.Count(c => c.StatusID >= 2 && c.StatusID <= 4);
                //Console.WriteLine("Pending Commands to process: " + result.ToString());
                if (result > 0) return result;

                // Find the minimum command sequence that has pending commands
                var minSequence = db.PendingCommands
                    .Where(c => c.StatusID == 1)
                    .Min(c => (int?)c.CommandSequence) ?? -1;

                //Console.WriteLine("Next Command Sequence to process: " + minSequence.ToString());

                if (minSequence == -1) return 0; // no commands waiting

                // Update all commands with the minimum sequence to ready status
                var affected = db.PendingCommands
                    .Where(c => c.CommandSequence == minSequence)
                    .ExecuteUpdate(s => s
                        .SetProperty(b => b.StatusID, 2));
                
                //Console.WriteLine("Marked Commands as Ready: " + affected.ToString());

                return affected;
            }
        }

        /*
        1	Robot wReply	1	0	0
        2	Robot No Reply	1	0	0
        3	DB	0	1	0
        4	PI	0	0	1
        5	Node 	0	0	0
        6	User Input	0	0	0
        7	Connection	1	0	0

# CommandStatus
#1-Waiting
#2-Ready (should execute now)
#3-Script Command (waiting for Python)
#4-In Progress (python is running)
#5-Script Complete (now update position)
#6-Command Complete

# - Game States
#7 - Run Phase (wait for input)
#8 - Running Phase (in process)

        */
//        public async Task<bool> ExecuteCommand(PendingCommandEntity onecommand)
        public bool ProcessCommand(PendingCommandEntity onecommand)
        {
            //Console.WriteLine($"Process Command({onecommand.CommandID})[{onecommand.CommandCatID}]{{{onecommand.CommandTypeID}}}{onecommand.Parameter},{onecommand.ParameterB}");

            using (var db = _dataService.CreateDbContext())
            {
                var command = db.PendingCommands.Find(onecommand.CommandID);
                if (command == null) return false;

                switch (onecommand.CommandCatID)
                {
                    case 1: // Robot with Reply
                    case 2: // Robot No Reply
                        var robot = command.RobotPlayer;
                        if (robot == null )
                        {
                            Console.WriteLine($"Robot not found for Command({onecommand.CommandID})[{onecommand.CommandCatID}]{{{onecommand.CommandTypeID}}}-{onecommand.Parameter},{onecommand.ParameterB}:{onecommand.Description}");
                            command.StatusID = 6; // command complete
                            db.SaveChanges();
                            return true;
                        }

                        if (robot.RobotConnection == null)
                        {
                            Console.WriteLine($"Robot not connected for Command({onecommand.CommandID})[{onecommand.CommandCatID}]{{{onecommand.CommandTypeID}}}-{onecommand.Parameter},{onecommand.ParameterB}:{onecommand.Description}");
                            command.StatusID = _dataService.GetIntFromDB("select funcProcessCommand(" + onecommand.CommandID + ",5);");
                            db.SaveChanges();
                            return true;
                        }

                        if (onecommand.StatusID == 2)
                        {
                            Console.WriteLine($"Robot Command    ({onecommand.CommandID})[{onecommand.CommandCatID}]{{{onecommand.CommandTypeID}}}-{onecommand.Parameter},{onecommand.ParameterB}:{onecommand.Description}");
                            command.StatusID = 3; // executing
                            robot.RobotConnection.SendRobotCommandAsync(onecommand).Wait();
                            if (onecommand.CommandCatID == 2)
                            {
                                // don't wait for reply
                                command.StatusID = 4; // not waiting for reply
                            }
                            //command.StatusID = onecommand.CommandCatID == 1 ? 5 : 6; // no connection
                            // set the state based on type 1/2
                            db.SaveChanges();
                            return true;
                        }

                        if (onecommand.StatusID == 3)
                        {
                            // check here to make sure the robot has replied
                            //Console.WriteLine($"Waiting for Robot Reply ({onecommand.CommandID})[{onecommand.CommandCatID}]{{{onecommand.CommandTypeID}}}-{onecommand.Parameter},{onecommand.ParameterB}:{onecommand.Description}");
                            // wait for robot to finish moving
                            // check robot status
                            robot.RobotConnection.CheckMovingStatus().Wait();
                            if (!robot.RobotConnection.isMoving)
                            {
                                Console.WriteLine($"Robot Command Done({onecommand.CommandID})[{onecommand.CommandCatID}]{{{onecommand.CommandTypeID}}}-{onecommand.Parameter},{onecommand.ParameterB}:{onecommand.Description}");
                                // get next state from DB
                                //command.StatusID = _dataService.GetIntFromDB("select funcProcessCommand(" + onecommand.CommandID + ",-1);");
                                command.StatusID = 4;
                                //db.SaveChanges();
                                //return true;
                            }
                        }

                        if (onecommand.StatusID == 4)
                        {
                            // no reply expected
                            //command.StatusID = 5; // command complete
                            command.StatusID = _dataService.GetIntFromDB("select funcProcessCommand(" + onecommand.CommandID + ",5);");
                            //command.StatusID = 6;
                            db.SaveChanges();
                        }
                        return true;


                    case 3: // DB
                        Console.WriteLine($"Database Command ({onecommand.CommandID})[{onecommand.CommandCatID}]{{{onecommand.CommandTypeID}}}-{onecommand.Parameter},{onecommand.ParameterB}:{onecommand.Description}");
                        // call database procedure here..
//                        command.StatusID = _dataService.ExecuteSQL("call funcProcessCommand(" + onecommand.CommandID + ",-1);");
                        command.StatusID = _dataService.GetIntFromDB("select funcProcessCommand(" + onecommand.CommandID + ",-1);");
                        //command.StatusID = 5;
                        db.SaveChanges();
                        return true;

                    case 6: // User Input
                        if (onecommand.StatusID < 4)
                        {
                            Console.WriteLine($"User Input       ({onecommand.CommandID})[{onecommand.CommandCatID}]{{{onecommand.CommandTypeID}}}-{onecommand.Parameter},{onecommand.ParameterB}:{onecommand.Description}");
                            var robot6 = db.Robots.FirstOrDefault(r => r.RobotID == onecommand.RobotID);

                            if (robot6 != null)
                            {
                                robot6.MessageCommandID = onecommand.CommandID;
                                command.StatusID = 4;
                                db.SaveChanges();
                                return false; // wait for user input
                            }
                        }
                        return false;

                    default:
                        Console.WriteLine($"Not processed here({onecommand.CommandID})[{onecommand.CommandCatID}]{{{onecommand.CommandTypeID}}}-{onecommand.Parameter},{onecommand.ParameterB}:{onecommand.Description}");
                        command.StatusID = _dataService.GetIntFromDB("select funcProcessCommand(" + onecommand.CommandID + ",-1);");
                        db.SaveChanges();
                        break;
                }
            }

            return false;
        }
    }
}
