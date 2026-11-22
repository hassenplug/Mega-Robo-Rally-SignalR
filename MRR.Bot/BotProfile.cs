namespace MRR.Bot
{
    /// <summary>
    /// One preset per computer level. The level is stored per robot in Robots.Computer
    /// (0 = human, 1-5 = computer). See COMPUTER_PLAYER_DESIGN.md §5.0.
    ///
    /// M1: every level plays the Level 1 (random) program. The search that makes Levels 2-5
    /// different arrives in M2 (Levels 2-4) and M4 (Expert).
    /// </summary>
    public sealed record BotProfile(int Level, string Name)
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 5;

        public static readonly IReadOnlyList<BotProfile> All = new[]
        {
            new BotProfile(1, "Beginner"),
            new BotProfile(2, "Easy"),
            new BotProfile(3, "Normal"),
            new BotProfile(4, "Hard"),
            new BotProfile(5, "Expert"),
        };

        /// <summary>The level clamped to 1-5.</summary>
        public static int ClampLevel(int level) => Math.Clamp(level, MinLevel, MaxLevel);

        /// <summary>The profile for a level; out-of-range values are clamped.</summary>
        public static BotProfile ForLevel(int level) => All[ClampLevel(level) - 1];
    }
}
