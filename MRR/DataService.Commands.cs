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

namespace MRR.Services
{
    /// <summary>
    /// DataService — CommandList: building a turn request, storing a plan, applying command effects.
    ///
    /// Part of the step 4 split (API_DECOMPOSITION_DESIGN.md section 4): DataService is
    /// one class doing several jobs, so it is first separated by concern into partials.
    /// Splitting the file changes nothing semantically, but it makes each concern's real
    /// dependencies visible, which is what the repository extraction needs.
    /// </summary>
    public partial class DataService
    {

        /// <summary>
        /// Stores a planned turn's commands, replacing whatever was there for that turn.
        /// Master's job, not the planner's: CommandList is a Master-owned table.
        ///
        /// One DbContext and one SaveChanges for the whole batch. The previous version
        /// created a DbContext inside the per-command loop and saved each command
        /// individually -- roughly 130 contexts and 130 round trips per turn.
        /// </summary>
        /// <summary>
        /// Assembles everything the planner needs for one turn. Refreshes live state first,
        /// so the plan is made from what is actually on the board.
        /// </summary>
        public TurnRequest BuildTurnRequest()
        {
            ReloadAllData();

            var request = new TurnRequest
            {
                Turn             = Turn,
                Phase            = Phase,
                PhaseCount       = PhaseCount,
                GameState        = GameState,
                BoardID          = BoardID,
                BoardFileName    = BoardFileName,
                TotalFlags       = TotalFlags,
                LaserDamage      = LaserDamage,
                GameType         = GameType,
                OptionsOnStartup = OptionsOnStartup,
                Board            = BoardLoadFromDB(BoardID),
                Players          = GetPlayerStatesFromDB(),
                GameCards        = GameCards,
                OptionCards      = OptionCards,
            };

            foreach (var player in request.Players)
                request.DrawPiles[player.ID] = BuildDrawPile(player.ID);

            return request;
        }

        public int PersistCommands(CommandList commands, int turn)
        {
            using var ctx = CreateDbContext();
            using var transaction = ctx.Database.BeginTransaction();

            // Delete and insert in ONE transaction. The turn's commands are the durable
            // record used to resume after a restart, so a half-written CommandList is worse
            // than no change at all: replacing the rows must be all-or-nothing.
            ctx.Database.ExecuteSqlRaw(
                "DELETE FROM CommandList WHERE Turn = {0} AND Phase > 0;", turn);

            foreach (var command in commands)
            {
                command.Turn = turn;
                ctx.CommandItems.Add(command);
            }
            ctx.SaveChanges();

            transaction.Commit();
            return commands.Count;
        }

        /// <summary>
        /// Fallback used only when no PendingCommands loop is live (GameController.
        /// ProcessDbCommand routes there first). Loads the command straight from the DB --
        /// there is no in-memory copy to keep in sync when nothing is iterating one.
        /// </summary>
        public int ProcessDbCommand(int p_CommandID, int p_NewStatus)
        {
            using var db = CreateDbContext();
            var command = db.CommandItems.FirstOrDefault(c => c.CommandID == p_CommandID);
            Console.WriteLine($"ProcessDbCommand: commandID={p_CommandID}, newStatus={p_NewStatus}, command={command}");
            if (command == null)
                return -1; // or throw an exception if preferred

            return ProcessDbCommand(command, p_NewStatus);
        }
        /// <summary>
        /// C# equivalent of funcProcessCommand(p_CommandID, p_NewStatus).
        /// Loads the command row, performs any DB-side effect based on CommandTypeID,
        /// applies position updates when p_NewStatus==5, then writes the final StatusID
        /// back to CommandList. Returns the resulting StatusID.
        /// Pass p_NewStatus=-1 to auto-complete (equivalent to SQL default).
        /// </summary>
        public int ProcessDbCommand(CommandItem p_Command, int p_NewStatus)
        {
            //int cType       = (int)p_Command.CommandType;
            int cRobotID    = p_Command.RobotID;
            int cParameter  = p_Command.Value;
            int cParameterB = p_Command.ValueB;
            int cRow        = p_Command.PositionRow;
            int cCol        = p_Command.PositionCol;
            int cDir        = p_Command.PositionDir;

            if (p_NewStatus == -1)
                p_NewStatus = 6; // command complete

            // Process side-effects by CommandTypeID
            switch (p_Command.CommandType)
            {
                case SquareAction.PlayerLocation: // Player Location — handled below in the status==5 block
                    p_NewStatus = 5;
                    break;

                case SquareAction.Damage: // Set Damage
                    ExecuteSQL($"Update Robots set Damage = {cParameter} " +
                        $" where RobotID = {cRobotID}");
                    break;

                case SquareAction.Archive: // Set Archive position
                    ExecuteSQL($"Update Robots set Damage = {cParameter}, " +
                        $" ArchivePosRow = {cRow}, " +
                        $" ArchivePosCol = {cCol}, " +
                        $" ArchivePosDir = {cDir}, " +
                        $" where RobotID = {cRobotID}");
                    break;

                case SquareAction.Flag: // Set Current Flag
                    ExecuteSQL($"UPDATE Robots SET CurrentFlag = {cParameter} " +
                        $" WHERE RobotID = {cRobotID}");
                    break;

                case SquareAction.Option: // Deal option card to robot
                {
                    int optionID = cParameter;
                    if (optionID == 0)
                        optionID = GetNextOption(cRobotID);
                    ExecuteSQL(
                        $"INSERT INTO RobotOptions (RobotID, OptionID, DestroyWhenDamaged, Quantity, IsActive, PhasePlayed, DataValue) " +
                        $"SELECT {cRobotID}, OptionID, false, Quantity, false, 0, 0 " +
                        $"FROM `Options` WHERE OptionID = {optionID}");
                    break;
                }

                case SquareAction.LostLife: // Set Lives
                    ExecuteSQL($"UPDATE Robots SET Lives = {cParameter} " +
                        $" WHERE RobotID = {cRobotID}");
                    break;

                case SquareAction.DealCard: // Deal card to player (assign card owner)
                    ExecuteSQL(
                        $"UPDATE MoveCards SET Owner = {cRobotID} WHERE CardID = {cParameter}");
                    break;

                case SquareAction.GameWinner: // Game Winner
                    GameState = 11;
                    ExecuteSQL(
                        $"UPDATE CurrentGameData SET iValue = {cRobotID} WHERE iKey = 13");
                    break;

                case SquareAction.Card: // Mark card as executed
                    ExecuteSQL(
                        $"UPDATE MoveCards SET Executed = 1 WHERE CardID = {cParameter} AND Owner = {cRobotID}");
                    ExecuteSQL(
                        $"UPDATE CurrentGameData SET sValue = 'Played Card' WHERE iKey = 21");
                    break;

                case SquareAction.SetPlayerStatus: // Set robot Status
                    ExecuteSQL($"UPDATE Robots SET Status = {cParameter} " +
                        $" WHERE RobotID = {cRobotID}");
                    break;

                case SquareAction.DeathPoints: // Set DamagePoints — column does not exist in schema; log and skip
                    Console.WriteLine($"ProcessDbCommand: DeathPoints is not supported — skipped.");
                    break;

                case SquareAction.DealOptionCard: // no-op in original SQL
                    break;

                case SquareAction.DestroyOptionCard: // Delete Option from player
                    ExecuteSQL(
                        $"DELETE FROM RobotOptions WHERE RobotID = {cRobotID} AND OptionID = {cParameter}");
                    break;

                case SquareAction.OptionCountSet: // Set option quantity
                    ExecuteSQL(
                        $"UPDATE RobotOptions SET Quantity = {cParameterB} WHERE RobotID = {cRobotID} AND OptionID = {cParameter}");
                    break;

                case SquareAction.SetDamagePointTotal: // Set MaxDamage
                    ExecuteSQL(
                        $"UPDATE CurrentGameData SET iValue = {cParameter} WHERE iKey = 17");
                    break;

                case SquareAction.DealSpamCard: // Deal Spam card to player
                    DealSpamToPlayer(cRobotID);
                    break;

                case SquareAction.SetShutDownMode: // Set ShutDown
                    ExecuteSQL($"UPDATE Robots SET ShutDown = {cParameter} " +
                        $" WHERE RobotID = {cRobotID}");
                    break;

                case SquareAction.SetCurrentGameData: // Set CurrentGameData iValue by iKey
                    ExecuteSQL(
                        $"UPDATE CurrentGameData SET iValue = {cParameterB} WHERE iKey = {cParameter}");
                    break;

                case SquareAction.EndOfGame: // End of game
                    GameState = 12;
                    break;

                case SquareAction.DeleteRobot: // Delete robot
                    ExecuteSQL($"DELETE FROM Robots WHERE RobotID = {cRobotID}");
                    break;

                case SquareAction.SetGameState: // Set GameState
                    GameState = cParameter;
                    ExecuteSQL(
                        $"UPDATE CurrentGameData SET iValue = {cRobotID} WHERE iKey = 13");
                    break;

                // The following are no-ops in the SQL original
                case SquareAction.BlockDirection:
                case SquareAction.RobotPush:
                case SquareAction.PhaseStart:
                case SquareAction.PlayOptionCard:
                case SquareAction.BeginBoardEffects:
                case SquareAction.Water:
                case SquareAction.DeletedMove:
                case SquareAction.FireCannon:
                    break;
                case SquareAction.SetButtonText:
                    ExecuteSQL($"UPDATE Robots SET PlayerMsg = '' " +
                        $" WHERE RobotID = {cRobotID}");
                    break;

                case SquareAction.SetEnergy:
                    ExecuteSQL($"UPDATE Robots SET Energy = {cParameter} " +
                        $" WHERE RobotID = {cRobotID}");
                    break;

                default:
                    // Unknown type — no side-effect, fall through to status update
                    break;
            }

            // Status 5 means "move complete — update robot position then mark done"
            if (p_NewStatus == 5)
            {
                if (cCol >= 0 && cRow >= 0)
                {
                    ExecuteSQL($"UPDATE Robots SET CurrentPosRow = {cRow}, " +
                        $" CurrentPosCol = {cCol}, " +
                        $" CurrentPosDir = {cDir}, " +
                        $" Score = {cParameterB} " +
                        $" WHERE RobotID = {cRobotID}");
                    //Console.WriteLine($"ProcessDbCommand: Robot {cRobotID} moved to row={cRow}, col={cCol}, dir={cDir}, score={cParameterB}");
                }
                p_NewStatus = 6; // command complete
            }

            // Write final status back to CommandList
            if (p_Command.CommandID > 0)
                ExecuteSQL($"UPDATE CommandList SET StatusID = {p_NewStatus} WHERE CommandID = {p_Command.CommandID}");

            return p_NewStatus;
        }
    }
}
