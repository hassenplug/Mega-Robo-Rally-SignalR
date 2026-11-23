namespace MRR.Bot
{
    /// <summary>How a level picks from the last stage's ranked programs (COMPUTER_PLAYER_DESIGN.md §5.0).</summary>
    public enum BotChoice
    {
        /// <summary>The highest score.</summary>
        Best,
        /// <summary>Uniformly at random among the top <see cref="BotProfile.ChoiceCount"/>.</summary>
        RandomTopN,
        /// <summary>Uniformly at random among those within <see cref="BotProfile.ChoiceMargin"/> of the best.</summary>
        WithinMargin,
    }

    /// <summary>
    /// One preset per computer level. The level is stored per robot in Robots.Computer
    /// (0 = human, 1-5 = computer). See COMPUTER_PLAYER_DESIGN.md §5.0. A level is defined by how
    /// much search it may do and how it chooses, never by wall-clock time. Every number here is a
    /// starting guess to tune.
    ///
    /// Stages are beam widths, run in order (§5.2); none means Level 1's random program is final.
    /// Expert runs Hard's first three stages, then re-ranks its best candidates against sampled
    /// opponent programs (ReRank, §5.2) -- starting while Hard computers are still on their wider
    /// stages, so most of its re-rank happens in the humans' time (option 1, 2026-11-22). MayShutDown: the level
    /// considers shutting down (ShutDownAdvisor); Beginner and Easy never do. SpamSamples: how
    /// many random replacement piles a program holding a Spam is averaged over (§5.4); 0 = Spam
    /// moves nothing. Reboot facing (§5.5): Easy faces along the path to its flag; Normal and up
    /// search all four facings.
    /// </summary>
    public sealed record BotProfile(int Level, string Name, int[] Stages, BotChoice Choice,
                                    int ChoiceCount = 1, double ChoiceMargin = 0, bool MayShutDown = false,
                                    int SpamSamples = 0, bool ReRank = false)
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 5;

        public static readonly IReadOnlyList<BotProfile> All = new[]
        {
            new BotProfile(1, "Beginner", [], BotChoice.Best),
            new BotProfile(2, "Easy",     [1, 3], BotChoice.RandomTopN, ChoiceCount: 5),
            new BotProfile(3, "Normal",   [1, 3, 10, 20], BotChoice.WithinMargin, ChoiceMargin: 30, MayShutDown: true, SpamSamples: 3),
            new BotProfile(4, "Hard",     [1, 3, 10, 30, 60], BotChoice.Best, MayShutDown: true, SpamSamples: 5),
            new BotProfile(5, "Expert",   [1, 3, 10], BotChoice.Best, MayShutDown: true, SpamSamples: 5, ReRank: true),
        };

        /// <summary>The level clamped to 1-5.</summary>
        public static int ClampLevel(int level) => Math.Clamp(level, MinLevel, MaxLevel);

        /// <summary>The profile for a level; out-of-range values are clamped.</summary>
        public static BotProfile ForLevel(int level) => All[ClampLevel(level) - 1];
    }

    /// <summary>
    /// Expert's re-rank (§5.2). Candidates per Expert = CandidateBudget / (Expert seats), clamped
    /// to [MinCandidates, MaxCandidates], so a table of Experts shares the time (option 2,
    /// 2026-11-22): 1-2 Experts 12 each, 3 → 8, 4 → 6, 5 → 4. Starting guesses.
    /// </summary>
    public sealed record ReRankSettings(int Rounds = 8, int MaxCandidates = 12, int MinCandidates = 4, int CandidateBudget = 24)
    {
        public int CandidatesFor(int expertSeats) =>
            Math.Clamp(CandidateBudget / Math.Max(expertSeats, 1), MinCandidates, MaxCandidates);
    }
}
