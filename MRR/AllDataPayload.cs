namespace MRR
{
    public class AllDataPayload
    {
        public string titlemsg { get; set; } = "";
        public int gamestate { get; set; }
        public List<RobotData> robots { get; set; } = new();
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
        public int AX { get; set; }
        public int AY { get; set; }
        public int Score { get; set; }
        public string OperatorName { get; set; } = "";
        public int PositionValid { get; set; }
        public int Priority { get; set; }
        public int ShutDown { get; set; }
        public string Password { get; set; } = "";
        public int PlayerSeat { get; set; }
        public int Energy { get; set; }
        public string FlagEnergy { get; set; } = "";
        public int PlayerViewDirection { get; set; }
        public int DirectionAdjustment { get; set; }
        public string CardsDealt { get; set; } = "";
        public string CardsPlayed { get; set; } = "";
        public string StatusToShow { get; set; } = "";
        public string msg { get; set; } = "";
        public int CardCount { get; set; } = 0;

        public override string ToString() =>
            $"[{RobotID}] {RobotName} ({PlayerStatus}) X={X} Y={Y} Cards={CardsDealt} Played={CardsPlayed} Status={StatusToShow}";
    }
}
