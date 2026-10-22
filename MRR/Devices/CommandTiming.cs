using System.Diagnostics;

namespace MRR.Devices
{
    /// <summary>
    /// How long robot traffic takes, for the GM page's Timing panel (GET /api/timing). Kept in
    /// memory only, since server start or the last Reset():
    ///   - Ack:     one ws_cmd message, send to the robot's reply (RobotConnection.SendCommandAsync)
    ///   - Status:  one ws_status poll round trip (RobotConnection.GetStatusAsync) -- move/turn
    ///              waits poll this every 50ms until the robot stops
    ///   - Command: one robot command, sent to finished, including the physical move
    ///              (CommandProcess.ProcessCommand -> Player.SendRobotCommandAsync)
    ///   - Server gap (all robots together): a robot command finishing to the next one being
    ///              sent, while nothing else was in flight -- time spent in the server, not the
    ///              robots or the network.
    /// </summary>
    public static class CommandTiming
    {
        public sealed class Stat
        {
            public int Count { get; private set; }
            public double LastMs { get; private set; }
            public double MaxMs { get; private set; }
            private double _totalMs;
            public double AvgMs => Count == 0 ? 0 : _totalMs / Count;

            public void Add(double ms)
            {
                Count++;
                LastMs = ms;
                _totalMs += ms;
                if (ms > MaxMs) MaxMs = ms;
            }

            public object Snapshot() => new
            {
                count = Count,
                lastMs = Math.Round(LastMs),
                avgMs = Math.Round(AvgMs),
                maxMs = Math.Round(MaxMs),
            };
        }

        private sealed class RobotStats
        {
            public readonly Stat Ack = new();
            public readonly Stat Status = new();
            public readonly Stat Command = new();
        }

        private static readonly object _lock = new();
        private static readonly Dictionary<int, RobotStats> _robots = [];
        private static Stat _serverGap = new();
        private static DateTime _since = DateTime.Now;

        // Server gap bookkeeping: robot commands currently in flight, and when the last one ended.
        private static int _inFlight;
        private static long? _idleSinceTicks;

        private static RobotStats For(int robotId)
        {
            if (!_robots.TryGetValue(robotId, out var stats))
                _robots[robotId] = stats = new RobotStats();
            return stats;
        }

        public static void RecordAck(int robotId, double ms) { lock (_lock) For(robotId).Ack.Add(ms); }
        public static void RecordStatus(int robotId, double ms) { lock (_lock) For(robotId).Status.Add(ms); }

        /// <summary>A robot command is being sent. Pair with CommandFinished.</summary>
        public static void CommandStarted()
        {
            lock (_lock)
            {
                if (_inFlight == 0 && _idleSinceTicks.HasValue)
                    _serverGap.Add(Stopwatch.GetElapsedTime(_idleSinceTicks.Value).TotalMilliseconds);
                _idleSinceTicks = null;
                _inFlight++;
            }
        }

        public static void CommandFinished(int robotId, double ms)
        {
            lock (_lock)
            {
                For(robotId).Command.Add(ms);
                if (_inFlight > 0) _inFlight--;
                if (_inFlight == 0) _idleSinceTicks = Stopwatch.GetTimestamp();
            }
        }

        /// <summary>
        /// A User Input command (a button the GM has to press) is outstanding: the wait that
        /// follows is a person, not the server, so don't count it as a server gap.
        /// </summary>
        public static void UserWait() { lock (_lock) _idleSinceTicks = null; }

        public static void Reset()
        {
            lock (_lock)
            {
                _robots.Clear();
                _serverGap = new Stat();
                _since = DateTime.Now;
                _idleSinceTicks = null;
                _inFlight = 0; // also unsticks it after a command the loop gave up on (CommandDeadline)
            }
        }

        public static object Snapshot()
        {
            lock (_lock)
            {
                return new
                {
                    since = _since.ToString("yyyy-MM-dd HH:mm:ss"),
                    serverGap = _serverGap.Snapshot(),
                    robots = _robots.OrderBy(r => r.Key).Select(r => new
                    {
                        robotID = r.Key,
                        ack = r.Value.Ack.Snapshot(),
                        status = r.Value.Status.Snapshot(),
                        command = r.Value.Command.Snapshot(),
                    }).ToList(),
                };
            }
        }
    }
}
