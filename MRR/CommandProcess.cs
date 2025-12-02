using System.Threading.Tasks;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Net.Cache;
using MRR.Services;
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
        private MRRDbContext? _dbContext;

        public PendingCommands(DataService dataService)
        {
            _dataService = dataService;
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
                        if (onecommand.StatusID == 2)
                        {
                            Console.WriteLine($"Robot Command    ({onecommand.CommandID})[{onecommand.CommandCatID}]{{{onecommand.CommandTypeID}}}-{onecommand.Parameter},{onecommand.ParameterB}:{onecommand.Description}");
                            command.StatusID = 4; // executing
                            db.SaveChanges();
                            var robot = command.RobotPlayer;
                            if (robot != null)
                            {
                                if (robot.RobotConnection != null)
                                {
                                    robot.RobotConnection.SendRobotCommandAsync(onecommand).Wait();
                                    if (onecommand.CommandCatID == 1)
                                    {
                                        // wait for reply
                                        command.StatusID = 4; // waiting for reply
                                        db.SaveChanges();
                                    }
                                    else
                                    {
                                        // no reply expected
                                        //command.StatusID = 5; // command complete
                                        command.StatusID = _dataService.GetIntFromDB("select funcProcessCommand(" + onecommand.CommandID + ",-1);");
                                        command.StatusID = 6;
                                        db.SaveChanges();
                                    }

                                }
                                else
                                {
                                    command.StatusID = 6; // onecommand.CommandCatID == 1 ? 5 : 6; // no connection
                                }
                            }
                            else
                            {
                                //command.StatusID = onecommand.CommandCatID == 1 ? 5 : 6; // no connection
                            }
                            // set the state based on type 1/2
                            //db.SaveChanges();
                            return true;
                        }
                        else // not status 2
                        {
                            // check to see if the previous command has completed

                            if (command.RobotPlayer == null)
                            {
                                command.StatusID = 6;
                                db.SaveChanges();
                                return true;
                            }
                            
                            if (command.RobotPlayer.RobotConnection == null)
                            {
                                command.StatusID = 6;
                                db.SaveChanges();
                                return true;
                            }
                            
                            command.RobotPlayer.RobotConnection.CheckMovingStatus().Wait();
                            if (!command.RobotPlayer.RobotConnection.isMoving)
                            {
                                Console.WriteLine($"Robot Command Done({onecommand.CommandID})[{onecommand.CommandCatID}]{{{onecommand.CommandTypeID}}}-{onecommand.Parameter},{onecommand.ParameterB}:{onecommand.Description}");
                                // get next state from DB
                                command.StatusID = _dataService.GetIntFromDB("select funcProcessCommand(" + onecommand.CommandID + ",-1);");
                                command.StatusID = 6;
                                db.SaveChanges();
                                return true;
                            }


                        }
                        return false;


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
                            var robot = db.Robots.FirstOrDefault(r => r.RobotID == onecommand.RobotID);

                            if (robot != null)
                            {
                                robot.MessageCommandID = onecommand.CommandID;
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
