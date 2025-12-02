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

            //string strSQL = $"call updatePlayer({playerId},{command},{data1},{data2});";
            //Console.WriteLine("Update: " + request);
            // update/player/card/removefrom/

            //string[] requestSplit = request.Split('/');
            //string commandID = requestSplit[2];
            //string playerid = requestSplit[3];
            switch (command)
            {
                case 1:
                    //string cardid = requestSplit[4];
                    //string position = requestSplit[5];
                    //DBConn.Command("call procUpdateCardPlayed(" + playerid + "," + cardid + "," + position + ");");
                    //Console.WriteLine($"UpdatePlayer called with playerId={playerId}, command={command}, data1={data1}, data2={data2}");
                    _dataService.ExecuteSQL("call procUpdateCardPlayed(" + playerId + "," + data1 + "," + data2 + ");");
                    //Console.WriteLine($"UpdatePlayer called with playerId={playerId}, command={command}, data1={data1}, data2={data2}");
                    // check to see if we an go to next state
                    break;
                case 2:
                    //string positionValid = requestSplit[4];
                    // clear message
                    //DBConn.Command("update Robots set PositionValid=" + positionValid + " where RobotID=" + playerid + ";");
                    break;
                case 3:
                    int markcommand = _dataService.GetIntFromDB("Select MessageCommandID from Robots where RobotID=" + playerId);
                    _dataService.ExecuteSQL("update Robots set MessageCommandID=null where RobotID=" + playerId + ";");
                    _dataService.ExecuteSQL("update CommandList set StatusID=6 where CommandID=" + markcommand + ";");
                    
                    break;

            }
            // check to see if we an go to next state
            //select funcGetNextGameState();
            
            //var gamestate = rDBConn.Exec("select funcGetNextGameState();"); //going to next state?
//            var gamestate = DBConn.Command("select funcGetNextGameState();"); //going to next state?
            
            //if (createCommands.UpdateGameState() == 6)
//            if (gamestate == 6)
//            {
//                createCommands.ExecuteTurn();
//            }
//            return MakeRobotsJson(request);

            var gamestate = _dataService.GetIntFromDB("select funcGetNextGameState();"); //going to next state?
            
            //if (createCommands.UpdateGameState() == 6)
            if (gamestate == 6)
            {
                //createCommands.ExecuteTurn();
            }

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

        public async Task NextState()
        {
            var newstate = _dataService.GetIntFromDB("select funcGetNextGameState(); ");
            Console.WriteLine("next:" + newstate.ToString());
            //return "State:" + newstate.ToString();
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