using MRR.Hubs;
using MRR.Services;
using Microsoft.AspNetCore.SignalR;
using System.Net.WebSockets;
using MRR.Controller;
using MRR;
using MRR.Data;
using MRR.Admin;
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
builder.Services.AddSingleton<AdminAudit>();
builder.Services.AddSingleton<AdminAccess>();

var app = builder.Build();

// Force GameController singleton construction at startup so LoadCurrentGame
// runs immediately (connects to robots, reloads game data).
app.Services.GetRequiredService<GameController>();

// UseDefaultFiles must come first, or "/" is never rewritten to index.html and returns
// 404 -- which is why phones had to be pointed at the explicit filename.
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

app.MapHub<DataHub>("/datahub");

// ── Admin & Diagnostics ─────────────────────────────────────────────────────
// Replaces /api/table. Audited, reloads game state after every write. Loopback always;
// remote only when Admin:AllowRemote and Admin:ApiKey are configured (see AdminAccess).
app.MapAdminApi(app.Services.GetRequiredService<AdminAudit>(),
                app.Services.GetRequiredService<AdminAccess>());

// ── Turn execution control ──────────────────────────────────────────────────
// Stops a turn that is going wrong. Until now there was no way to do that short of
// restarting the process -- and freezing it mid-turn is worse, because already-sent robot
// commands keep running while the dispatch loop is suspended.
app.MapPost("/api/execution/abort", (GameController gameController) =>
    gameController.AbortTurn()
        ? Results.Ok(new { aborted = true, note = "Dispatch stopped. Commands already sent are still moving their robots. Use Reload Position (state 16) or Create Program (state 15) to recover." })
        : Results.Ok(new { aborted = false, note = "No turn is running." }));

// ── Settings API ────────────────────────────────────────────────────────────

app.MapGet("/api/settings/robot-screen", (bool enabled, GameController gameController) =>
{
    GameController.UseRobotScreen = enabled;
    Console.WriteLine($"UseRobotScreen set to {enabled}");
    return Results.Ok(new { UseRobotScreen = enabled });
});

// Liveness probe for mrr-health.service. Deliberately cheap and side-effect free:
// no SignalR broadcast (unlike /api/alldata) and no dependency on a static file path
// (unlike /index.html, which the probe uses today only because "/" returns 404 —
// UseStaticFiles is registered before UseDefaultFiles above).
// Set MRR_HEALTH_URL=http://127.0.0.1:5000/api/health in /etc/default/mrr to use it.
app.MapGet("/api/health", (GameController gameController) =>
    Results.Ok(new { status = "ok", state = gameController.GameState }));

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
        case "clearpause":
            dataService.ExecuteSQL("UPDATE CommandList SET StatusID = 6 WHERE CommandTypeID = 92 AND StatusID = 4");
            gameController.NextState();
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


// Player update API — replaces DataHub.UpdatePlayer SignalR method
app.MapGet("/api/player/{command:int}/{playerId:int?}/{data1:int?}/{data2:int?}",
    async (int command, int? playerId, int? data1, int? data2,
           DataService dataService, IHubContext<DataHub> hubContext, GameController gameController) =>
{
    int pid  = playerId ?? 0;
    int d1   = data1   ?? 0;
    int d2   = data2   ?? 0;

    switch (command)
    {
        case 1:
            dataService.UpdateCardPlayed(pid, d1, d2);
            dataService.RefreshPlayerCards(pid);
            dataService.AllPlayers.GetPlayer(pid)?.UpdateStatusLEDs();
            gameController.RefreshPlayerScreenUI(pid);
            break;
        case 3:
            int markCommand = dataService.GetIntFromDB(
                $"SELECT MessageCommandID FROM Robots WHERE RobotID={pid}");
            
            dataService.ProcessDbCommand(markCommand,-1);
            break;
    }

    gameController.NextState();

    var dataout = dataService.GetAllDataJson();
    await hubContext.Clients.All.SendAsync("AllDataUpdate", dataout);
    return Results.Content(dataout, "application/json");
});

// Grid-alignment endpoint: download a camera frame, detect black grid lines,
// and nudge the robot until it is centered on its board square.
// GET /api/robot/align/{robotId}
app.MapGet("/api/robot/alignthis/{robotId:int}", async (int robotId, DataService dataService) =>
{
    var dt = dataService.GetQueryResults(
        $"SELECT rb.IPAddress FROM Robots r JOIN RobotBases rb ON r.RobotBaseID = rb.RobotBaseID WHERE r.RobotID={robotId};");
    if (dt.Rows.Count == 0)
        return Results.NotFound(new { error = $"Robot {robotId} not found" });

    var ipAddress = dt.Rows[0]["IPAddress"]?.ToString();
    if (string.IsNullOrWhiteSpace(ipAddress))
        return Results.BadRequest(new { error = $"Robot {robotId} has no IP address configured" });

    var robot = new Player { IPAddress = ipAddress };
    await robot.ConnectAsync();

    if (!robot.isConnected)
        return Results.Problem($"Could not connect to robot {robotId} at {ipAddress}");

    try
    {
        var result = await robot.AlignAsync();
        return Results.Ok(result);
    }
    finally
    {
        await robot.DisposeAsync();
    }
});

app.MapGet("/api/robot/{function?}/{parameter1?}", async (string? function, string? parameter1, DataService dataService, IHubContext<DataHub> hubContext, GameController gameController) =>
{

    if (function == null) function = "";
    if (parameter1 == null) parameter1 = "all";
    //var robot = AllPlayers.GetPlayer(parameter1); // Replace 1 with the actual player ID

    switch (function)
    {
        case "align":
            var robot1 = await new Player().Connect(parameter1 ?? "");
            await (robot1?.AlignAsync() ?? Task.CompletedTask);
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
    await hubContext.Clients.All.SendAsync("State", dataout);
    return Results.Ok(dataout);
});

app.MapGet("/api/board/{boardID?}", (int? boardID, DataService dataService, IHubContext<DataHub> hubContext, GameController gameController) =>
{
    if (boardID == null) boardID = dataService.BoardID;
    else dataService.BoardID = boardID.Value;
//    var dataout = dataService.GetQueryResultsJson($"Select * from BoardItems where BoardID={boardID};", "board");
    var dataout = dataService.GetQueryResultsJson(
        $"Select bi.*, bia.SquareAction, bia.Parameter from BoardItems bi left join BoardItemActions bia " +
        $" on bi.BoardID=bia.BoardID and bi.X=bia.X and bi.Y=bia.Y and (bia.SquareAction=19 or bia.SquareAction=16) " +
        $" where bi.BoardID={boardID};", "board");
    //hubContext.Clients.All.SendAsync("board", dataout);
    return Results.Content(dataout, "application/json");
});



// ────────────────────────────────────────────────────────────────────────────

// Listen URL comes from configuration ("Urls" in appsettings.json, overridable by
// the ASPNETCORE_URLS environment variable) so the host is not baked into the build.

app.Run();
