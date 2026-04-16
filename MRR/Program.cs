using MRR.Hubs;
using MRR.Services;
using Microsoft.AspNetCore.SignalR;
using System.Net.WebSockets;
using MRR.Controller;
using MRR;
using MRR.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Register DataService first so we can use its connection string
builder.Services.AddSingleton<DataService>();

/*
// Add database context factory using DataService's connection string
builder.Services.AddDbContextFactory<MRRDbContext>((serviceProvider, options) =>
{
    var dataService = serviceProvider.GetRequiredService<DataService>();
    options.UseMySql(
        dataService.ConnectionString,
        new MySqlServerVersion(new Version(8, 0, 0))
    );
});
*/

// Register SignalR before GameController so IHubContext<DataHub> is available
builder.Services.AddSignalR();
builder.Services.AddSingleton<GameController>();

var app = builder.Build();

//var seedDataService = app.Services.GetRequiredService<DataService>();
//SeedBoardTemplate(seedDataService);

app.UseStaticFiles();
app.UseDefaultFiles();
app.UseWebSockets();

app.MapHub<DataHub>("/datahub");

// Unified table data API - GET/POST for list, read, and write operations
app.MapGet("/api/table", (DataService dataService) =>
{
    var tables = dataService.GetTableList();
    return Results.Ok(new { tables });
});

app.MapGet("/api/table/{tablename}/{filter?}/{setvalue?}", (string tablename, string? filter, string? setvalue, DataService dataService, IHubContext<DataHub> hubContext) =>
{
    string whereClause = "";
    if (filter != "" && filter != null)
    {
        whereClause = " where " + filter;
    }

    if(setvalue != "" && setvalue != null)
    {
        var setStatement = "Update " + tablename + " set " + setvalue + whereClause + ";";
        dataService.ExecuteSQL(setStatement);
    }

    var dataout = dataService.GetQueryResultsJson($"Select * from {tablename}{whereClause};", tablename);
    hubContext.Clients.All.SendAsync(tablename, dataout);
    return Results.Content(dataout, "application/json");
});

app.MapPost("/api/table/{tablename}", async (string tablename, DataService dataService, HttpRequest request) =>
{
    try
    {
        using (var reader = new StreamReader(request.Body))
        {
            var json = await reader.ReadToEndAsync();
            var result = dataService.SaveTableData(tablename, json);
            return Results.Ok(result);
        }
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/alldata", (DataService dataService, IHubContext<DataHub> hubContext) =>
{
    var dataout = dataService.GetAllDataJson();
    _ = hubContext.Clients.All.SendAsync("AllDataUpdate", dataout);

    return Results.Content(dataout, "application/json");
});

app.MapGet("/api/state/{newstate?}/{parameter1?}", async (string? newstate, string? parameter1, DataService dataService, IHubContext<DataHub> hubContext, GameController gameController) =>
{

    if (newstate == null) newstate = "";
    int paramInt = 0;
    if (parameter1 != null) int.TryParse(parameter1, out paramInt);

    switch (newstate)
    {
        case "nextstate":
            if (paramInt > 0) gameController.SetGameState(paramInt);
            gameController.NextState();
            //return Results.Ok(nextstate);
            break;
        case "startgame":
            if (paramInt > 0)
            {
                // Load specified GameData into CurrentGameData and start game
                gameController.LoadGameData(paramInt);
            }
            gameController.SetGameState(0);
            //gameController.StartGame();
            gameController.NextState();
            //return Results.Ok(result);
            break;
        case "resetgame":
            //dataService.ExecuteSQL("call procResetGameState();");
            break;
        case "executeturn":
            Console.WriteLine("Executing turn...");
            await gameController.ExecuteTurn();
            break;
        case "processcommands":
            Console.WriteLine("Process Commands...");
            gameController.StartProcessCommandsThread();
            break;
        case "getalldata":
            var alldataout = dataService.GetAllDataJson();
            await hubContext.Clients.All.SendAsync("AllDataUpdate", alldataout);

            return Results.Ok(alldataout);
        case "gametables":

            return Results.Content(dataService.GetTableDataAsHTML("CurrentGameData/Robots/CommandList"), "text/html");
        case "loadboard":
            gameController.LoadBoard();
            break;
        default:
            Console.WriteLine("State change requested: " + newstate + " Param: " + parameter1);
        //        var setStatement = "Update " + tablename + " set " + setvalue + whereClause + ";";
        //        dataService.ExecuteSQL(setStatement);
            break;
    }   


//    var dataout = dataService.GetQueryResultsJson($"Select * from CurrentGameData;", "State");
//    hubContext.Clients.All.SendAsync("State", dataout);
//    return Results.Ok(dataout);
//    return Results.Ok(dataout);
    var dataout = dataService.GetAllDataJson();
    await hubContext.Clients.All.SendAsync("AllDataUpdate", dataout);

    return Results.Content(dataout, "application/json");
});


app.MapGet("/api/robot/{function?}/{parameter1?}", (string? function, string? parameter1, DataService dataService, IHubContext<DataHub> hubContext, GameController gameController) =>
{

    if (function == null) function = "";

    switch (function)
    {
        case "test":
            var robot = new Player().Connect(parameter1 ?? "");
            robot?.RunTest().Wait();
            break;
        case "connect":
            if (parameter1 == "all")
            {
                gameController.ConnectToAllRobots();
            }
            else
            {
                gameController.ConnectToRobot(Convert.ToInt32(parameter1));
            }

            break;  
        case "disconnect":
            if (parameter1 == "all")
            {
                //gameController.DisconnectAllRobots();
            }
            else
            {
                //gameController.DisconnectRobot(Convert.ToInt32(parameter1));
            }

            break;
        default:
            break;
    }   

    var dataout = dataService.GetQueryResultsJson($"Select * from CurrentGameData;", "State");
    hubContext.Clients.All.SendAsync("State", dataout);
    return Results.Ok(dataout);
});

app.MapGet("/api/board/{boardID?}", (int? boardID, DataService dataService, IHubContext<DataHub> hubContext, GameController gameController) =>
{
    if (boardID == null) boardID = dataService.BoardID;
    else dataService.BoardID = boardID.Value;
    var dataout = dataService.GetQueryResultsJson($"Select * from BoardItems where BoardID={boardID};", "board");
    //hubContext.Clients.All.SendAsync("board", dataout);
    return Results.Content(dataout, "application/json");
});


// ── Board Editor API ────────────────────────────────────────────────────────

app.MapGet("/api/boardeditor/types", (DataService dataService) =>
{
    var items = dataService.GetQueryResults(
        "SELECT X, Y, SquareType, Rotation FROM BoardItems WHERE BoardID=0 ORDER BY SquareType;");
    var actions = dataService.GetQueryResults(
        "SELECT X, Y, SquareAction, ActionSequence, Phase, Parameter FROM BoardItemActions WHERE BoardID=0;");

    var actLookup = new Dictionary<string, List<object>>();
    foreach (System.Data.DataRow r in actions.Rows)
    {
        var key = $"{r["X"]},{r["Y"]}";
        if (!actLookup.ContainsKey(key)) actLookup[key] = new List<object>();
        actLookup[key].Add(new {
            squareAction = Convert.ToInt32(r["SquareAction"]),
            actionName = Enum.GetName(typeof(SquareAction), Convert.ToInt32(r["SquareAction"])) ?? "Unknown",
            sequence = Convert.ToInt32(r["ActionSequence"]),
            phase = Convert.ToInt32(r["Phase"]),
            parameter = Convert.ToInt32(r["Parameter"])
        });
    }

    var types = new List<object>();
    foreach (System.Data.DataRow r in items.Rows)
    {
        int typeId = Convert.ToInt32(r["SquareType"]);
        int rotation = Convert.ToInt32(r["Rotation"]);
        var key = $"{r["X"]},{r["Y"]}";
        types.Add(new {
            name = Enum.GetName(typeof(SquareType), typeId) ?? typeId.ToString(),
            typeId,
            defaultRotation = rotation,
            canRotate = rotation != 0,
            actions = actLookup.TryGetValue(key, out var al) ? al : new List<object>()
        });
    }
    return Results.Ok(types);
});

app.MapGet("/api/boardeditor/boards", (DataService dataService) =>
{
    var data = dataService.GetQueryResultsJson(
        "SELECT BoardID, BoardName, X, Y, LaserDamage, PhaseCount FROM Boards ORDER BY BoardID;",
        "boards");
    return Results.Content(data, "application/json");
});

app.MapGet("/api/boardeditor/{boardId:int}", (int boardId, DataService dataService) =>
{
    // Board header
    var boardTable = dataService.GetQueryResults(
        $"SELECT BoardID, BoardName, X, Y, LaserDamage, PhaseCount, GameType FROM Boards WHERE BoardID={boardId};");

    if (boardTable.Rows.Count == 0)
        return Results.NotFound(new { error = $"Board {boardId} not found" });

    var br = boardTable.Rows[0];

    // Items
    var itemsTable = dataService.GetQueryResults(
        $"SELECT X, Y, SquareType, Rotation FROM BoardItems WHERE BoardID={boardId};");
    var items = new List<object>();
    foreach (System.Data.DataRow row in itemsTable.Rows)
    {
        items.Add(new
        {
            x = Convert.ToInt32(row["X"]),
            y = Convert.ToInt32(row["Y"]),
            squareType = Convert.ToInt32(row["SquareType"]),
            rotation = Convert.ToInt32(row["Rotation"])
        });
    }

    // Actions
    var actionsTable = dataService.GetQueryResults(
        $"SELECT X, Y, SquareAction, ActionSequence, Phase, Parameter FROM BoardItemActions WHERE BoardID={boardId};");
    var actions = new List<object>();
    foreach (System.Data.DataRow row in actionsTable.Rows)
    {
        actions.Add(new
        {
            x = Convert.ToInt32(row["X"]),
            y = Convert.ToInt32(row["Y"]),
            squareAction = Convert.ToInt32(row["SquareAction"]),
            sequence = Convert.ToInt32(row["ActionSequence"]),
            phase = Convert.ToInt32(row["Phase"]),
            parameter = Convert.ToInt32(row["Parameter"])
        });
    }

    return Results.Ok(new
    {
        boardId = Convert.ToInt32(br["BoardID"]),
        boardName = br["BoardName"]?.ToString() ?? "",
        x = Convert.ToInt32(br["X"]),
        y = Convert.ToInt32(br["Y"]),
        laserDamage = Convert.ToInt32(br["LaserDamage"]),
        phaseCount = Convert.ToInt32(br["PhaseCount"]),
        gameType = Convert.ToInt32(br["GameType"]),
        items,
        actions
    });
});

app.MapPost("/api/boardeditor", async (DataService dataService, HttpRequest request) =>
{
    try
    {
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var boardName = root.GetProperty("boardName").GetString() ?? "New Board";
        var w = root.GetProperty("x").GetInt32();
        var h = root.GetProperty("y").GetInt32();
        var laserDamage = root.GetProperty("laserDamage").GetInt32();
        var phaseCount = root.GetProperty("phaseCount").GetInt32();
        var gameType = root.GetProperty("gameType").GetInt32();

        // Escape board name for SQL
        var safeName = boardName.Replace("'", "''");

        // Boards has no AUTO_INCREMENT — calculate next ID manually (skip template BoardID=0)
        var idTable = dataService.GetQueryResults("SELECT COALESCE(MAX(BoardID), 0) + 1 AS NextID FROM Boards WHERE BoardID > 0;");
        var newBoardId = Convert.ToInt32(idTable.Rows[0]["NextID"]);
        if (newBoardId < 1) newBoardId = 1;

        dataService.ExecuteSQL(
            $"INSERT INTO Boards (BoardID, BoardName, X, Y, LaserDamage, PhaseCount, GameType) " +
            $"VALUES ({newBoardId}, '{safeName}', {w}, {h}, {laserDamage}, {phaseCount}, {gameType});");

        // Bulk-insert blank squares for all cells
        var sb = new StringBuilder();
        sb.Append("INSERT INTO BoardItems (BoardID, X, Y, SquareType, Rotation) VALUES ");
        bool first = true;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!first) sb.Append(',');
                sb.Append($"({newBoardId},{x},{y},0,0)");
                first = false;
            }
        }
        sb.Append(';');
        dataService.ExecuteSQL(sb.ToString());

        return Results.Ok(new { boardId = newBoardId });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPut("/api/boardeditor/{boardId:int}", async (int boardId, DataService dataService, HttpRequest request) =>
{
    try
    {
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var boardName = root.GetProperty("boardName").GetString() ?? "Board";
        var w = root.GetProperty("x").GetInt32();
        var h = root.GetProperty("y").GetInt32();
        var laserDamage = root.GetProperty("laserDamage").GetInt32();
        var phaseCount = root.GetProperty("phaseCount").GetInt32();
        var gameType = root.GetProperty("gameType").GetInt32();
        var squares = root.GetProperty("squares");

        // Compute totalFlags and totalPlayers from squares' actions
        int totalFlags = 0;
        int totalPlayers = 0;
        foreach (var sq in squares.EnumerateArray())
        {
            if (sq.TryGetProperty("actions", out var acts))
            {
                foreach (var act in acts.EnumerateArray())
                {
                    var sa = act.GetProperty("squareAction").GetInt32();
                    var param = act.GetProperty("parameter").GetInt32();
                    if (sa == 16 && param > totalFlags) totalFlags = param;   // Flag
                    if (sa == 19 && param > totalPlayers) totalPlayers = param; // PlayerStart
                }
            }
        }

        var safeName = boardName.Replace("'", "''");

        // UPDATE board header
        dataService.ExecuteSQL(
            $"UPDATE Boards SET BoardName='{safeName}', X={w}, Y={h}, LaserDamage={laserDamage}, " +
            $"PhaseCount={phaseCount}, GameType={gameType}, " +
            $"TotalFlags={totalFlags} WHERE BoardID={boardId};");

        // DELETE existing items and actions
        dataService.ExecuteSQL($"DELETE FROM BoardItemActions WHERE BoardID={boardId};");
        dataService.ExecuteSQL($"DELETE FROM BoardItems WHERE BoardID={boardId};");

        // Bulk-INSERT items
        var sbItems = new StringBuilder();
        sbItems.Append("INSERT INTO BoardItems (BoardID, X, Y, SquareType, Rotation) VALUES ");
        bool firstItem = true;
        foreach (var sq in squares.EnumerateArray())
        {
            var sx = sq.GetProperty("x").GetInt32();
            var sy = sq.GetProperty("y").GetInt32();
            var squareType = sq.GetProperty("squareType").GetInt32();
            var rotation = sq.GetProperty("rotation").GetInt32();
            if (!firstItem) sbItems.Append(',');
            sbItems.Append($"({boardId},{sx},{sy},{squareType},{rotation})");
            firstItem = false;
        }
        sbItems.Append(';');
        if (!firstItem) dataService.ExecuteSQL(sbItems.ToString());

        // Bulk-INSERT actions (only if there are any)
        var sbActions = new StringBuilder();
        sbActions.Append("INSERT INTO BoardItemActions (BoardID, X, Y, SquareAction, ActionSequence, Phase, Parameter) VALUES ");
        bool firstAction = true;
        foreach (var sq in squares.EnumerateArray())
        {
            var sx = sq.GetProperty("x").GetInt32();
            var sy = sq.GetProperty("y").GetInt32();
            if (sq.TryGetProperty("actions", out var acts))
            {
                foreach (var act in acts.EnumerateArray())
                {
                    var sa = act.GetProperty("squareAction").GetInt32();
                    var seq = act.GetProperty("sequence").GetInt32();
                    var phase = act.GetProperty("phase").GetInt32();
                    var param = act.GetProperty("parameter").GetInt32();
                    if (!firstAction) sbActions.Append(',');
                    sbActions.Append($"({boardId},{sx},{sy},{sa},{seq},{phase},{param})");
                    firstAction = false;
                }
            }
        }
        sbActions.Append(';');
        if (!firstAction) dataService.ExecuteSQL(sbActions.ToString());

        return Results.Ok(new { success = true, totalFlags, totalPlayers });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// ────────────────────────────────────────────────────────────────────────────

app.Urls.Add("http://mrobopi3:5000");

app.Run();

// ── Local Functions ──────────────────────────────────────────────────────────

void SeedBoardTemplate(DataService ds)
{
    string[] candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "..", "install", "BoardTemplate.srx"),
        Path.Combine(AppContext.BaseDirectory, "..", "install", "BoardTemplate.srx"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "install", "BoardTemplate.srx"),
    };

    string? foundPath = null;
    foreach (var c in candidates)
    {
        if (File.Exists(c)) { foundPath = Path.GetFullPath(c); break; }
    }

    if (foundPath == null)
    {
        Console.WriteLine("WARNING: BoardTemplate.srx not found — skipping board template seed.");
        return;
    }

    var collection = ds.LoadFile(typeof(BoardElementCollection), foundPath) as BoardElementCollection;
    if (collection == null)
    {
        Console.WriteLine("WARNING: Failed to parse BoardTemplate.srx — skipping board template seed.");
        return;
    }

    int cols = collection.BoardCols;
    int rows = collection.BoardRows;
    int laserDamage = collection.LaserDamage;
    int totalFlags = collection.TotalFlags;
    int gameType = collection.GameType;
    string safeName = "Template";

    // Upsert the Boards row for BoardID=0
    ds.ExecuteSQL(
        $"INSERT INTO Boards (BoardID, BoardName, X, Y, LaserDamage, PhaseCount, GameType, TotalFlags, Players) " +
        $"VALUES (0, '{safeName}', {cols}, {rows}, {laserDamage}, 5, {gameType}, {totalFlags}, 0) " +
        $"ON DUPLICATE KEY UPDATE BoardName='{safeName}', X=VALUES(X), Y=VALUES(Y), " +
        $"LaserDamage=VALUES(LaserDamage), GameType=VALUES(GameType), TotalFlags=VALUES(TotalFlags);");

    // Clear existing template data
    ds.ExecuteSQL("DELETE FROM BoardItemActions WHERE BoardID=0;");
    ds.ExecuteSQL("DELETE FROM BoardItems WHERE BoardID=0;");

    // Bulk-INSERT BoardItems
    if (collection.BoardElements.Count > 0)
    {
        var sbItems = new StringBuilder();
        sbItems.Append("INSERT INTO BoardItems (BoardID, X, Y, SquareType, Rotation) VALUES ");
        bool firstItem = true;
        foreach (var be in collection.BoardElements)
        {
            if (!firstItem) sbItems.Append(',');
            sbItems.Append($"(0,{be.BoardCol},{be.BoardRow},{(int)be.Type},{(int)be.Rotation})");
            firstItem = false;
        }
        sbItems.Append(';');
        ds.ExecuteSQL(sbItems.ToString());
    }

    // Bulk-INSERT BoardItemActions
    var sbActions = new StringBuilder();
    sbActions.Append("INSERT INTO BoardItemActions (BoardID, X, Y, SquareAction, ActionSequence, Phase, Parameter) VALUES ");
    bool firstAction = true;
    foreach (var be in collection.BoardElements)
    {
        foreach (var a in be.ActionList)
        {
            if (!firstAction) sbActions.Append(',');
            sbActions.Append($"(0,{be.BoardCol},{be.BoardRow},{(int)a.SquareAction},{a.ActionSequence},{a.Phase},{a.Parameter})");
            firstAction = false;
        }
    }
    if (!firstAction)
    {
        sbActions.Append(';');
        ds.ExecuteSQL(sbActions.ToString());
    }

    Console.WriteLine($"Board template seeded: BoardID=0, {collection.BoardElements.Count} squares from {foundPath}");
}