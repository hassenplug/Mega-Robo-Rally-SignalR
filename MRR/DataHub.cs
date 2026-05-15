using Microsoft.AspNetCore.SignalR;

namespace MRR.Hubs
{
    // Broadcast-only hub — clients connect to receive AllDataUpdate pushes.
    // All game actions go through the REST API in Program.cs.
    public class DataHub : Hub { }
}
