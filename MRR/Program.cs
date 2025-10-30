using MRR.Hubs;
using MRR.Services;
using Microsoft.AspNetCore.SignalR;
using System.Net.WebSockets;
using MRR.Controller;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DataService>();
builder.Services.AddSingleton<GameController>();

builder.Services.AddSignalR();

var app = builder.Build();

app.UseStaticFiles();
app.UseDefaultFiles();
app.UseWebSockets();

app.MapHub<DataHub>("/datahub");

app.MapGet("/api/alldata", (DataService dataService, IHubContext<DataHub> hubContext) =>
{
    var dataout = dataService.GetAllDataJson();
    hubContext.Clients.All.SendAsync("AllDataUpdate", dataout);
     
    return Results.Ok(dataout );
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
    return Results.Ok(dataout);
});

app.MapGet("/api/state/{newstate?}/{parameter1?}", (string? newstate, string? parameter1, DataService dataService, IHubContext<DataHub> hubContext, GameController gameController) =>
{

    if (newstate == null) newstate = "";

    switch (newstate)
    {
        case "nextstate":
            gameController.NextState();
            //return Results.Ok(nextstate);
            break;
        case "startgame":
            gameController.StartGame(Convert.ToInt32(parameter1));
            //return Results.Ok(result);
            break;
        case "resetgame":
            dataService.ExecuteSQL("call procResetGameState();");
            break;
        default:
        //        var setStatement = "Update " + tablename + " set " + setvalue + whereClause + ";";
        //        dataService.ExecuteSQL(setStatement);
            break;
    }   

    var dataout = dataService.GetQueryResultsJson($"Select * from CurrentGameData;", "State");
    hubContext.Clients.All.SendAsync("State", dataout);
    return Results.Ok(dataout);
});



app.Urls.Add("http://mrobopi3:5000"); 

app.Run();