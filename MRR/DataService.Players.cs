using System;
using System.Data;
using System.Linq;
using System.Text;
using System.IO;
using MySqlConnector;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using MRR.Data;
using MRR.Data.Entities;
using System.Xml.Serialization;
using MRR;
using MRR.Devices;

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

                // Reads straight off Robots' own denormalized RobotName/RobotColor/RobotColorFG/
                // IPAddress/DirectionAdjustment columns (kept current by RefreshRobotDenormalizedFields/
                // SelectSeat/UpdateRobotIPAddress) instead of joining RobotBodies/RobotBases/
                // SeatOrientation fresh -- those were INNER JOINs, so a StartGame() placeholder
                // row (RobotBodyID NULL, PlayerSeat 0, not yet claimed via SelectSeat) would
                // silently disappear from AllPlayers and never get connected to its physical
                // robot. See install/todo.md "Operator Data Setup".
                string strSQL = @"SELECT r.RobotID, r.RobotName, r.RobotColor, r.RobotColorFG,
                       r.OperatorName, r.Password, r.PlayerSeat, r.IPAddress,
                       r.DirectionAdjustment AS PlayerViewDirection
                FROM Robots r
                ORDER BY r.RobotID";

                var loadplayers = this.GetQueryResults(strSQL);

                // Prune the robot connection registry against this same roster, by RobotID
                // alone -- no separate query, and no new connections opened here. Refresh()
                // only drops entries for robots no longer present; a robot only ever gets
                // (re)connected through an explicit action (GameController's Connect/Connect
                // All, or a new game's start -- see RobotConnections.Reconnect/ReconnectAll).
                // Refresh() keeps a live socket alive across this rebuild (it lives on
                // RobotConnections, not on the Player objects below), so discarding
                // _allPlayers here can no longer orphan or duplicate a connection.
                _robotConnections.Refresh(loadplayers.Rows.Cast<DataRow>().Select(r => (int)r["RobotID"]));

                foreach (DataRow row in loadplayers.Rows)
                {
                    int robotId = (int)row["RobotID"];
                    players.Add(new Player()
                    {
                        ID                  = robotId,
                        PlayerSeat          = (int)row["PlayerSeat"],
                        Name                = row["RobotName"].ToString() ?? "",
                        Color               = row["RobotColor"].ToString() ?? "FFFFFF",
                        ForeColor           = row["RobotColorFG"].ToString() ?? "000000",
                        Password            = row["Password"]?.ToString() ?? "",
                        IPAddress           = row["IPAddress"].ToString(),
                        PlayerViewDirection = Convert.ToInt32(row["PlayerViewDirection"]),
                        AllGameCards        = GameCards,
                        Connection          = _robotConnections.Get(robotId),
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
                   r.Priority, r.Energy, r.Score, r.PositionValid, r.RespawnID
            FROM Robots r
            JOIN RobotBodies rb ON r.RobotBodyID = rb.RobotBodyID
            JOIN SeatOrientation so ON r.PlayerSeat = so.SeatID
            ORDER BY r.RobotID";

            var rows = GetQueryResults(strSQL);
            foreach (DataRow row in rows.Rows)
            {
                var loadedPos = new RobotLocation((Direction)(int)row["Dir"], (int)row["X"], (int)row["Y"]);
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
                    CurrentPos          = loadedPos,
                    // NextPos starts equal to CurrentPos ("no move planned yet") rather than
                    // the RobotLocation() default -- CommandItem's constructor trusts NextPos
                    // to reflect "not moving" whenever no move is in flight (see
                    // CommandList.cs), so it must never start out looking like an unset
                    // sentinel while CurrentPos already holds a real position.
                    NextPos             = new RobotLocation(loadedPos),
                    ArchivePosCol       = (int)row["AX"],
                    ArchivePosRow       = (int)row["AY"],
                    Priority            = (int)row["Priority"],
                    Energy              = (int)row["Energy"],
                    Score               = (int)row["Score"],
                    PositionValid       = (int)row["PositionValid"] != 0,
                    RespawnID           = (int)row["RespawnID"],
                    AllGameCards        = GameCards,
                });
            }

            return result;
        }

        // Denormalizes the RobotStatus/RobotDirections/MoveCards/CommandList joins onto Robots itself
        // (StatusColor, LEDColor, PlayerStatus, sDir, FlagEnergyCards, StatusToShow, PlayerMsg,
        // ConnectStatusColor, ConnectStatusDesc) so other reads (GetRobotsFromTable, SetStatus)
        // can use the plain Robots columns instead of re-joining. RobotStatus is joined twice --
        // once (rs) for the folded gameplay status IF(connected, Status, NotActive) that
        // StatusColor/LEDColor/PlayerStatus/StatusToShow reflect, and once (cs) for
        // ConnectStatusID's own color/description, which is never folded with anything.
//                JOIN RobotStatus rs ON IF(r.ConnectStatusID = {(int)tPlayerStatus.RobotConnected}, r.Status, 10) = rs.RobotStatusID

        public void RefreshRobotDenormalizedFields()
        {
            string updateSQL = $@"UPDATE Robots r
                JOIN RobotStatus rs ON r.Status = rs.RobotStatusID
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
                    r.FlagEnergyCards    = CONCAT(r.CurrentFlag,'/',r.Energy,'/',r.CardCount),
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
        // RobotBases.IPAddress that RobotConnection actually dials (it polls for its own copy
        // by RobotID), so no join is needed here either.
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
                    PositionValid       = (int)row["PositionValid"],
                    Priority            = (int)row["Priority"],
                    ShutDown            = (int)row["ShutDown"],
                    PlayerSeat          = (int)row["PlayerSeat"],
                    Energy              = (int)row["Energy"],
                    FlagEnergyCards     = row["FlagEnergyCards"].ToString() ?? "",
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
        /// One placeholder Robots row for a physical robot base, called from StartGame() once per
        /// RobotBases row that has a matching board start square. RobotBodyID is left NULL (not
        /// the column's schema default of 0, which has no matching RobotBodies row and would
        /// fail the FK) until a player claims this seat via SelectSeat.
        /// </summary>
        public void InsertPlaceholderRobot(int baseId, string ip, int row, int col, int dir)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var insert = new MySqlCommand(
                @"insert into Robots (RobotID, RobotBaseID, RobotBodyID, OperatorName, RobotName, RobotColor, RobotColorFG,
                      `Status`, IPAddress, CurrentPosRow, CurrentPosCol, CurrentPosDir, ArchivePosRow, ArchivePosCol, ArchivePosDir, PositionValid)
                  values (@baseId, @baseId, NULL, 'Seat ?', @robotName, '000000', 'FFFFFF',
                      0, @ip, @row, @col, @dir, @row, @col, @dir, 0)",
                connection);
            insert.Parameters.AddWithValue("@baseId", baseId);
            insert.Parameters.AddWithValue("@robotName", "Start " + baseId);
            insert.Parameters.AddWithValue("@ip", ip);
            insert.Parameters.AddWithValue("@row", row);
            insert.Parameters.AddWithValue("@col", col);
            insert.Parameters.AddWithValue("@dir", dir);
            insert.ExecuteNonQuery();
        }

        /// <summary>
        /// Setup-phase (GameState==1) broadcast data: which seat may currently act, which robot
        /// bodies are still unclaimed, and which placeholder rows (StartGame's one-per-RobotBase
        /// rows, keyed by RobotID) are still waiting for a player. "Claimed" means Status==1 --
        /// see SelectSeat below, which is the only thing that sets it during this phase.
        /// </summary>
        public GameConfigData BuildGameConfig()
        {
            var config = new GameConfigData
            {
                PlayerToSelect = GetIntFromDB("Select Count(*) from Robots where Status = 1") + 1,
                AvailableStartPositions = GetIntList(
                    "Select RobotID from Robots where Status <> 1 order by RobotID").ToList(),
            };

            var bodies = GetQueryResults(
                "Select RobotBodyID, Name, Color, ColorFG from RobotBodies " +
                "where BodyActive > 0 and RobotBodyID not in (Select RobotBodyID from Robots where Status = 1) " +
                "order by RobotBodyID");
            foreach (DataRow row in bodies.Rows)
            {
                config.AvailableRobots.Add(new AvailableRobotBody
                {
                    RobotBodyID = (int)row["RobotBodyID"],
                    Name        = row["Name"].ToString() ?? "",
                    Color       = row["Color"].ToString() ?? "",
                    ColorFG     = row["ColorFG"].ToString() ?? "",
                });
            }

            return config;
        }

        /// <summary>
        /// Setup-phase (GameState==1) claim: seat picks a still-open start position (a placeholder
        /// Robots row, RobotID==the board's starting-square number, created by StartGame) and an
        /// unclaimed RobotBodyID, and both commit in one step -- there's no separate Save
        /// (install/todo.md "Operator Data Setup"). The WHERE clause re-checks turn order and
        /// both uniqueness constraints atomically, so a stale/racing client can't win against a
        /// faster one: 0 rows affected just means "someone else got there first," not a specific
        /// error. No player-entered name is collected -- OperatorName is just "Seat {seat}".
        /// </summary>
        public bool SelectSeat(int seat, int startPosition, int robotBodyId)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var update = new MySqlCommand(
                @"UPDATE Robots r
                  JOIN RobotBodies rb ON rb.RobotBodyID = @robotBodyId
                  SET r.RobotBodyID   = @robotBodyId,
                      r.RobotName     = rb.Name,
                      r.RobotColor    = rb.Color,
                      r.RobotColorFG  = rb.ColorFG,
                      r.OperatorName  = CONCAT('Seat ', @seat),
                      r.Priority      = @seat,
                      r.PlayerSeat    = @seat,
                      r.PositionValid = 1,
                      r.Status        = 1
                  WHERE r.RobotID = @startPosition",
/*                    AND r.Status <> 1
                    AND @seat = (Select Coalesce(Min(RobotID),0) from Robots where Status <> 1)
                    AND NOT EXISTS (Select 1 from Robots r2 where r2.RobotBodyID = @robotBodyId and r2.Status = 1)",*/
                connection);
            update.Parameters.AddWithValue("@robotBodyId", robotBodyId);
            update.Parameters.AddWithValue("@seat", seat);
            update.Parameters.AddWithValue("@startPosition", startPosition);
            bool claimed = update.ExecuteNonQuery() > 0; 

            if (claimed) RefreshRobotDenormalizedFields();
            return claimed;
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
        /// Section 8 (install/todo.md) "Update IP": writes the robot's IP address to both
        /// RobotBases.IPAddress -- the column RobotConnection actually reads when it (re)dials --
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

        /// <summary>
        /// Writes through to Robots.ConnectStatusID -- and, in the same statement,
        /// ConnectStatusColor/ConnectStatusDesc from the matching RobotStatus row -- so the
        /// connection screen reflects whether we actually have a live socket to the robot.
        /// Deliberately narrower than RefreshRobotDenormalizedFields: this touches only the
        /// three connect-status columns for one robot, never the gameplay Status/StatusColor/
        /// LEDColor columns -- a connection dropping or recovering must never look like a
        /// gameplay status change. Callers that also want the change broadcast immediately
        /// (e.g. GameController.SetRobotConnectStatus) do that themselves afterward.
        /// </summary>
        public void SetRobotConnectStatus(int robotID, tPlayerStatus status)
        {
            ExecuteSQL($@"
                UPDATE Robots r
                JOIN RobotStatus cs ON cs.RobotStatusID = {(int)status}
                SET r.ConnectStatusID    = {(int)status},
                    r.ConnectStatusColor = cs.StatusColor,
                    r.ConnectStatusDesc  = cs.ShortDescription
                WHERE r.RobotID = {robotID};");
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
        // Called at the start of each turn. Respawns every dead robot (Status=11) at its
        // nearest Respawn square and resets per-turn option state. Renegade rules don't track
        // ShutDown/Circuit Breaker/Lives (see install/todo.md, ALLPLAYERS_REMOVAL_DESIGN.md
        // §11) -- the original SQL's steps for those are gone rather than kept as dead code.
        // =====================================================================
        public void ResetPlayers()
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            // Respawn: every dead robot moves to the nearest Respawn square (SquareAction.
            // Respawn), recording which one via RespawnID -- RespawnRobotAtRebootToken falls
            // back to ArchivePos if the board has none. This is the only place a robot still
            // gets moved off its death square (see that method's remarks).
            foreach (int robotID in GetIntList("SELECT RobotID FROM Robots WHERE Status = 11"))
            {
                RespawnRobotAtRebootToken(robotID);
            }

            // Reset RobotOptions.PhasePlayed
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
                    $" CurrentFlag, ShutDown, Computer, Score, Status, " +
                    $" CurrentPosRow, CurrentPosCol, CurrentPosDir, " +
                    $" ArchivePosRow, ArchivePosCol, ArchivePosDir, Priority) " +
                    $"SELECT {gameID}, {turn}, RobotID, OperatorName, RobotBaseID, RobotBodyID, " +
                    $"       CurrentFlag, ShutDown, Computer, Score, Status, " +
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
                    $" CurrentFlag, ShutDown, Computer, Score, Status, " +
                    $" CurrentPosRow, CurrentPosCol, CurrentPosDir, " +
                    $" ArchivePosRow, ArchivePosCol, ArchivePosDir, Priority, PositionValid) " +
                    $"SELECT RobotID, OperatorName, RobotBaseID, RobotBodyID, " +
                    $"       CurrentFlag, ShutDown, Computer, Score, Status, " +
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

            // The rebuild above (GameNewAddCards, then the MoveCards/RobotOptions restores)
            // touches MoveCards and RobotOptions directly via raw SQL, leaving the in-memory
            // GameCards/OptionCards collections stale -- reload everything rather than hand-sync
            // three separate tables' worth of restored rows.
            ReloadAllData();
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

        public void SaveToHistory()
        {
            ExecuteSQL($@"delete from HistoryRobotTurns where Turn = {Turn}");

            // save to new table HistoryRobotTurns
            ExecuteSQL($@"INSERT INTO HistoryRobotTurns (Turn, RobotID, StartRow, StartCol, StartDir, MoveCards)
                    Select {Turn}, RobotID, CurrentPosRow, CurrentPosCol, CurrentPosDir, CardsPlayed FROM Robots ");

        }

        // =====================================================================
        // procSetRobotDirection — C# equivalent (fixed: the original SQL declared
        // p_RobotID but referenced an undefined p_Robot in its body -- see
        // .claude/agents/mrr-database.md's "known bug" note).
        //
        // Direction-picker arrow (index.html): each tap cycles CurrentPosDir but does NOT
        // touch PositionValid, so a player can freely cycle through directions before
        // committing -- see ConfirmRobotDirection below, the "Set Direction" button.
        // =====================================================================
        public void SetRobotDirection(int robotID, int direction)
        {
            ExecuteSQL(
                $"UPDATE Robots SET CurrentPosDir = {direction} WHERE RobotID = {robotID}");
        }

        // =====================================================================
        // PositionValid is a 3-state flag, not a bool:
        //   0 = not set    -- CurrentPosDir needs a player's attention: set on a fresh respawn
        //                      (ResetPlayers(), reboot token placement) and by the GM's "Reload
        //                      Position" action (CurrentPosLoad()), neither of which has any
        //                      actual player choice behind the restored facing
        //   1 = user set   -- either a player (or GM) actively picked a direction via "Set
        //                      Direction", or (at game start, GameController.StartGame()) the
        //                      board's PlayerStart rotation was accepted as a reasonable
        //                      default without forcing every player through the picker first
        //   2 = locked      -- the turn using that direction already started executing
        //                      (LockAllRobotDirections below); no longer changeable, and the
        //                      picker stops appearing even in GM mode (js/loadrobots.js's
        //                      updateDirectionPicker)
        //
        // "Set Direction" button (index.html): this is the only place that ever moves a robot
        // from 0 to 1 (as last set by SetRobotDirection above), and (in GM mode only) the only
        // place that can move it back from 1 to 0 -- see ConfirmRobotDirection's caller,
        // js/loadrobots.js's confirmDirection.
        // =====================================================================
        public void ConfirmRobotDirection(int robotID, int positionValid)
        {
            ExecuteSQL($"UPDATE Robots SET PositionValid = {positionValid} WHERE RobotID = {robotID}");
        }

        // =====================================================================
        // Reboot mechanic (install/todo.md Section 1): called from ResetPlayers() for every
        // dead (Status=11) robot at the start of the next turn -- the earlier "respawn the
        // instant 'Remove Robot' is confirmed" call (DataService.Commands.cs's
        // ProcessDbCommand, the SquareAction.SetButtonText case) is disabled, so this is the
        // only place a robot is actually moved off its death square now. Moves the robot to the
        // nearest square carrying a SquareAction.Respawn action (Manhattan distance from where
        // it died -- Robots.CurrentPosRow/Col already holds the death square) and records which
        // one via RespawnID (that action's Parameter -- 1=A, 2=B, 3=C, ...), or falls back to
        // ArchivePos (RespawnID left at 0) if the board has no Respawn squares at all --
        // ArchivePos is the closest existing concept in this schema to "the robot's original
        // start square" (StartGame()/InsertPlaceholderRobot() seeds both CurrentPos and
        // ArchivePos to the same starting square, and nothing but an explicit
        // SquareAction.Archive board trigger -- e.g. touching a flag -- moves ArchivePos after
        // that).
        //
        // Sets PositionValid = 0, same as the GM's "Reload Position" action, so the phone UI's
        // existing direction picker shows up for this robot next turn with no further wiring --
        // GameController.NextState()'s state 4->5 gate (AllRobotDirectionsChosen) already
        // blocks the turn from starting until it's set. The direction itself is picked during
        // programming, not decided here.
        // =====================================================================
        public void RespawnRobotAtRebootToken(int robotID)
        {
            var robotRow = GetQueryResults(
                "SELECT CurrentPosRow, CurrentPosCol, ArchivePosRow, ArchivePosCol, ArchivePosDir " +
                $"FROM Robots WHERE RobotID = {robotID}");
            if (robotRow.Rows.Count == 0) return;

            int fromRow = (int)robotRow.Rows[0]["CurrentPosRow"];
            int fromCol = (int)robotRow.Rows[0]["CurrentPosCol"];

            BoardElement? nearestRespawn = BoardLoadFromDB(BoardID).BoardElements
                .Where(be => be.ActionList.Any(al => al.SquareAction == SquareAction.Respawn))
                .OrderBy(be => Math.Abs(be.BoardCol - fromCol) + Math.Abs(be.BoardRow - fromRow))
                .FirstOrDefault();

            int newRow, newCol, newDir, respawnID;
            if (nearestRespawn != null)
            {
                newRow = nearestRespawn.BoardRow;
                newCol = nearestRespawn.BoardCol;
                // The player picks any facing next turn via the direction picker (PositionValid
                // below), so this is only a starting seed for the arrow, not a real choice.
                newDir = (int)nearestRespawn.Rotation;
                respawnID = nearestRespawn.ActionList.First(al => al.SquareAction == SquareAction.Respawn).Parameter;
            }
            else
            {
                newRow = (int)robotRow.Rows[0]["ArchivePosRow"];
                newCol = (int)robotRow.Rows[0]["ArchivePosCol"];
                newDir = (int)robotRow.Rows[0]["ArchivePosDir"];
                respawnID = 0;
            }

            // Status must move off Dead (11) here, not just PositionValid off 0 -- PlayerState.
            // Active is derived fresh from Status on every DB reload (GetPlayerStatesFromDB:
            // Active = Status != NotActive(10)), not persisted itself, and only stays false for
            // the remainder of *this* turn because CreateCommands mutates the same in-memory
            // PlayerState the whole turn through. Left at Dead, the robot would misread as
            // Active again next turn (Status(11) != 10) while still genuinely needing to sit
            // out programming until PositionValid clears -- ReadyToProgram is the same status a
            // normal robot carries between turns, so this just rejoins it at that point.
            ExecuteSQL(
                $"UPDATE Robots SET CurrentPosRow = {newRow}, CurrentPosCol = {newCol}, " +
                $"CurrentPosDir = {newDir}, PositionValid = 0, ShutDown = 0, RespawnID = {respawnID}, " +
                $"Status = {(int)tPlayerStatus.ReadyToProgram} " +
                $"WHERE RobotID = {robotID}");
        }

        /// <summary>
        /// True once every robot has at least picked a facing direction (PositionValid != 0,
        /// i.e. 1 or 2 -- a robot already locked from a previous turn still counts). Gates the
        /// state 4 -> 5 transition (GameController.NextState()) so a turn can't be locked and
        /// executed while a robot's facing hasn't actually been looked at (PositionValid=0).
        /// StartGame() itself already sets PositionValid=1 for everyone, so in practice this
        /// only ever blocks after something resets a robot back to 0 later: a respawn
        /// (ResetPlayers()) or the GM's "Reload Position" action (CurrentPosLoad()).
        /// </summary>
        public bool AllRobotDirectionsChosen() =>
            GetIntFromDB("Select Count(*) from Robots where PositionValid = 0") == 0;

        /// <summary>
        /// Locks every robot's direction (PositionValid = 2) once AllRobotDirectionsChosen()
        /// passes and the turn is about to execute (GameController.NextState(), state 4 -> 5).
        /// The direction picker then stops appearing at all, even in GM mode, until the reboot
        /// mechanic resets a specific robot back to 0 (the same way ResetPlayers() already does
        /// for a fresh respawn).
        /// </summary>
        public void LockAllRobotDirections() => ExecuteSQL("Update Robots set PositionValid = 2");
    }
}
