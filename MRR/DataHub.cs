using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using MRR.Services;
using MRR.Controller;

namespace MRR.Hubs
{
    // The DataHub will manage the real-time connections
    public class DataHub : Hub
    {
        private readonly DataService _dataService;
        private readonly GameController _gameController;

        public DataHub(DataService dataService, GameController gameController)
        {
            _dataService = dataService;
            _gameController = gameController;
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
            switch (command)
            {
                case 1:
                    _dataService.ExecuteSQL("call procUpdateCardPlayed(" + playerId + "," + data1 + "," + data2 + ");");
                    break;
                case 2:
                    break;
                case 3:
                    int markcommand = _dataService.GetIntFromDB("Select MessageCommandID from Robots where RobotID=" + playerId);
                    _dataService.ExecuteSQL("update Robots set MessageCommandID=null where RobotID=" + playerId + ";");
                    _dataService.ExecuteSQL("update CommandList set StatusID=6 where CommandID=" + markcommand + ";");
                    break;
            }

            _gameController.NextState();

           await SendUpdate();
        }

        // Example: A method to manually request a database read
        public async Task GetCurrentDatabaseData()
        {
            // Use the injected DataService to read from the database
            var data = _dataService.GetAllDataJson();
            Console.WriteLine("DataHub: GetCurrentDatabaseData called.");

            // Send the retrieved data back to the caller
            await Clients.Caller.SendAsync("ReceiveDataUpdate", data);
        }

        ///////////////////////////////////////////////////////////////////////////
        // Method to send updated data to all connected clients
        ///////////////////////////////////////////////////////////////////////////

        public async Task SendUpdate()
        {
            var dataout = _dataService.GetAllDataJson();
            await Clients.All.SendAsync("AllDataUpdate", dataout);

        }

        public async Task SendActiveCommands()
        {
            var dataout = _dataService.GetAllDataJson();
            await Clients.All.SendAsync("ActiveCommandsUpdate", dataout);

        }

        public Task NextState()
        {
            _gameController.NextState();
            Console.WriteLine("next:" + _dataService.GameState.ToString());
            return Task.CompletedTask;
        }

        // Handle robot responses and events
        public async Task OnRobotResponse(string robotId, string response)
        {
            // Broadcast the robot's response to all clients
            await Clients.All.SendAsync("RobotResponse", robotId, response);
        }

        // Handle robot status updates
        public async Task UpdateRobotStatus(string robotId, string status)
        {
            await Clients.All.SendAsync("RobotStatusUpdate", robotId, status);
        }
    }
}
