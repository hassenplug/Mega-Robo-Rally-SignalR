namespace MRR
{
    public class AllDataPayload
    {
        public string titlemsg { get; set; } = "";
        // 25 means "no game running" (was a separate IsRunning bool, folded in here
        // 2026-09-17); every other value is a real state-machine position (see CLAUDE.md).
        public int gamestate { get; set; }
        public List<RobotData> robots { get; set; } = new();
        // Only set while gamestate==1 (operator/seat setup); DataService serializes with
        // NullValueHandling.Ignore so this key is genuinely absent from the json the rest of
        // the time, not just null -- see install/todo.md "Operator Data Setup".
        public GameConfigData? GameConfig { get; set; }
    }

    // Setup-phase broadcast: who may currently claim a seat, and what's left to claim.
    public class GameConfigData
    {
        public int PlayerToSelect { get; set; }
        public List<AvailableRobotBody> AvailableRobots { get; set; } = new();
        public List<int> AvailableStartPositions { get; set; } = new();
    }

    public class AvailableRobotBody
    {
        public int RobotBodyID { get; set; }
        public string Name { get; set; } = "";
        public string Color { get; set; } = "";
        public string ColorFG { get; set; } = "";
    }

    public class RobotData
    {
        public int RobotID { get; set; }
        public string RobotName { get; set; } = "";
        public string RobotColor { get; set; } = "";
        public string RobotColorFG { get; set; } = "";
        public int CurrentFlag { get; set; }
        public string StatusColor { get; set; } = "";
        public string LEDColor { get; set; } = "";
        public string PlayerStatus { get; set; } = "";
        public int StatusID { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Dir { get; set; }
        public string sDir { get; set; } = "";
//        public int AX { get; set; }
//        public int AY { get; set; }
//        public int Score { get; set; }
        public string OperatorName { get; set; } = "";
        // 0 until the player has actively picked a facing direction (SetRobotDirection) --
        // true from game start, since the board's PlayerStart rotation is a default, not a
        // choice, until VerifyPosition-era code re-enables the old auto-valid path. Drives
        // whether the direction-picker button shows on the phone (js/loadrobots.js).
        public int PositionValid { get; set; }
        public int Priority { get; set; }
        public int ShutDown { get; set; }
        // Password is deliberately NOT here. It was broadcast to every connected client,
        // so all six phones received all six players' passwords -- and nothing on either
        // side ever checked it, so it bought nothing in exchange. Player.Password still
        // loads from the Robots table; it just does not leave the server.
        public int PlayerSeat { get; set; }
        public int Energy { get; set; }
        public string FlagEnergyCards { get; set; } = "";
        public int PlayerViewDirection { get; set; }
        public int DirectionAdjustment { get; set; }
        public string CardsDealt { get; set; } = "";
        public string CardsPlayed { get; set; } = "";
        public string StatusToShow { get; set; } = "";
        public string PlayerMsg { get; set; } = "";
        public int CardCount { get; set; } = 0;
        public int ConnectStatusID { get; set; }
        public string ConnectStatusColor { get; set; } = "";
        public string ConnectStatusDesc { get; set; } = "";
        public string IPAddress { get; set; } = "";

        public override string ToString() =>
            $"[{RobotID}] {RobotName} ({PlayerStatus}) X={X} Y={Y} Cards={CardsDealt} Played={CardsPlayed} Status={StatusToShow}";
    }
}
