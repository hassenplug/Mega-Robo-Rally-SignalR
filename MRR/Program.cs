using MRR.Hubs;
using MRR.Services;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// 1. Add the DataService so it can be used by the application (Dependency Injection)
builder.Services.AddSingleton<DataService>();

// 2. Add SignalR service
builder.Services.AddSignalR();

// 3. Add CORS policy to allow a web browser (client) to connect from a different origin
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        policy =>
        {
            // Allow any client (web browser) to connect to the SignalR Hub
            // ⚠️ For production, replace '*' with the specific URL of your client
            policy.AllowAnyHeader()
                  .AllowAnyMethod()
                  .SetIsOriginAllowed((host) => true) // Allow any host/origin
                  .AllowCredentials(); // Needed for SignalR 
        });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseStaticFiles();

// 2. Map the default file (index.html) to the root URL (/)
app.UseDefaultFiles(); 

// 4. Use the CORS policy
app.UseCors();

// 5. Map the SignalR Hub to a specific URL path
app.MapHub<DataHub>("/datahub");

// 6. Define a simple endpoint to test the DB connection and SignalR broadcast
// This will simulate sending a DB update to all connected clients
app.MapGet("/api/dbtest", (DataService dataService, IHubContext<DataHub> hubContext) =>
{
    string data = dataService.GetUserCount();
    
    // Broadcast the database status to all connected clients in real-time
    hubContext.Clients.All.SendAsync("ReceiveDataUpdate", new { DatabaseStatus = data, ServerTime = DateTime.Now.ToLongTimeString() });
    
    return Results.Ok(new { Status = "Broadcast sent", Data = data });
});

app.MapGet("/api/robots", (DataService dataService, IHubContext<DataHub> hubContext) =>
{
    string strSQL = "select * from viewRobots;";
    //var data = new { Robots = dataService.GetJsonData(strSQL) };
    object data = dataService.GetQueryResults(strSQL);

    // Broadcast the database status to all connected clients in real-time
    //    hubContext.Clients.All.SendAsync("ReceiveDataUpdate", new { DatabaseStatus = data, ServerTime = DateTime.Now.ToLongTimeString() });
    hubContext.Clients.All.SendAsync("ReceiveDataUpdate", data);

    //    return Results.Ok(new { Status = "Broadcast sent", Data = data });
    //return Results.Ok(data);
    return Results.Ok(new { Robots = data });
});


// 7. Force the server to listen on all interfaces (0.0.0.0)
// This is critical for the Raspberry Pi to be accessible from the network
// The default port is usually 5000/5001 or 80/443
//app.Urls.Add("http://0.0.0.0:5000"); 
//app.Urls.Add("http://localhost:5000"); 
app.Urls.Add("http://mrobopi3:5000"); 

app.Run();