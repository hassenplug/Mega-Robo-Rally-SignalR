using Microsoft.Extensions.Configuration;
using MRR;

namespace MRR.Devices
{
    /// <summary>
    /// The one place that owns every robot's <see cref="RobotConnection"/>. <see cref="Refresh"/>
    /// prunes entries for robots no longer in the <c>Robots</c> roster, but never creates one --
    /// a robot only ever gets connected through <see cref="Reconnect"/>/<see cref="ReconnectAll"/>
    /// (driven by <c>GameController</c>'s connect actions and a new game's start), each of which
    /// hands the new <see cref="RobotConnection"/> nothing but the <c>RobotID</c>; it polls the
    /// database itself for everything else it needs (name, color, IP address) and connects
    /// itself on construction -- see the remarks on <see cref="RobotConnection"/>.
    ///
    /// Deliberately holds no other game state (Energy, Damage, CardsPlayed, ...) -- see
    /// API_DECOMPOSITION_DESIGN.md section 5.5. <see cref="MRR.Player"/> attaches to an entry
    /// here rather than owning a socket itself, so rebuilding the game's player list
    /// (<c>DataService.GetAllPlayers(forceRefresh: true)</c>) can no longer touch a live
    /// connection.
    /// </summary>
    public class RobotConnections : List<RobotConnection>
    {
        // Guards this list's own Add/Remove/lookup against concurrent Refresh()/Reconnect()
        // calls -- these run both on the command-processing background thread (via
        // CommandProcess.ReloadAllData) and on HTTP request threads (e.g.
        // GameController.LoadCurrentGame), so two callers can otherwise mutate the underlying
        // List<T> at the same time.
        private readonly object _listLock = new object();
        private readonly string _connectionString;

        public RobotConnections(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("Rally")
                ?? throw new InvalidOperationException("Connection string 'Rally' not found in configuration.");
        }

        /// <summary>
        /// Drops registry entries for robots no longer present in <paramref name="robotIds"/>.
        /// Never creates an entry for a robot it hasn't seen before -- a robot only gets a
        /// <see cref="RobotConnection"/> (and therefore only ever dials out) through an
        /// explicit connect action (<see cref="Reconnect"/>/<see cref="ReconnectAll"/>, driven
        /// by <c>GameController</c>), never as a side effect of rebuilding the player list on
        /// every broadcast. A robot already registered keeps its connection untouched,
        /// including a live socket.
        /// </summary>
        public void Refresh(IEnumerable<int> robotIds)
        {
            lock (_listLock)
            {
                var seenIds = new HashSet<int>(robotIds);

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

        /// <summary>
        /// Closes this robot's current connection (if any) and opens a fresh one in its place.
        /// This is the only way a robot ever (re)connects after the registry's first Refresh --
        /// <see cref="RobotConnection.ConnectAsync"/> is private, so reconnecting means
        /// replacing the object, not calling back into it. Used by the GM's manual
        /// "Connect"/"Connect All" actions and by <see cref="ReconnectAll"/>.
        /// </summary>
        public RobotConnection Reconnect(int robotId)
        {
            lock (_listLock)
            {
                var existing = this.Find(c => c.RobotID == robotId);
                if (existing != null)
                {
                    DisposeQuietly(existing);
                    Remove(existing);
                }

                var fresh = new RobotConnection(robotId, _connectionString);
                Add(fresh);
                return fresh;
            }
        }

        /// <summary>
        /// Closes every current connection and opens a fresh one for each robot in
        /// <paramref name="robotIds"/> -- called when a new game starts, so no robot carries a
        /// stale socket, LED state, or LCD screen across a game boundary, and by the GM's
        /// "Connect All" action.
        /// </summary>
        public List<RobotConnection> ReconnectAll(IEnumerable<int> robotIds)
        {
            lock (_listLock)
            {
                foreach (var connection in this.ToList())
                {
                    DisposeQuietly(connection);
                    Remove(connection);
                }

                var fresh = new List<RobotConnection>();
                foreach (var id in robotIds)
                {
                    var connection = new RobotConnection(id, _connectionString);
                    Add(connection);
                    fresh.Add(connection);
                }
                return fresh;
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
