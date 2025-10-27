using MRR.Hubs;
using MRR.Services;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using System.Net.Sockets;
using System.Text;

namespace MRR.RobotCommunication
{
    public class RobotCommand
    {
        public required string Command { get; set; }
        public string? Direction { get; set; }
        public int? Speed { get; set; }
        public int? Angle { get; set; }
        public string? Color { get; set; }
        public string? Shape { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public int? Radius { get; set; }
        public string? Text { get; set; }
        public string? Font { get; set; }
        public string? Sound { get; set; }
        public string? Target { get; set; }
        public string? Resolution { get; set; }
        public int? Fps { get; set; }
        public string? Event { get; set; }
        public string? Callback { get; set; }
    }

    public class RobotCommunication
    {
        private readonly DataService _dataService;
        private readonly IHubContext<DataHub> _hubContext;

        public RobotCommunication(DataService dataService, IHubContext<DataHub> hubContext)
        {
            _dataService = dataService;
            _hubContext = hubContext;
        }


        // Send a command directly to a robot at the given IP using TCP (Winsock).
        // Returns the robot's immediate response (if any) or an error string.
        public async Task<string> SendCommand(string ipAddress, int port = 8080)
        {
            var command = new RobotCommand
            {
                Command = "move",
                Direction = "forward",
                Speed = 50
            };

            var jsonCommand = JsonSerializer.Serialize(command, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            try
            {
                using var client = new TcpClient();
                // Connect to the robot IP/port (this uses Winsock under the hood)
                await client.ConnectAsync(ipAddress, port);

                using NetworkStream ns = client.GetStream();

                // Send the JSON command
                var outBytes = Encoding.UTF8.GetBytes(jsonCommand);
                await ns.WriteAsync(outBytes, 0, outBytes.Length);

                // Optionally forward the command to SignalR clients as well
                await _hubContext.Clients.All.SendAsync("RobotCommand", jsonCommand);

                // Try to read an immediate response (non-blocking with timeout)
                client.ReceiveTimeout = 3000; // ms
                var inBuffer = new byte[4096];
                var read = 0;
                // ReadAsync doesn't respect ReceiveTimeout on NetworkStream, so do a timed read
                using var cts = new CancellationTokenSource(client.ReceiveTimeout);
                try
                {
                    read = await ns.ReadAsync(inBuffer, 0, inBuffer.Length, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // timeout - no response
                    read = 0;
                }

                if (read > 0)
                {
                    var response = Encoding.UTF8.GetString(inBuffer, 0, read);
                    Console.WriteLine($"Received from {ipAddress}:{port} -> {response}");
                    return response;
                }

                return "OK";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Socket error when connecting to {ipAddress}:{port} - {ex.Message}");
                return "Error: " + ex.Message;
            }
        }

        public async Task SendCommandToVexAim(RobotCommand command)
        {
            var jsonCommand = JsonSerializer.Serialize(command, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
            
            await _hubContext.Clients.All.SendAsync("RobotCommand", jsonCommand);
            Console.WriteLine($"SignalR command sent: {jsonCommand}");
        }

        // Convenience methods for common robot commands
        public async Task MoveRobot(string direction, int speed)
        {
            var command = new RobotCommand
            {
                Command = "move",
                Direction = direction,
                Speed = speed
            };
            await SendCommandToVexAim(command);
        }

        public async Task TurnRobot(string direction, int angle)
        {
            var command = new RobotCommand
            {
                Command = "turn",
                Direction = direction,
                Angle = angle
            };
            await SendCommandToVexAim(command);
        }

        public async Task SetLED(string color)
        {
            var command = new RobotCommand
            {
                Command = "led",
                Color = color
            };
            await SendCommandToVexAim(command);
        }

        public async Task DrawShape(string shape, int x, int y, int radius, string color)
        {
            var command = new RobotCommand
            {
                Command = "screen.draw",
                Shape = shape,
                X = x,
                Y = y,
                Radius = radius,
                Color = color
            };
            await SendCommandToVexAim(command);
        }

        public async Task DisplayText(string text, int x, int y, string font, string color)
        {
            var command = new RobotCommand
            {
                Command = "screen.text",
                Text = text,
                X = x,
                Y = y,
                Font = font,
                Color = color
            };
            await SendCommandToVexAim(command);
        }

        public async Task PlaySound(string sound)
        {
            var command = new RobotCommand
            {
                Command = "sound.play",
                Sound = sound
            };
            await SendCommandToVexAim(command);
        }

        public async Task StartVisionDetection(string target)
        {
            var command = new RobotCommand
            {
                Command = "vision.detect",
                Target = target
            };
            await SendCommandToVexAim(command);
        }

        public async Task StartCameraStream(string resolution, int fps)
        {
            var command = new RobotCommand
            {
                Command = "camera.stream",
                Resolution = resolution,
                Fps = fps
            };
            await SendCommandToVexAim(command);
        }

        public async Task RegisterEventHandler(string eventName, string callback)
        {
            var command = new RobotCommand
            {
                Command = "event.register",
                Event = eventName,
                Callback = callback
            };
            await SendCommandToVexAim(command);
        }
        
            
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
            //return "Command Sent";
        //}
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
