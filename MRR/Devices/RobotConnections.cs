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
        /// <summary>
        /// Reconciles the registry against the current robot roster. A robot already present at
        /// the same IP keeps its connection untouched (including a live socket); a robot whose
        /// IP changed gets its old connection torn down and replaced; a robot no longer present
        /// is torn down and dropped. Never discards a still-valid connection just because the
        /// caller rebuilt its own player list around it.
        /// </summary>
        public void Refresh(List<RobotData> robots)
        {
            var seenIds = new HashSet<int>();

            foreach (var row in robots)
            {
                seenIds.Add(row.RobotID);
                var existing = this.Find(c => c.RobotID == row.RobotID);

                if (existing == null)
                {
                    Add(new RobotConnection(row.RobotID, row.IPAddress));
                }
                else if (existing.IPAddress != row.IPAddress)
                {
                    DisposeQuietly(existing);
                    Remove(existing);
                    Add(new RobotConnection(row.RobotID, row.IPAddress));
                }
                // else: same robot, same IP -- leave it alone, connected or not.
            }

            var stale = this.Where(c => !seenIds.Contains(c.RobotID)).ToList();
            foreach (var connection in stale)
            {
                DisposeQuietly(connection);
                Remove(connection);
            }
        }

        public RobotConnection? Get(int robotId) => this.Find(c => c.RobotID == robotId);

        // DisposeAsync itself never throws (see RobotConnection.DisposeAsync), but Refresh() runs
        // synchronously from GetAllPlayers(), so the async call still needs blocking here -- same
        // sync-over-async pattern GameController already uses for connect/disconnect.
        private static void DisposeQuietly(RobotConnection connection)
        {
            if (!connection.IsConnected) return;
            connection.DisposeAsync().AsTask().Wait();
        }
    }
}
