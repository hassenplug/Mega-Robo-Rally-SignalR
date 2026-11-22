using MRR.Bot;

namespace MRR.Controller
{
    /// <summary>
    /// Computer players (documents/COMPUTER_PLAYER_DESIGN.md §5.7, §5.8).
    ///
    /// M1: on entering programming (state 4) every computer seat gets a legal Level 1 program
    /// at once, inside NextState() and before its state-4 gate runs, so the gate never waits
    /// on a computer. Every level plays this program until the search arrives in M2.
    /// </summary>
    public partial class GameController
    {
        private static readonly Random s_botRandom = new();

        /// <summary>
        /// Programs every computer seat that still needs it (DataService.GetComputersToProgram).
        /// Called from NextState() right after SetGameState(4), from states 3 and 15.
        ///
        /// A computer player must never stall the game: any failure is logged and the robot is
        /// left as it was -- the GM can then use Sit Out for that seat.
        /// </summary>
        private void ProgramComputerPlayers()
        {
            try
            {
                var seats = _dataService.GetComputersToProgram();
                if (seats.Count == 0) return;

                int phaseCount = _dataService.PhaseCount;
                foreach (var seat in seats)
                {
                    try
                    {
                        Decision decision;
                        lock (s_botRandom)
                        {
                            decision = RandomBrain.Decide(seat.Hand, phaseCount, seat.Facing,
                                facingNeeded: seat.PositionValid == 0, s_botRandom);
                        }

                        if (decision.CardTypes.Count < phaseCount)
                            Console.WriteLine($"[CPU {seat.RobotID}] hand has only {seat.Hand.Count} card(s) for {phaseCount} register(s); it cannot be Ready.");

                        bool applied = _dataService.SetRobotProgram(seat.RobotID, decision.CardTypes, decision.Facing);
                        Console.WriteLine($"[CPU {seat.RobotID}] Level {seat.Level} program " +
                            $"[{string.Join(",", decision.CardTypes)}]" +
                            (decision.Facing.HasValue ? $" facing {decision.Facing}" : "") +
                            (applied ? "" : " REFUSED"));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CPU {seat.RobotID}] programming failed: {ex.Message}");
                    }
                }

                // Computer robots' lights stay off during programming (SetAllRobotLights skips
                // them on the way in); this covers the first turn, when they may still be on.
                foreach (var player in AllPlayers.Where(p => p.Computer > 0))
                {
                    _ = Task.Run(async () =>
                    {
                        try { await player.SetLightsAsync(false); }
                        catch (Exception ex) { Console.WriteLine($"[{player.ID}] SetLightsAsync error: {ex.Message}"); }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Computer players: programming failed: {ex.Message}");
            }
        }
    }
}
