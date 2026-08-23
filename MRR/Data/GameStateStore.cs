namespace MRR.Services
{
    /// <summary>
    /// In-memory cache of the CurrentGameData row set: the scalars describing the game in
    /// progress. Reload() refreshes from the database; GameState and TotalFlags write back
    /// when set, so a mid-game restart restores them.
    ///
    /// Second slice of the DataService split (design section 4). Scoped to the scalars on
    /// purpose — the live collections (players, cards, board) belong with their
    /// repositories, not here.
    ///
    /// The iKey numbers are the schema. CurrentGameData is keyed on iKey, and these values
    /// are what UpdateGameState has always read; do not renumber them.
    /// </summary>
    public class GameStateStore
    {
        private const int KeyGameType    = 1;
        private const int KeyTurn        = 2;
        private const int KeyPhase       = 3;
        private const int KeyLaserDamage = 6;
        private const int KeyTotalFlags  = 7;
        private const int KeyRobotsActive = 8;
        private const int KeyGameState   = 10;
        private const int KeyPhaseCount  = 16;
        private const int KeyBoardID     = 20;
        private const int KeyOptionCount = 22;

        private readonly SqlGateway _sql;

        public GameStateStore(SqlGateway sql) => _sql = sql;

        public int Turn { get; set; }
        public int Phase { get; set; }
        public int PhaseCount { get; set; }
        public int RobotsActive { get; set; }
        public int BoardID { get; set; }
        public string BoardFileName { get; set; } = string.Empty;
        public GameTypes GameType { get; set; }
        public int LaserDamage { get; set; } = 1;

        /// <summary>-1 disables option cards.</summary>
        public int OptionsOnStartup { get; set; } = -1;

        public bool IsOptionsEnabled
        {
            get => OptionsOnStartup > -1;
            set => OptionsOnStartup = value ? 1 : -1;
        }

        private int _gameState;
        /// <summary>Current state machine position (0–16). Written through to iKey 10.</summary>
        public int GameState
        {
            get => _gameState;
            set { _gameState = value; WriteThrough(KeyGameState, value); }
        }

        private int _totalFlags = 5;
        /// <summary>
        /// Flags needed to win — one value for the whole game, not per player. Set from the
        /// board at game start; written through to iKey 7 so a restart restores it.
        /// </summary>
        public int TotalFlags
        {
            get => _totalFlags;
            set { _totalFlags = value; WriteThrough(KeyTotalFlags, value); }
        }

        private void WriteThrough(int iKey, int value)
        {
            using var ctx = _sql.CreateDbContext();
            var row = ctx.CurrentGameData.Find(iKey);
            if (row == null) return;
            row.IValue = value;
            ctx.SaveChanges();
        }

        /// <summary>
        /// Refreshes every cached value from CurrentGameData. Assigns the backing fields for
        /// GameState and TotalFlags rather than the properties: this is a read, so going
        /// through the setters would write each value straight back again.
        /// </summary>
        public int Reload()
        {
            var table = _sql.GetQueryResults("Select iKey, sKey, iValue, sValue from CurrentGameData;");
            foreach (System.Data.DataRow row in table.Rows)
            {
                var key = Convert.ToInt32(row[0]);
                var value = Convert.ToInt32(row[2]);
                switch (key)
                {
                    case KeyGameType:     GameType = (GameTypes)value; break;
                    case KeyTurn:         Turn = value; break;
                    case KeyPhase:        Phase = value; break;
                    case KeyLaserDamage:  LaserDamage = value; break;
                    case KeyTotalFlags:   _totalFlags = value; break;
                    case KeyRobotsActive: RobotsActive = value; break;
                    case KeyGameState:    _gameState = value; break;
                    case KeyPhaseCount:   PhaseCount = value; break;
                    case KeyOptionCount:  OptionsOnStartup = value; break;
                    case KeyBoardID:
                        BoardID = value;
                        if (row[3] != DBNull.Value) BoardFileName = row[3].ToString() ?? "";
                        break;
                }
            }
            return GameState;
        }
    }
}
