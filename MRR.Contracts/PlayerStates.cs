namespace MRR
{
    /// <summary>
    /// A collection of robot states, for turn planning.
    ///
    /// Distinct from the host's Players (which is a List&lt;Player&gt; of live robots with
    /// open sockets) on purpose: the rules engine works on state snapshots and must never
    /// be able to reach a physical robot. Master builds one of these directly from the
    /// database via DataService.GetPlayerStatesFromDB() when it asks for a turn plan --
    /// deliberately not sourced from AllPlayers, which is a connection registry, not a
    /// game-state cache (see documents/ALLPLAYERS_REMOVAL_DESIGN.md).
    ///
    /// The helpers mirror Players' because both answer the same questions; the ~30 lines of
    /// duplication is the price of the rules engine not depending on the transport.
    /// </summary>
    public class PlayerStates : List<PlayerState>
    {
        public PlayerStates() { }

        public PlayerStates(IEnumerable<PlayerState> players) : base(players) { }

        public PlayerState? GetPlayer(int p_PlayerID) => GetPlayer(pl => pl.ID == p_PlayerID);

        public PlayerState? GetPlayer(RobotLocation p_Square) =>
            GetPlayer(ap => ap.CurrentPos.X == p_Square.X && ap.CurrentPos.Y == p_Square.Y && ap.Active);

        public PlayerState? GetPlayer(Func<PlayerState, bool> filter) => this.FirstOrDefault(filter);

        public void SetArchiveToCurrent()
        {
            foreach (var player in this) player.ArchivePos.SetLocation(player.CurrentPos);
        }

        /// <summary>
        /// Deep copy for turn simulation. The copy is mutated during planning and discarded;
        /// the outcome is recorded as commands, not written back.
        /// </summary>
        public PlayerStates DeepCopy()
        {
            var copy = new PlayerStates();
            foreach (var player in this) copy.Add(new PlayerState(player));
            return copy;
        }
    }
}
