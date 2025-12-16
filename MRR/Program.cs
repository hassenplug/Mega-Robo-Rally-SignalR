using MRR.Hubs;
using MRR.Services;
using Microsoft.AspNetCore.SignalR;
using System.Net.WebSockets;
using MRR.Controller;
using MRR;
using MRR.Data;
using Microsoft.EntityFrameworkCore;

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
    hubContext.Clients.All.SendAsync("AllDataUpdate", dataout);
     
    return Results.Content(dataout, "application/json");
});

app.MapGet("/api/state/{newstate?}/{parameter1?}", async (string? newstate, string? parameter1, DataService dataService, IHubContext<DataHub> hubContext, GameController gameController) =>
{

    if (newstate == null) newstate = "";

    switch (newstate)
    {
        case "nextstate":
            if (Convert.ToInt32(parameter1) > 0) gameController.SetGameState(Convert.ToInt32(parameter1));
            gameController.NextState();
            //return Results.Ok(nextstate);
            break;
        case "startgame":
            gameController.SetGameState(0);
            gameController.StartGame(Convert.ToInt32(parameter1));
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
            hubContext.Clients.All.SendAsync("AllDataUpdate", alldataout);

            return Results.Ok(alldataout);
        case "gametables":

            return Results.Content(dataService.GetTableDataAsHTML("CurrentGameData/Robots/CommandList"), "text/html");
            break;
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
    hubContext.Clients.All.SendAsync("AllDataUpdate", dataout);
     
    return Results.Content(dataout, "application/json");
});


app.MapGet("/api/robot/{function?}/{parameter1?}", (string? function, string? parameter1, DataService dataService, IHubContext<DataHub> hubContext, GameController gameController) =>
{

    if (function == null) function = "";

    switch (function)
    {
        case "test":
            var robot = new Player().Connect(parameter1);
            robot.RunTest().Wait();
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


app.Urls.Add("http://mrobopi3:5000"); 

app.Run();