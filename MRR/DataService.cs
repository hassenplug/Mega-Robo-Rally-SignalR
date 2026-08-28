using System;
using System.Data;
using System.Text;
using System.IO;
using MySqlConnector;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using MRR.Data;
using MRR.Data.Entities;
using System.Xml.Serialization;

namespace MRR.Services
{
    public partial class DataService
    {
        /// <summary>
        /// Raw database access. First slice of the DataService split: the plumbing lives in
        /// SqlGateway now, and the members below forward to it so no call site had to change.
        /// </summary>
        private readonly SqlGateway _sql;

        private readonly string _connectionString;
        private string DatabaseName => _sql.DatabaseName;

        public DataService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("Rally")
                ?? throw new InvalidOperationException("Connection string 'Rally' not found in configuration.");
            _sql = new SqlGateway(_connectionString);
            _state = new GameStateStore(_sql);
        }

        public string ConnectionString => _sql.ConnectionString;

        /// <summary>New MRRDbContext on the configured connection. Caller disposes.</summary>
        public MRRDbContext CreateDbContext() => _sql.CreateDbContext();

        ///////////////////////////////////////////////////////////////////////////
        // Retrieve all relevant data from the database to send to clients
        ///////////////////////////////////////////////////////////////////////////

        // Lazily-loaded players collection. First access will load from the database.
        //
        // This is now a connection registry, not a game-state cache: it exists to hold each
        // robot's live WebSocket connection (Player : PlayerState adds the socket + ScreenUI),
        // not to mirror Robots table data. GetRobotsFromTable() reads Robots fresh for every
        // broadcast, and turn planning reads a fresh snapshot via GetPlayerStatesFromDB() --
        // neither depends on this collection's data fields being current. See
        // documents/ALLPLAYERS_REMOVAL_DESIGN.md.
        private Players? _allPlayers;
        public Players AllPlayers
        {
            get
            {
                if (_allPlayers == null)
                {
                    _allPlayers = GetAllPlayers();
                }
                return _allPlayers;
            }
            set
            {
                _allPlayers = value;
            }
        }

        /// <summary>
        /// The CurrentGameData scalars. Second slice of the DataService split: the cache and
        /// its write-through live in GameStateStore, and the properties below forward, so no
        /// call site had to change.
        /// </summary>
        private readonly GameStateStore _state;

        public int RobotsActive     { get => _state.RobotsActive;     set => _state.RobotsActive = value; }
        public bool IsRunning       { get => _state.IsRunning;        set => _state.IsRunning = value; }
        public string BoardFileName { get => _state.BoardFileName;    set => _state.BoardFileName = value; }
        public int BoardID          { get => _state.BoardID;          set => _state.BoardID = value; }
        public int GameState        { get => _state.GameState;        set => _state.GameState = value; }
        public int PhaseCount       { get => _state.PhaseCount;       set => _state.PhaseCount = value; }
        public int Turn             { get => _state.Turn;             set => _state.Turn = value; }
        public int Phase            { get => _state.Phase;            set => _state.Phase = value; }
        public GameTypes GameType   { get => _state.GameType;         set => _state.GameType = value; }
        public int OptionsOnStartup { get => _state.OptionsOnStartup; set => _state.OptionsOnStartup = value; }
        public int LaserDamage      { get => _state.LaserDamage;      set => _state.LaserDamage = value; }
        public int TotalFlags       { get => _state.TotalFlags;       set => _state.TotalFlags = value; }
        public bool IsOptionsEnabled { get => _state.IsOptionsEnabled; set => _state.IsOptionsEnabled = value; }

        public CommandList ListOfCommands { get; set; } = new CommandList();

        public CardList GameCards { get; set; } = new CardList();

        public OptionCardList OptionCards { get; set; } = new OptionCardList();

        public Dictionary<int, string> OptionCardNames = new Dictionary<int, string>();

        public BoardElementCollection g_BoardElements { get; set; } = new BoardElementCollection();

        public AllDataPayload AllData { get; set; } = new AllDataPayload();

        public string GetAllDataJson() => JsonConvert.SerializeObject(GetAllDataFromPlayers());

        public AllDataPayload GetAllDataFromPlayers()
        {
            string titlemessage = "Turn " + Turn;
            if (Turn == 0) titlemessage = "Game Setup";
            if (Phase > 0) titlemessage += " Phase " + Phase;

            return new AllDataPayload
            {
                titlemsg  = titlemessage,
                gamestate = GameState,
                robots    = GetRobotsFromTable(),
            };
        }

        public int GetIntFromDB(string strSQL) => _sql.GetIntFromDB(strSQL);

        public int[] GetIntList(string strSQL) => _sql.GetIntList(strSQL);


        ///////////////////////////////////////////////////////////////////////////
        // Execute a command that does not return results (e.g., INSERT, UPDATE, DELETE)
        // Returns the number of affected rows or 0 if an error occurs
        ///////////////////////////////////////////////////////////////////////////        

        // use 
        // _dataService.ExecuteSQL( 
        // instead of 
        // DBConn.Command(


        public int ExecuteSQL(string query) => _sql.ExecuteSQL(query);

        ///////////////////////////////////////////////////////////////////////////
        // Execute a query that returns results (e.g., SELECT)
        // Returns a list of dictionaries representing rows or an error message
        ///////////////////////////////////////////////////////////////////////////

        public DataTable GetQueryResults(string query) => _sql.GetQueryResults(query);

        ////////////////////////////////////////////////////////////////////////////
        // Load board data from the database into a BoardElementCollection
        ////////////////////////////////////////////////////////////////////////////    
        public BoardElementCollection BoardLoadFromDB(int sourceID)
        {
            BoardElementCollection l_BoardElements = new BoardElementCollection();
            string strSQL;
            BoardActionsCollection squareActions = new BoardActionsCollection();

            strSQL = "Select X,Y,SquareAction,ActionSequence,Phase,Parameter from BoardItemActions where BoardID=" + sourceID + ";";
            var actionTable = GetQueryResults(strSQL);
            foreach (DataRow actionRow in actionTable.Rows)
            {
                BoardAction oneAction = new BoardAction((SquareAction)actionRow["SquareAction"], Convert.ToInt32(actionRow["Parameter"]), Convert.ToInt32(actionRow["ActionSequence"]), Convert.ToInt32(actionRow["Phase"]));
                oneAction.SquareX = Convert.ToInt32(actionRow["X"]);
                oneAction.SquareY = Convert.ToInt32(actionRow["Y"]);
                squareActions.Add(oneAction);
            }


            l_BoardElements = new BoardElementCollection();
            strSQL = "Select X,Y,SquareType,Rotation from BoardItems where BoardID=" + sourceID + ";";
            var readerTable = GetQueryResults(strSQL);
            foreach (DataRow row in readerTable.Rows)
            {
                int boardX = Convert.ToInt32(row["X"]);
                int boardY = Convert.ToInt32(row["Y"]);
                if (boardX + 1 > l_BoardElements.BoardCols) l_BoardElements.BoardCols = boardX + 1;
                if (boardY + 1 > l_BoardElements.BoardRows) l_BoardElements.BoardRows = boardY + 1;

                BoardActionsCollection boardSquareActions = new BoardActionsCollection();

                foreach (BoardAction thisaction in squareActions.Where(sa => sa.SquareX == boardX && sa.SquareY == boardY))
                {
                    boardSquareActions.Add(thisaction);
                }

                l_BoardElements.SetSquare(boardX, boardY, (SquareType)row["SquareType"], (Direction)row["Rotation"], boardSquareActions);
            }
            return l_BoardElements;
        }

        public void ResetGameState()
        {
            // retained for compatibility; original implementation was empty
            // but higher-level initialization should call appropriate procedures
        }

        public void ReloadAllData()
        {
            UpdateGameState(); // ensure C# state reflects any DB changes from UpdateGameState logic
            GetAllPlayers(forceRefresh: true);
            LoadGameCardsFromDatabase();
            LoadOptionCardsFromDatabase();
        }

        public int UpdateGameState() => _state.Reload();

        /// <summary>
        /// Validate table name to prevent SQL injection
        /// </summary>
        private bool IsValidTableName(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return false;

            // Only allow alphanumeric characters and underscores
            return System.Text.RegularExpressions.Regex.IsMatch(tableName, @"^[a-zA-Z0-9_]+$");
        }




    }
}