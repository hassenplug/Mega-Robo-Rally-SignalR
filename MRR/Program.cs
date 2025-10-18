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
//    hubContext.Clients.All.SendAsync("AllDataUpdate", JsonConvert.SerializeObject(dataout));
     
     return Results.Ok(dataout );
//     return Results.Ok(new { data = "testing", payload = dataout });
    //return Results.Ok(dataout);
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

app.Urls.Add("http://mrobopi3:5000"); 

app.Run();