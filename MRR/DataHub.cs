using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using MRR.Services;

namespace MRR.Hubs
{
    // The DataHub will manage the real-time connections
    public class DataHub : Hub
    {
        private readonly DataService _dataService;

        public DataHub(DataService dataService)
        {
            _dataService = dataService;
        }

        // This method can be called directly by a client (e.g., to send a message)
        public async Task SendMessage(string user, string message)
        {
            // 'Clients.All' sends the message to every connected client
            // 'ReceiveMessage' is the method name the clients listen to
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }

        public async Task UpdatePlayer(int command, int playerId = 0, int data1 = 0, int data2 = 0)
        {
            //Console.WriteLine($"UpdatePlayer called with playerId={playerId}, command={command}, data1={data1}, data2={data2}");
            _dataService.UpdatePlayer(command, playerId, data1, data2);

           await SendUpdate();
        }

        // Example: A method to manually request a database read
        public async Task GetCurrentDatabaseData()
        {
            // Use the injected DataService to read from the database
            var data = _dataService.GetAllData();

            // Send the retrieved data back to the caller
            await Clients.Caller.SendAsync("ReceiveDataUpdate", data);
        }

        public async Task SendUpdate()
        {
            var dataout = _dataService.GetAllData();
            await Clients.All.SendAsync("AllDataUpdate", dataout);

        }
    }
}