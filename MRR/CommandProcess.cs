using System.Threading.Tasks;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Net.Cache;
using MRR.Services;
using Microsoft.AspNetCore.SignalR;
using MRR.Hubs;
using MRR.Data;
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

        private List<CommandItem> _commandList;

        public PendingCommands(DataService dataService, IHubContext<DataHub> hubContext)
        {
            _dataService = dataService;
            _hubContext = hubContext;
            _dbContext = _dataService.CreateDbContext();
            _commandList = _dbContext.CommandItems
                .Where(c => c.Turn == _dataService.Turn)
                .ToList();

            // Attach the robot to each command. EF only materializes RobotID; this used to
            // happen invisibly inside CommandItem's RobotID setter via a static Players
            // reference. Doing it here keeps CommandItem.Description and ToString() able to
            // name the robot and its cards, without a static that can silently be unset.
            var players = _dataService.AllPlayers;
            foreach (var command in _commandList)
            {
                command.Robot = players.GetPlayer(p => p.ID == command.RobotID);
            }
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

        private List<CommandItem> GetActiveCommandList()
            => _commandList.Where(c => c.StatusID >= 2 && c.StatusID <= 4).ToList();

        public bool ProcessCommands() // make sure state = 7 or 8
        {
            bool stillRunning = true;

            while (MarkCommandsReady() > 0 && stillRunning)
            {
                var active = GetActiveCommandList();
                while (active.Count > 0 && stillRunning)
                {
                    stillRunning = false;
                    foreach (CommandItem onecommand in active)
                    {
                        //Console.WriteLine($"{active.Count} Active Command: {onecommand.CommandID}, Robot: {onecommand.RobotID}, Type: {onecommand.CommandType}");

                        stillRunning = ProcessCommand(onecommand) || stillRunning;
                    }
                    // refresh active set for the next inner loop iteration
                    active = GetActiveCommandList();
                    PublishSnapshot();
                }

                // The loop above skips a publish when one went out moments earlier, so make
                // sure the state the phones end on is the real one.
                PublishSnapshot(force: true);

                //Console.WriteLine("Process Commands:Done ");
                // update to next state (post execute state)
            }
            return false;
        }

        private DateTime _lastPublishUtc = DateTime.MinValue;
        private static readonly TimeSpan PublishInterval = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Pushes the game snapshot to the clients, at most once every 100 ms.
        ///
        /// This loop runs per command, and each publish rebuilt and re-serialized the whole
        /// payload -- for a turn of roughly 130 commands that is 130 full serializations and
        /// broadcasts, on a Pi that is driving robot I/O at the same time. Phones cannot
        /// render faster than this anyway. Pass force to bypass the interval, which the end
        /// of the turn does so the last state is never the one that got skipped.
        /// </summary>
        private void PublishSnapshot(bool force = false)
        {
            var now = DateTime.UtcNow;
            if (!force && now - _lastPublishUtc < PublishInterval) return;
            _lastPublishUtc = now;

            var allDataJson = _dataService.GetAllDataJson();
            _hubContext.Clients.All.SendAsync("AllDataUpdate", allDataJson).GetAwaiter().GetResult();
        }

        public int MarkCommandsReady()
        {
            var result = _commandList.Count(c => c.StatusID >= 2 && c.StatusID <= 4);
            if (result > 0) return result;

            var minSequence = _commandList
                .Where(c => c.StatusID == 1)
                .Min(c => (int?)c.NormalSequence) ?? -1;

            if (minSequence == -1) return 0; // no commands waiting

            // Update DB
            using var db = _dataService.CreateDbContext();
            var affected = db.CommandItems
                .Where(c => c.NormalSequence == minSequence && c.Turn == _dataService.Turn)
                .ExecuteUpdate(s => s.SetProperty(b => b.StatusID, 2));

            // Sync in-memory list
            foreach (var item in _commandList.Where(c => c.NormalSequence == minSequence))
                item.StatusID = 2;

            return affected;
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
        private static void LogCommand(CommandItem command, string text)
            => Console.WriteLine($"{text}({command.RobotID})[{command.CommandCatID}]{{{command.CommandType}}}-{command.Value},{command.ValueB}:{command.Description}");

//        public async Task<bool> ExecuteCommand(CommandItem onecommand)

        public bool ProcessCommand(CommandItem onecommand)
        {
            //Console.WriteLine($"Process Command({onecommand.CommandID})[{onecommand.CommandCatID}]{{{onecommand.CommandType}}}{onecommand.Value},{onecommand.ValueB}");

            var robot = _dataService.AllPlayers.GetPlayer(p => p.ID == onecommand.RobotID);
            //var robot = onecommand.Robot;
            switch (onecommand.CommandCatID)
            {
                case 1: // Robot with Reply
                case 2: // Robot No Reply
                    //var robot = _dataService.AllPlayers.GetPlayer(p => p.ID == onecommand.RobotID);
                    if (robot == null)
                    {
                        LogCommand(onecommand, "Robot not found for Command");
                        onecommand.StatusID = 6; // command complete
                        Db.SaveChanges();
                        return true;
                    }

                    if (!robot.isConnected)
                    {
                        LogCommand(onecommand, "Robot not connected for Command");
                        onecommand.StatusID = _dataService.ProcessDbCommand(onecommand, 5);
                        Db.SaveChanges();
                        return true;
                    }

                    if (onecommand.StatusID == 2)
                    {
                        if (onecommand.CommandCatID != 2)LogCommand(onecommand, "Robot Command    ");
                        
                        onecommand.StatusID = 3; // executing
                        //robot.SendRobotCommandAsync(onecommand).Wait();
                        _ = robot.SendRobotCommandAsync(onecommand);
                        if (onecommand.CommandCatID == 2)
                        {
                            // don't wait for reply
                            onecommand.StatusID = 4; // not waiting for reply
                        }
                        Db.SaveChanges();
                        return true;
                    }

                    if (onecommand.StatusID == 3)
                    {
                    }

                    if (onecommand.StatusID == 4)
                    {
                        // no reply expected
                        onecommand.StatusID = _dataService.ProcessDbCommand(onecommand, 5);
                        Db.SaveChanges();
                    }
                    return true;


                case 3: // DB
                    //LogCommand(onecommand, "Database Command ");
                    onecommand.StatusID = _dataService.ProcessDbCommand(onecommand, -1);
                    Db.SaveChanges();
                    return true;

                case 6: // User Input
                    var robotPlayer = _dataService.AllPlayers.GetPlayer(p => p.ID == onecommand.RobotID);
                    if (onecommand.StatusID < 4)
                    {
                        LogCommand(onecommand, "User Input       ");
                        //var robot6 = Db.Robots.FirstOrDefault(r => r.ID == onecommand.RobotID);

                        if (robotPlayer != null)
                        {
                            robotPlayer.PlayerMsg = onecommand.Description;
                            onecommand.StatusID = 4;
                            Db.SaveChanges();
                            return false; // wait for user input
                        }
                    }
                    return false;

                default:
                    LogCommand(onecommand, "Not processed here");
                    onecommand.StatusID = _dataService.ProcessDbCommand(onecommand, -1);
                    Db.SaveChanges();
                    break;
            }

            return false;
        }
    }
}
