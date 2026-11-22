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
    /// Expert plays as Hard until its opponent re-rank arrives (M4).
    /// </summary>
    public sealed record BotProfile(int Level, string Name, int[] Stages, BotChoice Choice,
                                    int ChoiceCount = 1, double ChoiceMargin = 0)
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 5;

        public static readonly IReadOnlyList<BotProfile> All = new[]
        {
            new BotProfile(1, "Beginner", [], BotChoice.Best),
            new BotProfile(2, "Easy",     [1, 3], BotChoice.RandomTopN, ChoiceCount: 5),
            new BotProfile(3, "Normal",   [1, 3, 10, 20], BotChoice.WithinMargin, ChoiceMargin: 30),
            new BotProfile(4, "Hard",     [1, 3, 10, 30, 60], BotChoice.Best),
            new BotProfile(5, "Expert",   [1, 3, 10, 30, 60], BotChoice.Best),
        };

        /// <summary>The level clamped to 1-5.</summary>
        public static int ClampLevel(int level) => Math.Clamp(level, MinLevel, MaxLevel);

        /// <summary>The profile for a level; out-of-range values are clamped.</summary>
        public static BotProfile ForLevel(int level) => All[ClampLevel(level) - 1];
    }
}
