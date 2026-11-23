namespace MRR.Bot
{
    /// <summary>
    /// The timing rules for one programming phase's search (COMPUTER_PLAYER_DESIGN.md §5.7), kept
    /// apart from the host so they can be tested with a fake clock.
    ///
    /// Levels 1-4 stop the moment the humans are done. Expert keeps going after that: until
    /// GraceSoftMs it may start new re-rank rounds; until GraceHardMs it may only finish the round
    /// in progress; at GraceHardMs it stops. With no human programming at all (no human clock) the
    /// clock starts with the run: Levels 2-4 get SoloBudgetMs, Expert the larger of that and its
    /// grace.
    ///
    /// Thread-safe: the worker, the poll and the state-4 gate all read it.
    /// </summary>
    public sealed class BotTiming
    {
        private readonly Func<long> _nowMs;
        private long _humansDoneAtMs = -1;

        public bool HumanClock { get; }
        public bool HasExpert { get; }
        public long SoloBudgetMs { get; }
        public long GraceSoftMs { get; }
        public long GraceHardMs { get; }

        /// <param name="nowMs">Milliseconds since the run began (a Stopwatch in the host, a fake in tests).</param>
        public BotTiming(bool humanClock, bool hasExpert, long soloBudgetMs, long graceSoftMs, long graceHardMs, Func<long> nowMs)
        {
            HumanClock = humanClock;
            HasExpert = hasExpert;
            SoloBudgetMs = soloBudgetMs;
            GraceSoftMs = graceSoftMs;
            GraceHardMs = Math.Max(graceHardMs, graceSoftMs);
            _nowMs = nowMs;
        }

        public long NowMs => _nowMs();
        public bool HumansDone => Interlocked.Read(ref _humansDoneAtMs) >= 0;
        public long HumansDoneAtMs => Interlocked.Read(ref _humansDoneAtMs);

        /// <summary>Records when the last human finished; the first call wins.</summary>
        public void StampHumansDone() => Interlocked.CompareExchange(ref _humansDoneAtMs, _nowMs(), -1);

        private long ExpertDeadline(long grace) =>
            HumanClock
                ? (HumansDone ? HumansDoneAtMs + grace : long.MaxValue)
                : Math.Max(SoloBudgetMs, grace);

        /// <summary>No human clock, and Levels 2-4's budget is used up.</summary>
        public bool PastBudget => !HumanClock && NowMs >= SoloBudgetMs;
        public bool PastSoft => NowMs >= ExpertDeadline(GraceSoftMs);
        public bool PastHard => NowMs >= ExpertDeadline(GraceHardMs);

        /// <summary>Whether a computer at this level must stop searching now.</summary>
        public bool StopFor(int level) =>
            level >= BotProfile.MaxLevel ? PastHard : (HumanClock ? HumansDone : PastBudget);

        /// <summary>Expert may start another re-rank round (before the soft deadline).</summary>
        public bool MayStartRound => !PastSoft;

        /// <summary>Every computer has run out of time.</summary>
        public bool PastAllDeadlines => HasExpert ? PastHard : (HumanClock ? HumansDone : PastBudget);

        /// <summary>
        /// The state-4 gate's question, asked once every robot is Ready (design gap 13). With a
        /// human clock this call means the humans are done, so it stamps that; then it is true at
        /// once unless an Expert is still inside its grace. Without one, true once every computer's
        /// time is up. The host also releases the gate when the search finishes early.
        /// </summary>
        public bool MayLockIn()
        {
            if (HumanClock)
            {
                StampHumansDone();
                return !HasExpert || PastHard;
            }
            return PastAllDeadlines;
        }
    }
}
