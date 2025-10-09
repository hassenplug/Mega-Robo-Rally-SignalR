using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Collections.ObjectModel;
using System.ComponentModel; // INotifyPropertyChanged
using System.Xml.Serialization; // serializer
using MRR.Services;

namespace MRR
{
    public class Communication
    {

        private readonly DataService _dataService;

        public Communication(DataService dataService)
        {
            _dataService = dataService;
            //DBConn = ldb;
            createCommands = new(DBConn);
        }        

        private Database DBConn { get; set; }

        private TcpListener myTCPListener;
        private IPAddress TCPAddr = IPAddress.Parse("0.0.0.0");
        private int TCPport = 5050;
        private UdpClient udpServer;
        private int UDPport = 5010;

        private string WebServerPath = "wwwroot";
        private string serverEtag = Guid.NewGuid().ToString("N");

        private Dictionary<IPAddress, TcpClient> clientlist = new Dictionary<IPAddress, TcpClient>();

        private CreateCommands createCommands;



        public void StartServer()
        {
            try
            {

                myTCPListener = new TcpListener(TCPAddr, TCPport);
                myTCPListener.Start();
                Console.WriteLine($"Web Server Running on {TCPAddr.ToString()} on port {TCPport}... Press ^C to Stop...");
                Thread th = new Thread(new ThreadStart(Start_TCP_Listen));
                th.Start();

                udpServer = new UdpClient(UDPport);
                Console.WriteLine($"UDP server listening on port {UDPport}");
                Thread th2 = new Thread(new ThreadStart(Start_UDP_Server));
                th2.Start();
            }
            catch (System.Exception)
            {

                throw;
            }
        }

        private string GetQuery(string query)
        {
            var sout = query.Split("/");
            //Console.WriteLine("query:" + sout[sout.Length-1]);
            var newQuery = "Select * from " + sout[sout.Length-1] + ";";
            //var newQuery = "" + sout[sout.Length-1] + ";";
            return DBConn.GetHTMLfromQuery(newQuery);
        }

        // commands
        // 2 = validate position
        // 1 = update card /player/card id/ position

        private string UpdatePlayer(string request)
        {
            Console.WriteLine("Update: " + request);
            // update/player/card/removefrom/
            
            string[] requestSplit = request.Split('/');
            string commandID = requestSplit[2];
            string playerid = requestSplit[3];
            switch (commandID)
            {
                case "1": 
                    string cardid = requestSplit[4];
                    string position = requestSplit[5];
                    DBConn.Command("call procUpdateCardPlayed(" + playerid + "," + cardid + "," + position + ");");
                    // check to see if we an go to next state
                    break;
                case "2":
                    string positionValid = requestSplit[4];
                    // clear message
                    DBConn.Command("update Robots set PositionValid=" + positionValid + " where RobotID=" + playerid + ";");
                    break;
                case "3":
                    int markcommand = DBConn.GetIntFromDB("Select MessageCommandID from Robots where RobotID=" + playerid);
                    DBConn.Command("update Robots set MessageCommandID=null where RobotID=" + playerid + ";");
                    DBConn.Command("update CommandList set StatusID=6 where CommandID=" + markcommand + ";");
                    break;

            }
            // check to see if we an go to next state
            //select funcGetNextGameState();
            
            //var gamestate = rDBConn.Exec("select funcGetNextGameState();"); //going to next state?
            var gamestate = DBConn.Command("select funcGetNextGameState();"); //going to next state?
            
            //if (createCommands.UpdateGameState() == 6)
            if (gamestate == 6)
            {
                createCommands.ExecuteTurn();
            }
            return MakeRobotsJson(request);

        }

        void HandleConnection(TcpClient client)
        {

            NetworkStream stream = client.GetStream();

            //read request 
            byte[] requestBytes = new byte[1024];
            int bytesRead = stream.Read(requestBytes, 0, requestBytes.Length);

            string request = Encoding.UTF8.GetString(requestBytes, 0, bytesRead);
            var requestHeaders = ParseHeaders(request);

            string[] requestFirstLine = requestHeaders.requestType.Split(" ");
            string httpVersion = requestFirstLine.LastOrDefault();
            string contentType = requestHeaders.headers.GetValueOrDefault("Accept");
            string contentEncoding = requestHeaders.headers.GetValueOrDefault("Acept-Encoding");

            if (!request.StartsWith("GET"))
            {
                SendHeaders(httpVersion, 405, "Method Not Allowed", contentType, contentEncoding, 0, ref stream);
            }
            else
            {
                var requestedPath = requestFirstLine[1];
                if (requestedPath == "/ws")
                {
                    var addr = IPAddress.Parse(((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString());

                    if (!clientlist.ContainsKey(addr))
                    {
                        clientlist.Add(addr,client);
                        Console.WriteLine("Client: {0} {1} ",addr,clientlist.Count.ToString());
                        // Insert your code here. (Do not accept socket again here)
                        client.Close();
                        return;
                    }
                }

                var fileContent = GetContent(requestedPath);
                //var fileContent = new byte[] {  };
                if(fileContent is not null)
                {
                    SendHeaders(httpVersion, 200, "OK", contentType, contentEncoding, 0, ref stream);
                    stream.Write(fileContent, 0, fileContent.Length);
                }
                else
                {
                    SendHeaders(httpVersion, 404, "Page Not Found", contentType, contentEncoding, 0, ref stream);
                }
            }

            client.Close();

        }

        private async void Send_UDP_Message(IPEndPoint destination, byte message)
        {
            // receivedResult.RemoteEndPoint
            // send message
            //UdpReceiveResult receivedResult ;
            //IPEndPoint destination; // = new IPEndPoint();
            string responseMessage = "Message received";
            byte[] responseBytes = Encoding.UTF8.GetBytes(responseMessage);
            //responseBytes[3] = (byte)counter;
            responseBytes[2] = message;
            await udpServer.SendAsync(responseBytes, responseBytes.Length, destination);
            Console.WriteLine($"Sent response to {destination}");
        }

        private async void Start_UDP_Server()
        {
            var counter = 0;

            while (true)
            {
                //  receive message
                var receivedResult = await udpServer.ReceiveAsync();
                string receivedMessage = Encoding.UTF8.GetString(receivedResult.Buffer);
                counter+=1;
                Console.WriteLine($"Received: {receivedMessage} from {receivedResult.RemoteEndPoint} count {counter}  ");

                Send_UDP_Message(receivedResult.RemoteEndPoint,(byte)counter);
            }
        }

        private void Start_TCP_Listen()
        {
            while (true)
            {
             
                TcpClient client = myTCPListener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(state => HandleConnection(client));

            }
        }

        private byte[] GetContent(string requestedPath)
        {
            if (requestedPath == "/") requestedPath = "index.html";
            string filePath = Path.Join(WebServerPath, requestedPath);

            //Console.WriteLine(requestedPath + " " + requestedPath.Length);
            Console.WriteLine(requestedPath);

            if (requestedPath == "/executeturn")
            {
                //return  Encoding.ASCII.GetBytes(""); //rRGame.ExecuteTurn());
                return  Encoding.ASCII.GetBytes(createCommands.ExecuteTurn());
            }
            else if (requestedPath == "/makerobotsjson")
            {
                return  Encoding.ASCII.GetBytes(MakeRobotsJson(filePath));
            }
            else if (requestedPath.Contains("/query"))
            {
                return  Encoding.ASCII.GetBytes(GetQuery(requestedPath));
            }
            else if (requestedPath.Contains("/nextstate"))
            {
                return  Encoding.ASCII.GetBytes(NextState());
            }
            else if (requestedPath.Contains("/startgame"))
            {
                return  Encoding.ASCII.GetBytes(StartGame(requestedPath));
            }
            else if (requestedPath.Contains("/editboard"))
            {
                return  Encoding.ASCII.GetBytes(EditBoard(requestedPath));
            }
            else if (requestedPath.Contains("/processcommands"))
            {
                //rPendingCommands.ProcessCommands();
                return  Encoding.ASCII.GetBytes(MakeRobotsJson(filePath));
            }
            else if (requestedPath.Contains("dbeditor"))
            {
               return  Encoding.ASCII.GetBytes(DBConn.GetEditor(requestedPath));
            }
            else if (requestedPath.Length > 13 && requestedPath.Substring(1,12) == "updatePlayer")
            {
                return  Encoding.ASCII.GetBytes(UpdatePlayer(requestedPath)) ; //Encoding.ASCII.GetBytes(MakeRobotsJson(filePath));
                //return  Encoding.ASCII.GetBytes(UpdatePlayer(requestedPath)) ; //Encoding.ASCII.GetBytes(MakeRobotsJson(filePath));
                //return  Encoding.ASCII.GetBytes(""); //Encoding.ASCII.GetBytes(UpdatePlayer(requestedPath)) ; //Encoding.ASCII.GetBytes(MakeRobotsJson(filePath));
            }
            else if (!File.Exists(filePath)) 
            {
                return null;
            }
            else
            {
                byte[] file = System.IO.File.ReadAllBytes(filePath);
                return file;
            }
        }

//        private static void SendHeaders(string? httpVersion, int statusCode, string statusMsg, string? contentType, string? contentEncoding,
        private void SendHeaders(string httpVersion, int statusCode, string statusMsg, string contentType, string contentEncoding,
            int byteLength, ref NetworkStream networkStream)
        {
            string responseHeaderBuffer = "";

            responseHeaderBuffer = $"HTTP/1.1 {statusCode} {statusMsg}\r\n" +
                $"Connection: Keep-Alive\r\n" +
                $"Date: {DateTime.UtcNow.ToString()}\r\n" +
                $"Server: MacOs PC \r\n" +
                $"Etag: \"{serverEtag}\"\r\n" +
                $"Content-Encoding: {contentEncoding}\r\n" +
                "X-Content-Type-Options: nosniff"+
                $"Content-Type: application/signed-exchange;v=b3\r\n\r\n";

            byte[] responseBytes = Encoding.UTF8.GetBytes(responseHeaderBuffer);
            networkStream.Write(responseBytes, 0, responseBytes.Length);
        }

        private (Dictionary<string, string> headers, string requestType) ParseHeaders(string headerString)
        {
            var headerLines = headerString.Split('\r', '\n');
            string firstLine = headerLines[0];
            var headerValues = new Dictionary<string, string>();
            foreach (var headerLine in headerLines)
            {
                var headerDetail = headerLine.Trim();
                var delimiterIndex = headerLine.IndexOf(':');
                if (delimiterIndex >= 0)
                {
                    var headerName = headerLine.Substring(0, delimiterIndex).Trim();
                    var headerValue = headerLine.Substring(delimiterIndex + 1).Trim();
                    headerValues.Add(headerName, headerValue);
                }
            }
            return (headerValues, firstLine);
        }

        public  string MakeCardJson(int playerID)
        {
            string strSQL = "Select * from MoveCards where Owner=" + playerID.ToString() + " order by CardID";
            string result = DBConn.jsonFromQuery(strSQL);
//            result = result.Replace("\"Cardlist["+playerID+"]\"", result1);
            return result;

        }


        public  string MakeRobotsJson(string filename)
        {
            string strSQL = "select * from viewRobots;";
            string result = DBConn.jsonFromQuery(strSQL);

            for (int c=1;c<9;c++)
            {
                //string result1 = MakeCardJson(c);
                //result = result.Replace("\"Cardlist["+c+"]\"", result1);

            }

            return result;
        }

        public  string NextState()
        {
            var newstate = DBConn.GetIntFromDB("select funcGetNextGameState(); ");
            Console.WriteLine("next:" + newstate.ToString());
            return "State:" + newstate.ToString();
        }

        public  string StartGame(string request)
        {
            string[] sout = request.Split('/');
            if (sout[sout.Length-1] != "startgame")
            {
                DBConn.Command("Update CurrentGameData set iValue = " + sout[sout.Length-1] + " where iKey = 26;");  // set game state
            }

            DBConn.Command("Update CurrentGameData set iValue = 0 where iKey = 10;");  // set state to 0

            var startstate = DBConn.GetIntFromDB("select funcGetNextGameState(); ");
            //Console.WriteLine("next:" + newstate.ToString());
            return "New Game:" + startstate.ToString();
        }

        public  string EditBoard(string request)
        {
            string[] sout = request.Split('/');
            int boardid;

            if (sout[sout.Length-1] == "editboard")
            {
                // combo box board selector
                // get board id for current board
                boardid = DBConn.GetIntFromDB("select iValue from CurrentGameData where iKey=20;");
            }
            else
            {
                // table with board
                // for row
                // for column
                int.TryParse( sout[sout.Length-1],out boardid);

            }

            BoardElementCollection g_BoardElements = DBConn.BoardLoadFromDB(boardid);

            ///rRGame.BoardLoadFromDB(boardid);
            //rRGame.
            //foreach()

            string output = "<html><head>";
//var bwidth = <?= $BoardCols ?>;
//var bheight = <?= $BoardRows ?>;
            output += "<script>var bwidth = " + g_BoardElements.BoardCols +  ";var bheight = " + g_BoardElements.BoardRows + ";</script>";

            output += "<script src='/jscode.js' type='text/javascript' charset='utf-8'></script>";
            //output += "<style>html, body {    height: 100%;    margin: 0;    padding: 0;} img {    padding: 0;    display: block;    margin: 0 auto;    max-height: 100%;    max-width: 100%;}  th, td {  padding: 0px;} table {  border-spacing: 0px;} </style>";
            output += "<link rel='stylesheet' type='text/css' href='/board.css'>";
            output += "</head><body><table id='currentboard'>";
            for(int y=0;y<g_BoardElements.BoardRows;y++)
            {
                output += "<tr>";
                for(int x=0;x<g_BoardElements.BoardCols;x++)
                {
                    //Console.WriteLine("line: {0:D} {1:D} " , y , x );
                    BoardElement l_square = g_BoardElements.GetSquare(x, y);
                    if (l_square!= null)
                    {                        
                        //<img src="your image" style="transform:rotate(90deg);">
                        //<img id="image_canv" src="/image.png" class="rotate90">
                        /*
                        string onecell = l_square.Type.ToString() ;
                        if (l_square.Rotation != Direction.None)
                        {
                            onecell += "-" + l_square.Rotation.ToString();
                        }

                        onecell += "<br>";
                        */
/*
                        foreach(BoardAction eachAction in l_square.ActionList)
                        {
                            onecell += eachAction.SquareAction.ToString() + "-";
                            onecell += eachAction.ActionSequence.ToString() + "-";
                            onecell += eachAction.Phase.ToString() + "-";
                            onecell += eachAction.Parameter.ToString() + "<br>";
                            //onecell += "<br>";
                        }
                        */
                        //<img src="your image" style="transform:rotate(90deg);">

                        int[] rot = {0,0,90,180,270,360};

                        output += "<td><img src='/images/Element" + ((int)l_square.Type) + ".jpg' style='transform:rotate(" + rot[(int)l_square.Rotation] + "deg);'></td>";
                    }
                    
                }
                output += "</tr>";
            }
            //output += "<h1>Board Editor</h1>";
            //output +=  GetTableNames(newQuery);
            //newQuery = "Select * from " + newQuery;
            //output += GetHTMLfromQuery(newQuery);
            output += "</table>";

            output += "<div class='triangle' id='player_image1' style='border-color: transparent;' onmousedown='MouseClick(event,this)'></div>";
            output += "<div class='triangle' id='player_image2' style='border-color: transparent;' onmousedown='MouseClick(event,this)'></div>";
            output += "<div class='triangle' id='player_image3' style='border-color: transparent;' onmousedown='MouseClick(event,this)'></div>";
            output += "<div class='triangle' id='player_image4' style='border-color: transparent;' onmousedown='MouseClick(event,this)'></div>";
            output += "<div class='triangle' id='player_image5' style='border-color: transparent;' onmousedown='MouseClick(event,this)'></div>";
            output += "<div class='triangle' id='player_image6' style='border-color: transparent;' onmousedown='MouseClick(event,this)'></div>";
            output += "<div class='triangle' id='player_image7' style='border-color: transparent;' onmousedown='MouseClick(event,this)'></div>";
            output += "<div class='triangle' id='player_image8' style='border-color: transparent;' onmousedown='MouseClick(event,this)'></div>";
            
            output += "</body></html>";

            //Console.WriteLine("output:" + output);
            return output;
        }


    }
}

