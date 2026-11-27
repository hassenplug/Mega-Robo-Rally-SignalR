using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using MRR.Data.Entities;

namespace MRR.Services
{
    /// <summary>
    /// DataService — game results (documents/GAME_RESULTS_DESIGN.md §4.1, §5): the stats shown
    /// after End Game, counted from the current game's CommandList and HistoryRobotTurns. Both
    /// tables hold only the current (or most recent) game -- StartGame() clears them -- so no
    /// GameID is needed. Read-only; nothing here writes the database.
    /// </summary>
    public partial class DataService
    {
        // CommandList.StatusID once a command is finished (ProcessDbCommand). Caveat: an aborted
        // turn (POST /api/execution/abort, CommandProcess.AbandonTurn) also sets 6 on commands that
        // never ran, so a turn aborted and not then re-run can over-count.
        private const int CommandComplete = 6;

        /// <summary>
        /// Ranking and per-robot stats for the current or most recent game. played = false when
        /// no command has run yet (e.g. no game since the server started, or still picking seats).
        /// Ranking: most flags touched in all (on a normal board the same as laps × TotalFlags +
        /// current flag, since a finish resets to flag 0), then whoever touched their last flag first.
        /// </summary>
        public object GetGameResults()
        {
            int totalFlags = TotalFlags;

            var robotTable = GetQueryResults(
                "SELECT RobotID, RobotName, OperatorName, RobotColor, RobotColorFG, PlayerSeat, " +
                "       IFNULL(Computer, 0) AS Computer, CurrentFlag " +
                "FROM Robots ORDER BY PlayerSeat, RobotID");

            var counts = GetQueryResults(
                "SELECT RobotID, " +
                $"  SUM(CommandTypeID = {(int)SquareAction.Flag} AND Parameter > 0) AS Flags, " +
                $"  SUM(CommandTypeID = {(int)SquareAction.Flag} AND Parameter >= {totalFlags} AND {totalFlags} > 0) AS Laps, " +
                $"  SUM(CommandTypeID = {(int)SquareAction.Dead}) AS Deaths, " +
                $"  SUM(CommandTypeID = {(int)SquareAction.DealSpamCard}) AS Spam, " +
                // LaserFired (14) is written for the robot that caused damage: lasers, ramming
                // gear and exploding options (CreateCommands.AddDamage).
                $"  SUM(CommandTypeID = {(int)SquareAction.SetPlayerStatus} AND Parameter = 14) AS DamageDealt, " +
                $"  SUM(CommandTypeID = {(int)SquareAction.RobotPush}) AS PushedBy " +
                $"FROM CommandList WHERE StatusID = {CommandComplete} GROUP BY RobotID")
                .Rows.Cast<DataRow>().ToDictionary(r => Int(r["RobotID"]));

            // RobotPush: RobotID is the robot pushed, Parameter the robot doing the pushing.
            var pushesGiven = GetQueryResults(
                $"SELECT Parameter AS RobotID, COUNT(*) AS N FROM CommandList " +
                $"WHERE StatusID = {CommandComplete} AND CommandTypeID = {(int)SquareAction.RobotPush} GROUP BY Parameter")
                .Rows.Cast<DataRow>().ToDictionary(r => Int(r["RobotID"]), r => Int(r["N"]));

            // Every executed flag touch, in the order it happened: gives the finish order and,
            // for the tie-break, when each robot last moved forward.
            var flagRows = GetQueryResults(
                "SELECT RobotID, Turn, Parameter FROM CommandList " +
                $"WHERE StatusID = {CommandComplete} AND CommandTypeID = {(int)SquareAction.Flag} AND Parameter > 0 " +
                "ORDER BY Turn, Phase, CommandSequence, CommandID")
                .Rows.Cast<DataRow>().ToList();

            int turnsPlayed = GetIntFromDB(
                $"SELECT IFNULL(MAX(Turn), 0) FROM CommandList WHERE StatusID = {CommandComplete}");

            var lastProgress = new Dictionary<int, int>();   // RobotID -> index in flagRows
            var finishes = new List<object>();
            var lapsSoFar = new Dictionary<int, int>();
            for (int i = 0; i < flagRows.Count; i++)
            {
                int robotId = Int(flagRows[i]["RobotID"]);
                lastProgress[robotId] = i;
                if (totalFlags > 0 && Int(flagRows[i]["Parameter"]) >= totalFlags)
                {
                    lapsSoFar[robotId] = lapsSoFar.GetValueOrDefault(robotId) + 1;
                    finishes.Add(new { robotID = robotId, turn = Int(flagRows[i]["Turn"]), lap = lapsSoFar[robotId] });
                }
            }

            var timing = GetSubmitTiming(robotTable, turnsPlayed);

            var robots = robotTable.Rows.Cast<DataRow>().Select(r =>
            {
                int id = Int(r["RobotID"]);
                counts.TryGetValue(id, out var c);
                timing.PerRobot.TryGetValue(id, out var t);
                return new
                {
                    robotID     = id,
                    name        = Str(r["RobotName"]),
                    player      = Str(r["OperatorName"]),
                    color       = Str(r["RobotColor"]),
                    colorFG     = Str(r["RobotColorFG"]),
                    seat        = Int(r["PlayerSeat"]),
                    computer    = Int(r["Computer"]),
                    currentFlag = Int(r["CurrentFlag"]),
                    laps        = c == null ? 0 : Int(c["Laps"]),
                    flags       = c == null ? 0 : Int(c["Flags"]),
                    deaths      = c == null ? 0 : Int(c["Deaths"]),
                    spam        = c == null ? 0 : Int(c["Spam"]),
                    damageDealt = c == null ? 0 : Int(c["DamageDealt"]),
                    pushedBy    = c == null ? 0 : Int(c["PushedBy"]),
                    pushesGiven = pushesGiven.GetValueOrDefault(id),
                    avgSecondsBehindFirst = t?.AvgSecondsBehindFirst,
                    lastToSubmit          = t?.LastToSubmit ?? 0,
                    // Not sent: only used to order the list.
                    progressIndex = lastProgress.TryGetValue(id, out int p) ? p : int.MaxValue,
                };
            })
            .OrderByDescending(r => r.flags)
            .ThenBy(r => r.progressIndex)
            .ThenBy(r => r.seat)
            .ToList();

            return new
            {
                played = turnsPlayed > 0 && robots.Count > 0,
                totalFlags,
                turnsPlayed,
                firstSubmit = timing.FirstSubmit?.ToString("yyyy-MM-dd HH:mm:ss"),
                lastSubmit  = timing.LastSubmit?.ToString("yyyy-MM-dd HH:mm:ss"),
                avgTurnSeconds = timing.AvgTurnSeconds,
                finishes,
                robots = robots.Select((r, i) => new
                {
                    place = i + 1,
                    r.robotID, r.name, r.player, r.color, r.colorFG, r.seat, r.computer,
                    r.currentFlag, r.laps, r.flags, r.deaths, r.spam, r.damageDealt,
                    r.pushedBy, r.pushesGiven, r.avgSecondsBehindFirst, r.lastToSubmit,
                }).ToList(),
            };
        }

        private sealed class RobotSubmitTiming
        {
            public double? AvgSecondsBehindFirst;
            public int LastToSubmit;
        }

        private sealed class SubmitTiming
        {
            public DateTime? FirstSubmit, LastSubmit;
            public double? AvgTurnSeconds;
            public Dictionary<int, RobotSubmitTiming> PerRobot = new();
        }

        /// <summary>
        /// From HistoryRobotTurns.SavedAt (each player's submit time, NULL if they never
        /// submitted). Turn length = gap between consecutive turns' last submit. Per player, only
        /// human players are compared: how long after the turn's first human they submitted, and
        /// how often they were the last of two or more. Rows written before 2026-11-27 hold the
        /// time the row was saved instead, which reads as everyone submitting at once.
        /// </summary>
        /// Only turns 1..turnsPlayed and robots in this game are used: a database that has not yet
        /// run StartGame() with the clearing in place still holds later turns from older games.
        private SubmitTiming GetSubmitTiming(DataTable robotTable, int turnsPlayed)
        {
            var result = new SubmitTiming();
            var inGame = robotTable.Rows.Cast<DataRow>().Select(r => Int(r["RobotID"])).ToHashSet();
            var computers = robotTable.Rows.Cast<DataRow>()
                .Where(r => Int(r["Computer"]) > 0).Select(r => Int(r["RobotID"])).ToHashSet();

            var rows = GetQueryResults(
                "SELECT Turn, RobotID, SavedAt FROM HistoryRobotTurns " +
                $"WHERE SavedAt IS NOT NULL AND Turn BETWEEN 1 AND {turnsPlayed} ORDER BY Turn")
                .Rows.Cast<DataRow>()
                .Where(r => inGame.Contains(Int(r["RobotID"])))
                .Select(r => (Turn: Int(r["Turn"]), RobotID: Int(r["RobotID"]), At: Convert.ToDateTime(r["SavedAt"])))
                .ToList();
            if (rows.Count == 0) return result;

            result.FirstSubmit = rows.Min(r => r.At);
            result.LastSubmit  = rows.Max(r => r.At);

            var turnEnds = rows.GroupBy(r => r.Turn).OrderBy(g => g.Key).Select(g => g.Max(r => r.At)).ToList();
            if (turnEnds.Count > 1)
                result.AvgTurnSeconds = Math.Round(
                    turnEnds.Zip(turnEnds.Skip(1), (a, b) => (b - a).TotalSeconds).Average(), 1);

            var behind = new Dictionary<int, List<double>>();
            foreach (var turn in rows.Where(r => !computers.Contains(r.RobotID)).GroupBy(r => r.Turn))
            {
                var humans = turn.ToList();
                DateTime first = humans.Min(r => r.At);
                foreach (var r in humans)
                {
                    if (!behind.TryGetValue(r.RobotID, out var list)) behind[r.RobotID] = list = new List<double>();
                    list.Add((r.At - first).TotalSeconds);
                }
                if (humans.Count >= 2)
                {
                    var last = humans.OrderByDescending(r => r.At).First();
                    if (!result.PerRobot.TryGetValue(last.RobotID, out var t))
                        result.PerRobot[last.RobotID] = t = new RobotSubmitTiming();
                    t.LastToSubmit++;
                }
            }
            foreach (var (robotId, list) in behind)
            {
                if (!result.PerRobot.TryGetValue(robotId, out var t))
                    result.PerRobot[robotId] = t = new RobotSubmitTiming();
                t.AvgSecondsBehindFirst = Math.Round(list.Average(), 1);
            }
            return result;
        }

        private static int Int(object v) => v is DBNull || v == null ? 0 : Convert.ToInt32(v);
        private static string Str(object v) => v is DBNull || v == null ? "" : Convert.ToString(v) ?? "";
    }
}
