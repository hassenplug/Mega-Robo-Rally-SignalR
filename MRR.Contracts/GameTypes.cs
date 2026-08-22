namespace MRR
{
    /// <summary>
    /// Game variant. Persisted in Boards.GameType, GameData.GameType and CurrentGameData
    /// (iKey 1), so the numbers are part of the schema -- do not renumber.
    /// </summary>
    public enum GameTypes
    {
        Standard = 0,
        KingOfTheHill = 1,
        StandardV2 = 2,
    }
}
