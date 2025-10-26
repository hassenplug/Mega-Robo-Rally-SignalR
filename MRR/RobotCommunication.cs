using MRR.Hubs;
using MRR.Services;
using Microsoft.AspNetCore.SignalR;

namespace MRR.RobotCommunication
{
    public class RobotCommunication
    {
        private readonly DataService _dataService;


        public RobotCommunication(DataService dataService)
        {
            _dataService = dataService;
        }

        public string SendCommandToVexAim()
        {
/*
            using (var ws = new WebSocket("ws://192.168.4.1:8080/aim"))
            {
                ws.OnOpen += (sender, e) =>
                {
                    Console.WriteLine("Connected to VEX AIM");

                    var command = @"{
                        ""command"": ""move"",
                        ""direction"": ""forward"",
                        ""speed"": 50
                    }";

                    ws.Send(command);
                    ws.Close();
                };

                ws.OnError += (sender, e) =>
                {
                    Console.WriteLine("Error: " + e.Message);
                };

                ws.Connect();
            }
            */
            return "Command Sent";
        }
    }
}



/*
VEX AIM WebSocket Commands (JSON)
• Move: {"command":"move","direction":"forward","speed":50}
• Turn: {"command":"turn","direction":"left","angle":90}
• LED: {"command":"led","color":"#FF0000"}
• Draw: {"command":"screen.draw","shape":"circle","x":100,"y":100,"radius":50,"color":"#00FF00"}
• Text: {"command":"screen.text","text":"Hello VEX!","x":50,"y":50,"font":"medium","color":"#00FF00"}
• Sound: {"command":"sound.play","sound":"beep"}
• Vision: {"command":"vision.detect","target":"april_tag"}
• Camera: {"command":"camera.stream","resolution":"640x480","fps":30}
• Event: {"command":"event.register","event":"screen.press","callback":"onScreenPress"}
*/
