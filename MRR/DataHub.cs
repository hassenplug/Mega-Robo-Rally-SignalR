using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace MRR.Hubs
{
    // The DataHub will manage the real-time connections
    public class DataHub : Hub
    {
        // This method can be called directly by a client (e.g., to send a message)
        public async Task SendMessage(string user, string message)
        {
            // 'Clients.All' sends the message to every connected client
            // 'ReceiveMessage' is the method name the clients listen to
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }
        
        public async Task UpdatePlayer(int playerId, int command, int data1 = 0, int data2 = 0)
        {
            // need to write to the database here
            // Broadcast the player's move to all connected clients
            //await Clients.All.SendAsync("PlayerUpdate", playerId, command, data1, data2);
        }

        // Example: A method to manually request a database read
        public async Task GetCurrentDatabaseData()
        {
            // In a real application, you'd call a data service here.
            var data = new { Message = "Data requested from server.", Timestamp = System.DateTime.Now.ToString() };

            await Clients.Caller.SendAsync("ReceiveDataUpdate", data);
        }
    }
}