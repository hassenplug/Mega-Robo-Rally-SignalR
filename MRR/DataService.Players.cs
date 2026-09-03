using System;
using System.Data;
using System.Text;
using System.IO;
using MySqlConnector;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using MRR.Data;
using MRR.Data.Entities;
using System.Xml.Serialization;
using MRR;

namespace MRR.Services
{
    /// <summary>
    /// DataService — Robots table: loading, resetting, positions, priority and status.
    ///
    /// Part of the step 4 split (API_DECOMPOSITION_DESIGN.md section 4): DataService is
    /// one class doing several jobs, so it is first separated by concern into partials.
    /// Splitting the file changes nothing semantically, but it makes each concern's real
    /// dependencies visible, which is what the repository extraction needs.
    /// </summary>
    public partial class DataService
    {


        /*

                select Robots.RobotID, 
        RobotBodies.`Name` as RobotName, 
        RobotBodies.Color as RobotColor, 
        RobotBodies.ColorFG as RobotColorFG,
        Robots.CurrentFlag, 
        RobotStatus.StatusColor as StatusColor,
        RobotStatus.LEDColor as LEDColor,
        RobotStatus.ShortDescription as PlayerStatus,
        Robots.Status as StatusID,
        CurrentPosCol as `X`,
        CurrentPosRow as `Y`,
        CurrentPosDir as Dir,
        ShortDirDesc as sDir,
        ArchivePosCol as `AX`,
        ArchivePosRow as `AY`,
        Robots.Score as Score,
        OperatorName,
        PositionValid,
        Priority,
        `ShutDown`,
        `Password`,
        PlayerSeat,
        Energy,
        Concat(CurrentFlag,"/",Energy) FlagEnergy,
        so.Direction as PlayerViewDirection,
        so.Direction as DirectionAdjustment,
        Robots.CardsDealt,
        Robots.CardsPlayed,
        if(isnull(ShowCardsPlayed) || RobotStatus.Active=0,RobotStatus.ShortDescription,ShowCardsPlayed) as StatusToShow,
        cl.Description PlayerMsg


        from (Robots inner join RobotBodies on Robots.RobotBodyID = RobotBodies.RobotBodyID)
         inner join RobotStatus on if(Robots.IsConnected=1,Robots.`Status`,10) = RobotStatus.RobotStatusID
         inner join RobotDirections on Robots.CurrentPosDir = RobotDirections.DirID
         inner join SeatOrientation so on PlayerSeat = so.SeatID

         left join (
         #show cards played
        select Owner, 
        GROUP_CONCAT(if(isnull(mc.CardID),"-",if(mc.Executed,mct.ShortDescription,"X")) order by PhasePlayed ) ShowCardsPlayed
        from MoveCards mc inner join MoveCardTypes mct on mc.CardTypeID = mct.CardTypeID 
        where mc.PhasePlayed>0 group by owner order by Owner) played
        on Robots.RobotID = played.Owner
        left join CommandList cl on Robots.MessageCommandID = cl.CommandID
        */

        public Players GetAllPlayers(bool forceRefresh = false)
        {
            if (_allPlayers == null || forceRefresh)
            {
                var players = new Players();

                string strSQL = @"SELECT r.RobotID, rb.Name AS RobotName, rb.Color AS RobotColor, rb.ColorFG AS RobotColorFG,
                       r.OperatorName, r.Password, r.PlayerSeat, rbase.IPAddress,
                       so.Direction AS PlayerViewDirection
                FROM Robots r
                JOIN RobotBodies rb ON r.RobotBodyID = rb.RobotBodyID
                JOIN RobotDirections rd ON r.CurrentPosDir = rd.DirID
                JOIN SeatOrientation so ON r.PlayerSeat = so.SeatID
                JOIN RobotBases rbase ON r.RobotBaseID = rbase.RobotBaseID
                ORDER BY r.RobotID";

                var loadplayers = this.GetQueryResults(strSQL);
                foreach (DataRow row in loadplayers.Rows)
                {
                    players.Add(new Player()
                    {
                        ID                  = (int)row["RobotID"],
                        PlayerSeat          = (int)row["PlayerSeat"],
                        Name                = row["RobotName"].ToString() ?? "",
                        Color               = row["RobotColor"].ToString() ?? "FFFFFF",
                        ForeColor           = row["RobotColorFG"].ToString() ?? "000000",
                        Password            = row["Password"]?.ToString() ?? "",
                        IPAddress           = row["IPAddress"].ToString(),
                        PlayerViewDirection = Convert.ToInt32(row["PlayerViewDirection"]),
                        AllGameCards        = GameCards
                    });
                    //Console.WriteLine("Loaded player ID:" + row["RobotID"].ToString() + " Name:" + row["RobotName"].ToString() + " IP:" + IPAddress);
                }
                _allPlayers = players;

            }

            return _allPlayers;
        }

        /// <summary>
        /// Builds a fresh turn-planning snapshot straight from the database -- independent of
        /// the AllPlayers connection registry. Used by BuildTurnRequest() (DataService.Commands.cs)
        /// to populate TurnRequest.Players. See documents/ALLPLAYERS_REMOVAL_DESIGN.md: the
        /// registry only needs to hold live robot connections now, not game-state data, and this
        /// is the one remaining place that needs a full player-state snapshot for planning.
        ///
        /// Field-for-field, this matches what GetAllPlayers() + the now-removed
        /// RefreshAllPlayers() used to populate onto AllPlayers before BuildTurnRequest copied
        /// it into a PlayerStates snapshot -- Damage and Lives were never part of that (see
        /// ALLPLAYERS_REMOVAL_DESIGN.md §11): confirmed intentional under the current rules,
        /// where ordinary damage converts to a dealt Spam card instead of accumulating, and only
        /// a single hit big enough to cross the death threshold in one go (e.g. a pit) needs
        /// Damage to reflect it -- which works within one turn's simulation without needing last
        /// turn's value.
        /// </summary>
        public PlayerStates GetPlayerStatesFromDB()
        {
            var result = new PlayerStates();

            string strSQL = @"SELECT r.RobotID, rb.Name AS RobotName, rb.Color AS RobotColor, rb.ColorFG AS RobotColorFG,
                   r.PlayerSeat, so.Direction AS PlayerViewDirection,
                   r.CurrentFlag, r.ShutDown, r.`Status` AS StatusID,
                   r.CurrentPosCol AS X, r.CurrentPosRow AS Y, r.CurrentPosDir AS Dir,
                   r.ArchivePosCol AS AX, r.ArchivePosRow AS AY,
                   r.Priority, r.Energy, r.Score, r.PositionValid
            FROM Robots r
            JOIN RobotBodies rb ON r.RobotBodyID = rb.RobotBodyID
            JOIN SeatOrientation so ON r.PlayerSeat = so.SeatID
            ORDER BY r.RobotID";

            var rows = GetQueryResults(strSQL);
            foreach (DataRow row in rows.Rows)
            {
                result.Add(new PlayerState
                {
                    ID                  = (int)row["RobotID"],
                    Name                = row["RobotName"].ToString() ?? "",
                    Color               = row["RobotColor"].ToString() ?? "FFFFFF",
                    ForeColor           = row["RobotColorFG"].ToString() ?? "000000",
                    PlayerSeat          = (int)row["PlayerSeat"],
                    PlayerViewDirection = Convert.ToInt32(row["PlayerViewDirection"]),
                    LastFlag            = (int)row["CurrentFlag"],
                    ShutDown            = (tShutDown)(int)row["ShutDown"],
                    PlayerStatus        = (tPlayerStatus)(int)row["StatusID"],
                    CurrentPos          = new RobotLocation((Direction)(int)row["Dir"], (int)row["X"], (int)row["Y"]),
                    ArchivePosCol       = (int)row["AX"],
                    ArchivePosRow       = (int)row["AY"],
                    Priority            = (int)row["Priority"],
                    Energy              = (int)row["Energy"],
                    Score               = (int)row["Score"],
                    PositionValid       = (int)row["PositionValid"] != 0,
                    Active              = (int)row["StatusID"] != 10,
                    AllGameCards        = GameCards,
                });
            }

            return result;
        }

        // Denormalizes the RobotStatus/RobotDirections/MoveCards/CommandList joins onto Robots itself
        // (StatusColor, LEDColor, PlayerStatus, sDir, FlagEnergy, StatusToShow, PlayerMsg,
        // ConnectStatusColor, ConnectStatusDesc) so other reads (GetRobotsFromTable, SetStatus)
        // can use the plain Robots columns instead of re-joining. RobotStatus is joined twice --
        // once (rs) for the folded gameplay status IF(connected, Status, NotActive) that
        // StatusColor/LEDColor/PlayerStatus/StatusToShow reflect, and once (cs) for
        // ConnectStatusID's own color/description, which is never folded with anything.
        public void RefreshRobotDenormalizedFields()
        {
            string updateSQL = $@"UPDATE Robots r
                JOIN RobotStatus rs ON IF(r.ConnectStatusID = {(int)tPlayerStatus.RobotConnected}, r.Status, 10) = rs.RobotStatusID
                JOIN RobotStatus cs ON r.ConnectStatusID = cs.RobotStatusID
                JOIN RobotDirections rd ON r.CurrentPosDir = rd.DirID
                LEFT JOIN (
                    SELECT mc.Owner,
                           GROUP_CONCAT(IF(mc.CardID IS NULL, '-', IF(mc.Executed, mct.ShortDescription, 'X'))
                                        ORDER BY mc.PhasePlayed) AS ShowCardsPlayed
                    FROM MoveCards mc
                    JOIN MoveCardTypes mct ON mc.CardTypeID = mct.CardTypeID
                    WHERE mc.PhasePlayed > 0
                    GROUP BY mc.Owner
                ) played ON r.RobotID = played.Owner
                LEFT JOIN CommandList cl ON r.MessageCommandID = cl.CommandID
                SET r.StatusColor        = rs.StatusColor,
                    r.LEDColor           = rs.LEDColor,
                    r.PlayerStatus       = rs.ShortDescription,
                    r.sDir               = rd.ShortDirDesc,
                    r.FlagEnergy         = CONCAT(r.CurrentFlag,'/',r.Energy),
                    r.PlayerMsg          = cl.Description,
                    r.ConnectStatusColor = cs.StatusColor,
                    r.ConnectStatusDesc  = cs.ShortDescription";
            this.ExecuteSQL(updateSQL);
//                    r.StatusToShow       = IF(played.ShowCardsPlayed IS NULL OR rs.Active = 0, rs.ShortDescription, played.ShowCardsPlayed),
        }

        // Reads Robots directly (after freshening the denormalized columns above) and maps it
        // straight to RobotData -- this is what AllDataPayload.robots sends to clients now,
        // replacing the old path that built RobotData from the in-memory Players/GameCards
        // collections. StatusID/X/Y/Dir are aliases of Status/CurrentPosCol/CurrentPosRow/
        // CurrentPosDir. PlayerViewDirection has no Robots column of its own -- it has always
        // just duplicated DirectionAdjustment -- and is kept in the payload for compatibility
        // with existing clients rather than dropping it. CardCount is a real column, kept
        // current by RefreshCardCount (DataService.Cards.cs) whenever a robot's cards change.
        // ConnectStatusColor/ConnectStatusDesc are likewise real columns now, kept current by
        // RefreshRobotDenormalizedFields -- no join needed here for them. IPAddress reads
        // Robots.IPAddress directly; UpdateRobotIPAddress keeps it in sync with the
        // RobotBases.IPAddress that Player.Connect() actually dials, so no join is needed here
        // either.
        public List<RobotData> GetRobotsFromTable()
        {
            //RefreshRobotDenormalizedFields();

            var table = GetQueryResults("SELECT * FROM Robots ORDER BY Priority");
            var result = new List<RobotData>();

            foreach (DataRow row in table.Rows)
            {
                int directionAdjustment = Convert.ToInt32(row["DirectionAdjustment"]);
                string cardsDealt = row["CardsDealt"].ToString() ?? "";

                result.Add(new RobotData
                {
                    RobotID             = (int)row["RobotID"],
                    RobotName           = row["RobotName"].ToString() ?? "",
                    RobotColor          = row["RobotColor"].ToString() ?? "",
                    RobotColorFG        = row["RobotColorFG"].ToString() ?? "",
                    CurrentFlag         = (int)row["CurrentFlag"],
                    StatusColor         = row["StatusColor"].ToString() ?? "",
                    LEDColor            = row["LEDColor"].ToString() ?? "",
                    PlayerStatus        = row["PlayerStatus"].ToString() ?? "",
                    StatusID            = (int)row["Status"],
                    X                   = (int)row["CurrentPosCol"],
                    Y                   = (int)row["CurrentPosRow"],
                    Dir                 = (int)row["CurrentPosDir"],
                    sDir                = row["sDir"].ToString() ?? "",
                    OperatorName        = row["OperatorName"].ToString() ?? "",
                    Priority            = (int)row["Priority"],
                    ShutDown            = (int)row["ShutDown"],
                    PlayerSeat          = (int)row["PlayerSeat"],
                    Energy              = (int)row["Energy"],
                    FlagEnergy          = row["FlagEnergy"].ToString() ?? "",
                    PlayerViewDirection = directionAdjustment,
                    DirectionAdjustment = directionAdjustment,
                    CardsDealt          = cardsDealt,
                    CardsPlayed         = row["CardsPlayed"].ToString() ?? "",
                    StatusToShow        = row["StatusToShow"].ToString() ?? "",
                    PlayerMsg           = row["PlayerMsg"].ToString() ?? "",
                    CardCount           = (int)row["CardCount"],
                    ConnectStatusID     = (int)row["ConnectStatusID"],
                    ConnectStatusColor  = row["ConnectStatusColor"].ToString() ?? "",
                    ConnectStatusDesc   = row["ConnectStatusDesc"].ToString() ?? "",
                    IPAddress           = row["IPAddress"].ToString() ?? "",
                });
            }

            return result;
        }

        /// <summary>
        /// C# equivalent of procUpdatePlayerPriority.
        /// Rotates robot turn-order priorities round-robin for 10-Turn (single-phase) mode:
        ///   1. Decrement every robot's Priority by 1.
        ///   2. Count the total number of robots.
        ///   3. The robot whose Priority wrapped to 0 is assigned the highest priority
        ///      (robotCount), so turn order cycles through all players evenly.
        /// Accepts an optional open connection so it can be called within the same
        /// connection context as MoveCardsShuffleAndDeal without opening a second one.
        /// </summary>
        public void UpdatePlayerPriority(MySqlConnection? connection = null, int change = -1)
        {
            bool ownConnection = connection == null;
            if (ownConnection)
            {
                connection = new MySqlConnection(_connectionString);
                connection.Open();
            }

            try
            {
                // Step 1: Count robots first so the mod range is known.
                int robotCount;
                using (var cmd = new MySqlCommand("SELECT COUNT(RobotID) FROM Robots", connection!))
                {
                    var result = cmd.ExecuteScalar();
                    robotCount = result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
                }

                if (robotCount == 0) return;

                // Step 2: Add change and wrap with mod (priorities are 1-based: 1..robotCount).
                // Double-mod keeps the result positive for any change value.
                using var update = new MySqlCommand(
                    "UPDATE Robots SET Priority = MOD(MOD(Priority - 1 + @change, @n) + @n, @n) + 1",
                    connection!);
                update.Parameters.AddWithValue("@change", change);
                update.Parameters.AddWithValue("@n", robotCount);
                update.ExecuteNonQuery();
            }
            finally
            {
                if (ownConnection)
                    connection!.Dispose();
            }
        }

        /// <summary>
        /// Section 9 (install/todo.md) "Update IP": writes the robot's IP address to both
        /// RobotBases.IPAddress -- the column Player.Connect()/GetAllPlayers() actually read --
        /// and Robots.IPAddress, so the two stay in sync instead of the latter going stale.
        /// Parameterized because the value comes straight from a form field on the connection
        /// screen.
        /// </summary>
        public bool UpdateRobotIPAddress(int robotId, string ipAddress)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var update = new MySqlCommand(
                @"UPDATE RobotBases rb
                  JOIN Robots r ON r.RobotBaseID = rb.RobotBaseID
                  SET rb.IPAddress = @ip,
                      r.IPAddress = @ip
                  WHERE r.RobotID = @robotId",
                connection);
            update.Parameters.AddWithValue("@ip", ipAddress);
            update.Parameters.AddWithValue("@robotId", robotId);
            return update.ExecuteNonQuery() > 0;
        }

        // =====================================================================
        // procSetStatus — C# equivalent
        // Syncs StatusLEDs from Robots.LEDColor (kept fresh via RefreshRobotDenormalizedFields).
        // Also applies the trigger logic for StatusLEDs_BEFORE_UPDATE:
        //   converts the hex Color string to R/G/B integers in the same UPDATE.
        // =====================================================================
        public void SetStatus()
        {
            // Robots.Status may have just changed (e.g. ResetPlayers), so refresh the
            // denormalized LEDColor/StatusColor columns before reading them below.
            RefreshRobotDenormalizedFields();

            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            // Step 1: set Color from Robots.LEDColor (no RobotStatus join needed anymore)
            using (var cmd = new MySqlCommand(
                "UPDATE StatusLEDs " +
                "INNER JOIN Robots vr ON StatusLEDs.LEDID = vr.RobotID " +
                "SET StatusLEDs.Color = vr.LEDColor, " +
                "    StatusLEDs.R = CONV(SUBSTRING(vr.LEDColor,1,2),16,10), " +
                "    StatusLEDs.G = CONV(SUBSTRING(vr.LEDColor,3,2),16,10), " +
                "    StatusLEDs.B = CONV(SUBSTRING(vr.LEDColor,5,2),16,10)",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Step 2: override with red for robots with invalid position
            using (var cmd = new MySqlCommand(
                "UPDATE StatusLEDs " +
                "INNER JOIN Robots vr ON StatusLEDs.LEDID = vr.RobotID " +
                "SET StatusLEDs.Color = 'FF0000', " +
                "    StatusLEDs.R = 255, StatusLEDs.G = 0, StatusLEDs.B = 0 " +
                "WHERE vr.PositionValid = 0",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Step 3: override with orange for robots with an active BT-connect command (CommandTypeID=70, StatusID=7)
            using (var cmd = new MySqlCommand(
                "UPDATE StatusLEDs " +
                "INNER JOIN CommandList cl ON StatusLEDs.LEDID = cl.RobotID " +
                "SET StatusLEDs.Color = 'FF8800', " +
                "    StatusLEDs.R = 255, StatusLEDs.G = 136, StatusLEDs.B = 0 " +
                "WHERE cl.CommandTypeID = 70 AND cl.StatusID = 7",
                connection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        // =====================================================================
        // procResetPlayers — C# equivalent
        // Called at the start of each turn. Advances ShutDown state machine,
        // applies Circuit Breaker, resets Status, handles death/respawn.
        // =====================================================================
        public void ResetPlayers()
        {
            // Read LaserDamage (respawn damage = LaserDamage * 2)
            int laserDamage  = GetIntFromDB("SELECT iValue FROM CurrentGameData WHERE sKey='LaserDamage'");
            int useDamage    = laserDamage * 2;

            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            // 1. Advance ShutDown state machine for all robots with ShutDown > 0
            //    (join RobotShutDown to get NextState)
            using (var cmd = new MySqlCommand(
                "UPDATE Robots " +
                "INNER JOIN RobotShutDown ON Robots.`ShutDown` = RobotShutDown.ShutDownID " +
                "SET `ShutDown` = NextState " +
                "WHERE Robots.`ShutDown` > 0",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // 2. Circuit Breaker (OptionID=9): auto-shutdown at Damage >= 3
            using (var cmd = new MySqlCommand(
                "UPDATE Robots " +
                "INNER JOIN RobotOptions ON Robots.RobotID = RobotOptions.RobotID AND RobotOptions.OptionID = 9 " +
                "SET ShutDown = 4 " +
                "WHERE Damage >= 3",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // 3. Set Status=2 (Ready to Program) for non-shutdown robots
            //    Robots_BEFORE_UPDATE trigger logic: ShutDown=4 → Damage=0, ShutDown=2; ShutDown=2 → Status=9
            //    We apply the ShutDown=4 transition inline here.
            using (var cmd = new MySqlCommand(
                "UPDATE Robots SET Status = 2 WHERE ShutDown = 0",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Apply trigger logic for ShutDown state transitions before writing
            // ShutDown=4 → Damage=0, ShutDown=2; ShutDown=2 → Status=9
            // We do this inline since triggers are being removed.
            using (var cmd = new MySqlCommand(
                "UPDATE Robots SET Damage = 0, ShutDown = 2 WHERE ShutDown = 4",
                connection))
            {
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new MySqlCommand(
                "UPDATE Robots SET `Status` = 9 WHERE ShutDown = 2",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // 4. Mark robots with Damage > 9 or already Dead as Dead (Status=11)
            using (var cmd = new MySqlCommand(
                "UPDATE Robots SET Damage = 10, ShutDown = 0, Lives = Lives - 1, Status = 11 " +
                "WHERE Damage > 9 OR Status = 11",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Discard played cards for dead/shutdown robots
            using (var cmd = new MySqlCommand(
                "UPDATE MoveCards " +
                "INNER JOIN Robots ON MoveCards.Owner = Robots.RobotID " +
                "SET PhasePlayed = 0, Owner = -1 " +
                "WHERE PhasePlayed > 5 AND (Robots.Status = 11 OR Robots.ShutDown > 0)",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // 5. Superior Archive Copy (OptionID=49): dead robots with lives > 0 respawn undamaged
            using (var cmd = new MySqlCommand(
                "UPDATE Robots " +
                "INNER JOIN RobotOptions ON Robots.RobotID = RobotOptions.RobotID AND RobotOptions.OptionID = 49 " +
                "SET Damage = 0, ShutDown = 0, " +
                "    CurrentPosRow = ArchivePosRow, CurrentPosCol = ArchivePosCol, CurrentPosDir = ArchivePosDir, " +
                "    Status = 1, PositionValid = 0 " +
                "WHERE Status = 11 AND Lives > 0",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // 6. Standard respawn: dead robots with lives > 0 respawn with laser damage penalty
            using (var cmd = new MySqlCommand(
                $"UPDATE Robots " +
                $"SET Damage = {useDamage}, ShutDown = 0, " +
                $"    CurrentPosRow = ArchivePosRow, CurrentPosCol = ArchivePosCol, CurrentPosDir = ArchivePosDir, " +
                $"    Status = 1, PositionValid = 0 " +
                $"WHERE Status = 11 AND Lives > 0",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // 7. Reset RobotOptions.PhasePlayed
            using (var cmd = new MySqlCommand(
                "UPDATE RobotOptions SET PhasePlayed = 0",
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Sync status LEDs
            SetStatus();
        }

        // =====================================================================
        // procCurrentPosSave — C# equivalent
        // Snapshots current Robots, MoveCards, and RobotOptions into History tables.
        // =====================================================================
        public void CurrentPosSave()
        {
            int gameID = GetIntFromDB("SELECT iValue FROM CurrentGameData WHERE sKey='GameDataID'");
            int turn   = GetIntFromDB("SELECT iValue FROM CurrentGameData WHERE sKey='Turn'");

            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();
            try
            {
                // HistoryRobots
                using (var cmd = new MySqlCommand(
                    $"DELETE FROM HistoryRobots WHERE GameID = {gameID} AND Turn = {turn}",
                    connection, tx))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new MySqlCommand(
                    $"INSERT INTO HistoryRobots " +
                    $"(GameID, Turn, RobotID, OperatorName, RobotBaseID, RobotBodyID, " +
                    $" CurrentFlag, Lives, Damage, ShutDown, Computer, Score, Status, " +
                    $" CurrentPosRow, CurrentPosCol, CurrentPosDir, " +
                    $" ArchivePosRow, ArchivePosCol, ArchivePosDir, Priority) " +
                    $"SELECT {gameID}, {turn}, RobotID, OperatorName, RobotBaseID, RobotBodyID, " +
                    $"       CurrentFlag, Lives, Damage, ShutDown, Computer, Score, Status, " +
                    $"       CurrentPosRow, CurrentPosCol, CurrentPosDir, " +
                    $"       ArchivePosRow, ArchivePosCol, ArchivePosDir, Priority " +
                    $"FROM Robots",
                    connection, tx))
                {
                    cmd.ExecuteNonQuery();
                }

                // HistoryMoveCards
                using (var cmd = new MySqlCommand(
                    $"DELETE FROM HistoryMoveCards WHERE GameID = {gameID} AND Turn = {turn}",
                    connection, tx))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new MySqlCommand(
                    $"INSERT INTO HistoryMoveCards (GameID, Turn, CardID, Owner, PhasePlayed, Locked) " +
                    $"SELECT {gameID}, {turn}, CardID, Owner, PhasePlayed, Locked " +
                    $"FROM MoveCards WHERE Owner > 0",
                    connection, tx))
                {
                    cmd.ExecuteNonQuery();
                }

                // HistoryRobotOptions
                using (var cmd = new MySqlCommand(
                    $"DELETE FROM HistoryRobotOptions WHERE GameID = {gameID} AND Turn = {turn}",
                    connection, tx))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new MySqlCommand(
                    $"INSERT INTO HistoryRobotOptions " +
                    $"(GameID, Turn, RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue) " +
                    $"SELECT {gameID}, {turn}, RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue " +
                    $"FROM RobotOptions",
                    connection, tx))
                {
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        // =====================================================================
        // procCurrentPosLoad — C# equivalent
        // Restores Robots, MoveCards, and RobotOptions from History tables.
        // =====================================================================
        public void CurrentPosLoad()
        {
            int gameID = GetIntFromDB("SELECT iValue FROM CurrentGameData WHERE sKey='GameDataID'");
            int turn   = GetIntFromDB("SELECT iValue FROM CurrentGameData WHERE sKey='Turn'");

            // Clear live tables (mirrors procResetGame minus CurrentGameData copy)
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();
            try
            {
                foreach (var tbl in new[] { "MoveCards", "CommandList", "RobotOptions", "StatusLEDs", "Robots" })
                {
                    using var del = new MySqlCommand($"DELETE FROM {tbl}", connection, tx);
                    del.ExecuteNonQuery();
                }

                // Restore Robots
                using (var cmd = new MySqlCommand(
                    $"INSERT INTO Robots " +
                    $"(RobotID, OperatorName, RobotBaseID, RobotBodyID, " +
                    $" CurrentFlag, Lives, Damage, ShutDown, Computer, Score, Status, " +
                    $" CurrentPosRow, CurrentPosCol, CurrentPosDir, " +
                    $" ArchivePosRow, ArchivePosCol, ArchivePosDir, Priority, PositionValid) " +
                    $"SELECT RobotID, OperatorName, RobotBaseID, RobotBodyID, " +
                    $"       CurrentFlag, Lives, Damage, ShutDown, Computer, Score, Status, " +
                    $"       CurrentPosRow, CurrentPosCol, CurrentPosDir, " +
                    $"       ArchivePosRow, ArchivePosCol, ArchivePosDir, Priority, 0 " +
                    $"FROM HistoryRobots WHERE GameID = {gameID} AND Turn = {turn}",
                    connection, tx))
                {
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }

            // Rebuild card deck and restore card state
            GameNewAddCards();

            using var connection2 = new MySqlConnection(_connectionString);
            connection2.Open();
            using var tx2 = connection2.BeginTransaction();
            try
            {
                using (var cmd = new MySqlCommand(
                    $"UPDATE MoveCards " +
                    $"INNER JOIN HistoryMoveCards ON MoveCards.CardID = HistoryMoveCards.CardID " +
                    $"SET MoveCards.Owner = HistoryMoveCards.Owner, " +
                    $"    MoveCards.PhasePlayed = HistoryMoveCards.PhasePlayed, " +
                    $"    MoveCards.Locked = HistoryMoveCards.Locked " +
                    $"WHERE HistoryMoveCards.GameID = {gameID} AND HistoryMoveCards.Turn = {turn}",
                    connection2, tx2))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new MySqlCommand(
                    $"INSERT INTO RobotOptions " +
                    $"(RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue) " +
                    $"SELECT RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue " +
                    $"FROM HistoryRobotOptions WHERE GameID = {gameID} AND Turn = {turn}",
                    connection2, tx2))
                {
                    cmd.ExecuteNonQuery();
                }

                tx2.Commit();
            }
            catch
            {
                tx2.Rollback();
                throw;
            }
        }

        // =====================================================================
        // procVerifyPosition — C# equivalent
        // Sets PositionValid=1 if direction != 0, row != 0, col != 0,
        // and no duplicate robot positions exist.
        // =====================================================================
        public void VerifyPosition(int robotID)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            int posRow = 0, posCol = 0, posDir = 0;
            using (var cmd = new MySqlCommand(
                "SELECT CurrentPosRow, CurrentPosCol, CurrentPosDir FROM Robots WHERE RobotID = @id",
                connection))
            {
                cmd.Parameters.AddWithValue("@id", robotID);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    posRow = reader.GetInt32(0);
                    posCol = reader.GetInt32(1);
                    posDir = reader.GetInt32(2);
                }
            }

            int duplicates = 0;
            using (var cmd = new MySqlCommand(
                "SELECT COUNT(RobotID) FROM Robots WHERE CurrentPosRow = @row AND CurrentPosCol = @col",
                connection))
            {
                cmd.Parameters.AddWithValue("@row", posRow);
                cmd.Parameters.AddWithValue("@col", posCol);
                duplicates = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            }

            int passed = (posDir == 0 || posRow == 0 || posCol == 0 || duplicates > 1) ? 0 : 1;

            using (var cmd = new MySqlCommand(
                "UPDATE Robots SET PositionValid = @passed WHERE RobotID = @id",
                connection))
            {
                cmd.Parameters.AddWithValue("@passed", passed);
                cmd.Parameters.AddWithValue("@id", robotID);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
