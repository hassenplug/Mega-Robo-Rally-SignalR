using MRR.Hubs;
using MRR.Services;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DataService>();

builder.Services.AddSignalR();

var app = builder.Build();

app.UseStaticFiles();
app.UseDefaultFiles(); 

app.MapHub<DataHub>("/datahub");

app.MapGet("/api/alldata", (DataService dataService, IHubContext<DataHub> hubContext) =>
{
    var dataout = dataService.GetAllDataJson();
    hubContext.Clients.All.SendAsync("AllDataUpdate", dataout);
    return Results.Ok(dataout);
});

app.MapGet("/api/currentgamedata", (DataService dataService, IHubContext<DataHub> hubContext) =>
{
    var dataout = dataService.GetQueryResultsJson("Select iKey, sKey, iValue, sValue from CurrentGameData;", "currentgamedata");
    hubContext.Clients.All.SendAsync("currentgamedata", dataout);
    return Results.Ok(dataout);
});


app.Urls.Add("http://mrobopi3:5000"); 

app.Run();