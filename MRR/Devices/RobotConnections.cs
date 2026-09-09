using MRR;

namespace MRR.Devices
{
    /// <summary>
    /// The one place that owns every robot's <see cref="RobotConnection"/>. Populated by
    /// <see cref="Refresh"/> from the same rows <c>AllDataPayload.robots</c> already sends to
    /// every client (<c>DataService.GetRobotsFromTable()</c>) -- no separate query, no second
    /// source of truth for a robot's IP address.
    ///
    /// Deliberately holds no game state (Energy, Damage, CardsPlayed, ...) and runs no SQL of
    /// its own -- see API_DECOMPOSITION_DESIGN.md section 5.5. <see cref="MRR.Player"/> attaches
    /// to an entry here rather than owning a socket itself, so rebuilding the game's player list
    /// (<c>DataService.GetAllPlayers(forceRefresh: true)</c>) can no longer touch a live
    /// connection.
    /// </summary>
    public class RobotConnections : List<RobotConnection>
    {
        // Guards this list's own Add/Remove/lookup against concurrent Refresh() calls -- Refresh()
        // runs both on the command-processing background thread (via CommandProcess.ReloadAllData)
        // and on HTTP request threads (e.g. GameController.LoadCurrentGame), so two callers can
        // otherwise mutate the underlying List<T> at the same time.
        private readonly object _listLock = new object();

        /// <summary>
        /// Reconciles the registry against the current robot roster. A robot already present at
        /// the same IP keeps its connection untouched (including a live socket); a robot whose
        /// IP changed gets its old connection torn down and replaced; a robot no longer present
        /// is torn down and dropped. Never discards a still-valid connection just because the
        /// caller rebuilt its own player list around it.
        /// </summary>
        public void Refresh(List<RobotData> robots)
        {
            lock (_listLock)
            {
                var seenIds = new HashSet<int>();

                foreach (var row in robots)
                {
                    seenIds.Add(row.RobotID);
                    var existing = this.Find(c => c.RobotID == row.RobotID);

                    if (existing == null)
                    {
                        Console.WriteLine($"RobotConnections.Refresh: new robot {row.RobotID} at {row.IPAddress}");
                        Add(new RobotConnection(row.RobotID, row.IPAddress));
                    }
                    else if (existing.IPAddress != row.IPAddress)
                    {
                        Console.WriteLine($"RobotConnections.Refresh: robot {row.RobotID} changed IP from {existing.IPAddress} to {row.IPAddress}");
                        DisposeQuietly(existing);
                        Remove(existing);
                        Add(new RobotConnection(row.RobotID, row.IPAddress));
                    }
                    // else: same robot, same IP -- leave it alone, connected or not.
                }

                var stale = this.Where(c => !seenIds.Contains(c.RobotID)).ToList();
                foreach (var connection in stale)
                {
                    Console.WriteLine($"RobotConnections.Refresh: robot {connection.RobotID} removed");
                    DisposeQuietly(connection);
                    Remove(connection);
                }
            }
        }

        public RobotConnection? Get(int robotId)
        {
            lock (_listLock)
            {
                return this.Find(c => c.RobotID == robotId);
            }
        }

        // RobotConnection.DisposeAsync is idempotent and safe to call concurrently (it no-ops
        // past the first call), so this really can't throw from a double-dispose anymore -- but
        // it still guards the call rather than trusting that promise a second time, since that
        // exact assumption ("DisposeAsync itself never throws") is what let a double-dispose
        // crash the process before DisposeAsync gained its own internal guard.
        private static void DisposeQuietly(RobotConnection connection)
        {
            if (!connection.IsConnected) return;
            try
            {
                connection.DisposeAsync().AsTask().Wait();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{connection.RobotID}] DisposeAsync error during Refresh(): {ex.Message}");
            }
        }
    }
}
