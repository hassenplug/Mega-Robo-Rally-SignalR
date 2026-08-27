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

            while (!_aborted && MarkCommandsReady() > 0 && stillRunning)
            {
                var active = GetActiveCommandList();
                while (!_aborted && active.Count > 0 && stillRunning)
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

                    // Commands in flight complete on their own threads, so without this the
                    // loop spins a core polling them while the robots move.
                    if (active.Count > 0) Thread.Sleep(PollInterval);
                }

                // The loop above skips a publish when one went out moments earlier, so make
                // sure the state the phones end on is the real one.
                PublishSnapshot(force: true);

                if (_aborted) break;

                //Console.WriteLine("Process Commands:Done ");
                // update to next state (post execute state)
            }

            if (_aborted) AbandonTurn();
            return false;
        }

        /// <summary>
        /// Retires whatever never ran, so the turn cannot resume on the next pass or after a
        /// restart. Commands already sent to a robot are left alone: the robot is moving and
        /// its result still has to be recorded.
        ///
        /// The state machine is deliberately not touched. Aborting says "stop"; what to do
        /// next -- reload the previous positions (state 16) or reprogram (state 15) -- is the
        /// GM's call, using controls that already exist.
        /// </summary>
        private void AbandonTurn()
        {
            var pending = _commandList.Where(c => c.StatusID == 1 || c.StatusID == 2).ToList();
            AbandonedCount = pending.Count;

            foreach (var command in pending) command.StatusID = 6; // complete, never executed
            if (pending.Count > 0) Db.SaveChanges();

            Console.WriteLine($"Turn aborted: {AbandonedCount} command(s) abandoned, " +
                              $"{_commandList.Count(c => c.StatusID is 3 or 4)} still in flight.");
            PublishSnapshot(force: true);
        }

        private volatile bool _aborted;

        /// <summary>How many commands were still pending when the turn was aborted.</summary>
        public int AbandonedCount { get; private set; }

        /// <summary>
        /// Stops the turn. The loop notices between commands and stops dispatching; anything
        /// already handed to a robot is beyond recall, since the robot is physically moving.
        ///
        /// This is the safe alternative to freezing the process mid-turn. "mrrctl pause"
        /// suspends the dispatch loop while already-sent AIM commands keep running, so the
        /// board drifts out of step with the state the process resumes believing. See
        /// install/PROCESS_MANAGER.md section 10.1.
        /// </summary>
        public void Abort() => _aborted = true;

        /// <summary>When each command was handed to a robot, for the deadline check.</summary>
        private readonly Dictionary<int, DateTime> _sentAtUtc = [];

        /// <summary>
        /// How long to wait for a robot to finish a command before giving up on it. Generous:
        /// a full-square move plus an IMU-corrected turn takes a few seconds.
        /// </summary>
        private static readonly TimeSpan CommandDeadline = TimeSpan.FromSeconds(30);

        /// <summary>Gap between polls of the in-flight command set.</summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

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
                        _sentAtUtc[onecommand.CommandID] = DateTime.UtcNow;

                        // Deliberately not awaited: SendRobotCommandAsync waits for the robot
                        // to finish moving and sets StatusID = 4 itself when it does, so the
                        // loop stays responsive meanwhile. What was missing is observing the
                        // failure -- "_ = task" discarded it, so a socket dropping mid-move
                        // left the command at StatusID 3 forever and the loop spinning on it
                        // with no message anywhere.
                        var sentTo = robot;
                        var sentCommand = onecommand;
                        _ = robot.SendRobotCommandAsync(onecommand).ContinueWith(t =>
                        {
                            if (!t.IsFaulted) return;
                            var error = t.Exception?.GetBaseException();
                            Console.WriteLine(
                                $"[robot {sentTo.ID} {sentTo.Name}] send FAILED for command " +
                                $"{sentCommand.CommandID} ({sentCommand.CommandType}): {error?.Message}");
                            sentTo.isConnected = false;   // the socket really is gone
                            sentCommand.StatusID = 4;     // let the turn move on instead of hanging
                        }, TaskScheduler.Default);

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
                        // Still moving. If it has been too long the robot is not coming back
                        // -- a lost ack, a robot switched off mid-turn -- so say so and move
                        // on rather than spinning here for the rest of the evening.
                        if (_sentAtUtc.TryGetValue(onecommand.CommandID, out var sentAt)
                            && DateTime.UtcNow - sentAt > CommandDeadline)
                        {
                            Console.WriteLine(
                                $"[robot {onecommand.RobotID}] command {onecommand.CommandID} " +
                                $"({onecommand.CommandType}) did not complete within " +
                                $"{CommandDeadline.TotalSeconds:0}s; giving up on it.");
                            _sentAtUtc.Remove(onecommand.CommandID);
                            onecommand.StatusID = 4;
                            Db.SaveChanges();
                        }
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

                        if (robotPlayer != null)
                        {
                            // MessageCommandID points at the CommandList row whose Description is
                            // the message to show; RefreshRobotDenormalizedFields() joins on it to
                            // populate Robots.msg for GetRobotsFromTable() to serve to clients.
                            robotPlayer.MessageCommandID = onecommand.CommandID;
                            Db.Robots.Where(r => r.ID == onecommand.RobotID)
                                .ExecuteUpdate(s => s.SetProperty(r => r.MessageCommandID, onecommand.CommandID));
                            _dataService.RefreshAllPlayers();

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
